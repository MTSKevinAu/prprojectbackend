using medxbackend.Models;
using Microsoft.AspNetCore.Mvc;

namespace medxbackend.Services;

public interface ICrmSyncApiService
{
    Task<ActionResult<CrmSyncPatientDetailsResponse>> SyncPatientDetailsFinal(CrmSyncPatientDetailsRequest request, CancellationToken cancellationToken, bool updateOnly = false);
    Task<ActionResult<CrmBatchPreviewResponse>> PreviewPatientDetailsFinal(CrmPreviewRequest request, CancellationToken cancellationToken);
    Task<ActionResult<CrmTokenResponse>> GetCrmToken(CrmTokenRequest request, CancellationToken cancellationToken);
    Task<ActionResult<CrmTokenResponse>> GetCrmTokenWithConfiguredCredentials(CancellationToken cancellationToken);
}
