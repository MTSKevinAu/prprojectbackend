using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using medxbackend.Models;

namespace medxbackend.Services;

public sealed class CrmSyncService(
    IHttpClientFactory httpClientFactory,
    ILogger<CrmSyncService> logger,
    SyncFailureDetails failureDetails) : ICrmSyncService
{
    private static readonly (string Column, string Field)[] FieldMappings =
    [
        ("Daily No.", "Daily_No__c"),
        ("English Name", "English_Name__c"),
        ("Chinese Name", "Chinese_Name__c"),
        ("Product Code", "Product_Code__c"),
        ("Maternal Age", "Maternal_Age__c"),
        ("Gestational Week", "Gestational_Week__c"),
        ("Pregnancy", "Pregnancy__c"),
        ("Ethnic Origin", "Ethnic_Origin__c"),
        ("ID/Passport No.", "ID_Passport_No__c"),
        ("Clinic Code", "Clinic_Code__c"),
        ("Sample Type", "Sample_Type__c"),
        ("Patient Ref No.", "Patient_Ref_No__c"),
        ("TBC Status", "TBC_Status__c"),
        ("Hold Status", "Hold_Status__c"),
        ("Remarks", "Remarks__c")
    ];

    private static readonly (string Column, string Field, string Format)[] DateMappings =
    [
        ("Date of Birth", "Date_of_Birth__c", "dd/MM/yyyy"),
        ("Date Received", "Date_Received__c", "yyyy-MM-dd"),
        ("Date Collected", "Date_Collected__c", "yyyy-MM-dd")
    ];

    public CrmUpsertPreviewResponse Preview(
        IReadOnlyList<Dictionary<string, object?>> rows,
        CancellationToken cancellationToken)
    {
        var labNos = rows.Select(row => Text(row.GetValueOrDefault("Lab No."))?.Trim()).ToArray();
        var duplicates = labNos.Where(value => !string.IsNullOrEmpty(value))
            .GroupBy(value => value!, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var results = new List<CrmPatientDetailsPreviewResponse>(rows.Count);
        for (var index = 0; index < rows.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = rows[index];
            var labNo = labNos[index];
            var payload = BuildPayload(row, out var error);
            if (string.IsNullOrWhiteSpace(labNo))
            {
                error = "Lab No. is required for upsert.";
            }
            else if (labNo is "." or "..")
            {
                error = "Lab No. must not be a URL path segment marker.";
            }
            else if (duplicates.Contains(labNo))
            {
                error = "Duplicate Lab No. in the selected export rows.";
            }

            var path = error is null
                ? $"/services/data/v68.0/sobjects/Lab_Booking__c/Lab_No__c/{Uri.EscapeDataString(labNo!)}"
                : null;
            results.Add(new CrmPatientDetailsPreviewResponse(
                index + 1, labNo, Text(row.GetValueOrDefault("Daily No.")), error is null,
                "PATCH", path, error is null ? payload : null,
                error is null ? null : "INVALID_EXPORT_ROW", error));
        }

        var validCount = results.Count(item => item.IsValid);
        return new CrmUpsertPreviewResponse(rows.Count, validCount, rows.Count - validCount, results);
    }

    public async Task<CrmSyncPatientDetailsResponse> SyncAsync(
        IReadOnlyList<Dictionary<string, object?>> rows,
        CrmTokenResponse token,
        CancellationToken cancellationToken, bool updateOnly = false,
        Func<CancellationToken, Task<CrmTokenResponse?>>? refreshToken = null)
    {
        var results = new List<CrmSyncPatientDetailsItemResponse>(rows.Count);
        var preview = Preview(rows, cancellationToken);
        var stopped = false;
        var client = httpClientFactory.CreateClient("Crm");
        var instanceUri = new Uri(token.InstanceUrl, UriKind.Absolute);

        for (var index = 0; index < rows.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var prepared = preview.Results[index];
            var item = new CrmSyncPatientDetailsItemResponse(
                prepared.RowNumber, prepared.LabNo, prepared.DailyNo, "notAttempted");
            if (stopped)
            {
                results.Add(item with
                {
                    ErrorCode = "BATCH_STOPPED",
                    Message = "Not sent because an earlier request failed with an authentication, availability, or uncertain-result error."
                });
                continue;
            }

            if (!prepared.IsValid)
            {
                logger.LogWarning("CRM export row {RowNumber} failed local validation.", item.RowNumber);
                results.Add(item with { Outcome = "failed", ErrorCode = prepared.ErrorCode, Message = prepared.Message });
                continue;
            }

            // Salesforce takes the External ID from the URL; omit Lab_No__c from the body.
            var uri = new Uri(instanceUri, prepared.Path + (updateOnly ? "?updateOnly=true" : string.Empty));
            HttpResponseMessage? response = null;
            try
            {
                response = await SendUpsertRequest(client, uri, token, prepared.Payload, cancellationToken);
                if (response.StatusCode == HttpStatusCode.Unauthorized && refreshToken is not null)
                {
                    var refreshedToken = await refreshToken(cancellationToken);
                    if (refreshedToken is not null)
                    {
                        response.Dispose();
                        token = refreshedToken;
                        instanceUri = new Uri(token.InstanceUrl, UriKind.Absolute);
                        uri = new Uri(instanceUri, prepared.Path + (updateOnly ? "?updateOnly=true" : string.Empty));
                        response = await SendUpsertRequest(client, uri, token, prepared.Payload, cancellationToken);
                    }
                }

                var httpStatus = (int)response.StatusCode;
                if (response.StatusCode == HttpStatusCode.NoContent)
                {
                    results.Add(item with { Outcome = "updated", HttpStatus = httpStatus });
                    continue;
                }

                if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created)
                {
                    var confirmation = await response.Content.ReadAsStringAsync(cancellationToken);
                    UpsertResponse? created;
                    try
                    {
                        created = JsonSerializer.Deserialize<UpsertResponse>(confirmation);
                    }
                    catch (JsonException)
                    {
                        failureDetails.Capture("CRM", "Upsert", httpStatus, confirmation, rows[index], token.AccessToken);
                        throw;
                    }
                    var wasCreated = response.StatusCode == HttpStatusCode.Created ? true : created?.Created;
                    if (created is { Success: true } && !string.IsNullOrWhiteSpace(created.Id)
                        && wasCreated.HasValue && !(updateOnly && wasCreated.Value)
                        && (created.Errors.ValueKind == JsonValueKind.Undefined
                            || (created.Errors.ValueKind == JsonValueKind.Array && created.Errors.GetArrayLength() == 0)))
                    {
                        results.Add(item with
                        {
                            Outcome = wasCreated.Value ? "created" : "updated",
                            SalesforceId = created.Id, HttpStatus = httpStatus
                        });
                        continue;
                    }

                    logger.LogWarning("CRM returned invalid upsert confirmation for export row {RowNumber}.", item.RowNumber);
                    failureDetails.Capture("CRM", "Upsert", httpStatus, confirmation, rows[index], token.AccessToken);
                    results.Add(item with
                    {
                        Outcome = "unknown", HttpStatus = httpStatus, ErrorCode = "INVALID_CRM_RESPONSE",
                        Message = "CRM may have saved the record, but returned an invalid confirmation. Reconcile before retrying."
                    });
                    stopped = true;
                    continue;
                }

                stopped = httpStatus is not (400 or 409 or 422);
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                failureDetails.Capture("CRM", "Upsert", httpStatus, errorBody, rows[index], token.AccessToken);
                var errorCode = ReadErrorCode(errorBody);
                logger.LogWarning("CRM upsert rejected export row {RowNumber} with HTTP {StatusCode}.", item.RowNumber, httpStatus);
                results.Add(item with
                {
                    Outcome = httpStatus >= 500 || response.IsSuccessStatusCode ? "unknown" : "failed",
                    HttpStatus = httpStatus,
                    ErrorCode = errorCode ?? "CRM_REQUEST_REJECTED",
                    Message = "CRM did not confirm the upsert. Check the error code and Salesforce configuration before retrying."
                });
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                results.Add(Uncertain(item, "CRM_TIMEOUT"));
                stopped = true;
            }
            catch (HttpRequestException)
            {
                results.Add(Uncertain(item, "CRM_NETWORK_ERROR"));
                stopped = true;
            }
            catch (JsonException)
            {
                results.Add(Uncertain(item, "INVALID_CRM_RESPONSE"));
                stopped = true;
            }
            catch (NotSupportedException)
            {
                results.Add(Uncertain(item, "UNSUPPORTED_CRM_RESPONSE"));
                stopped = true;
            }
            finally
            {
                response?.Dispose();
            }
        }

        var createdCount = results.Count(item => item.Outcome == "created");
        var updatedCount = results.Count(item => item.Outcome == "updated");
        return new CrmSyncPatientDetailsResponse(
            createdCount + updatedCount == rows.Count, rows.Count, createdCount, updatedCount,
            results.Count(item => item.Outcome == "failed"),
            results.Count(item => item.Outcome == "unknown"),
            results.Count(item => item.Outcome == "notAttempted"), results);
    }

    private CrmSyncPatientDetailsItemResponse Uncertain(CrmSyncPatientDetailsItemResponse item, string code)
    {
        logger.LogWarning("CRM upsert result is uncertain for export row {RowNumber}: {ErrorCode}.", item.RowNumber, code);
        return item with
        {
            Outcome = "unknown", ErrorCode = code,
            Message = "The write result is unknown. Reconcile in CRM before retrying; the remaining batch was stopped."
        };
    }

    private static async Task<HttpResponseMessage> SendUpsertRequest(
        HttpClient client, Uri uri, CrmTokenResponse token,
        IReadOnlyDictionary<string, object?>? payload, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        request.Content = JsonContent.Create(payload);
        return await client.SendAsync(request, cancellationToken);
    }

    private static Dictionary<string, object?> BuildPayload(
        Dictionary<string, object?> row, out string? error)
    {
        error = null;
        var payload = new Dictionary<string, object?>();
        foreach (var (column, field) in FieldMappings)
        {
            if (!row.TryGetValue(column, out var value))
            {
                error = $"Export column '{column}' is missing.";
                return payload;
            }

            payload[field] = Text(value);
        }

        foreach (var (column, field, format) in DateMappings)
        {
            if (!row.TryGetValue(column, out var value))
            {
                error = $"Export column '{column}' is missing.";
                return payload;
            }

            if (!TryFormatDate(value, format, out var date))
            {
                error = $"Export column '{column}' has an invalid date.";
                return payload;
            }

            payload[field] = date;
        }

        payload["Pending_Report__c"] = false;
        payload["Edit_Now__c"] = false;
        payload["FOC__c"] = false;
        return payload;
    }

    private static bool TryFormatDate(object? value, string format, out string? formatted)
    {
        formatted = null;
        if (value is null or DBNull || string.IsNullOrWhiteSpace(Text(value)))
        {
            return true;
        }

        DateOnly date;
        switch (value)
        {
            case DateOnly dateOnly:
                date = dateOnly;
                break;
            case DateTime dateTime:
                date = DateOnly.FromDateTime(dateTime);
                break;
            case DateTimeOffset offset:
                date = DateOnly.FromDateTime(offset.DateTime);
                break;
            default:
                if (!DateOnly.TryParseExact(Text(value)?.Trim(),
                    ["dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd"],
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                {
                    return false;
                }
                break;
        }

        formatted = date.ToString(format, CultureInfo.InvariantCulture);
        return true;
    }

    private static string? Text(object? value) => value switch
    {
        null or DBNull => null,
        Array values => string.Join(", ", values.Cast<object?>().Select(Text)),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()
    };

    private string? ReadErrorCode(string body)
    {
        // Only expose machine-readable codes; Salesforce messages can include patient data.
        try
        {
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var error in json.RootElement.EnumerateArray())
                {
                    if (error.ValueKind == JsonValueKind.Object
                        && error.TryGetProperty("errorCode", out var code)
                        && code.ValueKind == JsonValueKind.String)
                    {
                        return code.GetString();
                    }
                }
            }
        }
        catch (JsonException)
        {
            logger.LogWarning("CRM error response was not valid JSON; using its HTTP status.");
        }

        return null;
    }

    private sealed record UpsertResponse(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("created")] bool? Created,
        [property: JsonPropertyName("errors")] JsonElement Errors);
}
