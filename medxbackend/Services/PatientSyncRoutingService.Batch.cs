using medxbackend.Models;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace medxbackend.Services;

public sealed partial class PatientSyncRoutingService
{
    public async Task<IActionResult> SyncBatch(SyncBatchRequest request, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var validDateRange = TryGetDateRange(
            request.StartDate, request.EndDate, out var startDate, out var endDate);
        if (request.CentreIds is not { Length: > 0 }
            || request.CentreIds.Any(value => string.IsNullOrWhiteSpace(value) || value.Trim().Length > 255))
        {
            ModelState.AddModelError(nameof(request.CentreIds), "Provide at least one non-empty centreId, each at most 255 characters.");
            return ValidationProblem(ModelState);
        }
        if (!validDateRange)
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
        }
        catch (PostgresException exception) when (IsCentreIdConflict(exception))
        {
            logger.LogWarning("Batch centreid generation was blocked: {Reason}", exception.MessageText);
            return Problem(409, "Cannot generate centreid because the selected data contains gaps or duplicate records.",
                "No synchronization was submitted. Queue preparation for earlier dates or target systems may already have completed.");
        }
        catch (NpgsqlException exception)
        {
            logger.LogError(exception, "Unable to prepare orders for batch synchronization.");
            return Problem(500, "Unable to prepare orders for batch synchronization.",
                "No synchronization was submitted. Centreid generation and queue preparation may already have completed.");
        }

        var groups = new SortedDictionary<string, List<SyncRoutingPatient>>(StringComparer.Ordinal);
        try
        {
            foreach (var centreId in request.CentreIds.Select(value => value.Trim()).Distinct(StringComparer.Ordinal))
            {
                var patients = await repository.LoadPatients(centreId, cancellationToken);
                if (patients.Count == 0)
                    return Problem(404, "A selected final patient was not found. No synchronization was submitted.");
                if (patients.Count != 1)
                    return Problem(409, "Each centreId must identify exactly one final patient. No synchronization was submitted.");
                var patient = patients[0];
                var targets = await repository.LoadTargets(patient.FormType, cancellationToken);
                if (targets.Count == 0)
                    return Problem(409, "A selected patient's formtype has no enabled rules. No synchronization was submitted.");
                if (targets.Any(target => target is not ("LIS" or "CRM")))
                    return Problem(422, "An enabled rule targets an unsupported system. No synchronization was submitted.");
                foreach (var target in targets.Distinct(StringComparer.Ordinal))
                {
                    if (!groups.TryGetValue(target, out var group))
                        groups[target] = group = [];
                    group.Add(patient);
                }
            }
        }
        catch (NpgsqlException exception)
        {
            logger.LogError(exception, "Unable to select orders for batch synchronization.");
            return Problem(500, "Unable to load selected patients or rules. No synchronization was submitted.");
        }

        var results = new List<SyncBatchTargetResponse>();
        long? batchId = null;
        SyncPatientTargetResult outcome;
        try
        {
            outcome = await history.RunBatch("Batch", async () =>
            {
                batchId = history.BatchId;
                foreach (var (target, patients) in groups)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var orders = new List<SyncBatchOrderResponse>();
                    results.Add(new(target, batchId, false, null, null, orders));
                    var stopped = false;
                    foreach (var patient in patients)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (stopped)
                        {
                            orders.Add(NotAttempted(patient, target));
                            continue;
                        }
                        ModelState.Clear();
                        failureDetails.Clear(target);
                        SyncPatientTargetResult result;
                        try
                        {
                            var queue = await repository.GetOrCreateQueue(patient, target, cancellationToken);
                            result = await history.RunAttempt(queue.Id, target, () => target == "LIS"
                                ? SyncLis(patient, queue, cancellationToken)
                                : SyncCrm(patient, queue, cancellationToken));
                        }
                        catch (NpgsqlException exception)
                        {
                            logger.LogError(exception, "Unable to persist a {TargetSystem} batch delivery.", target);
                            result = new(target, false, "Sync", 500, "SyncQueue",
                                "Unable to process synchronization state. Verify history and remote outcome before retrying.");
                            if (history.HasAttempt)
                            {
                                orders.Add(new(patient.CentreId, patient.FormType, result));
                                throw;
                            }
                        }
                        orders.Add(new(patient.CentreId, patient.FormType, result));
                        var status = failureDetails.GetHttpStatus(target) ?? result.HttpStatus;
                        stopped = !result.Success && (status is 401 or 403 or 408 or 429 or >= 500
                            || result.Stage == "RequestToken" || failureDetails.GetStage(target) == "RequestToken"
                            || (status < 400 && result.HttpStatus >= 500)
                            || (target == "LIS" && result.Operation == "UpdateOrderAndPatient"
                                && result.HttpStatus >= 500)
                            || result.Stage is "SaveSyncStatus" or "SyncQueue" or "SaveSyncHistory");
                    }
                    results[^1] = new(target, batchId, orders.All(order => order.Result.Success), null, null, orders);
                }
                return new("Batch", results.All(result => result.Success), "Batch", 200);
            }, cancellationToken);
        }
        catch (NpgsqlException exception)
        {
            logger.LogError(exception, "Unable to process the shared synchronization batch.");
            outcome = new("Batch", false, "Batch", 500, "SyncBatch",
                "Unable to persist the synchronization batch. Verify its state and remote outcomes before retrying.");
        }
        foreach (var (target, patients) in groups)
        {
            var index = results.FindIndex(result => result.TargetSystem == target);
            var orders = index < 0 ? [] : results[index].Orders.ToList();
            foreach (var patient in patients.Skip(orders.Count))
                orders.Add(NotAttempted(patient, target));
            var result = new SyncBatchTargetResponse(target, batchId,
                outcome.Stage is null && orders.All(order => order.Result.Success),
                outcome.Stage, outcome.Message, orders);
            if (index < 0)
                results.Add(result);
            else
                results[index] = result;
        }
        var success = results.All(result => result.Success);
        return StatusCode(success ? 200 : 207, new SyncBatchResponse(success, results));
    }

    private static SyncBatchOrderResponse NotAttempted(SyncRoutingPatient patient, string target) =>
        new(patient.CentreId, patient.FormType, new(target, false, "NotAttempted", 424, "BatchStopped",
            "Not sent because this system's batch could not safely continue."));
}
