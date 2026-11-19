using System.Text.Json.Serialization;

namespace medxbackend.Models;

/// <summary>Reports whether a local final-patient update was propagated to its already-synchronized LIS order.</summary>
public sealed record LisFinalPatientUpdateResult(
    [property: JsonPropertyName("attempted")] bool Attempted,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("stage")] string? Stage,
    [property: JsonPropertyName("httpStatus")] int? HttpStatus,
    [property: JsonPropertyName("message")] string? Message);
