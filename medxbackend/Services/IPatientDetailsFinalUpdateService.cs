using System.Text.Json;
using medxbackend.Models;

namespace medxbackend.Services;

public interface IPatientDetailsFinalUpdateService
{
    Task<JsonElement> Update(UpdatePatientDetailsFinalRequest request, int userId, CancellationToken cancellationToken);
}
