using System;
using System.IO;
using System.Text.RegularExpressions;

namespace DictaMeeting.App.Services;

/// <summary>
/// Proporciona un sistema de registro ligero, no bloqueante y seguro para la fase de arranque de DictaMeeting.
/// Aplica rotación automática con un tamaño máximo de 256 KB y un máximo de 2 archivos históricos (boot.1.log, boot.2.log).
/// </summary>
public static class BootLogger
{
    public const int MaxLogSizeBytes = 256 * 1024; // 256 KB
    public const int MaxHistoryFiles = 2;
    public const string LogFileName = "boot.log";

    private static readonly Regex SkKeyRegex = new(@"\bsk(-or-v1)?-[a-zA-Z0-9_\-]{8,}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BearerTokenRegex = new(@"Bearer\s+[a-zA-Z0-9_\-\.]{8,}", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SensitiveParamRegex = new(@"(api[_-]?key|secret|password|token)\s*[:=]\s*[""']?([^""'\s;,]{4,})[""']?", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Directorio predeterminado de logs en %LOCALAPPDATA%\DictaMeeting\logs.
    /// </summary>
    public static string DefaultLogDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DictaMeeting", "logs");

    /// <summary>
    /// Obtiene la ruta completa al archivo de log activo.
    /// </summary>
    public static string GetLogFilePath(string? customLogDirectory = null)
    {
        var dir = customLogDirectory ?? DefaultLogDirectory;
        return Path.Combine(dir, LogFileName);
    }

    /// <summary>
    /// Registra un mensaje de diagnóstico en boot.log con rotación y sanitización previa.
    /// Si ocurre cualquier fallo de E/S o bloqueo de archivo, nunca propaga excepciones ni retrasa el arranque.
    /// </summary>
    public static void LogBoot(string message, string? customLogDirectory = null)
    {
        try
        {
            var logDir = customLogDirectory ?? DefaultLogDirectory;
            if (!Directory.Exists(logDir))
            {
                Directory.CreateDirectory(logDir);
            }

            var logFile = Path.Combine(logDir, LogFileName);

            Rotate(logDir, logFile, MaxLogSizeBytes);

            var sanitized = Sanitize(message);
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {sanitized}{Environment.NewLine}";
            File.AppendAllText(logFile, line);
        }
        catch
        {
            // El logging de diagnóstico nunca debe bloquear la aplicación ni propagar excepciones.
        }
    }

    /// <summary>
    /// Aplica rotación si el archivo de log activo supera el límite de tamaño especificado.
    /// Mantiene como máximo 2 archivos históricos (boot.1.log y boot.2.log).
    /// </summary>
    public static void Rotate(string logDir, string logFile, long maxSizeBytes = MaxLogSizeBytes)
    {
        try
        {
            if (!File.Exists(logFile))
            {
                return;
            }

            var fi = new FileInfo(logFile);
            if (fi.Length < maxSizeBytes)
            {
                return;
            }

            var log1 = Path.Combine(logDir, "boot.1.log");
            var log2 = Path.Combine(logDir, "boot.2.log");
            var legacyOld = Path.Combine(logDir, "boot.old.log");

            // Limpieza de archivos históricos obsoletos de versiones previas si existen
            try
            {
                if (File.Exists(legacyOld))
                {
                    File.Delete(legacyOld);
                }
            }
            catch { }

            // Si existe boot.2.log, se elimina para mantener estrictamente un máximo de 2 históricos
            try
            {
                if (File.Exists(log2))
                {
                    File.Delete(log2);
                }
            }
            catch { }

            // boot.1.log -> boot.2.log
            try
            {
                if (File.Exists(log1))
                {
                    File.Move(log1, log2, overwrite: true);
                }
            }
            catch { }

            // boot.log -> boot.1.log
            try
            {
                File.Move(logFile, log1, overwrite: true);
            }
            catch { }
        }
        catch
        {
            // La rotación nunca debe propagar excepciones ni retrasar la ejecución.
        }
    }

    /// <summary>
    /// Enmascara claves de API, tokens y credenciales que pudieran aparecer en trazas de error o mensajes.
    /// </summary>
    public static string Sanitize(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return string.Empty;
        }

        var result = SkKeyRegex.Replace(message, "sk-***[REDACTED]***");
        result = BearerTokenRegex.Replace(result, "Bearer [REDACTED]");
        result = SensitiveParamRegex.Replace(result, "$1=[REDACTED]");

        return result;
    }
}
