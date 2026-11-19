using System.Text.Json;

namespace medxbackend.Services;

public interface IPatientDetailsFinalQueryService
{
    Task<IReadOnlyList<JsonElement>> GetByDailyNo(string dailyNo, CancellationToken cancellationToken);
}
