using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.ComponentModel.DataAnnotations;
using medxbackend.Data;
using medxbackend.Models;
using Npgsql;

namespace medxbackend.Services;

public sealed class LisFinalPatientUpdateService(
    DbHelper dbHelper,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    LisAccessTokenCache tokenCache,
    ILogger<LisFinalPatientUpdateService> logger,
    SyncFailureDetails failureDetails,
    ISyncHistoryService history) : ILisFinalPatientUpdateService
{
    private static readonly JsonSerializerOptions LisJsonOptions = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<LisFinalPatientUpdateResult> SyncIfAlreadySucceeded(
        string dailyNo, string formType, CancellationToken cancellationToken)
    {
        failureDetails.Clear("LIS");
        List<BatchPatientRow> rows;
        try
        {
            rows = await dbHelper.QueryAsync(
                """
                SELECT sync.id, sync.centreid, sync.sync_status,
                       patient.refcustordr, patient.billtocustordr, users.username AS modifiedby,
                       patient.hkidno, patient.patientnameen, patient.patientnamechi, patient.gender,
                       patient.dateofbirth, patient.age, sync.remote_record_id
                FROM public.patientdetails_final AS patient
                INNER JOIN public.patientdetails_sync AS sync
                    ON sync.centreid = patient.centreid AND sync.formtype = patient.formtype
                LEFT JOIN public.users AS users ON users.id = patient.userid
                WHERE patient.dailyno = @dailyNo
                  AND patient.formtype = @formType
                  AND sync.target_system = 'LIS'
                LIMIT 2
                """,
                MapPatient,
                [
                    new NpgsqlParameter("dailyNo", dailyNo.Trim()),
                    new NpgsqlParameter("formType", formType.Trim())
                ],
                cancellationToken);
        }
        catch (NpgsqlException exception)
        {
            logger.LogError(exception, "Unable to check LIS synchronization status after final patient update.");
            return Failure("CheckSyncStatus", null,
                "Local patient data was updated, but LIS sync status could not be checked.");
        }

        if (rows.Count == 0)
        {
            return new(false, "skipped", null, null,
                "No LIS synchronization record exists; local patient data was updated only.");
        }

        if (rows.Count > 1)
        {
            return Failure("CheckSyncStatus", null,
                "Local patient data was updated, but multiple LIS sync records matched.");
        }

        var row = rows[0];
        if (!CanUpdate(row.SyncStatus, row.RemoteRecordId, row.CentreId))
        {
            return new(false, "skipped", null, null,
                $"LIS sync status is '{row.SyncStatus}' and the record is not eligible for an update; no LIS update was sent.");
        }

        var orderPayload = new LisUpdateOrderRequest
        {
            LabId = row.CentreId,
            SiteCode = configuration["Lis:SiteCode"]?.Trim() ?? "LAB",
            ReferringDoctorCode = row.ReferringDoctorCode ?? string.Empty,
            BillingDoctorCode = row.BillingDoctorCode ?? string.Empty,
            ModifiedBy = row.ModifiedBy ?? string.Empty
        };
        var patientPayload = new LisUpdateOrderPatientRequest
        {
            LabId = row.CentreId,
            ModifiedBy = row.ModifiedBy ?? string.Empty,
            Patient = new LisUpdateOrderPatientDetails
            {
                HKID = row.HkidNo,
                PatientNameEng = row.PatientNameEng,
                PatientNameChi = row.PatientNameChi,
                Sex = NormalizeSex(row.Gender),
                AgeType = "DOB",
                DOB = row.DateOfBirth,
                Age = row.Age,
                AgeUnit = "Y"
            }
        };
        try
        {
            var claimed = history.IsRecording
                ? (await history.Claim(row.SyncId,
                    JsonSerializer.SerializeToElement(new { UpdateOrder = orderPayload, UpdateOrderPatient = patientPayload },
                        LisJsonOptions),
                    ["succeeded", "unknown"], true, cancellationToken) ? 1 : 0)
                : await dbHelper.ExecuteNonQueryAsync(
                """
                UPDATE public.patientdetails_sync
                SET sync_status = 'in_progress',
                    remote_record_id = centreid,
                    attempt_count = attempt_count + 1,
                    last_attempt_at = CURRENT_TIMESTAMP,
                    last_http_status = NULL,
                    last_error = NULL
                WHERE id = @id AND target_system = 'LIS'
                  AND (sync_status = 'succeeded'
                       OR (sync_status = 'unknown' AND remote_record_id = centreid))
                """,
                [new NpgsqlParameter("id", row.SyncId)],
                cancellationToken);
            if (claimed != 1)
            {
                return new(false, "skipped", null, null,
                    "LIS synchronization status changed before the update started; no LIS update was sent.");
            }
        }
        catch (NpgsqlException exception)
        {
            logger.LogError(exception, "Unable to claim LIS sync record {SyncId} for patient updates.", row.SyncId);
            return Failure("ClaimSyncRecord", null,
                "Local patient data was updated, but the LIS sync record could not be claimed.");
        }

        var stage = "RequestToken";
        int? httpStatus = null;
        string failureMessage;
        try
        {
            var baseUri = GetLisBaseUri();
            if (baseUri is null)
            {
                failureMessage = "LIS base URL is not configured correctly.";
                return await FailedUpdate(row.SyncId, stage, httpStatus, failureMessage);
            }

            var token = await RequestToken(baseUri, cancellationToken);
            if (token.Token is null)
            {
                failureMessage = token.Error!;
                httpStatus = token.HttpStatus;
                return await FailedUpdate(row.SyncId, stage, httpStatus, failureMessage);
            }

            var validationErrors = Validate(orderPayload, patientPayload);
            if (validationErrors is not null)
            {
                return await FailedUpdate(row.SyncId, "ValidatePayload", null, validationErrors);
            }

            var authorization = new AuthenticationHeaderValue(token.Token.TokenType, token.Token.AccessToken);
            stage = "UpdateOrder";
            httpStatus = null;
            var orderResult = await SendPatch(baseUri, "UpdateOrder", orderPayload, authorization,
                cancellationToken, patientPayload);
            authorization = orderResult.Authorization;
            httpStatus = orderResult.StatusCode;
            if (!orderResult.Succeeded)
            {
                failureMessage = orderResult.Error!;
                return await FailedUpdate(row.SyncId, stage, httpStatus, failureMessage);
            }

            stage = "UpdateOrderPatient";
            httpStatus = null;
            var patientResult = await SendPatch(baseUri, "UpdateOrderPatient", patientPayload, authorization, cancellationToken);
            authorization = patientResult.Authorization;
            httpStatus = patientResult.StatusCode;
            if (!patientResult.Succeeded)
            {
                failureMessage = "LIS UpdateOrder succeeded, but UpdateOrderPatient failed. Verify the LIS order before retrying.";
                if (patientResult.Error is not null)
                {
                    failureMessage += " " + patientResult.Error;
                }

                return await FailedUpdate(row.SyncId, stage, httpStatus, failureMessage);
            }

            var saved = await SaveSuccess(row.SyncId, row.CentreId, httpStatus);
            if (!saved)
            {
                return Failure("SaveSyncStatus", httpStatus,
                    "Both LIS updates succeeded, but the local sync status could not be saved.");
            }

            return new(true, "succeeded", null, httpStatus,
                "LIS order and patient were updated successfully.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            failureMessage = $"The request was cancelled during {stage}. Verify the LIS order before retrying.";
            return await FailedUpdate(row.SyncId, stage, httpStatus, failureMessage);
        }
        catch (OperationCanceledException)
        {
            failureMessage = $"LIS timed out during {stage}. Verify the LIS order before retrying.";
            return await FailedUpdate(row.SyncId, stage, httpStatus, failureMessage);
        }
        catch (HttpRequestException)
        {
            failureMessage = $"Unable to connect to LIS during {stage}. Verify the LIS order before retrying.";
            return await FailedUpdate(row.SyncId, stage, httpStatus, failureMessage);
        }
        catch (JsonException)
        {
            failureMessage = $"LIS returned invalid JSON during {stage}. Verify the LIS order before retrying.";
            return await FailedUpdate(row.SyncId, stage, httpStatus, failureMessage);
        }
    }

    private async Task<(LisTokenResponse? Token, int? HttpStatus, string? Error)> RequestToken(
        Uri baseUri, CancellationToken cancellationToken)
    {
        (LisTokenResponse? Token, int? HttpStatus, string? Error) acquisitionResult = default;
        var token = await tokenCache.GetOrCreateAsync(async tokenCancellationToken =>
        {
            acquisitionResult = await RequestTokenUncached(baseUri, tokenCancellationToken);
            return acquisitionResult.Token;
        }, cancellationToken);

        return token is not null ? (token, null, null) : acquisitionResult;
    }

    private async Task<(LisTokenResponse? Token, int? HttpStatus, string? Error)> RequestTokenUncached(
        Uri baseUri, CancellationToken cancellationToken)
    {
        var username = configuration["Lis:username"];
        var password = configuration["Lis:password"];
        var grantType = string.IsNullOrWhiteSpace(configuration["Lis:grant_type"])
            ? "password" : configuration["Lis:grant_type"];
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password) || grantType != "password")
        {
            return (null, null, "LIS token credentials or password grant configuration are missing or invalid.");
        }

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = username,
            ["password"] = password,
            ["grant_type"] = grantType
        });
        using var response = await httpClientFactory.CreateClient("Lis")
            .PostAsync(new Uri($"{baseUri.AbsoluteUri.TrimEnd('/')}/api/token"), content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            failureDetails.Capture("LIS", "RequestToken", (int)response.StatusCode,
                await response.Content.ReadAsStringAsync(cancellationToken), null, username, password);
            return (null, (int)response.StatusCode,
                $"LIS token request failed with HTTP {(int)response.StatusCode}.");
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
            return (null, (int)response.StatusCode, "LIS returned an invalid token response.");
        }

        return (token, (int)response.StatusCode, null);
    }

    private async Task<(bool Succeeded, int? StatusCode, string? Error, AuthenticationHeaderValue Authorization)> SendPatch(
        Uri baseUri, string operation, object payload,
        AuthenticationHeaderValue authorization, CancellationToken cancellationToken, object? patientPayload = null)
    {
        var currentAuthorization = authorization;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var message = new HttpRequestMessage(HttpMethod.Patch,
                new Uri($"{baseUri.AbsoluteUri.TrimEnd('/')}/api/{operation}/"));
            message.Headers.Authorization = currentAuthorization;
            message.Content = JsonContent.Create(payload, payload.GetType(), options: LisJsonOptions);
            using var response = await httpClientFactory.CreateClient("Lis").SendAsync(message, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return (true, (int)response.StatusCode, null, currentAuthorization);
            }

            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            failureDetails.Capture("LIS", operation, (int)response.StatusCode, errorBody,
                new { payload, patientPayload }, currentAuthorization.Parameter);
            logger.LogWarning("LIS {Operation} rejected the patient update with HTTP {StatusCode}.",
                operation, (int)response.StatusCode);

            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                tokenCache.Invalidate(currentAuthorization.Parameter);
                var refreshedToken = await RequestToken(baseUri, cancellationToken);
                if (refreshedToken.Token is not null)
                {
                    currentAuthorization = new AuthenticationHeaderValue(
                        refreshedToken.Token.TokenType, refreshedToken.Token.AccessToken);
                    continue;
                }
            }

            return (false, (int)response.StatusCode,
                $"LIS {operation} failed with HTTP {(int)response.StatusCode}.", currentAuthorization);
        }

        throw new InvalidOperationException("LIS request retry loop exited unexpectedly.");
    }

    private async Task<LisFinalPatientUpdateResult> FailedUpdate(
        long syncId, string stage, int? httpStatus, string error)
    {
        return await SaveFailure(syncId, httpStatus, failureDetails.Get("LIS")
            ?? failureDetails.Capture("LIS", stage, httpStatus, error))
            ? Failure(stage, httpStatus, error)
            : Failure("SaveSyncStatus", httpStatus,
                $"{error} The sync record could not be marked unknown; inspect its current state before retrying.");
    }

    private async Task<bool> SaveFailure(long syncId, int? httpStatus, string error)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            if (history.IsRecording)
                return await history.Complete(syncId, "unknown", null, httpStatus, error, timeout.Token);
            var updated = await dbHelper.ExecuteNonQueryAsync(
                """
                UPDATE public.patientdetails_sync
                SET sync_status = 'unknown',
                    synced_at = NULL,
                    last_http_status = @httpStatus,
                    last_error = @error
                WHERE id = @id AND target_system = 'LIS' AND sync_status = 'in_progress'
                """,
                [
                    new NpgsqlParameter("id", syncId),
                    new NpgsqlParameter("httpStatus", NpgsqlTypes.NpgsqlDbType.Integer)
                    {
                        Value = (object?)httpStatus ?? DBNull.Value
                    },
                    new NpgsqlParameter("error", error)
                ],
                timeout.Token);
            if (updated != 1)
            {
                logger.LogError("Unable to mark LIS sync record {SyncId} unknown after patient update failure.", syncId);
                return false;
            }

            return true;
        }
        catch (Exception exception) when (exception is NpgsqlException or OperationCanceledException)
        {
            logger.LogError(exception, "Unable to persist the LIS update failure for sync record {SyncId}.", syncId);
            return false;
        }
    }

    private async Task<bool> SaveSuccess(long syncId, string centreId, int? httpStatus)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            if (history.IsRecording)
                return await history.Complete(syncId, "succeeded", centreId, httpStatus, null, timeout.Token);
            return await dbHelper.ExecuteNonQueryAsync(
                """
                UPDATE public.patientdetails_sync
                SET sync_status = 'succeeded',
                    synced_at = CURRENT_TIMESTAMP,
                    remote_record_id = @centreId,
                    last_http_status = @httpStatus,
                    last_error = NULL
                WHERE id = @id AND target_system = 'LIS' AND sync_status = 'in_progress'
                """,
                [
                    new NpgsqlParameter("id", syncId),
                    new NpgsqlParameter("centreId", NpgsqlTypes.NpgsqlDbType.Text) { Value = centreId },
                    new NpgsqlParameter("httpStatus", NpgsqlTypes.NpgsqlDbType.Integer)
                    {
                        Value = (object?)httpStatus ?? DBNull.Value
                    }
                ],
                timeout.Token) == 1;
        }
        catch (Exception exception) when (exception is NpgsqlException or OperationCanceledException)
        {
            logger.LogError(exception, "Unable to save successful LIS patient update for sync record {SyncId}.", syncId);
            return false;
        }
    }

    private static string? Validate(LisUpdateOrderRequest order, LisUpdateOrderPatientRequest patient)
    {
        var errors = new List<ValidationResult>();
        if (!Validator.TryValidateObject(order, new ValidationContext(order), errors, true)
            || !Validator.TryValidateObject(patient, new ValidationContext(patient), errors, true)
            || !Validator.TryValidateObject(patient.Patient, new ValidationContext(patient.Patient), errors, true))
        {
            return "Required LIS update fields are missing or invalid; no LIS updates were completed.";
        }

        if (string.IsNullOrWhiteSpace(patient.Patient.PatientNameEng)
            && string.IsNullOrWhiteSpace(patient.Patient.PatientNameChi))
        {
            return "An English or Chinese patient name is required for the LIS patient update.";
        }

        return null;
    }

    private static BatchPatientRow MapPatient(NpgsqlDataReader reader) => new(
        reader.GetInt64(0),
        reader.GetString(1),
        reader.GetString(2),
        ReadNullableString(reader, 3),
        ReadNullableString(reader, 4),
        ReadNullableString(reader, 5),
        ReadNullableString(reader, 6),
        ReadNullableString(reader, 7),
        ReadNullableString(reader, 8),
        ReadNullableString(reader, 9),
        reader.IsDBNull(10) ? null : new DateTimeOffset(
            DateTime.SpecifyKind(reader.GetDateTime(10), DateTimeKind.Utc)),
        reader.IsDBNull(11) ? null : reader.GetInt32(11),
        ReadNullableString(reader, 12));

    private static bool CanUpdate(string status, string? remoteRecordId, string centreId) =>
        string.Equals(status, "succeeded", StringComparison.Ordinal)
        || (string.Equals(status, "unknown", StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(centreId)
            && string.Equals(remoteRecordId, centreId, StringComparison.Ordinal));

    private static string NormalizeSex(string? sex) => sex?.Trim().ToUpperInvariant() switch
    {
        "M" or "MALE" => "M",
        "F" or "FEMALE" => "F",
        var value => value ?? string.Empty
    };

    private static string? ReadNullableString(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static LisFinalPatientUpdateResult Failure(string stage, int? status, string message) =>
        new(true, "unknown", stage, status, message);

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

    private sealed record BatchPatientRow(
        long SyncId,
        string CentreId,
        string SyncStatus,
        string? ReferringDoctorCode,
        string? BillingDoctorCode,
        string? ModifiedBy,
        string? HkidNo,
        string? PatientNameEng,
        string? PatientNameChi,
        string? Gender,
        DateTimeOffset? DateOfBirth,
        int? Age,
        string? RemoteRecordId);
}
