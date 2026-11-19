using System.ComponentModel.DataAnnotations;
using medxbackend.Models;
using medxbackend.Services;
using Microsoft.AspNetCore.Mvc;

namespace medxbackend.Controllers;

public sealed partial class SyncController
{
    /// <summary>Gets one rule by its generated identifier.</summary>
    [HttpGet("rules/{id:long}")]
    [EndpointSummary("Get a synchronization rule by id")]
    [ProducesResponseType(typeof(SyncRuleResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public Task<IActionResult> GetRuleById(
        [FromRoute, Range(typeof(long), "1", "9223372036854775807")] long id,
        [FromServices] ISyncRuleService service,
        CancellationToken cancellationToken) => service.GetById(id, cancellationToken);

    /// <summary>Enables or disables a rule by id without changing existing queue records.</summary>
    [HttpPut("rules/{id:long}")]
    [EndpointSummary("Enable or disable a synchronization rule by id")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(SyncRuleResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public Task<IActionResult> UpdateRuleById(
        [FromRoute, Range(typeof(long), "1", "9223372036854775807")] long id,
        [FromBody] UpdateSyncRuleRequest request,
        [FromServices] ISyncRuleService service,
        CancellationToken cancellationToken) => service.UpdateById(id, request, cancellationToken);

    /// <summary>Deletes a rule by id without deleting synchronization queue records.</summary>
    [HttpDelete("rules/{id:long}")]
    [EndpointSummary("Delete a synchronization rule by id")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public Task<IActionResult> DeleteRuleById(
        [FromRoute, Range(typeof(long), "1", "9223372036854775807")] long id,
        [FromServices] ISyncRuleService service,
        CancellationToken cancellationToken) => service.DeleteById(id, cancellationToken);

    /// <summary>Lists all rules, optionally filtered by formType, including disabled rules.</summary>
    [HttpGet("rules")]
    [EndpointSummary("List synchronization routing rules")]
    [ProducesResponseType(typeof(IReadOnlyList<SyncRuleResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public Task<IActionResult> ListRules(
        [FromQuery, StringLength(2), RegularExpression("^[A-Z0-9]{1,2}$")] string? formType,
        [FromServices] ISyncRuleService service,
        CancellationToken cancellationToken) => service.List(formType, cancellationToken);

    /// <summary>Gets a rule by its unique formType and targetSystem pair.</summary>
    [HttpGet("rules/{formType}/{targetSystem}")]
    [EndpointSummary("Get a synchronization routing rule")]
    [ProducesResponseType(typeof(SyncRuleResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public Task<IActionResult> GetRule(
        [FromRoute, Required, StringLength(2), RegularExpression("^[A-Z0-9]{1,2}$")] string formType,
        [FromRoute, Required, RegularExpression("^[A-Z][A-Z0-9_]*$")] string targetSystem,
        [FromServices] ISyncRuleService service,
        CancellationToken cancellationToken) => service.Get(formType, targetSystem, cancellationToken);

    /// <summary>Creates one rule; only LIS and CRM are currently supported synchronization targets.</summary>
    [HttpPost("rules")]
    [EndpointSummary("Create a synchronization routing rule")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(SyncRuleResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public Task<IActionResult> CreateRule(
        [FromBody] CreateSyncRuleRequest request,
        [FromServices] ISyncRuleService service,
        CancellationToken cancellationToken) => service.Create(request, cancellationToken);

    /// <summary>Enables or disables a rule without changing its key or existing queue records.</summary>
    [HttpPut("rules/{formType}/{targetSystem}")]
    [EndpointSummary("Enable or disable a synchronization routing rule")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(SyncRuleResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public Task<IActionResult> UpdateRule(
        [FromRoute, Required, StringLength(2), RegularExpression("^[A-Z0-9]{1,2}$")] string formType,
        [FromRoute, Required, RegularExpression("^[A-Z][A-Z0-9_]*$")] string targetSystem,
        [FromBody] UpdateSyncRuleRequest request,
        [FromServices] ISyncRuleService service,
        CancellationToken cancellationToken) => service.Update(formType, targetSystem, request, cancellationToken);

    /// <summary>Deletes a routing rule without deleting deliveries already stored in the synchronization queue.</summary>
    [HttpDelete("rules/{formType}/{targetSystem}")]
    [EndpointSummary("Delete a synchronization routing rule")]
    [ProducesResponseType(typeof(DeleteSyncRuleResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public Task<IActionResult> DeleteRule(
        [FromRoute, Required, StringLength(2), RegularExpression("^[A-Z0-9]{1,2}$")] string formType,
        [FromRoute, Required, RegularExpression("^[A-Z][A-Z0-9_]*$")] string targetSystem,
        [FromServices] ISyncRuleService service,
        CancellationToken cancellationToken) => service.Delete(formType, targetSystem, cancellationToken);
}
