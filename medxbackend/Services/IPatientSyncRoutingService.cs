using medxbackend.Models;
using Microsoft.AspNetCore.Mvc;

namespace medxbackend.Services;

public interface IPatientSyncRoutingService
{
    Task<IActionResult> SyncBatch(SyncBatchRequest request, CancellationToken cancellationToken);
    Task<IActionResult> GetRecent(int? count, string? targetSystem, CancellationToken cancellationToken);
    Task<IActionResult> GetStatistics(CancellationToken cancellationToken);
    Task<IActionResult> Preview(SyncOrderPreviewRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<SyncPatientTargetResult>> UpdateAlreadySynced(
        SyncRoutingPatient patient, CancellationToken cancellationToken);
    Task<IActionResult> Sync(SyncPatientRequest request, CancellationToken cancellationToken);
    Task<IActionResult> CreateLisOrder(SyncPatientRequest request, CancellationToken cancellationToken);
}
