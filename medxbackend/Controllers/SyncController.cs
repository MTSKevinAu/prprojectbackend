using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using medxbackend.Models;
using medxbackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace medxbackend.Controllers;

/// <summary>Hosts unified LIS and CRM synchronization APIs using shared services.</summary>
[Authorize]
[ApiController]
[ServiceFilter(typeof(SyncApiActionContext))]
[Route("api/Sync")]
public sealed partial class SyncController(
    ILisSyncApiService lisService,
    ICrmSyncApiService crmService,
    IPatientSyncRoutingService routingService) : ControllerBase
{
    /// <summary>Prepares a date range, then synchronizes selected orders in one shared batch.</summary>
    [HttpPost("orderpatients")]
    [EndpointSummary("Prepare orders in a date range, then synchronize selected orders in one shared batch")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(SyncBatchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(SyncBatchResponse), StatusCodes.Status207MultiStatus)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public Task<IActionResult> SyncOrdersAndPatients(
        [FromBody] SyncBatchRequest request,
        CancellationToken cancellationToken) => routingService.SyncBatch(request, cancellationToken);

    /// <summary>Returns the globally latest completed batch, optionally filtering its target system.</summary>
    [HttpGet("recent")]
    [EndpointSummary("Get history from the globally latest completed synchronization batch")]
    [ProducesResponseType(typeof(RecentSyncResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public Task<IActionResult> GetRecent(
        CancellationToken cancellationToken,
        [FromQuery, Range(1, int.MaxValue)] int? count = null,
        [FromQuery] string? targetSystem = null)
        => routingService.GetRecent(count, targetSystem, cancellationToken);

    /// <summary>Counts all patientdetails_sync rows, including all dates and target systems.</summary>
    [HttpGet("statistics")]
    [EndpointSummary("Get total, pending, succeeded and failed synchronization record counts")]
    [ProducesResponseType(typeof(SyncStatisticsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public Task<IActionResult> GetStatistics(CancellationToken cancellationToken)
        => routingService.GetStatistics(cancellationToken);

    /// <summary>Prepares centreids and previews orders in an inclusive date range without uploading them.</summary>
    [HttpPost("preview")]
    [EndpointSummary("Generate centreids and preview synchronization queue records in a date range without uploading")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(SyncOrderPreviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public Task<IActionResult> Preview(
        [FromBody] SyncOrderPreviewRequest request,
        CancellationToken cancellationToken) => routingService.Preview(request, cancellationToken);

    /// <summary>Synchronizes an order and its patient data to enabled targets configured for its formtype.</summary>
    [HttpPost("orderpatient")]
    [HttpPost("patient")]
    [EndpointSummary("Synchronize an order and patient using enabled formtype routing rules")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(SyncPatientResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(SyncPatientResponse), StatusCodes.Status207MultiStatus)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public Task<IActionResult> SyncOrderAndPatient(
        [FromBody] SyncPatientRequest request,
        CancellationToken cancellationToken) => routingService.Sync(request, cancellationToken);

    /// <summary>Retrieves an order directly from LIS using centreid as the order identifier.</summary>
    [HttpGet("lis/order/{centreid}")]
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
        => lisService.GetOrder(centreid, cancellationToken);

    [HttpPost("lis/token")]
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
        => lisService.GetToken(request, cancellationToken);

    [HttpGet("lis/token")]
    [ProducesResponseType(typeof(LisTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status504GatewayTimeout)]
    public Task<ActionResult<LisTokenResponse>> GetTokenWithConfiguredCredentials(
        CancellationToken cancellationToken)
        => lisService.GetTokenWithConfiguredCredentials(cancellationToken);

    [HttpPost("lis/createorder")]
    [EndpointSummary("Create an LIS order when the patient's formtype has an enabled LIS rule")]
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
        [FromBody] SyncPatientRequest request,
        CancellationToken cancellationToken)
        => routingService.CreateLisOrder(request, cancellationToken);

    /// <summary>Retries a failed or unknown synchronization by creating an order with the original centreid.</summary>
    [HttpPost("lis/createorder/retry")]
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
        => lisService.RetryCreateOrder(request, cancellationToken);

    /// <summary>Updates LIS order details from current local data, identified by centreid.</summary>
    [HttpPost("lis/updateorder")]
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
        => lisService.UpdateOrder(request, cancellationToken);

    /// <summary>Updates LIS patient details using a nested patient object and centreid as LabId.</summary>
    [HttpPost("lis/updateorderpatient")]
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
        => lisService.UpdateOrderPatient(request, cancellationToken);

    [HttpPost("lis/createorders")]
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
        => lisService.CreateOrders(request, cancellationToken);

    [HttpPost("lis/createorders/preview")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(LisBatchPreviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public Task<ActionResult<LisBatchPreviewResponse>> PreviewOrders(
        [FromBody] LisPreviewOrdersRequest request,
        CancellationToken cancellationToken)
        => lisService.PreviewOrders(request, cancellationToken);

    /// <summary>Queries final patients by centreid and CRM queue membership, then upserts them by Lab_No__c.</summary>
    [HttpPost("crm/syncPatientdetailsFinal")]
    [EndpointSummary("Sync a selected centreid from the CRM queue to Salesforce Lab_Booking__c")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(CrmSyncPatientDetailsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status504GatewayTimeout)]
    public Task<ActionResult<CrmSyncPatientDetailsResponse>> SyncPatientDetailsFinal(
        [FromBody] CrmSyncPatientDetailsRequest request,
        CancellationToken cancellationToken)
        => crmService.SyncPatientDetailsFinal(request, cancellationToken);

    /// <summary>Generates centreids and prepares the CRM queue, then returns its records without calling Salesforce.</summary>
    [HttpPost("crm/syncPatientdetailsFinal/preview")]
    [EndpointSummary("Prepare centreids and preview CRM synchronization queue records")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(CrmBatchPreviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public Task<ActionResult<CrmBatchPreviewResponse>> PreviewPatientDetailsFinal(
        [FromBody] CrmPreviewRequest request,
        CancellationToken cancellationToken)
        => crmService.PreviewPatientDetailsFinal(request, cancellationToken);

    /// <summary>Obtains a Salesforce token using the client credentials grant.</summary>
    [HttpPost("crm/token")]
    [EndpointSummary("Get a Salesforce CRM access token")]
    [Consumes("application/x-www-form-urlencoded")]
    [ProducesResponseType(typeof(CrmTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status504GatewayTimeout)]
    public Task<ActionResult<CrmTokenResponse>> GetCrmToken(
        [FromForm] CrmTokenRequest request,
        CancellationToken cancellationToken)
        => crmService.GetCrmToken(request, cancellationToken);

    /// <summary>Obtains a Salesforce token using server-configured client credentials.</summary>
    [HttpGet("crm/token")]
    [EndpointSummary("Get a Salesforce CRM access token using configured credentials")]
    [ProducesResponseType(typeof(CrmTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status504GatewayTimeout)]
    public Task<ActionResult<CrmTokenResponse>> GetCrmTokenWithConfiguredCredentials(
        CancellationToken cancellationToken)
        => crmService.GetCrmTokenWithConfiguredCredentials(cancellationToken);
}
