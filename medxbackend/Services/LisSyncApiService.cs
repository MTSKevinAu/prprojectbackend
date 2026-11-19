using System.Net;
using System.Net.Http.Headers;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using medxbackend.Models;
using medxbackend.Data;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace medxbackend.Services;

public sealed class LisSyncApiService(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    DbHelper dbHelper,
    ILogger<LisSyncApiService> logger,
    SyncApiActionContext actionContext,
    SyncFailureDetails failureDetails,
    ISyncHistoryService history,
    LisAccessTokenCache tokenCache) : SyncApiService(actionContext), ILisSyncApiService
{
    private static readonly JsonSerializerOptions OrderJsonOptions = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Retrieves an order directly from LIS using centreid as the order identifier.</summary>
    public async Task<IActionResult> GetOrder(
        string centreid,
        CancellationToken cancellationToken)
        => await GetOrderCore(centreid, cancellationToken);

    private async Task<IActionResult> GetOrderCore(
        string centreid, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        failureDetails.Clear("LIS");
        var orderId = centreid?.Trim();
        if (string.IsNullOrEmpty(orderId) || orderId.Length > 255 || orderId is "." or "..")
        {
            ModelState.AddModelError("centreid", "Provide a non-empty centreid of at most 255 characters, not a path segment marker.");
            return ValidationProblem(ModelState);
        }

        var baseUri = GetLisBaseUri();
        if (baseUri is null)
        {
            return Problem(statusCode: StatusCodes.Status500InternalServerError,
                title: "LIS base URL is not configured correctly.");
        }

        var (token, tokenError) = await GetConfiguredToken(cancellationToken);
        if (token is null)
        {
            return tokenError!;
        }

        var result = await SendOrderLookup(orderId, baseUri, token, cancellationToken);
        if (!IsUnauthorized(result))
        {
            return result;
        }

        tokenCache.Invalidate(token.AccessToken);
        failureDetails.Clear("LIS");
        var (refreshedToken, refreshError) = await GetConfiguredToken(cancellationToken);
        return refreshedToken is null
            ? refreshError!
            : await SendOrderLookup(orderId, baseUri, refreshedToken, cancellationToken);
    }

    private async Task<IActionResult> SendOrderLookup(
        string orderId, Uri baseUri, LisTokenResponse token, CancellationToken cancellationToken)
    {
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Get,
                new Uri($"{baseUri.AbsoluteUri.TrimEnd('/')}/api/Order/{Uri.EscapeDataString(orderId)}"));
            message.Headers.Authorization = new AuthenticationHeaderValue(token.TokenType, token.AccessToken);
            using var response = await httpClientFactory.CreateClient("Lis").SendAsync(message, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("LIS order lookup failed with HTTP {StatusCode}.", (int)response.StatusCode);
                var status = response.StatusCode switch
                {
                    HttpStatusCode.BadRequest => StatusCodes.Status400BadRequest,
                    HttpStatusCode.Unauthorized => StatusCodes.Status401Unauthorized,
                    HttpStatusCode.Forbidden => StatusCodes.Status403Forbidden,
                    HttpStatusCode.NotFound => StatusCodes.Status404NotFound,
                    _ => StatusCodes.Status502BadGateway
                };
                return Problem(statusCode: status, title: "LIS rejected the order lookup request.");
            }

            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                return NoContent();
            }

            var result = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            return StatusCode((int)response.StatusCode, result);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("LIS order lookup timed out.");
            return Problem(statusCode: StatusCodes.Status504GatewayTimeout, title: "LIS order lookup timed out.");
        }
        catch (HttpRequestException)
        {
            logger.LogWarning("LIS order lookup connection failed.");
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: "Unable to connect to LIS.");
        }
        catch (JsonException)
        {
            logger.LogWarning("LIS order lookup returned invalid JSON.");
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: "LIS returned invalid JSON.");
        }
        catch (NotSupportedException)
        {
            logger.LogWarning("LIS order lookup returned an unsupported response.");
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: "LIS returned an unsupported response.");
        }
    }

    public async Task<ActionResult<LisTokenResponse>> GetToken(
        LisTokenRequest request,
        CancellationToken cancellationToken)
    {
        return await RequestToken(request, cancellationToken);
    }

    public async Task<ActionResult<LisTokenResponse>> GetTokenWithConfiguredCredentials(
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";

        ActionResult<LisTokenResponse>? tokenFailure = null;
        var token = await tokenCache.GetOrCreateAsync(async tokenCancellationToken =>
        {
            var result = await RequestToken(new LisTokenRequest(), tokenCancellationToken);
            if (result.Result is OkObjectResult { Value: LisTokenResponse acquiredToken })
            {
                return acquiredToken;
            }

            tokenFailure = result;
            return null;
        }, cancellationToken);

        return token is not null
            ? Ok(token)
            : tokenFailure ?? Problem(statusCode: StatusCodes.Status502BadGateway,
                title: "Unable to obtain a LIS token.");
    }

    private async Task<(LisTokenResponse? Token, IActionResult? Error)> GetConfiguredToken(
        CancellationToken cancellationToken)
    {
        var result = await GetTokenWithConfiguredCredentials(cancellationToken);
        if (result.Result is OkObjectResult { Value: LisTokenResponse token })
        {
            return (token, null);
        }

        var error = result.Result ?? Problem(
            statusCode: StatusCodes.Status502BadGateway,
            title: "Unable to obtain a LIS token.");
        if (failureDetails.GetStage("LIS") != "RequestToken")
        {
            failureDetails.Capture("LIS", "RequestToken", GetActionStatus(error),
                GetFailureReason(error) ?? "Unable to obtain a LIS token.");
        }

        return (null, error);
    }

    private static int? GetActionStatus(IActionResult action) => action switch
    {
        ObjectResult result => result.StatusCode,
        ContentResult result => result.StatusCode,
        StatusCodeResult result => result.StatusCode,
        _ => null
    };

    private static bool IsUnauthorized(IActionResult action) =>
        GetActionStatus(action) == StatusCodes.Status401Unauthorized;

    private async Task<ActionResult<LisTokenResponse>> RequestToken(
        LisTokenRequest request,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";

        var username = string.IsNullOrWhiteSpace(request.Username)
            ? configuration["Lis:username"] : request.Username;
        var password = string.IsNullOrWhiteSpace(request.Password)
            ? configuration["Lis:password"] : request.Password;
        var grantType = string.IsNullOrWhiteSpace(request.GrantType)
            ? configuration["Lis:grant_type"] : request.GrantType;
        if (string.IsNullOrWhiteSpace(grantType))
        {
            grantType = "password";
        }

        if (string.IsNullOrWhiteSpace(username))
        {
            ModelState.AddModelError("username", "Provide username in the form or configure Lis:username.");
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            ModelState.AddModelError("password", "Provide password in the form or configure Lis:password.");
        }

        if (grantType != "password")
        {
            ModelState.AddModelError("grant_type", "Only the password grant type is supported.");
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var baseUri = GetLisBaseUri();
        if (baseUri is null)
        {
            return Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "LIS base URL is not configured correctly.");
        }

        var tokenUri = new Uri($"{baseUri.AbsoluteUri.TrimEnd('/')}/api/token");

        try
        {
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["username"] = username!,
                ["password"] = password!,
                ["grant_type"] = grantType
            });
            var client = httpClientFactory.CreateClient("Lis");
            using var response = await client.PostAsync(tokenUri, content, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                failureDetails.Capture("LIS", "RequestToken", (int)response.StatusCode,
                    await response.Content.ReadAsStringAsync(cancellationToken), null, username, password);
                var statusCode = response.StatusCode switch
                {
                    HttpStatusCode.BadRequest => StatusCodes.Status400BadRequest,
                    HttpStatusCode.Unauthorized => StatusCodes.Status401Unauthorized,
                    _ => StatusCodes.Status502BadGateway
                };
                return await LisErrorResponse(
                    response, statusCode, "LIS rejected the token request.", cancellationToken);
            }

            var tokenBody = await response.Content.ReadAsStringAsync(cancellationToken);
            LisTokenResponse? token;
            try
            {
                token = JsonSerializer.Deserialize<LisTokenResponse>(tokenBody);
            }
            catch (JsonException)
            {
                failureDetails.Capture("LIS", "RequestToken", (int)response.StatusCode, tokenBody, null, username, password);
                throw;
            }
            if (token is null || string.IsNullOrWhiteSpace(token.AccessToken)
                || string.IsNullOrWhiteSpace(token.TokenType) || token.ExpiresIn <= 0
                || !AuthenticationHeaderValue.TryParse($"{token.TokenType} {token.AccessToken}", out _))
            {
                failureDetails.Capture("LIS", "RequestToken", (int)response.StatusCode, tokenBody, null, username, password);
                return Problem(
                    statusCode: StatusCodes.Status502BadGateway,
                    title: "LIS returned an invalid token response.");
            }

            return Ok(token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Problem(statusCode: StatusCodes.Status504GatewayTimeout, title: "LIS request timed out.");
        }
        catch (HttpRequestException)
        {
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: "Unable to connect to LIS.");
        }
        catch (JsonException)
        {
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: "LIS returned invalid JSON.");
        }
        catch (NotSupportedException)
        {
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: "LIS returned an unsupported response.");
        }
    }

    public async Task<IActionResult> CreateOrder(
        LisCreateOrderFromPatientRequest request,
        CancellationToken cancellationToken)
        => await CreateOrderFromPatient(request, false, cancellationToken);

    /// <summary>Retries a failed or unknown synchronization by creating an order with the original centreid.</summary>
    public async Task<IActionResult> RetryCreateOrder(
        LisCreateOrderFromPatientRequest request,
        CancellationToken cancellationToken)
        => await CreateOrderFromPatient(request, true, cancellationToken);

    private async Task<IActionResult> CreateOrderFromPatient(
        LisCreateOrderFromPatientRequest request,
        bool isRetry,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var targetSystem = request.TargetSystem.Trim();
        if (!string.Equals(targetSystem, "LIS", StringComparison.Ordinal))
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Only the LIS target system is currently supported.");
        }

        List<BatchPatient> matches;
        try
        {
            matches = await LoadPatientMatches(request.CentreId.Trim(), targetSystem, cancellationToken);
        }
        catch (NpgsqlException exception)
        {
            logger.LogError(exception, "Unable to load the LIS synchronization record.");
            return Problem(statusCode: StatusCodes.Status500InternalServerError,
                title: "Unable to load the LIS synchronization record. No order was submitted.");
        }

        if (matches.Count == 0)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound,
                title: "No patient synchronization record matched CentreId and TargetSystem.");
        }

        if (matches.Count != 1)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "CentreId and TargetSystem must identify a unique patient synchronization record.",
                detail: "No order was submitted. Resolve the ambiguous patient selection before retrying.");
        }

        var patient = matches[0];
        var order = CreatePatientOrder(patient);
        if (!TryValidateModel(order))
        {
            return ValidationProblem(ModelState);
        }

        return await SynchronizeOrder(
            order, patient, cancellationToken,
            isRetry);
    }

    private Task<List<BatchPatient>> LoadPatientMatches(
        string centreId, string targetSystem, CancellationToken cancellationToken) =>
        dbHelper.QueryAsync(
                """
                SELECT patient.*, sync.id AS sync_id, sync.sync_status,
                       sync.synced_at, sync.remote_record_id, users.username AS createdby
                FROM public.patientdetails_sync AS sync
                INNER JOIN public.patientdetails_final AS patient
                    ON patient.centreid = sync.centreid AND patient.formtype = sync.formtype
                LEFT JOIN public.users AS users ON users.id = patient.userid
                WHERE sync.target_system = @targetSystem AND sync.centreid = @centreId
                """,
                MapBatchPatient,
                [
                    new NpgsqlParameter("targetSystem", targetSystem),
                    new NpgsqlParameter("centreId", centreId)
                ],
                cancellationToken);

    /// <summary>Updates LIS order details from current local data, identified by centreid.</summary>
    public Task<IActionResult> UpdateOrder(
        LisUpdateOrderFromPatientRequest request,
        CancellationToken cancellationToken) =>
        UpdateOrderFromPatient(request, false, cancellationToken);

    /// <summary>Updates LIS patient details using a nested patient object and centreid as LabId.</summary>
    public Task<IActionResult> UpdateOrderPatient(
        LisUpdateOrderFromPatientRequest request,
        CancellationToken cancellationToken) =>
        UpdateOrderFromPatient(request, true, cancellationToken);

    private async Task<IActionResult> UpdateOrderFromPatient(
        LisUpdateOrderFromPatientRequest request, bool patientOnly, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        failureDetails.Clear("LIS");

        var baseUri = GetLisBaseUri();
        if (baseUri is null)
        {
            return Problem(statusCode: StatusCodes.Status500InternalServerError,
                title: "LIS base URL is not configured correctly.");
        }

        List<BatchPatient> matches;
        try
        {
            matches = await LoadPatientMatches(request.CentreId.Trim(), "LIS", cancellationToken);
        }
        catch (NpgsqlException exception)
        {
            logger.LogError(exception, "Unable to load the patient for LIS update.");
            return Problem(statusCode: StatusCodes.Status500InternalServerError,
                title: "Unable to load the patient. No LIS update was submitted.");
        }

        if (matches.Count == 0)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound,
                title: "No patient with an LIS synchronization record matched CentreId.");
        }

        if (matches.Count != 1)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "CentreId must identify a unique LIS patient synchronization record. No update was submitted.");
        }

        var payload = CreateUpdatePayload(matches[0], patientOnly);
        if (!ValidateUpdatePayload(payload))
        {
            return ValidationProblem(ModelState);
        }

        var operation = patientOnly ? "UpdateOrderPatient" : "UpdateOrder";
        return await SendUpdateWithConfiguredToken(payload, operation, baseUri, cancellationToken);
    }

    private object CreateUpdatePayload(BatchPatient patient, bool patientOnly)
    {
        if (patientOnly)
        {
            var patientData = patient.Patient;
            return new LisUpdateOrderPatientRequest
            {
                LabId = patient.CentreId,
                ModifiedBy = patient.CreatedBy,
                Patient = new LisUpdateOrderPatientDetails
                {
                    HKID = patientData.HKID,
                    PatientNameEng = patientData.PatientNameEng,
                    PatientNameChi = patientData.PatientNameChi,
                    DOB = patient.DateOfBirth,
                    Sex = patientData.Sex,
                    Age = patientData.Age
                }
            };
        }

        return new LisUpdateOrderRequest
        {
            LabId = patient.CentreId,
            ModifiedBy = patient.CreatedBy,
            SiteCode = configuration["Lis:SiteCode"]?.Trim() ?? "LAB",
            ReferringDoctorCode = patient.ReferringDoctorCode ?? string.Empty,
            BillingDoctorCode = patient.BillingDoctorCode ?? string.Empty
        };
    }

    private bool ValidateUpdatePayload(object payload)
    {
        if (payload is LisUpdateOrderPatientRequest patient
            && string.IsNullOrWhiteSpace(patient.Patient.PatientNameEng)
            && string.IsNullOrWhiteSpace(patient.Patient.PatientNameChi))
        {
            ModelState.AddModelError("PatientNameEng", "An English or Chinese patient name is required.");
        }

        return TryValidateModel(payload);
    }

    private async Task<IActionResult> SendUpdate(
        object payload, string operation, Uri baseUri,
        AuthenticationHeaderValue authorization, CancellationToken cancellationToken,
        Action<int>? receivedStatus = null)
    {
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Patch,
                new Uri($"{baseUri.AbsoluteUri.TrimEnd('/')}/api/{operation}/"));
            message.Headers.Authorization = authorization;
            message.Content = JsonContent.Create(payload, payload.GetType(), options: OrderJsonOptions);
            using var response = await httpClientFactory.CreateClient("Lis").SendAsync(message, cancellationToken);
            receivedStatus?.Invoke((int)response.StatusCode);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("LIS {Operation} rejected the update with HTTP {StatusCode}.", operation, (int)response.StatusCode);
                var status = response.StatusCode switch
                {
                    HttpStatusCode.BadRequest => StatusCodes.Status400BadRequest,
                    HttpStatusCode.Unauthorized => StatusCodes.Status401Unauthorized,
                    HttpStatusCode.Forbidden => StatusCodes.Status403Forbidden,
                    HttpStatusCode.NotFound => StatusCodes.Status404NotFound,
                    HttpStatusCode.Conflict => StatusCodes.Status409Conflict,
                    HttpStatusCode.UnprocessableEntity => StatusCodes.Status422UnprocessableEntity,
                    _ => StatusCodes.Status502BadGateway
                };
                return Problem(statusCode: status, title: $"LIS rejected the {operation} request.");
            }

            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                return NoContent();
            }

            var result = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            return StatusCode((int)response.StatusCode, result);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("LIS {Operation} timed out.", operation);
            return Problem(statusCode: StatusCodes.Status504GatewayTimeout,
                title: "LIS update timed out. Verify the order in LIS before retrying.");
        }
        catch (HttpRequestException)
        {
            logger.LogWarning("LIS {Operation} connection failed.", operation);
            return Problem(statusCode: StatusCodes.Status502BadGateway,
                title: "LIS connection failed. Verify the order in LIS before retrying.");
        }
        catch (JsonException)
        {
            logger.LogWarning("LIS {Operation} returned invalid JSON.", operation);
            return Problem(statusCode: StatusCodes.Status502BadGateway,
                title: "LIS returned invalid JSON. Verify the order in LIS before retrying.");
        }
        catch (NotSupportedException)
        {
            logger.LogWarning("LIS {Operation} returned an unsupported response.", operation);
            return Problem(statusCode: StatusCodes.Status502BadGateway,
                title: "LIS returned an unsupported response. Verify the order in LIS before retrying.");
        }
    }

    private async Task<IActionResult> SendUpdateWithConfiguredToken(
        object payload, string operation, Uri baseUri, CancellationToken cancellationToken)
    {
        var (token, tokenError) = await GetConfiguredToken(cancellationToken);
        if (token is null)
        {
            return tokenError!;
        }

        var result = await SendUpdate(payload, operation, baseUri,
            new AuthenticationHeaderValue(token.TokenType, token.AccessToken), cancellationToken);
        if (!IsUnauthorized(result))
        {
            return result;
        }

        tokenCache.Invalidate(token.AccessToken);
        failureDetails.Clear("LIS");
        var (refreshedToken, refreshError) = await GetConfiguredToken(cancellationToken);
        return refreshedToken is null
            ? refreshError!
            : await SendUpdate(payload, operation, baseUri,
                new AuthenticationHeaderValue(refreshedToken.TokenType, refreshedToken.AccessToken), cancellationToken);
    }

    public async Task<IActionResult> CreateOrders(
        LisBatchCreateOrdersRequest request,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (GetLisBaseUri() is null)
        {
            return Problem(statusCode: StatusCodes.Status500InternalServerError,
                title: "LIS base URL is not configured correctly.");
        }

        List<BatchPatient> patients;
        try
        {
            patients = await PrepareBatchPatients(
                request.SequenceDate, request.FormTypes, request.Status, cancellationToken);
        }
        catch (PostgresException exception) when (IsCentreIdConflict(exception))
        {
            return CentreIdConflict(exception);
        }
        catch (NpgsqlException exception)
        {
            logger.LogError(exception, "Unable to load patients for LIS batch order creation.");
            return Problem(statusCode: StatusCodes.Status500InternalServerError,
                title: "Unable to load patients for LIS batch order creation. No orders were submitted.");
        }

        return await ProcessBatchOrders(patients, cancellationToken);
    }

    public async Task<ActionResult<LisBatchPreviewResponse>> PreviewOrders(
        LisPreviewOrdersRequest request,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            await GenerateCentreIds(request.SequenceDate, request.FormTypes, request.Status, cancellationToken);
            var records = await dbHelper.QueryAsync(
                """
                SELECT id, target_system, centreid, formtype, sequence_date,
                       sync_status, attempt_count, last_attempt_at, synced_at,
                       remote_record_id, last_http_status, last_error, created_at
                FROM public.patientdetails_sync
                WHERE sequence_date = COALESCE(@sequenceDate::date, CURRENT_DATE)
                  AND formtype = ANY(@formTypes)
                ORDER BY id
                """,
                reader => new LisSyncRecordResponse(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetFieldValue<DateOnly>(4),
                    reader.GetString(5),
                    reader.GetInt32(6),
                    reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7),
                    reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8),
                    reader.IsDBNull(9) ? null : reader.GetString(9),
                    reader.IsDBNull(10) ? null : reader.GetInt32(10),
                    reader.IsDBNull(11) ? null : reader.GetString(11),
                    reader.GetFieldValue<DateTimeOffset>(12)),
                [
                    new NpgsqlParameter("sequenceDate", NpgsqlTypes.NpgsqlDbType.Date)
                    {
                        Value = (object?)request.SequenceDate ?? DBNull.Value
                    },
                    new NpgsqlParameter("formTypes", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Text)
                    {
                        Value = request.FormTypes.Select(formType => formType.Trim()).Distinct(StringComparer.Ordinal).ToArray()
                    }
                ],
                cancellationToken: cancellationToken);
            return Ok(new LisBatchPreviewResponse(records.Count, records));
        }
        catch (PostgresException exception) when (IsCentreIdConflict(exception))
        {
            return CentreIdConflict(exception);
        }
        catch (NpgsqlException exception)
        {
            logger.LogError(exception, "Unable to prepare the LIS synchronization preview.");
            return Problem(statusCode: StatusCodes.Status500InternalServerError,
                title: "Unable to prepare the LIS synchronization preview.",
                detail: "No orders were submitted. Centreid generation and queue preparation may already have completed.");
        }

    }

    private ObjectResult CentreIdConflict(PostgresException exception)
    {
        logger.LogWarning("LIS centreid generation was blocked: {Reason}", exception.MessageText);
        return Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Cannot generate centreid because the selected data contains gaps or duplicate records.",
            detail: exception.MessageText);
    }

    private async Task GenerateCentreIds(
        DateOnly? sequenceDate,
        string[] formTypes,
        string status,
        CancellationToken cancellationToken)
    {
        await dbHelper.ExecuteNonQueryAsync(
                """
                SELECT * FROM public.update_patientdetails_final_centreid(
                    p_target_system => 'LIS',
                    p_sequence_date => COALESCE(@sequenceDate::date, CURRENT_DATE),
                    p_formtypes => @formTypes,
                    p_statuses => ARRAY[@status]::text[],
                    p_source_table => 'patientdetails_final')
                """,
                [
                    new NpgsqlParameter("sequenceDate", NpgsqlTypes.NpgsqlDbType.Date)
                    {
                        Value = (object?)sequenceDate ?? DBNull.Value
                    },
                    new NpgsqlParameter("formTypes", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Text)
                    {
                        Value = formTypes.Select(formType => formType.Trim()).Distinct(StringComparer.Ordinal).ToArray()
                    },
                    new NpgsqlParameter("status", status.Trim())
                ],
                cancellationToken);
    }

    private async Task<List<BatchPatient>> PrepareBatchPatients(
        DateOnly? sequenceDate,
        string[] formTypes,
        string status,
        CancellationToken cancellationToken)
    {
        await GenerateCentreIds(sequenceDate, formTypes, status, cancellationToken);

        return await dbHelper.QueryAsync(
                """
                SELECT patient.*, 'pending'::text AS sync_status,
                       NULL::timestamptz AS synced_at, NULL::text AS remote_record_id,
                       users.username AS createdby
                FROM public.get_patientdetails_final_pending_sync(
                    COALESCE(@sequenceDate::date, CURRENT_DATE), 'LIS') AS patient
                LEFT JOIN public.users AS users ON users.id = patient.userid
                WHERE patient.formtype = ANY(@formTypes) AND patient.status = @status
                ORDER BY patient.sync_id
                """,
                MapBatchPatient,
                [
                    new NpgsqlParameter("sequenceDate", NpgsqlTypes.NpgsqlDbType.Date)
                    {
                        Value = (object?)sequenceDate ?? DBNull.Value
                    },
                    new NpgsqlParameter("formTypes", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Text)
                    {
                        Value = formTypes.Select(formType => formType.Trim()).Distinct(StringComparer.Ordinal).ToArray()
                    },
                    new NpgsqlParameter("status", status.Trim())
                ],
                cancellationToken);
    }

    private static BatchPatient MapBatchPatient(NpgsqlDataReader reader) => new(
                    reader.GetInt64(reader.GetOrdinal("sync_id")),
                    reader.GetString(reader.GetOrdinal("centreid")),
                    reader.IsDBNull(reader.GetOrdinal("dailyno")) ? string.Empty : reader.GetString(reader.GetOrdinal("dailyno")),
                    ReadNullableString(reader, "createdby")?.Trim() ?? string.Empty,
                    new LisOrderPatientRequest
                    {
                        HKID = ReadNullableString(reader, "hkidno"),
                        PatientNameEng = ReadNullableString(reader, "patientnameen"),
                        PatientNameChi = ReadNullableString(reader, "patientnamechi"),
                        Sex = ReadNullableString(reader, "gender")?.Trim().ToUpperInvariant() ?? string.Empty,
                        AgeType = "AGE",
                        AgeUnit = "Y",
                        DOB = reader.IsDBNull(reader.GetOrdinal("dateofbirth"))
                            ? null
                            : new DateTimeOffset(DateTime.SpecifyKind(
                                reader.GetDateTime(reader.GetOrdinal("dateofbirth")), DateTimeKind.Utc)),
                        Age = reader.IsDBNull(reader.GetOrdinal("age")) ? null : reader.GetInt32(reader.GetOrdinal("age"))
                    },
                    reader.GetString(reader.GetOrdinal("sync_status")),
                    reader.IsDBNull(reader.GetOrdinal("synced_at"))
                        ? null : reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("synced_at")),
                    ReadNullableString(reader, "remote_record_id"),
                    ReadNullableString(reader, "refcustordr")?.Trim(),
                    ReadNullableString(reader, "billtocustordr")?.Trim(),
                    reader.IsDBNull(reader.GetOrdinal("dateofbirth"))
                        ? null
                        : new DateTimeOffset(DateTime.SpecifyKind(
                            reader.GetDateTime(reader.GetOrdinal("dateofbirth")), DateTimeKind.Utc)),
                    reader.IsDBNull(reader.GetOrdinal("productcode"))
                        ? []
                        : reader.GetFieldValue<string[]>(reader.GetOrdinal("productcode")));

    private LisCreateOrderRequest CreatePatientOrder(BatchPatient patient) => new()
        {
            DailyNo = patient.DailyNo,
            LabId = patient.CentreId,
            CreatedBy = patient.CreatedBy,
            Patient = patient.Patient,
            SiteCode = configuration["Lis:SiteCode"]?.Trim() ?? "LAB",
            ReferringDoctorCode = patient.ReferringDoctorCode,
            BillingDoctorCode = patient.BillingDoctorCode,
            TestProfiles = patient.ProductCodes
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code =>
                {
                    var testProfileCode = code.Trim();
                    var separatorIndex = testProfileCode.IndexOf('-');
                    if (separatorIndex >= 0)
                        testProfileCode = testProfileCode[(separatorIndex + 1)..];

                    return new LisOrderTestProfileRequest
                    {
                        TestProfileCode = testProfileCode,
                        IsBillable = true
                    };
                })
                .ToArray()
        };

    private async Task<IActionResult> ProcessBatchOrders(
        List<BatchPatient> patients,
        CancellationToken cancellationToken)
    {
        if (patients.GroupBy(patient => patient.SyncId)
            .Any(group => group.Count() > 1))
        {
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "The patient selection contains duplicate synchronization records. No orders were submitted.");
        }

        var results = new List<LisBatchOrderResult>();
        foreach (var patient in patients)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var order = CreatePatientOrder(patient);

            IActionResult result;
            if (!TryValidateModel(order))
            {
                result = ValidationProblem(ModelState);
            }
            else
            {
                result = await SynchronizeOrder(
                    order, patient, cancellationToken);
            }

            var item = ToBatchOrderResult(patient.DailyNo, result) with
            {
                SyncId = patient.SyncId,
                CentreId = patient.CentreId
            };
            results.Add(item);
            if (!item.Success)
            {
                break;
            }
        }

        var failedCount = results.Count(result => !result.Success);
        var response = new LisBatchCreateOrdersResponse(
            patients.Count,
            0,
            results.Count - failedCount,
            failedCount,
            patients.Count - results.Count,
            results);
        return StatusCode(failedCount == 0 ? StatusCodes.Status200OK : StatusCodes.Status207MultiStatus, response);
    }

    private async Task<IActionResult> SynchronizeOrder(
        LisCreateOrderRequest request,
        BatchPatient patient,
        CancellationToken cancellationToken,
        bool isRetry = false)
    {
        Response.Headers.CacheControl = "no-store";

        if (GetLisBaseUri() is null)
        {
            return Problem(statusCode: StatusCodes.Status500InternalServerError,
                title: "LIS base URL is not configured correctly.");
        }

        failureDetails.Clear("LIS");
        var (token, tokenError) = await GetConfiguredToken(cancellationToken);
        if (token is null)
        {
            return tokenError!;
        }

        try
        {
            var claimed = history.IsRecording
                ? (await history.Claim(patient.SyncId,
                    JsonSerializer.SerializeToElement(
                        CreateLisOrderPayload(request with { LabId = patient.CentreId }), OrderJsonOptions),
                    isRetry ? ["failed", "unknown"] : ["pending"], false, cancellationToken) ? 1 : 0)
                : await dbHelper.ExecuteNonQueryAsync(
                """
                UPDATE public.patientdetails_sync
                SET sync_status = 'in_progress',
                    attempt_count = attempt_count + 1,
                    last_attempt_at = CURRENT_TIMESTAMP,
                    last_http_status = NULL,
                    last_error = NULL
                WHERE id = @id AND target_system = 'LIS' AND sync_status = ANY(@allowedSyncStatuses)
                """,
                [
                    new NpgsqlParameter("id", patient.SyncId),
                    new NpgsqlParameter("allowedSyncStatuses", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Text)
                    {
                        Value = isRetry ? new[] { "failed", "unknown" } : new[] { "pending" }
                    }
                ],
                cancellationToken);
            if (claimed != 1)
            {
                return Problem(statusCode: StatusCodes.Status409Conflict,
                    title: isRetry
                        ? "Only failed or unknown synchronization records can be retried. No order was submitted."
                        : "The synchronization record is not pending. No order was submitted.",
                    detail: "Check its current state before retrying.");
            }
        }
        catch (NpgsqlException exception)
        {
            logger.LogError(exception, "Unable to claim LIS synchronization record {SyncId}.", patient.SyncId);
            return Problem(statusCode: StatusCodes.Status500InternalServerError,
                title: "Unable to start LIS synchronization. No order was submitted.");
        }

        int? httpStatus = null;
        string? failureReason = null;
        IActionResult result;
        try
        {
            var payload = request with { LabId = patient.CentreId };
            result = await CreateOrderCore(
                payload,
                token.AccessToken,
                cancellationToken,
                status => httpStatus = status,
                reason => failureReason = failureDetails.Capture(
                    "LIS", "CreateOrder", httpStatus, reason ?? string.Empty, payload, token.AccessToken));
            if (httpStatus == StatusCodes.Status401Unauthorized)
            {
                tokenCache.Invalidate(token.AccessToken);
                failureDetails.Clear("LIS");
                var (refreshedToken, refreshError) = await GetConfiguredToken(cancellationToken);
                if (refreshedToken is not null)
                {
                    httpStatus = null;
                    failureReason = null;
                    result = await CreateOrderCore(
                        payload,
                        refreshedToken.AccessToken,
                        cancellationToken,
                        status => httpStatus = status,
                        reason => failureReason = failureDetails.Capture(
                            "LIS", "CreateOrder", httpStatus, reason ?? string.Empty,
                            payload, refreshedToken.AccessToken));
                }
                else
                {
                    result = refreshError!;
                    httpStatus = GetActionStatus(result);
                    failureReason = GetFailureReason(result) ?? "Unable to refresh the LIS token.";
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await SaveSyncOutcome(patient.SyncId, "unknown", httpStatus, null,
                failureReason ?? "The caller cancelled the request. Verify the order in LIS before retrying.");
            throw;
        }

        var outcome = ToBatchOrderResult(request.DailyNo!, result);
        var syncStatus = outcome.Success ? "succeeded"
            : httpStatus is >= 400 and < 500 and not 408 ? "failed" : "unknown";
        var saved = await SaveSyncOutcome(
            patient.SyncId,
            syncStatus,
            httpStatus,
            outcome.LabId,
            outcome.Success ? null
                : failureReason ?? GetFailureReason(result)
                    ?? (syncStatus == "failed" ? "LIS rejected the synchronization request."
                        : "The LIS order outcome is uncertain. Verify the order in LIS before retrying."));
        if (!saved)
        {
            return Problem(statusCode: StatusCodes.Status500InternalServerError,
                title: "The LIS request completed, but its synchronization state could not be saved.",
                detail: "The record may remain in_progress. Verify the order in LIS before retrying.");
        }

        return result;
    }

    private async Task<bool> SaveSyncOutcome(
        long syncId,
        string status,
        int? httpStatus,
        string? labId,
        string? error)
    {
        // Persist the remote outcome even when the caller disconnects.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            if (history.IsRecording)
                return await history.Complete(syncId, status, labId, httpStatus, error, timeout.Token);
            var affected = await dbHelper.ExecuteNonQueryAsync(
                """
                UPDATE public.patientdetails_sync
                SET sync_status = @status,
                    synced_at = CASE WHEN @status = 'succeeded' THEN CURRENT_TIMESTAMP ELSE NULL END,
                    remote_record_id = @labId,
                    last_http_status = @httpStatus,
                    last_error = @error
                WHERE id = @id AND target_system = 'LIS' AND sync_status = 'in_progress'
                """,
                [
                    new NpgsqlParameter("id", syncId),
                    new NpgsqlParameter("status", status),
                    new NpgsqlParameter("labId", NpgsqlTypes.NpgsqlDbType.Text) { Value = (object?)labId ?? DBNull.Value },
                    new NpgsqlParameter("httpStatus", NpgsqlTypes.NpgsqlDbType.Integer) { Value = (object?)httpStatus ?? DBNull.Value },
                    new NpgsqlParameter("error", NpgsqlTypes.NpgsqlDbType.Text) { Value = (object?)error ?? DBNull.Value }
                ],
                timeout.Token);
            if (affected != 1)
            {
                logger.LogError("LIS synchronization outcome was not saved for record {SyncId}.", syncId);
                return false;
            }

            return true;
        }
        catch (NpgsqlException exception)
        {
            logger.LogError(exception, "Unable to save LIS synchronization outcome for record {SyncId}.", syncId);
            return false;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            logger.LogError("Saving LIS synchronization outcome timed out for record {SyncId}.", syncId);
            return false;
        }
    }

    private async Task<IActionResult> CreateOrderCore(
        LisCreateOrderRequest request,
        string lisAccessToken,
        CancellationToken cancellationToken,
        Action<int> receivedStatus,
        Action<string?> failureReason)
    {
        Response.Headers.CacheControl = "no-store";

        if (string.IsNullOrWhiteSpace(lisAccessToken) || lisAccessToken.Any(char.IsWhiteSpace)
            || !AuthenticationHeaderValue.TryParse($"Bearer {lisAccessToken}", out var authorization))
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "X-Lis-Access-Token must contain the LIS access token without the Bearer prefix.");
        }

        var baseUri = GetLisBaseUri();
        if (baseUri is null)
        {
            return Problem(statusCode: StatusCodes.Status500InternalServerError,
                title: "LIS base URL is not configured correctly.");
        }

        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post,
                new Uri($"{baseUri.AbsoluteUri.TrimEnd('/')}/api/CreateOrder/"));
            message.Headers.Authorization = authorization;
            message.Content = JsonContent.Create(CreateLisOrderPayload(request), options: OrderJsonOptions);
            var client = httpClientFactory.CreateClient("Lis");
            using var response = await client.SendAsync(message, cancellationToken);
            receivedStatus((int)response.StatusCode);

            if (!response.IsSuccessStatusCode)
            {
                var statusCode = response.StatusCode switch
                {
                    HttpStatusCode.BadRequest => StatusCodes.Status400BadRequest,
                    HttpStatusCode.Unauthorized => StatusCodes.Status401Unauthorized,
                    HttpStatusCode.Forbidden => StatusCodes.Status403Forbidden,
                    HttpStatusCode.Conflict => StatusCodes.Status409Conflict,
                    HttpStatusCode.UnprocessableEntity => StatusCodes.Status422UnprocessableEntity,
                    _ => StatusCodes.Status502BadGateway
                };
                var error = await LisErrorResponse(
                    response, statusCode, "LIS rejected the create-order request.", cancellationToken,
                    reason => failureReason(reason));
                if (error is ContentResult { Content: not null } content
                    && IsDuplicateOrder(content.Content, content.ContentType, request.LabId))
                {
                    return DuplicateOrderResponse(request.LabId!);
                }

                return error;
            }

            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                failureReason("LIS returned HTTP 204 without the LabId required to confirm the order.");
                return Problem(
                    statusCode: StatusCodes.Status502BadGateway,
                    title: "LIS created the order but did not return a LabId.",
                    detail: "The synchronization outcome is uncertain. Verify the order in LIS before retrying.");
            }

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            JsonElement result;
            try
            {
                result = JsonSerializer.Deserialize<JsonElement>(responseBody);
            }
            catch (JsonException)
            {
                failureReason(responseBody);
                return Problem(statusCode: StatusCodes.Status502BadGateway,
                    title: "LIS returned invalid JSON. Verify the order in LIS before retrying.");
            }
            if (IsDuplicateOrder(result.GetRawText(), "application/json", request.LabId))
            {
                return DuplicateOrderResponse(request.LabId!);
            }

            var labId = GetLabId(result);
            if (string.IsNullOrWhiteSpace(labId))
            {
                failureReason(responseBody);
                return Problem(
                    statusCode: StatusCodes.Status502BadGateway,
                    title: "LIS created the order but did not return a LabId.",
                    detail: "The synchronization outcome is uncertain. Verify the order in LIS before retrying.");
            }

            return StatusCode((int)response.StatusCode, result);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            failureReason($"LIS request timed out: {exception.Message}");
            return Problem(statusCode: StatusCodes.Status504GatewayTimeout,
                title: "LIS create-order request timed out. Verify the order in LIS before retrying.");
        }
        catch (HttpRequestException exception)
        {
            failureReason($"{exception.GetType().Name}: {exception.Message}");
            return Problem(statusCode: StatusCodes.Status502BadGateway,
                title: "LIS connection failed. Verify the order in LIS before retrying.");
        }
        catch (JsonException exception)
        {
            failureReason($"Invalid JSON from LIS: {exception.Message}");
            return Problem(statusCode: StatusCodes.Status502BadGateway,
                title: "LIS returned invalid JSON. Verify the order in LIS before retrying.");
        }
        catch (NotSupportedException exception)
        {
            failureReason($"Unsupported response from LIS: {exception.Message}");
            return Problem(statusCode: StatusCodes.Status502BadGateway,
                title: "LIS returned an unsupported response. Verify the order in LIS before retrying.");
        }
    }

    private static string? ReadNullableString(NpgsqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static LisBatchOrderResult ToBatchOrderResult(string dailyNo, IActionResult result)
    {
        return result switch
        {
            ObjectResult objectResult => new LisBatchOrderResult(
                dailyNo,
                objectResult.StatusCode is >= 200 and < 300,
                objectResult.StatusCode ?? StatusCodes.Status200OK,
                objectResult.Value is JsonElement json ? GetLabId(json) : null,
                objectResult.Value is ProblemDetails ? "application/problem+json" : "application/json",
                objectResult.Value),
            ContentResult contentResult => new LisBatchOrderResult(
                dailyNo,
                contentResult.StatusCode is >= 200 and < 300,
                contentResult.StatusCode ?? StatusCodes.Status200OK,
                null,
                contentResult.ContentType,
                contentResult.Content),
            _ => throw new InvalidOperationException("Unexpected LIS order action result.")
        };
    }

    private sealed record BatchPatient(
        long SyncId,
        string CentreId,
        string DailyNo,
        string CreatedBy,
        LisOrderPatientRequest Patient,
        string SyncStatus,
        DateTimeOffset? SyncedAt,
        string? RemoteRecordId,
        string? ReferringDoctorCode,
        string? BillingDoctorCode,
        DateTimeOffset? DateOfBirth,
        string[] ProductCodes);

    private sealed record LisCreateOrderPayload(
        [property: JsonPropertyName("LabId")] string? LabId,
        [property: JsonPropertyName("referringDoctorCode")] string? ReferringDoctorCode,
        [property: JsonPropertyName("BillingDoctorCode")] string? BillingDoctorCode,
        [property: JsonPropertyName("SiteCode")] string SiteCode,
        [property: JsonPropertyName("createdBy")] string CreatedBy,
        [property: JsonPropertyName("patient")] LisOrderPatientRequest Patient,
        [property: JsonPropertyName("TestProfiles")] IReadOnlyList<LisOrderTestProfileRequest> TestProfiles);

    private static LisCreateOrderPayload CreateLisOrderPayload(LisCreateOrderRequest request) =>
        new(request.LabId, request.ReferringDoctorCode, request.BillingDoctorCode, request.SiteCode,
            request.CreatedBy, request.Patient!, request.TestProfiles ?? []);

    private async Task<ActionResult> LisErrorResponse(
        HttpResponseMessage response,
        int statusCode,
        string fallbackTitle,
        CancellationToken cancellationToken,
        Action<string?>? failureReason = null)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        failureReason?.Invoke(content);
        if (!string.IsNullOrWhiteSpace(content))
        {
            return new ContentResult
            {
                Content = content,
                ContentType = response.Content.Headers.ContentType?.ToString() ?? "text/plain; charset=utf-8",
                StatusCode = statusCode
            };
        }

        return Problem(statusCode: statusCode, title: fallbackTitle);
    }

    private OkObjectResult DuplicateOrderResponse(string labId)
    {
        return Ok(JsonSerializer.SerializeToElement(new
        {
            result = new { LabId = labId },
            alreadySynced = true,
            message = $"Duplicate Data: {labId}"
        }));
    }

    private static bool IsDuplicateOrder(string content, string? contentType, string? labId)
    {
        if (string.IsNullOrWhiteSpace(labId))
        {
            return false;
        }

        var expected = $"Duplicate Data: {labId}";
        if (string.Equals(content.Trim(), expected, StringComparison.Ordinal))
        {
            return true;
        }

        return string.Equals(
            ExtractLisErrorReason(content, StatusCodes.Status400BadRequest, contentType)?.Trim(),
            expected,
            StringComparison.Ordinal);
    }

    private static string? GetFailureReason(IActionResult result)
    {
        return result switch
        {
            ContentResult { Content: not null } content =>
                ExtractLisErrorReason(content.Content, content.StatusCode ?? 502, content.ContentType),
            ObjectResult { Value: ProblemDetails problem } =>
                string.Join(" ", new[] { problem.Title, problem.Detail }.Where(value => !string.IsNullOrWhiteSpace(value))),
            _ => null
        };
    }

    private static string? ExtractLisErrorReason(string content, int statusCode, string? contentType)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return $"LIS returned HTTP {statusCode} without an error message.";
        }

        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.ValueKind == JsonValueKind.String)
            {
                return document.RootElement.GetString();
            }

            var messages = new List<string>();
            CollectLisErrorMessages(document.RootElement, messages, false);
            var reason = string.Join("; ", messages
                .Where(message => !string.IsNullOrWhiteSpace(message))
                .Distinct(StringComparer.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(reason))
            {
                return reason;
            }
        }
        catch (JsonException)
        {
        }

        if (contentType is null || contentType.StartsWith("text/plain", StringComparison.OrdinalIgnoreCase))
        {
            return content.Length <= 2000 ? content : content[..2000];
        }

        return $"LIS returned HTTP {statusCode} with an unreadable error response ({contentType}).";
    }

    private static void CollectLisErrorMessages(JsonElement element, List<string> messages, bool insideErrors)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var isMessage = property.Name.Equals("title", StringComparison.OrdinalIgnoreCase)
                        || property.Name.Equals("detail", StringComparison.OrdinalIgnoreCase)
                        || property.Name.Equals("message", StringComparison.OrdinalIgnoreCase)
                        || property.Name.Equals("description", StringComparison.OrdinalIgnoreCase)
                        || property.Name.Equals("error_description", StringComparison.OrdinalIgnoreCase);
                    if (isMessage && property.Value.ValueKind == JsonValueKind.String)
                    {
                        messages.Add(property.Value.GetString()!);
                    }
                    else if (property.Name.Equals("errors", StringComparison.OrdinalIgnoreCase))
                    {
                        CollectLisErrorMessages(property.Value, messages, true);
                    }
                    else if (property.Name.Equals("error", StringComparison.OrdinalIgnoreCase))
                    {
                        if (property.Value.ValueKind == JsonValueKind.String)
                        {
                            messages.Add(property.Value.GetString()!);
                        }
                        else
                        {
                            CollectLisErrorMessages(property.Value, messages, false);
                        }
                    }
                    else if (insideErrors)
                    {
                        CollectLisErrorMessages(property.Value, messages, true);
                    }
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectLisErrorMessages(item, messages, insideErrors);
                }

                break;
            case JsonValueKind.String when insideErrors:
                messages.Add(element.GetString()!);
                break;
        }
    }

    private static string? SanitizeFailureReason(string? reason, LisCreateOrderRequest request)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return null;
        }

        var sanitized = reason;
        var sensitiveValues = new List<string?>
        {
            request.DailyNo,
            request.LabId,
            request.SID,
            request.RefNumber,
            request.Patient?.HKID,
            request.Patient?.ID,
            request.Patient?.PatientNameEng,
            request.Patient?.PatientNameChi,
            request.Patient?.DOB?.ToString("O"),
            request.Email,
            request.ReportComment,
            request.InternalComment,
            request.Fax,
            request.SMS,
            request.Whatsapp
        };
        foreach (var value in sensitiveValues.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            sanitized = sanitized.Replace(value!, "[REDACTED]", StringComparison.OrdinalIgnoreCase);
        }

        sanitized = Regex.Replace(sanitized, @"(?i)\bBearer\s+[^\s,;\""']+", "Bearer [REDACTED]");
        sanitized = Regex.Replace(sanitized,
            @"(?i)(access[_-]?token|refresh[_-]?token|password|authorization|cookie)\s*[:=]\s*[^\s,;]+",
            "$1=[REDACTED]");
        sanitized = Regex.Replace(sanitized, @"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}", "[EMAIL REDACTED]",
            RegexOptions.IgnoreCase);
        sanitized = Regex.Replace(sanitized, @"(?<!\w)(?:\+?\d[\d ()-]{6,}\d)(?!\w)", "[PHONE REDACTED]");
        sanitized = Regex.Replace(sanitized, @"\s+", " ").Trim();
        return sanitized.Length <= 2000 ? sanitized : sanitized[..2000];
    }

    private static string? GetLabId(JsonElement response)
    {
        if (response.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in response.EnumerateObject())
        {
            if (property.Name.Equals("result", StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == JsonValueKind.Object)
            {
                foreach (var resultProperty in property.Value.EnumerateObject())
                {
                    if (resultProperty.Name.Equals("LabId", StringComparison.OrdinalIgnoreCase)
                        && resultProperty.Value.ValueKind == JsonValueKind.String)
                    {
                        return resultProperty.Value.GetString()?.Trim();
                    }
                }

                return null;
            }
        }

        return null;
    }

    private Uri? GetLisBaseUri()
    {
        if (!Uri.TryCreate(configuration["Lis:BaseUrl"], UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp)
            || !string.IsNullOrEmpty(baseUri.UserInfo)
            || !string.IsNullOrEmpty(baseUri.Query)
            || !string.IsNullOrEmpty(baseUri.Fragment))
        {
            return null;
        }

        return baseUri;
    }
}