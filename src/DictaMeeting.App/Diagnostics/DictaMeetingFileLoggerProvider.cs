using System;
using System.IO;
using System.Text;
using DictaMeeting.App.Services;
using Microsoft.Extensions.Logging;

namespace DictaMeeting.App.Diagnostics;

/// <summary>
/// Proveedor de registro en archivo persistente para Microsoft.Extensions.Logging.
/// Escribe trazas estructuradas y sanitizadas en %LOCALAPPDATA%\DictaMeeting\logs\app.log
/// con rotación automática a 512 KB y retención de 2 archivos históricos (app.1.log, app.2.log).
/// </summary>
public sealed class DictaMeetingFileLoggerProvider : ILoggerProvider
{
    public const int MaxLogSizeBytes = 512 * 1024; // 512 KB
    public const string LogFileName = "app.log";

    private readonly string _logDirectory;
    private readonly object _lock = new();
    private bool _disposed;

    public DictaMeetingFileLoggerProvider(string? customLogDirectory = null)
    {
        _logDirectory = customLogDirectory ?? BootLogger.DefaultLogDirectory;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new FileLogger(categoryName, this);
    }

    internal void WriteLogEntry(string category, LogLevel level, string message, Exception? exception)
    {
        if (_disposed) return;

        lock (_lock)
        {
            try
            {
                if (!Directory.Exists(_logDirectory))
                {
                    Directory.CreateDirectory(_logDirectory);
                }

                var logPath = Path.Combine(_logDirectory, LogFileName);
                RotateIfNeeded(logPath);

                var sanitizedMsg = BootLogger.Sanitize(message);
                var corrId = CrashDiagnosticTracker.CorrelationId;
                var corrPart = !string.IsNullOrEmpty(corrId) ? $"[{corrId}] " : string.Empty;

                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{GetShortLevel(level)}] [{category}] {corrPart}{sanitizedMsg}";

                if (exception != null)
                {
                    line += $"{Environment.NewLine}  Excepción: {exception.GetType().FullName} - {BootLogger.Sanitize(exception.Message)}";
                    if (!string.IsNullOrEmpty(exception.StackTrace))
                    {
                        line += $"{Environment.NewLine}  Stack: {BootLogger.Sanitize(exception.StackTrace.Trim())}";
                    }
                }

                line += Environment.NewLine;
                File.AppendAllText(logPath, line, Encoding.UTF8);
            }
            catch
            {
                // El registro de diagnóstico nunca debe propagar excepciones ni bloquear la aplicación.
            }
        }
    }

    private void RotateIfNeeded(string logFile)
    {
        try
        {
            if (!File.Exists(logFile)) return;

            var fi = new FileInfo(logFile);
            if (fi.Length < MaxLogSizeBytes) return;

            var log1 = Path.Combine(_logDirectory, "app.1.log");
            var log2 = Path.Combine(_logDirectory, "app.2.log");

            try
            {
                if (File.Exists(log2)) File.Delete(log2);
            }
            catch { }

            try
            {
                if (File.Exists(log1)) File.Move(log1, log2, overwrite: true);
            }
            catch { }

            try
            {
                File.Move(logFile, log1, overwrite: true);
            }
            catch { }
        }
        catch { }
    }

    private static string GetShortLevel(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "LOG"
    };

    public void Dispose()
    {
        _disposed = true;
    }

    private sealed class FileLogger : ILogger
    {
        private readonly string _category;
        private readonly DictaMeetingFileLoggerProvider _provider;

        public FileLogger(string category, DictaMeetingFileLoggerProvider provider)
        {
            _category = category;
            _provider = provider;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var message = formatter(state, exception);
            _provider.WriteLogEntry(_category, logLevel, message, exception);
        }
    }
}
