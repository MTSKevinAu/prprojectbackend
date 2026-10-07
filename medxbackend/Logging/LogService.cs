using Microsoft.Extensions.Logging;

namespace medxbackend.Logging;

public sealed class LogService : ILogService
{
    private readonly ILogger<LogService> _logger;

    public LogService(ILogger<LogService> logger)
    {
        _logger = logger;
    }

    public void Debug(string message, params object?[] args)
    {
        _logger.LogDebug(message, args);
    }

    public void Information(string message, params object?[] args)
    {
        _logger.LogInformation(message, args);
    }

    public void Warning(string message, params object?[] args)
    {
        _logger.LogWarning(message, args);
    }

    public void Error(Exception exception, string message, params object?[] args)
    {
        _logger.LogError(exception, message, args);
    }
}