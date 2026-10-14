using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using DictaMeeting.App.Services;

namespace DictaMeeting.App.Diagnostics;

/// <summary>
/// Registrador y rastreador de diagnóstico para cierres inesperados, excepciones no controladas
/// y auditoría del ciclo de vida de operaciones críticas.
/// </summary>
public static class CrashDiagnosticTracker
{
    private const int MaxBreadcrumbs = 35;
    private const int MaxRetainedCrashReports = 10;

    private static readonly ConcurrentQueue<string> _breadcrumbs = new();
    private static readonly AsyncLocal<string?> _currentOperation = new();
    private static readonly AsyncLocal<string?> _currentMeetingId = new();
    private static readonly AsyncLocal<string?> _correlationId = new();

    private static volatile string? _globalOperation = "Startup";
    private static volatile string? _globalMeetingId;

    public static string? CurrentOperation
    {
        get => _currentOperation.Value ?? _globalOperation;
        set
        {
            _currentOperation.Value = value;
            _globalOperation = value;
        }
    }

    public static string? CurrentMeetingId
    {
        get => _currentMeetingId.Value ?? _globalMeetingId;
        set
        {
            _currentMeetingId.Value = value;
            _globalMeetingId = value;
        }
    }

    public static string? CorrelationId
    {
        get => _correlationId.Value;
        set => _correlationId.Value = value;
    }

    public static string DefaultCrashReportsDirectory =>
        Path.Combine(BootLogger.DefaultLogDirectory, "crash_reports");

    /// <summary>
    /// Establece el contexto de operación actual con un identificador de correlación opcional.
    /// </summary>
    public static IDisposable BeginOperation(string operation, string? meetingId = null, string? correlationId = null)
    {
        var prevOp = _currentOperation.Value;
        var prevId = _currentMeetingId.Value;
        var prevCorr = _correlationId.Value;

        var effectiveCorr = correlationId ?? Guid.NewGuid().ToString("N")[..8];
        _currentOperation.Value = operation;
        _globalOperation = operation;
        _currentMeetingId.Value = meetingId;
        if (!string.IsNullOrEmpty(meetingId)) _globalMeetingId = meetingId;
        _correlationId.Value = effectiveCorr;

        AddBreadcrumb($"[Inicio Op] {operation} (Meeting: {meetingId ?? "N/A"}, Corr: {effectiveCorr})");

        return new OperationScope(() =>
        {
            AddBreadcrumb($"[Fin Op] {operation} (Meeting: {meetingId ?? "N/A"}, Corr: {effectiveCorr})");
            _currentOperation.Value = prevOp;
            _globalOperation = prevOp;
            _currentMeetingId.Value = prevId;
            _globalMeetingId = prevId;
            _correlationId.Value = prevCorr;
        });
    }

    /// <summary>
    /// Agrega una miga de pan de diagnóstico al búfer circular en memoria.
    /// </summary>
    public static void AddBreadcrumb(string message)
    {
        try
        {
            var sanitized = BootLogger.Sanitize(message);
            var entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [TID:{Environment.CurrentManagedThreadId:D2}] {sanitized}";
            _breadcrumbs.Enqueue(entry);

            while (_breadcrumbs.Count > MaxBreadcrumbs && _breadcrumbs.TryDequeue(out _))
            {
            }
        }
        catch
        {
            // Las operaciones de diagnóstico nunca deben fallar ni propagar excepciones.
        }
    }

