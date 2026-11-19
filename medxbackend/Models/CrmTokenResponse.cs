using System.Text.Json.Serialization;

namespace medxbackend.Models;

/// <summary>Salesforce access token and associated instance metadata.</summary>
public sealed record CrmTokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; init; } = string.Empty;

    [JsonPropertyName("instance_url")]
    public string InstanceUrl { get; init; } = string.Empty;

    [JsonPropertyName("token_type")]
    public string TokenType { get; init; } = string.Empty;

    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("issued_at")]
    public string? IssuedAt { get; init; }

    [JsonPropertyName("signature")]
    public string? Signature { get; init; }

    [JsonPropertyName("scope")]
    public string? Scope { get; init; }
}
