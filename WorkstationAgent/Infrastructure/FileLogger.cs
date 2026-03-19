using System.Text;

namespace WorkstationAgent.Infrastructure;

internal sealed class FileLogger
{
    private readonly string _logFilePath;
    private readonly Lock _sync = new();

    public FileLogger(string? configuredPath)
    {
        _logFilePath = string.IsNullOrWhiteSpace(configuredPath)
            ? new AgentPaths().LogFilePath
            : configuredPath;

        var directory = Path.GetDirectoryName(_logFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public void Info(string message)
    {
        Write("INFO", message);
    }

    public void Error(string message, Exception? exception = null)
    {
        var builder = new StringBuilder(message);
        if (exception is not null)
        {
            builder.AppendLine();
            builder.Append(exception);
        }

        Write("ERROR", builder.ToString());
    }

    private void Write(string level, string message)
    {
        var line = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}] [{level}] {message}{Environment.NewLine}";
        lock (_sync)
        {
            File.AppendAllText(_logFilePath, line, Encoding.UTF8);
        }
    }
}
