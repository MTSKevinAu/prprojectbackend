using medxbackend.Models;
using Microsoft.AspNetCore.Mvc;

namespace medxbackend.Services;

public interface ISyncRuleService
{
    Task<IActionResult> List(string? formType, CancellationToken cancellationToken);
    Task<IActionResult> Get(string formType, string targetSystem, CancellationToken cancellationToken);
    Task<IActionResult> Create(CreateSyncRuleRequest request, CancellationToken cancellationToken);
    Task<IActionResult> Update(string formType, string targetSystem, UpdateSyncRuleRequest request, CancellationToken cancellationToken);
    Task<IActionResult> Delete(string formType, string targetSystem, CancellationToken cancellationToken);
    Task<IActionResult> GetById(long id, CancellationToken cancellationToken);
    Task<IActionResult> UpdateById(long id, UpdateSyncRuleRequest request, CancellationToken cancellationToken);
    Task<IActionResult> DeleteById(long id, CancellationToken cancellationToken);
}
