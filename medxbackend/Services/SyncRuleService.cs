using medxbackend.Data;
using medxbackend.Models;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using NpgsqlTypes;

namespace medxbackend.Services;

public sealed class SyncRuleService(
    DbHelper dbHelper,
    SyncApiActionContext actionContext,
    ILogger<SyncRuleService> logger) : SyncApiService(actionContext), ISyncRuleService
{
    public Task<IActionResult> List(string? formType, CancellationToken cancellationToken) =>
        Execute(async () => Ok(await dbHelper.QueryAsync(
            """
            SELECT id, formtype, target_system, is_enabled
            FROM public.patientdetails_sync_rules
            WHERE @formType IS NULL OR formtype = @formType
            ORDER BY formtype, target_system
            """,
            Map,
            [new NpgsqlParameter("formType", NpgsqlDbType.Varchar) { Value = (object?)formType ?? DBNull.Value }],
            cancellationToken)));

    public Task<IActionResult> Get(string formType, string targetSystem, CancellationToken cancellationToken) =>
        Execute(async () =>
        {
            var rows = await dbHelper.QueryAsync(
                """
                SELECT id, formtype, target_system, is_enabled
                FROM public.patientdetails_sync_rules
                WHERE formtype = @formType AND target_system = @targetSystem
                """,
                Map, Keys(formType, targetSystem), cancellationToken);
            return rows.Count == 0 ? Problem(404, "Synchronization rule not found.") : Ok(rows[0]);
        });

    public Task<IActionResult> Create(CreateSyncRuleRequest request, CancellationToken cancellationToken) =>
        Execute(async () =>
        {
            var inserted = await dbHelper.QueryAsync(
                """
                INSERT INTO public.patientdetails_sync_rules (formtype, target_system, is_enabled)
                VALUES (@formType, @targetSystem, @enabled)
                ON CONFLICT (formtype, target_system) DO NOTHING
                RETURNING id, formtype, target_system, is_enabled
                """,
                Map,
                [.. Keys(request.FormType, request.TargetSystem), new NpgsqlParameter("enabled", request.IsEnabled)],
                cancellationToken);
            if (inserted.Count == 0)
                return Problem(409, "A synchronization rule already exists for this formType and targetSystem.");
            return new CreatedResult(
                $"{Response.HttpContext.Request.PathBase}/api/Sync/rules/{inserted[0].Id}",
                inserted[0]);
        });

    public Task<IActionResult> Update(
        string formType, string targetSystem, UpdateSyncRuleRequest request, CancellationToken cancellationToken) =>
        Execute(async () =>
        {
            if (request.IsEnabled is not bool enabled)
            {
                ModelState.AddModelError(nameof(request.IsEnabled), "isEnabled is required.");
                return ValidationProblem(ModelState);
            }
            var updated = await dbHelper.QueryAsync(
                """
                UPDATE public.patientdetails_sync_rules SET is_enabled = @enabled
                WHERE formtype = @formType AND target_system = @targetSystem
                RETURNING id, formtype, target_system, is_enabled
                """,
                Map,
                [.. Keys(formType, targetSystem), new NpgsqlParameter("enabled", enabled)],
                cancellationToken);
            return updated.Count == 0 ? Problem(404, "Synchronization rule not found.")
                : Ok(updated[0]);
        });

    public Task<IActionResult> Delete(string formType, string targetSystem, CancellationToken cancellationToken) =>
        Execute(async () =>
        {
            var deleted = await dbHelper.ExecuteNonQueryAsync(
                """
                DELETE FROM public.patientdetails_sync_rules
                WHERE formtype = @formType AND target_system = @targetSystem
                """,
                Keys(formType, targetSystem), cancellationToken);
            return deleted == 0 ? Problem(404, "Synchronization rule not found.")
                : Ok(new DeleteSyncRuleResponse(true, "Synchronization rule deleted successfully.", formType, targetSystem));
        });

    public Task<IActionResult> GetById(long id, CancellationToken cancellationToken) =>
        Execute(async () =>
        {
            var rows = await dbHelper.QueryAsync(
                """
                SELECT id, formtype, target_system, is_enabled
                FROM public.patientdetails_sync_rules WHERE id = @id
                """,
                Map, [new NpgsqlParameter("id", id)], cancellationToken);
            return rows.Count == 0 ? Problem(404, "Synchronization rule not found.") : Ok(rows[0]);
        });

    public Task<IActionResult> UpdateById(long id, UpdateSyncRuleRequest request, CancellationToken cancellationToken) =>
        Execute(async () =>
        {
            if (request.IsEnabled is not bool enabled)
            {
                ModelState.AddModelError(nameof(request.IsEnabled), "isEnabled is required.");
                return ValidationProblem(ModelState);
            }
            var rows = await dbHelper.QueryAsync(
                """
                UPDATE public.patientdetails_sync_rules SET is_enabled = @enabled
                WHERE id = @id
                RETURNING id, formtype, target_system, is_enabled
                """,
                Map, [new NpgsqlParameter("id", id), new NpgsqlParameter("enabled", enabled)],
                cancellationToken);
            return rows.Count == 0 ? Problem(404, "Synchronization rule not found.") : Ok(rows[0]);
        });

    public Task<IActionResult> DeleteById(long id, CancellationToken cancellationToken) =>
        Execute(async () =>
        {
            var deleted = await dbHelper.ExecuteNonQueryAsync(
                "DELETE FROM public.patientdetails_sync_rules WHERE id = @id",
                [new NpgsqlParameter("id", id)], cancellationToken);
            return deleted == 0 ? Problem(404, "Synchronization rule not found.") : NoContent();
        });

    private async Task<IActionResult> Execute(Func<Task<IActionResult>> operation)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            return await operation();
        }
        catch (NpgsqlException exception)
        {
            logger.LogError(exception, "Unable to perform the synchronization rule operation.");
            return Problem(500, "Unable to perform the synchronization rule operation.",
                "If the connection failed during a write, check the rule before retrying.");
        }
    }

    private static NpgsqlParameter[] Keys(string formType, string targetSystem) =>
        [new NpgsqlParameter("formType", formType), new NpgsqlParameter("targetSystem", targetSystem)];

    private static SyncRuleResponse Map(NpgsqlDataReader reader) =>
        new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3));
}
