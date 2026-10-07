using System.ComponentModel.DataAnnotations;

namespace medxbackend.Models;

public sealed class ProductRequest
{
    [Required]
    [StringLength(100)]
    public string ProductCode { get; init; } = string.Empty;

    public string? Name { get; init; }

    public int? Ord { get; init; }
}