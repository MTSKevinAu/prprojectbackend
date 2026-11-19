using System.Text.Json.Serialization;

namespace medxbackend.Models;

/// <summary>The token and original expiry metadata returned by LIS.</summary>
public sealed record LisTokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; init; } = string.Empty;

    [JsonPropertyName("token_type")]
    public string TokenType { get; init; } = string.Empty;

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; init; }

    [JsonPropertyName("userName")]
    public string? UserName { get; init; }

    [JsonPropertyName(".issued")]
    public string? Issued { get; init; }

    [JsonPropertyName(".expires")]
    public string? Expires { get; init; }
}