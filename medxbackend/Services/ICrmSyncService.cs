using medxbackend.Models;

namespace medxbackend.Services;

public interface ICrmSyncService
{
    CrmUpsertPreviewResponse Preview(
        IReadOnlyList<Dictionary<string, object?>> rows,
        CancellationToken cancellationToken);

    Task<CrmSyncPatientDetailsResponse> SyncAsync(
        IReadOnlyList<Dictionary<string, object?>> rows,
        CrmTokenResponse token,
        CancellationToken cancellationToken, bool updateOnly = false,
        Func<CancellationToken, Task<CrmTokenResponse?>>? refreshToken = null);
}
