using medxbackend.Data;
using medxbackend.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace medxbackend.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public sealed class CustomerController : ControllerBase
{
    private readonly DbHelper _dbHelper;

    /// <summary>初始化客户控制器及数据库访问帮助器。</summary>
    public CustomerController(DbHelper dbHelper)
    {
        _dbHelper = dbHelper;
    }

    /// <summary>创建客户并返回新建的客户资料。</summary>
    [HttpPost("createcustomer")]
    public async Task<IActionResult> CreateCustomer(
        [FromBody] CustomerRequest request,
        CancellationToken cancellationToken)
    {
        var customers = await _dbHelper.QueryAsync(
            "INSERT INTO public.customers (code, customer, billtocode, ord) VALUES (@code, @customer, @billtocode, COALESCE(@ord, 0)) RETURNING id, code, customer, billtocode, ord",
            MapCustomer,
            [
                Parameter("code", request.Code),
                Parameter("customer", request.Customer),
                Parameter("billtocode", request.BillToCode),
                Parameter("ord", request.Ord)
            ],
            cancellationToken);

        return StatusCode(StatusCodes.Status201Created, customers[0]);
    }

    /// <summary>获取客户列表，并按是否存在上级客户分组。</summary>
    [HttpPost("getcustomers")]
    [HttpGet("getcustomers")]
    public async Task<IActionResult> GetCustomers(CancellationToken cancellationToken)
    {
        var customers = await _dbHelper.QueryAsync(
            "SELECT id, code, customer, billtocode, ord FROM public.customers ORDER BY ord, id",
            MapCustomer,
            cancellationToken: cancellationToken);

        var referringCustomers = customers
            .Where(customer => string.IsNullOrWhiteSpace(customer["billtocode"]?.ToString()))
            .ToList();
        var billToCustomers = customers
            .Where(customer => !string.IsNullOrWhiteSpace(customer["billtocode"]?.ToString()))
            .ToList();

        return Ok(new
        {
            referringcustomers = referringCustomers,
            billtocustomers = billToCustomers
        });
    }

    /// <summary>获取所有客户资料。</summary>
    [HttpGet("getallcustomers")]
    [HttpPost("getallcustomers")]
    public async Task<IActionResult> GetAllCustomers(CancellationToken cancellationToken)
    {
        var customers = await _dbHelper.QueryAsync(
            "SELECT id, code, customer, billtocode, ord FROM public.customers ORDER BY id",
            MapCustomer,
            cancellationToken: cancellationToken);

        return Ok(customers);
    }

    /// <summary>根据客户编号更新客户资料。</summary>
    [HttpPost("updatecustomer")]
    public async Task<IActionResult> UpdateCustomer(
        [FromBody] UpdateCustomerRequest request,
        CancellationToken cancellationToken)
    {
        var customers = await _dbHelper.QueryAsync(
            "UPDATE public.customers SET code = @code, customer = @customer, billtocode = @billtocode, ord = COALESCE(@ord, 0) WHERE id = @id RETURNING id, code, customer, billtocode, ord",
            MapCustomer,
            [
                Parameter("id", request.Id),
                Parameter("code", request.Code),
                Parameter("customer", request.Customer),
                Parameter("billtocode", request.BillToCode),
                Parameter("ord", request.Ord)
            ],
            cancellationToken);

        if (customers.Count == 0)
        {
            return NotFound(new { message = "找不到对应的客户" });
        }

        return Ok(customers[0]);
    }

    /// <summary>根据客户编号删除客户。</summary>
    [HttpPost("deletecustomer")]
    public async Task<IActionResult> DeleteCustomer(
        [FromBody] CustomerIdRequest request,
        CancellationToken cancellationToken)
    {
        var affectedRows = await _dbHelper.ExecuteNonQueryAsync(
            "DELETE FROM public.customers WHERE id = @id",
            [Parameter("id", request.Id)],
            cancellationToken);

        if (affectedRows == 0)
        {
            return NotFound(new { message = "找不到对应的客户" });
        }

        return Ok(new { message = "客户已删除", id = request.Id });
    }

    /// <summary>将数据库读取器当前行转换为客户字典。</summary>
    private static Dictionary<string, object?> MapCustomer(NpgsqlDataReader reader)
    {
        var customer = new Dictionary<string, object?>();
        for (var index = 0; index < reader.FieldCount; index++)
        {
            var value = reader.GetValue(index);
            customer[reader.GetName(index)] = value is DBNull ? null : value;
        }

        return customer;
    }

    /// <summary>建立数据库参数，并将空值转换为数据库空值。</summary>
    private static NpgsqlParameter Parameter(string name, object? value)
    {
        return new NpgsqlParameter(name, value ?? DBNull.Value);
    }
}