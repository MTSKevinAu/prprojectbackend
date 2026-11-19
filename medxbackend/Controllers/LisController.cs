using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using medxbackend.Models;
using medxbackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace medxbackend.Controllers;

[Authorize]
[ApiController]
[ServiceFilter(typeof(SyncApiActionContext))]
[Route("api/[controller]")]
public sealed class LisController(ILisSyncApiService service) : ControllerBase
{
    /// <summary>Retrieves an order directly from LIS using centreid as the order identifier.</summary>
    [HttpGet("order/{centreid}")]
    [EndpointSummary("Get an LIS order by centreid")]
    [ProducesResponseType(typeof(JsonElement), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status504GatewayTimeout)]
    public Task<IActionResult> GetOrder(
        [FromRoute, Required, StringLength(255)] string centreid,
        CancellationToken cancellationToken)
        => service.GetOrder(centreid, cancellationToken);

    [HttpPost("token")]
    [Consumes("application/x-www-form-urlencoded")]
    [ProducesResponseType(typeof(LisTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status504GatewayTimeout)]
    public Task<ActionResult<LisTokenResponse>> GetToken(
        [FromForm] LisTokenRequest request,
        CancellationToken cancellationToken)
        => service.GetToken(request, cancellationToken);

    [HttpGet("token")]
    [ProducesResponseType(typeof(LisTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status504GatewayTimeout)]
    public Task<ActionResult<LisTokenResponse>> GetTokenWithConfiguredCredentials(
        CancellationToken cancellationToken)
        => service.GetTokenWithConfiguredCredentials(cancellationToken);

    [HttpPost("createorder")]
    [EndpointSummary("Synchronize an LIS order by submitting a create request")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(JsonElement), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(JsonElement), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status504GatewayTimeout)]
    public Task<IActionResult> CreateOrder(
        [FromBody] LisCreateOrderFromPatientRequest request,
        CancellationToken cancellationToken)
        => service.CreateOrder(request, cancellationToken);

    /// <summary>Retries a failed or unknown synchronization by creating an order with the original centreid.</summary>
    [HttpPost("createorder/retry")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(JsonElement), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(JsonElement), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status504GatewayTimeout)]
    public Task<IActionResult> RetryCreateOrder(
        [FromBody] LisCreateOrderFromPatientRequest request,
        CancellationToken cancellationToken)
        => service.RetryCreateOrder(request, cancellationToken);

    /// <summary>Updates LIS order details from current local data, identified by centreid.</summary>
    [HttpPost("updateorder")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(JsonElement), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status504GatewayTimeout)]
    public Task<IActionResult> UpdateOrder(
        [FromBody] LisUpdateOrderFromPatientRequest request,
        CancellationToken cancellationToken)
        => service.UpdateOrder(request, cancellationToken);

    /// <summary>Updates LIS patient details using a nested patient object and centreid as LabId.</summary>
    [HttpPost("updateorderpatient")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(JsonElement), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status504GatewayTimeout)]
    public Task<IActionResult> UpdateOrderPatient(
        [FromBody] LisUpdateOrderFromPatientRequest request,
        CancellationToken cancellationToken)
        => service.UpdateOrderPatient(request, cancellationToken);

    [HttpPost("createorders")]
    [EndpointSummary("Synchronize pending LIS orders by submitting create requests")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(LisBatchCreateOrdersResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(LisBatchCreateOrdersResponse), StatusCodes.Status207MultiStatus)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public Task<IActionResult> CreateOrders(
        [FromBody] LisBatchCreateOrdersRequest request,
        CancellationToken cancellationToken)
        => service.CreateOrders(request, cancellationToken);

    [HttpPost("createorders/preview")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(LisBatchPreviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public Task<ActionResult<LisBatchPreviewResponse>> PreviewOrders(
        [FromBody] LisPreviewOrdersRequest request,
        CancellationToken cancellationToken)
        => service.PreviewOrders(request, cancellationToken);
}
