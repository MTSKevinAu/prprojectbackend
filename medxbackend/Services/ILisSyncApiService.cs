using medxbackend.Models;
using Microsoft.AspNetCore.Mvc;

namespace medxbackend.Services;

public interface ILisSyncApiService
{
    Task<IActionResult> GetOrder(string centreid, CancellationToken cancellationToken);
    Task<ActionResult<LisTokenResponse>> GetToken(LisTokenRequest request, CancellationToken cancellationToken);
    Task<ActionResult<LisTokenResponse>> GetTokenWithConfiguredCredentials(CancellationToken cancellationToken);
    Task<IActionResult> CreateOrder(LisCreateOrderFromPatientRequest request, CancellationToken cancellationToken);
    Task<IActionResult> RetryCreateOrder(LisCreateOrderFromPatientRequest request, CancellationToken cancellationToken);
    Task<IActionResult> UpdateOrder(LisUpdateOrderFromPatientRequest request, CancellationToken cancellationToken);
    Task<IActionResult> UpdateOrderPatient(LisUpdateOrderFromPatientRequest request, CancellationToken cancellationToken);
    Task<IActionResult> CreateOrders(LisBatchCreateOrdersRequest request, CancellationToken cancellationToken);
    Task<ActionResult<LisBatchPreviewResponse>> PreviewOrders(LisPreviewOrdersRequest request, CancellationToken cancellationToken);
}
