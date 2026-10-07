using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using medxbackend.Data;
using medxbackend.Logging;
using medxbackend.Models;
using medxbackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

namespace medxbackend.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class UserController : ControllerBase
{
    private readonly DbHelper _dbHelper;
    private readonly IConfiguration _configuration;
    private readonly IEmailService _emailService;
    private readonly ILogService _logService;

    /// <summary>初始化用户控制器及其数据库、配置、邮件和日志服务。</summary>
    public UserController(
        DbHelper dbHelper,
        IConfiguration configuration,
        IEmailService emailService,
        ILogService logService)
    {
        _dbHelper = dbHelper;
        _configuration = configuration;
        _emailService = emailService;
        _logService = logService;
    }

    /// <summary>注册用户账号，并处理重复用户名或邮箱。</summary>
    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<IActionResult> Register(
        [FromBody] RegisterRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var email = request.Email.Trim().ToLowerInvariant();
            var emailExists = await _dbHelper.ExecuteScalarAsync<int>(
                "SELECT 1 FROM users WHERE LOWER(email) = @email LIMIT 1",
                [new NpgsqlParameter("email", email)],
                cancellationToken);

            if (emailExists == 1)
            {
                return Conflict(new { message = "该邮箱已注册账号" });
            }

            var userId = await _dbHelper.ExecuteScalarAsync<string>(
                """
                INSERT INTO users (username, email, password_hash, createdate)
                VALUES (@username, @email, @password_hash, CURRENT_TIMESTAMP)
                RETURNING id
                """,
                [
                    new NpgsqlParameter("username", request.Username.Trim()),
                    new NpgsqlParameter("email", email),
                    new NpgsqlParameter("password_hash", CreateMd5PasswordHash(request.Password))
                ],
                cancellationToken);

            return StatusCode(StatusCodes.Status201Created, new
            {
                id = userId,
                username = request.Username,
                email = request.Email,
                message = "账号创建成功"
            });
        }
        catch (PostgresException exception) when (exception.SqlState == "23505")
        {
            return Conflict(new { message = "用户名或邮箱已存在" });
        }
    }

    /// <summary>为已注册邮箱生成限时密码重置令牌并发送邮件。</summary>
    [AllowAnonymous]
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(
        [FromBody] ForgotPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var userExists = await _dbHelper.ExecuteScalarAsync<int>(
            "SELECT 1 FROM users WHERE email = @email LIMIT 1",
            [new NpgsqlParameter("email", email)],
            cancellationToken);

        if (userExists == 1)
        {
            var resetToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .Replace("+", "-")
                .Replace("/", "_")
                .TrimEnd('=');
            var tokenHash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(resetToken)));

            await _dbHelper.ExecuteNonQueryAsync(
                """
                INSERT INTO password_reset_tokens (user_id, token_hash, expires_at, created_at)
                SELECT id, @token_hash, CURRENT_TIMESTAMP + INTERVAL '15 minutes', CURRENT_TIMESTAMP
                FROM users
                WHERE email = @email
                """,
                [
                    new NpgsqlParameter("token_hash", tokenHash),
                    new NpgsqlParameter("email", email)
                ],
                cancellationToken);

            await _emailService.SendPasswordResetEmailAsync(
                email,
                resetToken,
                cancellationToken);
        }

        return Ok(new { message = "如果该邮箱已注册，密码重置邮件将会发送到该邮箱" });
    }

    /// <summary>验证密码重置令牌并更新用户密码。</summary>
    [AllowAnonymous]
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(
        [FromBody] ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var tokenHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(request.Token)));
        var passwordHash = CreateMd5PasswordHash(request.NewPassword);

        var updated = await _dbHelper.ExecuteNonQueryAsync(
            """
            UPDATE users
            SET password_hash = @password_hash
            WHERE id IN
            (
                SELECT user_id
                FROM password_reset_tokens
                WHERE token_hash = @token_hash
                  AND expires_at > CURRENT_TIMESTAMP
            )
            """,
            [
                new NpgsqlParameter("password_hash", passwordHash),
                new NpgsqlParameter("token_hash", tokenHash)
            ],
            cancellationToken);

        if (updated == 0)
        {
            return BadRequest(new { message = "重置 token 无效或已过期" });
        }

        await _dbHelper.ExecuteNonQueryAsync(
            "DELETE FROM public.refresh_tokens WHERE user_id IN (SELECT user_id::text FROM password_reset_tokens WHERE token_hash = @token_hash)",
            [new NpgsqlParameter("token_hash", tokenHash)],
            cancellationToken);

        await _dbHelper.ExecuteNonQueryAsync(
            "DELETE FROM password_reset_tokens WHERE token_hash = @token_hash",
            [new NpgsqlParameter("token_hash", tokenHash)],
            cancellationToken);

        _logService.Information("用户密码重置成功");
        return Ok(new { message = "密码重置成功" });
    }

    /// <summary>验证用户凭据并签发访问令牌和刷新令牌。</summary>
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var users = await _dbHelper.QueryAsync(
            """
            SELECT id, username, password_hash
            FROM users
            WHERE username = @username
            LIMIT 1
            """,
            reader => new UserRecord(
                Convert.ToString(reader.GetValue(0))!,
                reader.GetString(1),
                reader.GetString(2)),
            [new NpgsqlParameter("username", request.Username)],
            cancellationToken);

        var user = users.SingleOrDefault();
        if (user is null)
        {
            return Unauthorized(new { message = "用户名或密码错误" });
        }

        if (!VerifyMd5Password(request.Password, user.PasswordHash))
        {
            return Unauthorized(new { message = "用户名或密码错误" });
        }

        var menus = await GetMenusAsync(Convert.ToInt32(user.Id), cancellationToken);
        var refreshToken = CreateRefreshToken();
        var refreshTokenExpirationDays = GetRefreshTokenExpirationDays();
        await _dbHelper.ExecuteNonQueryAsync(
            "INSERT INTO public.refresh_tokens (token_hash, user_id, expires_at) VALUES (@token_hash, @user_id, CURRENT_TIMESTAMP + (@expires_days * INTERVAL '1 day'))",
            [
                new NpgsqlParameter("token_hash", HashRefreshToken(refreshToken)),
                new NpgsqlParameter("user_id", user.Id),
                new NpgsqlParameter("expires_days", refreshTokenExpirationDays)
            ],
            cancellationToken);

        var token = CreateToken(user.Id, user.Username);
        return Ok(new
        {
            token,
            expiresInMinutes = GetTokenExpirationMinutes(),
            refreshToken,
            refreshTokenExpiresInDays = refreshTokenExpirationDays,
            menus,
            user = new
            {
                id = user.Id,
                username = user.Username
            }
        });
    }

    /// <summary>获取当前用户通过角色获准访问的菜单权限。</summary>
    [Authorize]
    [HttpGet("menus")]
    public async Task<IActionResult> GetMenus(CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return Unauthorized();
        }

        return Ok(await GetMenusAsync(userId, cancellationToken));
    }

    private Task<List<Dictionary<string, object?>>> GetMenusAsync(
        int userId,
        CancellationToken cancellationToken)
    {
        return _dbHelper.QueryAsync(
            """
            SELECT DISTINCT p.id, p.code, p.name, p.page_path
            FROM public.permissions AS p
            JOIN public.role_permissions AS rp ON rp.permission_id = p.id
            JOIN public.user_roles AS ur ON ur.role_id = rp.role_id
            WHERE ur.user_id = @userId
            ORDER BY p.id
            """,
            reader => new Dictionary<string, object?>
            {
                ["id"] = reader.GetInt32(0),
                ["code"] = reader.GetString(1),
                ["name"] = reader.GetString(2),
                ["page_path"] = reader.GetString(3)
            },
            [new NpgsqlParameter("userId", userId)],
            cancellationToken);
    }

    /// <summary>轮换有效的刷新令牌并签发新的访问令牌。</summary>
    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<IActionResult> RefreshToken(
        [FromBody] RefreshTokenRequest request,
        CancellationToken cancellationToken)
    {
        var refreshToken = CreateRefreshToken();
        var refreshTokenExpirationDays = GetRefreshTokenExpirationDays();
        var users = await _dbHelper.QueryAsync(
            """
            WITH consumed AS
            (
                DELETE FROM public.refresh_tokens
                WHERE token_hash = @token_hash
                  AND expires_at > CURRENT_TIMESTAMP
                RETURNING user_id
            ), issued AS
            (
                INSERT INTO public.refresh_tokens (token_hash, user_id, expires_at)
                SELECT @new_token_hash,
                       consumed.user_id,
                       CURRENT_TIMESTAMP + (@expires_days * INTERVAL '1 day')
                FROM consumed
                RETURNING user_id
            )
            SELECT users.id::text, users.username
            FROM issued
            INNER JOIN public.users AS users ON users.id::text = issued.user_id
            """,
            reader => new TokenUserRecord(reader.GetString(0), reader.GetString(1)),
            [
                new NpgsqlParameter("token_hash", HashRefreshToken(request.RefreshToken)),
                new NpgsqlParameter("new_token_hash", HashRefreshToken(refreshToken)),
                new NpgsqlParameter("expires_days", refreshTokenExpirationDays)
            ],
            cancellationToken);

        var user = users.SingleOrDefault();
        if (user is null)
        {
            return Unauthorized(new { message = "Refresh token 无效或已过期" });
        }

        return Ok(new
        {
            token = CreateToken(user.Id, user.Username),
            expiresInMinutes = GetTokenExpirationMinutes(),
            refreshToken,
            refreshTokenExpiresInDays = refreshTokenExpirationDays
        });
    }

    /// <summary>根据用户资料和 JWT 配置创建签名访问令牌。</summary>
    private string CreateToken(string userId, string username)
    {
        var jwtSection = _configuration.GetSection("Jwt");
        var key = jwtSection["Key"]
            ?? throw new InvalidOperationException("Jwt:Key is not configured.");
        var issuer = jwtSection["Issuer"];
        var audience = jwtSection["Audience"];
        var expires = DateTime.UtcNow.AddMinutes(GetTokenExpirationMinutes());

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId),
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Name, username),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer,
            audience,
            claims,
            expires: expires,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>读取访问令牌有效分钟数，未配置时使用默认值。</summary>
    private int GetTokenExpirationMinutes()
    {
        return _configuration.GetValue<int?>("Jwt:ExpireMinutes") ?? 60;
    }

    /// <summary>读取刷新令牌有效天数，未配置时使用默认值。</summary>
    private int GetRefreshTokenExpirationDays()
    {
        return _configuration.GetValue<int?>("Jwt:RefreshTokenExpireDays") ?? 30;
    }

    /// <summary>生成具有密码学安全随机性的刷新令牌。</summary>
    private static string CreateRefreshToken()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
    }

    /// <summary>使用 SHA-256 计算刷新令牌摘要。</summary>
    private static string HashRefreshToken(string refreshToken)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));
    }

    /// <summary>计算输入密码的 MD5 摘要并与已存摘要比较。</summary>
    private static bool VerifyMd5Password(string password, string passwordHash)
    {
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        var hashBytes = MD5.HashData(passwordBytes);
        var calculatedHash = Convert.ToHexString(hashBytes);

        return string.Equals(
            calculatedHash,
            passwordHash,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>计算密码的 MD5 摘要以供现有用户存储流程使用。</summary>
    private static string CreateMd5PasswordHash(string password)
    {
        return Convert.ToHexString(
            MD5.HashData(Encoding.UTF8.GetBytes(password)));
    }

    private sealed record TokenUserRecord(string Id, string Username);

}
