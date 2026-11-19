using medxbackend.Models;
using medxbackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace medxbackend.Controllers;

[Authorize]
[ApiController]
[ServiceFilter(typeof(SyncApiActionContext))]
[Route("api/[controller]")]
public sealed class CrmController(ICrmSyncApiService service) : ControllerBase
{
    /// <summary>Queries final patients by centreid and CRM queue membership, then upserts them by Lab_No__c.</summary>
    [HttpPost("syncPatientdetailsFinal")]
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
        => service.SyncPatientDetailsFinal(request, cancellationToken);

    /// <summary>Generates centreids and prepares the CRM queue, then returns its records without calling Salesforce.</summary>
    [HttpPost("syncPatientdetailsFinal/preview")]
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
        => service.PreviewPatientDetailsFinal(request, cancellationToken);

    /// <summary>Obtains a Salesforce token using the client credentials grant.</summary>
    [HttpPost("token")]
    [EndpointSummary("Get a Salesforce CRM access token")]
    [Consumes("application/x-www-form-urlencoded")]
    [ProducesResponseType(typeof(CrmTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status504GatewayTimeout)]
    public Task<ActionResult<CrmTokenResponse>> GetToken(
        [FromForm] CrmTokenRequest request,
        CancellationToken cancellationToken)
        => service.GetCrmToken(request, cancellationToken);

    /// <summary>Obtains a Salesforce token using server-configured client credentials.</summary>
    [HttpGet("token")]
    [EndpointSummary("Get a Salesforce CRM access token using configured credentials")]
    [ProducesResponseType(typeof(CrmTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status504GatewayTimeout)]
    public Task<ActionResult<CrmTokenResponse>> GetTokenWithConfiguredCredentials(
        CancellationToken cancellationToken)
        => service.GetCrmTokenWithConfiguredCredentials(cancellationToken);
}
