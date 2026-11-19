using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace medxbackend.Models;

/// <summary>Optional LIS credentials that override configured defaults.</summary>
public sealed record LisTokenRequest
{
    [FromForm(Name = "username")]
    public string? Username { get; init; }

    [FromForm(Name = "password")]
    public string? Password { get; init; }

    [RegularExpression("^password$")]
    [FromForm(Name = "grant_type")]
    public string? GrantType { get; init; }
}