using System.Net;
using System.Text.Json;
using medxbackend.Models;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace medxbackend.Services;

public sealed class CrmSyncApiService(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<CrmSyncApiService> logger,
    IPatientDetailsExportService exportService,
    ICrmSyncService syncService,
    ICrmPreviewService previewService,
    SyncApiActionContext actionContext,
    SyncFailureDetails failureDetails,
    ISyncHistoryService history) : SyncApiService(actionContext), ICrmSyncApiService
{
    /// <summary>Queries final patients by centreid and CRM queue membership, then upserts them by Lab_No__c.</summary>
    public async Task<ActionResult<CrmSyncPatientDetailsResponse>> SyncPatientDetailsFinal(
        CrmSyncPatientDetailsRequest request,
        CancellationToken cancellationToken, bool updateOnly = false)
    {
        var selection = await LoadSyncRows(request, cancellationToken);
        if (selection.Result is not null)
        {
            return selection.Result;
        }

        var rows = selection.Value!;
        if (history.IsRecording)
        {
            var prepared = syncService.Preview(rows, cancellationToken);
            if (prepared.Results.Count != 1 || !prepared.Results[0].IsValid)
                return Problem(422, "CRM synchronization requires exactly one valid patient payload. No record was submitted.");
            if (!await history.Claim(history.SyncId, JsonSerializer.SerializeToElement(prepared.Results[0].Payload),
                ["pending", "succeeded", "failed", "unknown"], false, cancellationToken))
                return Problem(409, "CRM synchronization is already in progress or its state changed.");
        }
        var tokenResult = await RequestCrmToken(new CrmTokenRequest(), cancellationToken);
        if (tokenResult.Result is not OkObjectResult { Value: CrmTokenResponse token })
        {
            return tokenResult.Result
                ?? throw new InvalidOperationException("CRM token request did not return an action result.");
        }

        return Ok(await syncService.SyncAsync(rows, token, cancellationToken, updateOnly,
            async refreshCancellationToken =>
            {
                var refreshedResult = await RequestCrmToken(new CrmTokenRequest(), refreshCancellationToken);
                return refreshedResult.Result is OkObjectResult { Value: CrmTokenResponse refreshedToken }
                    ? refreshedToken
                    : null;
            }));
    }

    /// <summary>Generates centreids and prepares the CRM queue, then returns its records without calling Salesforce.</summary>
    public async Task<ActionResult<CrmBatchPreviewResponse>> PreviewPatientDetailsFinal(
        CrmPreviewRequest request,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        try
        {
            return Ok(await previewService.PrepareAsync(request, cancellationToken));
        }
        catch (PostgresException exception) when (
            exception.SqlState == PostgresErrorCodes.RaiseException
            && (exception.MessageText.StartsWith(
                    "Cannot generate centreid: dailyno gaps exist for sequence_date ", StringComparison.Ordinal)
                || exception.MessageText.StartsWith(
                    "Cannot generate centreid: duplicate records exist for sequence_date ", StringComparison.Ordinal)))
        {
            logger.LogWarning("CRM centreid generation was blocked: {Reason}", exception.MessageText);
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Cannot generate centreid because the selected data contains gaps or duplicate records.",
                detail: exception.MessageText);
        }
        catch (NpgsqlException exception)
        {
            logger.LogError(exception, "Unable to prepare the CRM synchronization preview.");
            return Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Unable to prepare the CRM synchronization preview.",
                detail: "No records were submitted to Salesforce. Centreid generation and queue preparation may already have completed.");
        }
    }

    private async Task<ActionResult<List<Dictionary<string, object?>>>> LoadSyncRows(
        CrmSyncPatientDetailsRequest request,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        var centreId = request.CentreId?.Trim();
        var targetSystem = request.TargetSystem?.Trim();
        if (string.IsNullOrEmpty(centreId))
        {
            ModelState.AddModelError("centreid", "A non-empty centreid is required.");
        }

        if (targetSystem != "CRM")
        {
            ModelState.AddModelError("targetSystem", "Only the CRM target system is supported.");
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var rows = await exportService.GetRowsByCentreIdAsync(centreId!, targetSystem!, cancellationToken);
        if (rows.Count == 0)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "No final patient records match the centreid and CRM synchronization queue. No records were synchronized.");
        }

        return rows;
    }

    /// <summary>Obtains a Salesforce token using the client credentials grant.</summary>
    public async Task<ActionResult<CrmTokenResponse>> GetCrmToken(
        CrmTokenRequest request,
        CancellationToken cancellationToken)
        => await RequestCrmToken(request, cancellationToken);

    /// <summary>Obtains a Salesforce token using server-configured client credentials.</summary>
    public async Task<ActionResult<CrmTokenResponse>> GetCrmTokenWithConfiguredCredentials(
        CancellationToken cancellationToken)
        => await RequestCrmToken(new CrmTokenRequest(), cancellationToken);

    private async Task<ActionResult<CrmTokenResponse>> RequestCrmToken(
        CrmTokenRequest request, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";

        var clientId = string.IsNullOrWhiteSpace(request.ClientId)
            ? configuration["Crm:client_id"] : request.ClientId;
        var clientSecret = string.IsNullOrWhiteSpace(request.ClientSecret)
            ? configuration["Crm:client_secret"] : request.ClientSecret;
        var grantType = string.IsNullOrWhiteSpace(request.GrantType)
            ? configuration["Crm:grant_type"] : request.GrantType;
        if (string.IsNullOrWhiteSpace(grantType))
        {
            grantType = "client_credentials";
        }

        if (string.IsNullOrWhiteSpace(clientId))
        {
            ModelState.AddModelError("client_id", "Provide client_id in the form or configure Crm:client_id.");
        }

        if (string.IsNullOrWhiteSpace(clientSecret))
        {
            ModelState.AddModelError("client_secret", "Provide client_secret in the form or configure Crm:client_secret.");
        }

        if (grantType != "client_credentials")
        {
            ModelState.AddModelError("grant_type", "Only the client_credentials grant type is supported.");
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        if (!Uri.TryCreate(configuration["Crm:BaseUrl"], UriKind.Absolute, out var baseUri)
            || baseUri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(baseUri.UserInfo)
            || baseUri.AbsolutePath != "/"
            || !string.IsNullOrEmpty(baseUri.Query)
            || !string.IsNullOrEmpty(baseUri.Fragment))
        {
            logger.LogError("Crm:BaseUrl must be an HTTPS origin without a path, credentials, query, or fragment.");
            return Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "CRM base URL is not configured correctly.");
        }

        try
        {
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = grantType,
                ["client_id"] = clientId!,
                ["client_secret"] = clientSecret!
            });
            var client = httpClientFactory.CreateClient("Crm");
            using var response = await client.PostAsync(
                new Uri(baseUri, "services/oauth2/token"), content, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                failureDetails.Capture("CRM", "RequestToken", (int)response.StatusCode,
                    await response.Content.ReadAsStringAsync(cancellationToken), null, clientId, clientSecret);
                logger.LogWarning("CRM token request was rejected with HTTP {StatusCode}.", (int)response.StatusCode);
                var statusCode = response.StatusCode switch
                {
                    HttpStatusCode.BadRequest => StatusCodes.Status400BadRequest,
                    HttpStatusCode.Unauthorized => StatusCodes.Status401Unauthorized,
                    _ => StatusCodes.Status502BadGateway
                };
                return Problem(statusCode: statusCode, title: "CRM rejected the token request.");
            }

            var tokenBody = await response.Content.ReadAsStringAsync(cancellationToken);
            CrmTokenResponse? token;
            try
            {
                token = JsonSerializer.Deserialize<CrmTokenResponse>(tokenBody);
            }
            catch (JsonException)
            {
                failureDetails.Capture("CRM", "RequestToken", (int)response.StatusCode, tokenBody, null, clientId, clientSecret);
                throw;
            }
            if (token is null || string.IsNullOrWhiteSpace(token.AccessToken)
                || !string.Equals(token.TokenType, "Bearer", StringComparison.OrdinalIgnoreCase)
                || !Uri.TryCreate(token.InstanceUrl, UriKind.Absolute, out var instanceUri)
                || instanceUri.Scheme != Uri.UriSchemeHttps
                || !string.IsNullOrEmpty(instanceUri.UserInfo)
                || instanceUri.AbsolutePath != "/"
                || !string.IsNullOrEmpty(instanceUri.Query)
                || !string.IsNullOrEmpty(instanceUri.Fragment))
            {
                logger.LogWarning("CRM returned an invalid token response.");
                failureDetails.Capture("CRM", "RequestToken", (int)response.StatusCode, tokenBody, null, clientId, clientSecret);
                return Problem(
                    statusCode: StatusCodes.Status502BadGateway,
                    title: "CRM returned an invalid token response.");
            }

            return Ok(token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("CRM token request timed out.");
            return Problem(statusCode: StatusCodes.Status504GatewayTimeout, title: "CRM request timed out.");
        }
        catch (HttpRequestException)
        {
            logger.LogWarning("Unable to connect to CRM for a token request.");
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: "Unable to connect to CRM.");
        }
        catch (JsonException)
        {
            logger.LogWarning("CRM returned invalid token JSON.");
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: "CRM returned invalid JSON.");
        }
        catch (NotSupportedException)
        {
            logger.LogWarning("CRM returned an unsupported token response.");
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: "CRM returned an unsupported response.");
        }
    }
}
