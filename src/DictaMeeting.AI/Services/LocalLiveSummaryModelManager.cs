using System.IO;
using System.Net.Http;
using DictaMeeting.AI.Interfaces;
using Microsoft.Extensions.Logging;

namespace DictaMeeting.AI.Services;

/// <summary>
/// Gestor de descarga, verificación y almacenamiento del modelo local GGUF para resúmenes.
/// Administra el modelo Qwen2.5-1.5B-Instruct en cuantización Q4_K_M.
/// </summary>
public class LocalLiveSummaryModelManager : ILiveSummaryModelManager
{
    public const string DefaultModelName = "Qwen2.5-1.5B-Instruct";
    public const string DefaultQuantization = "Q4_K_M";
    public const string DefaultFileName = "qwen2.5-1.5b-instruct-q4_k_m.gguf";
    public const long ExpectedSizeBytes = 986L * 1024 * 1024; // ~1,034,000,000 bytes
    public const string DefaultDownloadUrl = "https://huggingface.co/Qwen/Qwen2.5-1.5B-Instruct-GGUF/resolve/main/qwen2.5-1.5b-instruct-q4_k_m.gguf";

    private string _modelsFolder;
    private readonly HttpClient _httpClient;
    private readonly ILogger<LocalLiveSummaryModelManager>? _logger;

    public string ModelsDirectory => _modelsFolder;
    public string ModelName => DefaultModelName;
    public string Quantization => DefaultQuantization;
    public long ModelSizeBytes => ExpectedSizeBytes;
    public string LocalModelPath => Path.Combine(_modelsFolder, DefaultFileName);

    public LocalLiveSummaryModelManager(
        string? modelsFolder = null,
        HttpClient? httpClient = null,
        ILogger<LocalLiveSummaryModelManager>? logger = null)
    {
        _logger = logger;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(30) };

        if (!string.IsNullOrEmpty(modelsFolder))
        {
            _modelsFolder = Path.GetFullPath(modelsFolder);
        }
        else
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _modelsFolder = Path.Combine(localAppData, "DictaMeeting", "models", "summary");
        }

        try
        {
            if (!Directory.Exists(_modelsFolder))
            {
                Directory.CreateDirectory(_modelsFolder);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "No se pudo asegurar el directorio de modelos de resumen en '{Path}'.", _modelsFolder);
        }
    }

    public void SetModelsDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("La ruta de modelos de resumen no puede estar vacía.", nameof(path));
        }

        var fullPath = Path.GetFullPath(path);
        _modelsFolder = fullPath;

        try
        {
            if (!Directory.Exists(_modelsFolder))
            {
                Directory.CreateDirectory(_modelsFolder);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "No se pudo asegurar el directorio de modelos de resumen tras actualizar ruta a '{Path}'.", _modelsFolder);
        }
    }

    public string GetModelPath()
    {
        // 1. Comprobar si está en la carpeta de usuario
        if (File.Exists(LocalModelPath) && IsValidFile(LocalModelPath))
        {
            return LocalModelPath;
        }

        // 2. Comprobar si está incluido junto al binario de la aplicación
        var appPath = Path.Combine(AppContext.BaseDirectory, "models", "summary", DefaultFileName);
        if (File.Exists(appPath) && IsValidFile(appPath))
        {
            return appPath;
        }

        return LocalModelPath;
    }

    public bool IsModelDownloaded()
    {
        return (File.Exists(LocalModelPath) && IsValidFile(LocalModelPath)) ||
               (File.Exists(Path.Combine(AppContext.BaseDirectory, "models", "summary", DefaultFileName)) &&
                IsValidFile(Path.Combine(AppContext.BaseDirectory, "models", "summary", DefaultFileName)));
    }

    private static bool IsValidFile(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            // El modelo Q4_K_M mide aproximadamente 986 MB (mínimo 900 MB para considerarse íntegro)
            return fi.Length >= 900L * 1024 * 1024;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string> EnsureModelDownloadedAsync(
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var resolvedPath = GetModelPath();
        if (IsModelDownloaded())
        {
            _logger?.LogInformation("Modelo de resumen '{Name}' ya disponible en: {Path}", ModelName, resolvedPath);
            progress?.Report(1.0);
            return resolvedPath;
        }

        var tempPath = LocalModelPath + ".download";
        _logger?.LogInformation("Descargando modelo de resumen '{Name}' desde {Url}...", ModelName, DefaultDownloadUrl);

        try
        {
            using var response = await _httpClient.GetAsync(DefaultDownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            long totalBytes = response.Content.Headers.ContentLength ?? ExpectedSizeBytes;
            if (totalBytes <= 0) totalBytes = ExpectedSizeBytes;

            await using (var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var fileStream = File.Create(tempPath))
            {
                var buffer = new byte[81920];
                long totalBytesRead = 0;
                int read;

                while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    totalBytesRead += read;

                    if (progress != null)
                    {
                        double p = Math.Min(0.99, (double)totalBytesRead / totalBytes);
                        progress.Report(p);
                    }
                }
            }

            if (!IsValidFile(tempPath))
            {
                throw new InvalidDataException($"El archivo descargado no alcanza el tamaño mínimo esperado de un modelo GGUF íntegro ({ModelName}).");
            }

            if (File.Exists(LocalModelPath))
            {
                File.Delete(LocalModelPath);
            }

            File.Move(tempPath, LocalModelPath);
            progress?.Report(1.0);

            _logger?.LogInformation("Modelo de resumen '{Name}' descargado con éxito en: {Path}", ModelName, LocalModelPath);
            return LocalModelPath;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error al descargar el modelo de resumen '{Name}'.", ModelName);
            try
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
            catch { }
            throw;
        }
    }

    public bool DeleteModel()
    {
        try
        {
            if (File.Exists(LocalModelPath))
            {
                File.Delete(LocalModelPath);
                _logger?.LogInformation("Modelo de resumen '{Name}' eliminado del disco.", ModelName);
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error al eliminar el modelo de resumen '{Name}'.", ModelName);
        }

        return false;
    }
}