    /// <summary>
    /// Genera y persiste un informe detallado de fallo en %LOCALAPPDATA%\DictaMeeting\logs\crash_reports\.
    /// </summary>
    public static string? RecordCrashReport(
        Exception? exception,
        string source,
        bool isTerminating = false,
        string? customDirectory = null)
    {
        try
        {
            var reportsDir = customDirectory ?? DefaultCrashReportsDirectory;
            if (!Directory.Exists(reportsDir))
            {
                Directory.CreateDirectory(reportsDir);
            }

            // Poda automática de informes antiguos para no consumir espacio innecesario
            PruneOldReports(reportsDir);

            var timestamp = DateTime.Now;
            var fileName = $"crash_{timestamp:yyyy-MM-dd_HHmmss_fff}.log";
            var filePath = Path.Combine(reportsDir, fileName);

            var sb = new StringBuilder();
            sb.AppendLine("================================================================================");
            sb.AppendLine("                   INFORME DE CIERRE INESPERADO / DIAGNÓSTICO");
            sb.AppendLine("================================================================================");
            sb.AppendLine($"Fecha/Hora Local : {timestamp:yyyy-MM-dd HH:mm:ss.fff}");
            sb.AppendLine($"Fecha/Hora UTC   : {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}Z");
            sb.AppendLine($"Versión App      : {GetAppVersion()}");
            sb.AppendLine($"Origen del Evento: {source}");
            sb.AppendLine($"¿Termina Proceso?: {(isTerminating ? "SÍ (Fatal)" : "NO (Recuperable o advertencia)")}");
            sb.AppendLine($"Operación Activa : {CurrentOperation ?? "Desconocida / Idle"}");
            sb.AppendLine($"Reunión ID       : {CurrentMeetingId ?? "N/A"}");
            sb.AppendLine($"Correlación ID   : {CorrelationId ?? "N/A"}");
            sb.AppendLine();

            sb.AppendLine("--------------------------------------------------------------------------------");
            sb.AppendLine("ENTORNO Y ESTADO DEL PROCESO");
            sb.AppendLine("--------------------------------------------------------------------------------");
            try
            {
                var proc = Process.GetCurrentProcess();
                sb.AppendLine($"Proceso ID       : {proc.Id} ({proc.ProcessName})");
                sb.AppendLine($"Tiempo Activo    : {DateTime.Now - proc.StartTime}");
                sb.AppendLine($"Memoria Privada  : {proc.PrivateMemorySize64 / (1024 * 1024)} MB");
                sb.AppendLine($"Working Set      : {proc.WorkingSet64 / (1024 * 1024)} MB");
                sb.AppendLine($"Heap Administrado: {GC.GetTotalMemory(false) / (1024 * 1024)} MB");
                sb.AppendLine($"Hilos Activos    : {proc.Threads.Count}");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"[No se pudieron consultar métricas de proceso: {ex.Message}]");
            }

            sb.AppendLine($"Hilo Actual      : ID {Environment.CurrentManagedThreadId} ('{Thread.CurrentThread.Name ?? "SinNombre"}'), ThreadPool: {Thread.CurrentThread.IsThreadPoolThread}, Background: {Thread.CurrentThread.IsBackground}");
            sb.AppendLine($"Sistema Operativo: {Environment.OSVersion} ({(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")})");
            sb.AppendLine($"Runtime .NET     : {Environment.Version} (Proceso {(Environment.Is64BitProcess ? "64-bit" : "32-bit")})");
            sb.AppendLine($"Procesadores     : {Environment.ProcessorCount}");
            sb.AppendLine();

            sb.AppendLine("--------------------------------------------------------------------------------");
            sb.AppendLine("DETALLES DE LA EXCEPCIÓN");
            sb.AppendLine("--------------------------------------------------------------------------------");
            if (exception != null)
            {
                FormatExceptionDetails(sb, exception, isInner: false);
            }
            else
            {
                sb.AppendLine("No se proporcionó objeto de excepción (posible terminación externa o señal nativa).");
            }
            sb.AppendLine();

            sb.AppendLine("--------------------------------------------------------------------------------");
            sb.AppendLine("ÚLTIMAS ACCIONES (MIGAS DE PAN)");
            sb.AppendLine("--------------------------------------------------------------------------------");
            var crumbs = _breadcrumbs.ToArray();
            if (crumbs.Length > 0)
            {
                foreach (var crumb in crumbs)
                {
                    sb.AppendLine(crumb);
                }
            }
            else
            {
                sb.AppendLine("[No se registraron migas de pan previas]");
            }

            sb.AppendLine();
            sb.AppendLine("================================================================================");
            sb.AppendLine("FIN DEL INFORME");
            sb.AppendLine("================================================================================");

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
            return filePath;
        }
        catch
        {
            // El guardado del informe de diagnóstico nunca debe propagar excepciones
            return null;
        }
    }

    private static void FormatExceptionDetails(StringBuilder sb, Exception ex, bool isInner)
    {
        var prefix = isInner ? "  [InnerException] " : string.Empty;
        sb.AppendLine($"{prefix}Tipo         : {ex.GetType().FullName}");
        sb.AppendLine($"{prefix}Mensaje      : {BootLogger.Sanitize(ex.Message)}");
        sb.AppendLine($"{prefix}HResult      : 0x{ex.HResult:X8}");
        if (ex.TargetSite != null)
        {
            sb.AppendLine($"{prefix}Método Origen: {ex.TargetSite.DeclaringType?.FullName}.{ex.TargetSite.Name}");
        }

        if (!string.IsNullOrEmpty(ex.StackTrace))
        {
            sb.AppendLine($"{prefix}Stack Trace  :");
            var lines = ex.StackTrace.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            foreach (var line in lines)
            {
                sb.AppendLine($"{prefix}  {BootLogger.Sanitize(line.Trim())}");
            }
        }

        if (ex is AggregateException agg && agg.InnerExceptions.Count > 1)
        {
            sb.AppendLine($"{prefix}Sub-excepciones en AggregateException ({agg.InnerExceptions.Count}):");
            int i = 1;
            foreach (var inner in agg.InnerExceptions)
            {
                sb.AppendLine($"{prefix}  --- Sub-excepción #{i++} ---");
                FormatExceptionDetails(sb, inner, isInner: true);
            }
        }
        else if (ex.InnerException != null)
        {
            sb.AppendLine();
            FormatExceptionDetails(sb, ex.InnerException, isInner: true);
        }
    }

    private static void PruneOldReports(string reportsDir)
    {
        try
        {
            var files = new DirectoryInfo(reportsDir)
                .GetFiles("crash_*.log")
                .OrderByDescending(f => f.CreationTimeUtc)
                .ToList();

            if (files.Count >= MaxRetainedCrashReports)
            {
                for (int i = MaxRetainedCrashReports - 1; i < files.Count; i++)
                {
                    try
                    {
                        files[i].Delete();
                    }
                    catch { }
                }
            }
        }
        catch { }
    }

    private static string GetAppVersion()
    {
        try
        {
            return Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0.0";
        }
        catch
        {
            return "1.0.0.0";
        }
    }

    private sealed class OperationScope : IDisposable
    {
        private readonly Action _onDispose;
        private int _disposed;

        public OperationScope(Action onDispose)
        {
            _onDispose = onDispose;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _onDispose();
            }
        }
    }
}
