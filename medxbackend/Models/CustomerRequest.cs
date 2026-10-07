using System.ComponentModel.DataAnnotations;

namespace medxbackend.Models;

public class CustomerRequest
{
    [StringLength(20)]
    public string? Code { get; init; }

    [StringLength(100)]
    public string? Customer { get; init; }

    [StringLength(20)]
    public string? BillToCode { get; init; }

    public int? Ord { get; init; }
}