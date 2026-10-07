using System.ComponentModel.DataAnnotations;

namespace medxbackend.Models;

public sealed class DeleteProductRequest
{
    [Range(1, int.MaxValue)]
    public int Id { get; init; }
}