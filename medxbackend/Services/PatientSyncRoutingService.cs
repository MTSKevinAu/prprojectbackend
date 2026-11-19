using medxbackend.Models;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace medxbackend.Services;

public sealed partial class PatientSyncRoutingService(
    ISyncRoutingRepository repository,
    ILisSyncApiService lisService,
    ICrmSyncApiService crmService,
    ILisFinalPatientUpdateService lisUpdateService,
    SyncApiActionContext actionContext,
    ILogger<PatientSyncRoutingService> logger,
    SyncFailureDetails failureDetails,
    ISyncHistoryService history) : SyncApiService(actionContext), IPatientSyncRoutingService
{
    public async Task<IActionResult> GetRecent(int? count, string? targetSystem, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var target = targetSystem?.Trim().ToUpperInvariant();
        if (count is < 1)
            ModelState.AddModelError(nameof(count), "count must be a positive integer.");
        if (target is not (null or "LIS" or "CRM"))
            ModelState.AddModelError(nameof(targetSystem), "targetSystem must be LIS or CRM when specified.");
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        try
        {
            return Ok(await repository.LoadRecent(count, target, cancellationToken));
        }
        catch (NpgsqlException exception)
        {
            logger.LogError(exception, "Unable to read recent synchronization attempts.");
            return Problem(500, "Unable to read recent synchronization attempts.");
        }
    }

    public async Task<IActionResult> GetStatistics(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            return Ok(await repository.GetStatistics(cancellationToken));
        }
        catch (NpgsqlException exception)
        {
            logger.LogError(exception, "Unable to read synchronization queue statistics.");
            return Problem(500, "Unable to read synchronization queue statistics.");
        }
    }

    public async Task<IActionResult> Preview(SyncOrderPreviewRequest request, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        if (!TryGetDateRange(request.StartDate, request.EndDate, out var startDate, out var endDate))
            return ValidationProblem(ModelState);

        try
        {
            var rules = await repository.LoadEnabledRules(cancellationToken);
            if (rules.Any(rule => rule.TargetSystem is not ("LIS" or "CRM")))
                return Problem(422, "An enabled synchronization rule targets an unsupported system. No orders were prepared.");

            foreach (var group in rules.GroupBy(rule => rule.TargetSystem, StringComparer.Ordinal))
            {
                var formTypes = group.Select(rule => rule.FormType).Distinct(StringComparer.Ordinal).ToArray();
                for (var dayNumber = startDate.DayNumber; dayNumber <= endDate.DayNumber; dayNumber++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await repository.PrepareOrders(group.Key, DateOnly.FromDayNumber(dayNumber),
                        formTypes, cancellationToken);
                }
            }

            var records = await repository.LoadPreview(startDate, endDate, cancellationToken);
            return Ok(new SyncOrderPreviewResponse(startDate, endDate, records.Count, records));
        }
        catch (PostgresException exception) when (IsCentreIdConflict(exception))
        {
            logger.LogWarning("Preview centreid generation was blocked: {Reason}", exception.MessageText);
            return Problem(409, "Cannot generate centreid because the selected data contains gaps or duplicate records.",
                "No synchronization was submitted. Queue preparation for earlier dates or target systems may already have completed.");
        }
        catch (NpgsqlException exception)
        {
            logger.LogError(exception, "Unable to prepare or read the rule-based order preview.");
            return Problem(500, "Unable to prepare or read the rule-based order preview.",
                "Queue preparation may already have completed for earlier dates or target systems.");
        }
    }

    private bool TryGetDateRange(
        DateOnly? requestedStartDate, DateOnly? requestedEndDate,
        out DateOnly startDate, out DateOnly endDate)
    {
        startDate = requestedStartDate.GetValueOrDefault();
        endDate = requestedEndDate.GetValueOrDefault();
        if (requestedStartDate is null)
            ModelState.AddModelError("start_date", "start_date is required.");
        if (requestedEndDate is null)
            ModelState.AddModelError("end_date", "end_date is required.");
        if (requestedStartDate is not null && requestedEndDate is not null && startDate > endDate)
            ModelState.AddModelError("end_date", "end_date must be on or after start_date.");
        return ModelState.IsValid;
    }

    public async Task<IReadOnlyList<SyncPatientTargetResult>> UpdateAlreadySynced(
        SyncRoutingPatient patient, CancellationToken cancellationToken)
    {
        List<string> targets;
        try
        {
            targets = await repository.LoadTargets(patient.FormType, cancellationToken);
        }
        catch (NpgsqlException exception)
        {
            logger.LogError(exception, "Unable to load synchronization rules after local patient update.");
            return [new("Rules", false, "Update", 500, "LoadRules",
                "Local patient data was updated, but synchronization rules could not be read.")];
        }

        var results = new List<SyncPatientTargetResult>();
        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ModelState.Clear();
            if (target is not ("LIS" or "CRM"))
            {
                results.Add(new(target, false, "Update", 422, "ValidateRule", "Unsupported synchronization target."));
                continue;
            }
            if (string.IsNullOrWhiteSpace(patient.CentreId))
            {
                results.Add(new(target, true, "Skipped", 200, null, "No centreId; no remote update was sent."));
                continue;
            }
            try
            {
                var queue = await repository.LoadQueue(patient, target, cancellationToken);
                var eligible = queue is not null && (queue.Status == "succeeded"
                    || (queue.Status == "unknown" && target == "LIS" && queue.RemoteRecordId == patient.CentreId)
                    || (target == "CRM" && (queue.Status is "pending" or "failed" or "unknown")));
                if (queue is null || !eligible)
                {
                    results.Add(new(target, true, "Skipped", 200, null,
                        "No confirmed prior synchronization or synchronization is in progress; no remote update was sent."));
                    continue;
                }
                results.Add(await history.Run(target, queue.Id, () => target == "LIS"
                    ? SyncLis(patient, queue, cancellationToken)
                    : SyncCrm(patient, queue, cancellationToken, updateOnly: true), cancellationToken));
            }
            catch (NpgsqlException exception)
            {
                logger.LogError(exception, "Unable to update {TargetSystem} after local patient update.", target);
                results.Add(new(target, false, "Update", 500, "SyncQueue", "Unable to process synchronization state."));
            }
        }
        return results;
    }

    public async Task<IActionResult> CreateLisOrder(
        SyncPatientRequest request, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        if (string.IsNullOrWhiteSpace(request.CentreId))
        {
            ModelState.AddModelError(nameof(request.CentreId), "A non-empty centreId is required.");
            return ValidationProblem(ModelState);
        }

        try
        {
            var patients = await repository.LoadPatients(request.CentreId.Trim(), cancellationToken);
            if (patients.Count == 0)
                return Problem(404, "No final patient matches centreId.");
            if (patients.Count != 1)
                return Problem(409, "CentreId must identify exactly one final patient.");

            var patient = patients[0];
            var targets = await repository.LoadTargets(patient.FormType, cancellationToken);
            if (!targets.Contains("LIS", StringComparer.Ordinal))
                return Problem(409, "No enabled LIS synchronization rule exists for this patient's formtype. No order was submitted.");

            await repository.GetOrCreateQueue(patient, "LIS", cancellationToken);
            return await lisService.CreateOrder(
                new LisCreateOrderFromPatientRequest { CentreId = patient.CentreId, TargetSystem = "LIS" },
                cancellationToken);
        }
        catch (NpgsqlException exception)
        {
            logger.LogError(exception, "Unable to prepare rule-based LIS order creation.");
            return Problem(500, "Unable to process rule-based LIS order creation.",
                "Check synchronization state before retrying; no default routing rule was used.");
        }
    }

    public async Task<IActionResult> Sync(SyncPatientRequest request, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        if (string.IsNullOrWhiteSpace(request.CentreId))
        {
            ModelState.AddModelError(nameof(request.CentreId), "A non-empty centreId is required.");
            return ValidationProblem(ModelState);
        }

        List<SyncRoutingPatient> patients;
        List<string> targets;
        try
        {
            patients = await repository.LoadPatients(request.CentreId.Trim(), cancellationToken);
            if (patients.Count == 0)
                return Problem(404, "No final patient matches centreId.");
            if (patients.Count != 1)
                return Problem(409, "CentreId must identify exactly one final patient.");
            targets = await repository.LoadTargets(patients[0].FormType, cancellationToken);
        }
        catch (NpgsqlException exception)
        {
            logger.LogError(exception, "Unable to load the patient or synchronization rules.");
            return Problem(500, "Unable to load the patient or synchronization rules. No synchronization was submitted.");
        }

        if (targets.Count == 0)
            return Problem(409, "No enabled synchronization rules exist for this patient's formtype.");
        if (targets.Any(target => target is not ("LIS" or "CRM")))
            return Problem(422, "An enabled synchronization rule targets an unsupported system. No synchronization was submitted.");

        var patient = patients[0];
        var results = new List<SyncPatientTargetResult>();
        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ModelState.Clear();
            try
            {
                var queue = await repository.GetOrCreateQueue(patient, target, cancellationToken);
                results.Add(await history.Run(target, queue.Id, () => target == "LIS"
                    ? SyncLis(patient, queue, cancellationToken)
                    : SyncCrm(patient, queue, cancellationToken), cancellationToken));
            }
            catch (NpgsqlException exception)
            {
                logger.LogError(exception, "Unable to process the {TargetSystem} synchronization queue.", target);
                results.Add(new(target, false, "Sync", 500, "SyncQueue",
                    "Unable to process synchronization state. Check its current state before retrying."));
            }
        }

        var success = results.All(result => result.Success);
        return StatusCode(success ? 200 : 207,
            new SyncPatientResponse(patient.CentreId, patient.FormType, success, results));
    }

    private async Task<SyncPatientTargetResult> SyncLis(
        SyncRoutingPatient patient, SyncRoutingQueue queue, CancellationToken cancellationToken)
    {
        if (queue.Status == "in_progress")
            return new("LIS", false, "Sync", 409, "ClaimSyncRecord", "LIS synchronization is already in progress.");

        if (queue.Status == "succeeded"
            || (queue.Status == "unknown" && queue.RemoteRecordId == patient.CentreId))
        {
            if (string.IsNullOrWhiteSpace(patient.DailyNo))
                return new("LIS", false, "UpdateOrderAndPatient", 422, "ValidatePatient", "DailyNo is required for the LIS update.");
            var updated = await lisUpdateService.SyncIfAlreadySucceeded(
                patient.DailyNo, patient.FormType, cancellationToken);
            return new("LIS", updated.Status == "succeeded", "UpdateOrderAndPatient",
                updated.Status == "succeeded" ? 200 : updated.Status == "skipped" ? 409 : 502,
                updated.Stage, updated.Message);
        }

        if (queue.Status == "unknown")
            return new("LIS", false, "CreateOrder", 409, "ReconcileOrder",
                "The original LIS create outcome is uncertain. Verify the order before using the explicit create retry API.");
        if (queue.Status is not ("pending" or "failed"))
            return new("LIS", false, "CreateOrder", 409, "SyncStatus", "The LIS queue state does not allow creation.");

        failureDetails.Clear("LIS");
        var createRequest = new LisCreateOrderFromPatientRequest { CentreId = patient.CentreId, TargetSystem = "LIS" };
        var action = queue.Status == "failed"
            ? await lisService.RetryCreateOrder(createRequest, cancellationToken)
            : await lisService.CreateOrder(createRequest, cancellationToken);
        if (failureDetails.GetStage("LIS") == "RequestToken")
        {
            var error = failureDetails.Get("LIS")
                ?? failureDetails.Capture("LIS", "RequestToken", null, "Unable to obtain a LIS token.");
            if (!await SaveLisTokenFailure(queue.Id, error))
            {
                logger.LogError("Unable to save LIS token failure for queue {SyncId}.", queue.Id);
                return new("LIS", false, "CreateOrder", 500, "SaveSyncStatus",
                    "LIS token retrieval failed and its diagnostic could not be saved.");
            }

            return FromAction("LIS", "CreateOrder", action, "RequestToken");
        }

        return FromAction("LIS", "CreateOrder", action);
    }

    private async Task<bool> SaveLisTokenFailure(long syncId, string error)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            return await repository.SaveLisTokenFailure(
                syncId, failureDetails.GetHttpStatus("LIS"), error, timeout.Token);
        }
        catch (Exception exception) when (exception is NpgsqlException or OperationCanceledException)
        {
            logger.LogError(exception, "Unable to persist LIS token failure for queue {SyncId}.", syncId);
            return false;
        }
    }

    private async Task<SyncPatientTargetResult> SyncCrm(
        SyncRoutingPatient patient, SyncRoutingQueue queue, CancellationToken cancellationToken, bool updateOnly = false)
    {
        failureDetails.Clear("CRM");
        SyncPatientTargetResult result;
        string state;
        string? remoteId = null;
        int? remoteHttpStatus = null;
        try
        {
            var action = await crmService.SyncPatientDetailsFinal(
                new CrmSyncPatientDetailsRequest { CentreId = patient.CentreId, TargetSystem = "CRM" },
                cancellationToken, updateOnly);
            if (action.Result is OkObjectResult { Value: CrmSyncPatientDetailsResponse response })
            {
                var item = response.Items.Count == 1 ? response.Items[0] : null;
                var success = response.Success && item?.Outcome is "created" or "updated";
                state = success ? "succeeded" : item?.Outcome == "failed" ? "failed" : "unknown";
                remoteId = item?.SalesforceId ?? (success ? patient.CentreId : null);
                remoteHttpStatus = item?.HttpStatus;
                result = new("CRM", success, "Upsert", success ? 200 : 502, "Upsert",
                    success ? null : item?.Message ?? "CRM did not confirm synchronization of exactly one patient.");
                if (updateOnly && item is { HttpStatus: 404, ErrorCode: "NOT_FOUND" })
                {
                    state = "unknown";
                    result = new("CRM", false, "Upsert", 502, "Upsert",
                        "CRM did not find the existing record to update. The sync state is unknown; no record was created.");
                }
            }
            else
            {
                result = FromAction("CRM", "Upsert", action.Result);
                state = result.HttpStatus >= 500 ? "unknown" : "failed";
            }
        }
        catch (NpgsqlException exception)
        {
            logger.LogError(exception, "Unable to read the patient for CRM synchronization.");
            state = "failed";
            result = new("CRM", false, "Upsert", 500, "LoadPatient", "Unable to read patient data for CRM synchronization.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (history.HasAttempt)
                await SaveCrm(queue.Id, "unknown", null, null, "CRM synchronization was cancelled; verify its remote outcome.");
            throw;
        }

        if (!history.HasAttempt)
            return result;
        remoteHttpStatus ??= failureDetails.GetHttpStatus("CRM");
        var savedError = state == "succeeded" ? null : failureDetails.Get("CRM")
            ?? failureDetails.Capture("CRM", result.Stage ?? "Sync", remoteHttpStatus,
                result.Message ?? "CRM did not confirm synchronization.");
        if (!await SaveCrm(queue.Id, state, remoteId, remoteHttpStatus, savedError))
            return new("CRM", false, "Upsert", 500, "SaveSyncStatus",
                "The CRM request completed, but its synchronization state could not be saved. Verify CRM before retrying.");
        return result;
    }

    private async Task<bool> SaveCrm(
        long syncId, string state, string? remoteId, int? httpStatus, string? error)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            var saved = await history.Complete(syncId, state, remoteId, httpStatus, error, timeout.Token);
            if (!saved)
                logger.LogError("CRM queue {SyncId} changed before its outcome could be saved.", syncId);
            return saved;
        }
        catch (Exception exception) when (exception is NpgsqlException or OperationCanceledException)
        {
            logger.LogError(exception, "Unable to persist CRM outcome for queue {SyncId}.", syncId);
            return false;
        }
    }

    private static SyncPatientTargetResult FromAction(
        string target, string operation, IActionResult? action, string? stage = null)
    {
        var status = action switch
        {
            ObjectResult value => value.StatusCode ?? 200,
            ContentResult value => value.StatusCode ?? 200,
            StatusCodeResult value => value.StatusCode,
            _ => throw new InvalidOperationException("Synchronization returned an unsupported API result.")
        };
        var message = action is ObjectResult { Value: ProblemDetails problem }
            ? problem.Title : status >= 400 ? "The target system did not confirm the request." : null;
        return new(target, status is >= 200 and < 300, operation, status, stage, message);
    }

}
