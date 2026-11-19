using medxbackend.Models;

namespace medxbackend.Services;

public interface ICrmPreviewService
{
    Task<CrmBatchPreviewResponse> PrepareAsync(CrmPreviewRequest request, CancellationToken cancellationToken);
}
