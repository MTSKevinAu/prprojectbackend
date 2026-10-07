using medxbackend.Data;
using medxbackend.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace medxbackend.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public sealed class ProductController : ControllerBase
{
    private readonly DbHelper _dbHelper;

    /// <summary>初始化产品控制器及数据库访问帮助器。</summary>
    public ProductController(DbHelper dbHelper)
    {
        _dbHelper = dbHelper;
    }

    /// <summary>创建产品。</summary>
    [HttpPost("createproduct")]
    public async Task<IActionResult> CreateProduct(
        [FromBody] ProductRequest request,
        CancellationToken cancellationToken)
    {
        var productCode = request.ProductCode.Trim();
        if (productCode.Length == 0)
        {
            return BadRequest(new { message = "productCode 不能为空" });
        }

        try
        {
            var products = await _dbHelper.QueryAsync(
                "INSERT INTO public.products (productcode, name, ord) VALUES (@productCode, @name, @ord) RETURNING id, productcode, name, ord",
                MapProduct,
                [
                    Parameter("productCode", productCode),
                    Parameter("name", request.Name),
                    Parameter("ord", request.Ord)
                ],
                cancellationToken);

            return StatusCode(StatusCodes.Status201Created, products[0]);
        }
        catch (PostgresException exception) when (
            exception.SqlState == PostgresErrorCodes.UniqueViolation &&
            exception.ConstraintName == "products_productcode_key")
        {
            return Conflict(new { message = "productCode 已存在" });
        }
    }

    /// <summary>获取按排序值排列的产品列表。</summary>
    [HttpPost("getproducts")]
    public async Task<IActionResult> GetProducts(CancellationToken cancellationToken)
    {
        var products = await _dbHelper.QueryAsync(
            "SELECT id, productcode, name, ord FROM public.products ORDER BY ord NULLS LAST, id",
            MapProduct,
            cancellationToken: cancellationToken);

        return Ok(products);
    }

    /// <summary>根据产品编号更新产品名称和排序值。</summary>
    [HttpPost("updateproduct")]
    public async Task<IActionResult> UpdateProduct(
        [FromBody] UpdateProductRequest request,
        CancellationToken cancellationToken)
    {
        var products = await _dbHelper.QueryAsync(
            "UPDATE public.products SET name = @name, ord = @ord WHERE id = @id RETURNING id, productcode, name, ord",
            MapProduct,
            [
                Parameter("id", request.Id),
                Parameter("name", request.Name),
                Parameter("ord", request.Ord)
            ],
            cancellationToken);

        if (products.Count == 0)
        {
            return NotFound(new { message = "找不到对应的产品" });
        }

        return Ok(products[0]);
    }

    /// <summary>根据产品编号删除产品。</summary>
    [HttpPost("deleteproduct")]
    public async Task<IActionResult> DeleteProduct(
        [FromBody] DeleteProductRequest request,
        CancellationToken cancellationToken)
    {
        var affectedRows = await _dbHelper.ExecuteNonQueryAsync(
            "DELETE FROM public.products WHERE id = @id",
            [Parameter("id", request.Id)],
            cancellationToken);

        if (affectedRows == 0)
        {
            return NotFound(new { message = "找不到对应的产品" });
        }

        return Ok(new { message = "产品已删除", id = request.Id });
    }

    /// <summary>将数据库读取器当前行映射为产品资料。</summary>
    private static object MapProduct(NpgsqlDataReader reader)
    {
        return new
        {
            id = reader.GetInt32(0),
            productcode = reader.IsDBNull(1) ? null : reader.GetString(1),
            name = reader.IsDBNull(2) ? null : reader.GetString(2),
            ord = reader.IsDBNull(3) ? (int?)null : reader.GetInt32(3)
        };
    }

    /// <summary>建立数据库参数，并将空值转换为数据库空值。</summary>
    private static NpgsqlParameter Parameter(string name, object? value)
    {
        return new NpgsqlParameter(name, value ?? DBNull.Value);
    }
}