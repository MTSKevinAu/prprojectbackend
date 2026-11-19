namespace medxbackend.Services;

public interface IPatientDetailsExportService
{
    Task<List<Dictionary<string, object?>>> GetRowsAsync(
        string[] statuses, CancellationToken cancellationToken);

    Task<List<Dictionary<string, object?>>> GetRowsByCentreIdAsync(
        string centreId, string targetSystem, CancellationToken cancellationToken);
}
