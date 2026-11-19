using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace medxbackend.Models;

/// <summary>Optional Salesforce client credentials overriding server configuration.</summary>
public sealed record CrmTokenRequest
{
    [FromForm(Name = "client_id")]
    public string? ClientId { get; init; }

    [FromForm(Name = "client_secret")]
    public string? ClientSecret { get; init; }

    [RegularExpression("^client_credentials$")]
    [FromForm(Name = "grant_type")]
    public string? GrantType { get; init; }
}
