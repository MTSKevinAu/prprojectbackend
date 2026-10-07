using System.ComponentModel.DataAnnotations;

namespace medxbackend.Models;

public sealed class RefreshTokenRequest
{
    [Required]
    public string RefreshToken { get; init; } = string.Empty;
}