using System.ComponentModel.DataAnnotations;

namespace medxbackend.Models;

public sealed class UpdateProductRequest
{
    [Range(1, int.MaxValue)]
    public int Id { get; init; }

    public string? Name { get; init; }

    public int? Ord { get; init; }
}