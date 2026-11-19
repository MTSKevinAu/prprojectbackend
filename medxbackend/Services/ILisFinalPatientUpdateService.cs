using medxbackend.Models;

namespace medxbackend.Services;

public interface ILisFinalPatientUpdateService
{
    Task<LisFinalPatientUpdateResult> SyncIfAlreadySucceeded(
        string dailyNo, string formType, CancellationToken cancellationToken);
}
