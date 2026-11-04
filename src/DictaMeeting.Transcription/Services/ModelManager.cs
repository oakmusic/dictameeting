using DictaMeeting.Transcription.Interfaces;
using DictaMeeting.Transcription.Models;
using Microsoft.Extensions.Logging;
using Whisper.net.Ggml;

namespace DictaMeeting.Transcription.Services;

public class ModelManager : IModelManager
{
    private string _modelsFolder;
    private readonly ILogger<ModelManager>? _logger;

    public string ModelsDirectory => _modelsFolder;

    public ModelManager(string? modelsFolder = null, ILogger<ModelManager>? logger = null, HttpClient? httpClient = null)
    {
        _logger = logger;
        if (httpClient != null)
        {
            SharedHttpClient = httpClient;
        }
        if (!string.IsNullOrEmpty(modelsFolder))
        {
            _modelsFolder = Path.GetFullPath(modelsFolder);
        }
        else
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _modelsFolder = Path.Combine(localAppData, "DictaMeeting", "models");
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
            _logger?.LogWarning(ex, "No se pudo asegurar el directorio de modelos en '{Path}'.", _modelsFolder);
        }
    }

    public void SetModelsDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("La ruta de modelos no puede estar vacía.", nameof(path));
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
            _logger?.LogWarning(ex, "No se pudo asegurar el directorio de modelos tras actualizar ruta a '{Path}'.", _modelsFolder);
        }
    }

    private bool IsDefaultModelsFolder()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var defaultFolder = Path.Combine(localAppData, "DictaMeeting", "models");
        return string.Equals(
            Path.GetFullPath(_modelsFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(defaultFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }

    public Task<HardwareCapabilities> DetectHardwareAsync()
    {
        return Task.FromResult(HardwareDetector.Detect(_logger));
    }

    public IReadOnlyList<TranscriptionModelInfo> GetAvailableModels()
    {
        var models = new List<TranscriptionModelInfo>
        {
            new()
            {
                Size = ModelSize.Qwen3_06B,
                Engine = ModelEngineType.Qwen3,
                FamilyName = "Qwen3-ASR (Alibaba)",
                Name = "Qwen3-ASR 0.6B",
                Description = "Rápido en CPU y adecuado para reuniones técnicas.",
                FileSizeBytes = 950L * 1024 * 1024,
                LocalPath = Path.Combine(_modelsFolder, "qwen3-asr-0.6b-int8"),
                DownloadUrl = "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-qwen3-asr-0.6B-int8-2026-03-25.tar.bz2",
                IsDirectory = true
            },
            new()
            {
                Size = ModelSize.Qwen3_17B,
                Engine = ModelEngineType.Qwen3,
                FamilyName = "Qwen3-ASR (Alibaba)",
                Name = "Qwen3-ASR 1.7B",
                Description = "Máxima fidelidad para reuniones técnicas y audio complejo.",
                FileSizeBytes = 2404222421L,
                LocalPath = Path.Combine(_modelsFolder, "qwen3-asr-1.7b-int8"),
                DownloadUrl = "https://huggingface.co/thieunv-asilla/sherpa-onnx-qwen3-asr-1.7B-int8",
                IsDirectory = true,
                DownloadFiles = new List<ModelFileDownloadInfo>
                {
                    new()
                    {
                        RelativePath = "conv_frontend.onnx",
                        PrimaryUrl = "https://huggingface.co/thieunv-asilla/sherpa-onnx-qwen3-asr-1.7B-int8/resolve/main/conv_frontend.onnx",
                        FallbackUrl = "https://huggingface.co/ilmina/qwen3-asr-1.7b-sherpa-onnx/resolve/main/conv_frontend.onnx",
                        ExpectedSizeBytes = 48_080_441
                    },
                    new()
                    {
                        RelativePath = "encoder.int8.onnx",
                        PrimaryUrl = "https://huggingface.co/thieunv-asilla/sherpa-onnx-qwen3-asr-1.7B-int8/resolve/main/encoder.int8.onnx",
                        FallbackUrl = "https://huggingface.co/ilmina/qwen3-asr-1.7b-sherpa-onnx/resolve/main/encoder.int8.onnx",
                        ExpectedSizeBytes = 314_222_162
                    },
                    new()
                    {
                        RelativePath = "decoder.int8.onnx",
                        PrimaryUrl = "https://huggingface.co/thieunv-asilla/sherpa-onnx-qwen3-asr-1.7B-int8/resolve/main/decoder.int8.onnx",
                        FallbackUrl = "https://huggingface.co/ilmina/qwen3-asr-1.7b-sherpa-onnx/resolve/main/decoder.int8.onnx",
                        ExpectedSizeBytes = 2_037_458_645
                    },
                    new()
                    {
                        RelativePath = Path.Combine("tokenizer", "merges.txt"),
                        PrimaryUrl = "https://huggingface.co/thieunv-asilla/sherpa-onnx-qwen3-asr-1.7B-int8/resolve/main/tokenizer/merges.txt",
                        FallbackUrl = "https://huggingface.co/ilmina/qwen3-asr-1.7b-sherpa-onnx/resolve/main/tokenizer/merges.txt",
                        ExpectedSizeBytes = 1_671_853
                    },
                    new()
                    {
                        RelativePath = Path.Combine("tokenizer", "tokenizer_config.json"),
                        PrimaryUrl = "https://huggingface.co/thieunv-asilla/sherpa-onnx-qwen3-asr-1.7B-int8/resolve/main/tokenizer/tokenizer_config.json",
                        FallbackUrl = "https://huggingface.co/ilmina/qwen3-asr-1.7b-sherpa-onnx/resolve/main/tokenizer/tokenizer_config.json",
                        ExpectedSizeBytes = 12_487
                    },
                    new()
                    {
                        RelativePath = Path.Combine("tokenizer", "vocab.json"),
                        PrimaryUrl = "https://huggingface.co/thieunv-asilla/sherpa-onnx-qwen3-asr-1.7B-int8/resolve/main/tokenizer/vocab.json",
                        FallbackUrl = "https://huggingface.co/ilmina/qwen3-asr-1.7b-sherpa-onnx/resolve/main/tokenizer/vocab.json",
                        ExpectedSizeBytes = 2_776_833
                    }
                }
            },
            new()
            {
                Size = ModelSize.Tiny,
                Engine = ModelEngineType.Whisper,
                FamilyName = "Whisper (OpenAI)",
                Name = "Whisper Tiny",
                Description = "Ultra rápido para equipos con pocos recursos.",
                FileSizeBytes = 75 * 1024 * 1024,
                LocalPath = Path.Combine(_modelsFolder, "ggml-tiny.bin")
            },
            new()
            {
                Size = ModelSize.Base,
                Engine = ModelEngineType.Whisper,
                FamilyName = "Whisper (OpenAI)",
                Name = "Whisper Base",
                Description = "Equilibrio entre velocidad y precisión en CPU.",
                FileSizeBytes = 142 * 1024 * 1024,
                LocalPath = Path.Combine(_modelsFolder, "ggml-base.bin")
            },
            new()
            {
                Size = ModelSize.Small,
                Engine = ModelEngineType.Whisper,
                FamilyName = "Whisper (OpenAI)",
                Name = "Whisper Small",
                Description = "Alta precisión, especialmente en nombres propios.",
                FileSizeBytes = 466 * 1024 * 1024,
                LocalPath = Path.Combine(_modelsFolder, "ggml-small.bin")
            },
            new()
            {
                Size = ModelSize.Medium,
                Engine = ModelEngineType.Whisper,
                FamilyName = "Whisper (OpenAI)",
                Name = "Whisper Medium",
                Description = "Alta precisión para reuniones técnicas complejas.",
                FileSizeBytes = 1500L * 1024 * 1024,
                LocalPath = Path.Combine(_modelsFolder, "ggml-medium.bin")
            },
            new()
            {
                Size = ModelSize.LargeV3Turbo,
                Engine = ModelEngineType.Whisper,
                FamilyName = "Whisper (OpenAI)",
                Name = "Whisper Large-v3 Turbo",
                Description = "Máxima calidad de transcripción.",
                FileSizeBytes = 1600L * 1024 * 1024,
                LocalPath = Path.Combine(_modelsFolder, "ggml-large-v3-turbo.bin")
            }
        };

        string defaultDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DictaMeeting", "models");
        string baseDir = Path.Combine(AppContext.BaseDirectory, "models");

        foreach (var m in models)
        {
            string relName = Path.GetFileName(m.LocalPath);
            var candidateNames = new List<string> { relName };
            if (m.Size == ModelSize.Qwen3_06B)
            {
                candidateNames.Add("sherpa-onnx-qwen3-asr-0.6B");
                candidateNames.Add("sherpa-onnx-qwen3-asr-0.6B-int8-2026-03-25");
            }
            else if (m.Size == ModelSize.Qwen3_17B)
            {
                candidateNames.Add("sherpa-onnx-qwen3-asr-1.7B");
                candidateNames.Add("sherpa-onnx-qwen3-asr-1.7B-int8");
            }

            // 1. Ubicación configurada actual (_modelsFolder)
            string? foundPrimary = null;
            foreach (var cand in candidateNames)
            {
                string candPath = Path.Combine(_modelsFolder, cand);
                bool isCandDownloaded = m.IsDirectory
                    ? IsQwenDirectoryValid(candPath)
                    : (File.Exists(candPath) && new FileInfo(candPath).Length > 1024 * 1024);

                if (isCandDownloaded)
                {
                    foundPrimary = candPath;
                    break;
                }
            }

            if (foundPrimary != null)
            {
                m.LocalPath = foundPrimary;
                m.IsDownloaded = true;
                continue;
            }

            string primaryPath = Path.Combine(_modelsFolder, relName);

            // 2. Comprobar instalación previa / directorio base solo si estamos en la ubicación predeterminada
            if (IsDefaultModelsFolder())
            {
                string fallbackDefault = Path.Combine(defaultDir, relName);
                bool isDefaultDownloaded = m.IsDirectory
                    ? IsQwenDirectoryValid(fallbackDefault)
                    : (File.Exists(fallbackDefault) && new FileInfo(fallbackDefault).Length > 1024 * 1024);

                if (isDefaultDownloaded)
                {
                    m.LocalPath = fallbackDefault;
                    m.IsDownloaded = true;
                    continue;
                }

                string fallbackBase = Path.Combine(baseDir, relName);
                bool isBaseDownloaded = m.IsDirectory
                    ? IsQwenDirectoryValid(fallbackBase)
                    : (File.Exists(fallbackBase) && new FileInfo(fallbackBase).Length > 1024 * 1024);

                if (isBaseDownloaded)
                {
                    m.LocalPath = fallbackBase;
                    m.IsDownloaded = true;
                    continue;
                }
            }

            // No descargado: la ruta por defecto será dentro de la carpeta configurada
            m.LocalPath = primaryPath;
            m.IsDownloaded = false;
        }

        return models;
    }

    public bool IsModelDownloaded(ModelSize modelSize)
    {
        var modelInfo = GetAvailableModels().FirstOrDefault(m => m.Size == modelSize);
        return modelInfo?.IsDownloaded ?? false;
    }

    public string GetModelPath(ModelSize modelSize)
    {
        var modelInfo = GetAvailableModels().FirstOrDefault(m => m.Size == modelSize)
            ?? throw new ArgumentException($"Modelo desconocido: {modelSize}");

        if (!modelInfo.IsDownloaded)
        {
            throw new InvalidOperationException($"El modelo '{modelInfo.Name}' no está instalado en el disco. Descárguelo manualmente antes de utilizarlo.");
        }

        return modelInfo.LocalPath;
    }

    public bool IsModelIncomplete(ModelSize modelSize)
    {
        var modelInfo = GetAvailableModels().FirstOrDefault(m => m.Size == modelSize);
        if (modelInfo == null || modelInfo.IsDownloaded) return false;

        string path = Path.Combine(_modelsFolder, Path.GetFileName(modelInfo.LocalPath));
        if (modelInfo.IsDirectory)
        {
            if (Directory.Exists(path))
            {
                var files = Directory.GetFiles(path, "*", SearchOption.AllDirectories);
                return files.Length > 0 && !IsQwenDirectoryValid(path);
            }
        }
        else
        {
            if (File.Exists(path + ".download")) return true;
            if (File.Exists(path) && new FileInfo(path).Length > 0 && new FileInfo(path).Length <= 1024 * 1024) return true;
        }

        return false;
    }

    public static bool IsQwenDirectoryValid(string path)
    {
        if (!Directory.Exists(path)) return false;
        bool hasFrontend = File.Exists(Path.Combine(path, "conv_frontend.onnx"));
        bool hasEncoder = File.Exists(Path.Combine(path, "encoder.int8.onnx")) || File.Exists(Path.Combine(path, "encoder.onnx"));
        bool hasDecoder = File.Exists(Path.Combine(path, "decoder.int8.onnx")) || File.Exists(Path.Combine(path, "decoder.onnx"));
        return hasFrontend && hasEncoder && hasDecoder;
    }

    internal static HttpClient SharedHttpClient = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DictaMeeting/1.0 (Windows; x64)");
        return client;
    }

    public Task<string> EnsureModelDownloadedAsync(
        ModelSize modelSize,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (IsModelDownloaded(modelSize))
        {
            progress?.Report(1.0);
            return Task.FromResult(GetModelPath(modelSize));
        }

        return DownloadModelAsync(modelSize, progress, cancellationToken);
    }

    public async Task<string> DownloadModelAsync(
        ModelSize modelSize,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var modelInfo = GetAvailableModels().FirstOrDefault(m => m.Size == modelSize)
            ?? throw new ArgumentException($"Modelo desconocido: {modelSize}");

        if (IsModelDownloaded(modelSize))
        {
            _logger?.LogInformation("Modelo '{Name}' ya descargado en: {Path}", modelInfo.Name, modelInfo.LocalPath);
            progress?.Report(1.0);
            return modelInfo.LocalPath;
        }

        // Asegurar que el destino final sea siempre dentro de la carpeta configurada actual (_modelsFolder)
        string targetPath = Path.Combine(_modelsFolder, Path.GetFileName(modelInfo.LocalPath));
        modelInfo.LocalPath = targetPath;

        if (modelInfo.Engine == ModelEngineType.Qwen3)
        {
            if (modelInfo.DownloadFiles != null && modelInfo.DownloadFiles.Count > 0)
            {
                return await DownloadQwenFilesModelAsync(modelInfo, progress, cancellationToken);
            }
            return await DownloadAndExtractQwenModelAsync(modelInfo, progress, cancellationToken);
        }

        var tempPath = targetPath + ".download";
        _logger?.LogInformation("Descargando modelo '{Name}'...", modelInfo.Name);

        var ggmlType = modelSize switch
        {
            ModelSize.Tiny => GgmlType.Tiny,
            ModelSize.Base => GgmlType.Base,
            ModelSize.Small => GgmlType.Small,
            ModelSize.Medium => GgmlType.Medium,
            ModelSize.LargeV3Turbo => GgmlType.LargeV3Turbo,
            _ => GgmlType.Base
        };

        var downloader = new WhisperGgmlDownloader(SharedHttpClient);
        try
        {
            using (var modelStream = await downloader.GetGgmlModelAsync(ggmlType, cancellationToken: cancellationToken))
            using (var fileStream = File.Create(tempPath))
            {
                var buffer = new byte[81920];
                long totalBytesRead = 0;
                long estimatedTotal = modelInfo.FileSizeBytes > 0 ? modelInfo.FileSizeBytes : 150_000_000;
                int read;

                while ((read = await modelStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, read, cancellationToken);
                    totalBytesRead += read;

                    if (progress != null)
                    {
                        double p = Math.Min(0.99, (double)totalBytesRead / estimatedTotal);
                        progress.Report(p);
                    }
                }
            }

            if (File.Exists(modelInfo.LocalPath))
            {
                File.Delete(modelInfo.LocalPath);
            }

            File.Move(tempPath, modelInfo.LocalPath);
            progress?.Report(1.0);

            _logger?.LogInformation("Modelo '{Name}' descargado con éxito.", modelInfo.Name);
            return modelInfo.LocalPath;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error al descargar el modelo de transcripción '{Name}'.", modelInfo.Name);
            try
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
            catch { }
            throw;
        }
    }

    private async Task<string> DownloadAndExtractQwenModelAsync(
        TranscriptionModelInfo modelInfo,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var tempArchive = Path.Combine(Path.GetTempPath(), $"dictameeting_qwen_{Guid.NewGuid():N}.tar.bz2");
        var tempExtractDir = Path.Combine(Path.GetTempPath(), $"dictameeting_qwen_extract_{Guid.NewGuid():N}");

        try
        {
            _logger?.LogInformation("Descargando archivo comprimido para '{Name}' desde {Url}...", modelInfo.Name, modelInfo.DownloadUrl);

            using (var response = await SharedHttpClient.GetAsync(modelInfo.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();

                long? contentLength = response.Content.Headers.ContentLength;
                long totalBytes = contentLength ?? modelInfo.FileSizeBytes;
                if (totalBytes <= 0) totalBytes = 950_000_000;

                using (var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken))
                using (var fileStream = File.Create(tempArchive))
                {
                    var buffer = new byte[81920];
                    long totalBytesRead = 0;
                    int read;

                    while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, read, cancellationToken);
                        totalBytesRead += read;

                        if (progress != null)
                        {
                            // 0% a 85% para descarga
                            double p = Math.Min(0.85, ((double)totalBytesRead / totalBytes) * 0.85);
                            progress.Report(p);
                        }
                    }
                }
            }

            _logger?.LogInformation("Descarga completa. Descomprimiendo modelo '{Name}'...", modelInfo.Name);
            progress?.Report(0.88);

            if (Directory.Exists(tempExtractDir))
            {
                Directory.Delete(tempExtractDir, true);
            }
            Directory.CreateDirectory(tempExtractDir);

            // Descompresión con SharpZipLib (BZip2 + Tar)
            using (var inStream = File.OpenRead(tempArchive))
            using (var bz2Stream = new ICSharpCode.SharpZipLib.BZip2.BZip2InputStream(inStream))
            using (var tarArchive = ICSharpCode.SharpZipLib.Tar.TarArchive.CreateInputTarArchive(bz2Stream, System.Text.Encoding.UTF8))
            {
                tarArchive.ExtractContents(tempExtractDir);
            }

            progress?.Report(0.95);

            // Si el tar contiene una subcarpeta raíz, usarla como origen
            string sourceDir = tempExtractDir;
            var subDirs = Directory.GetDirectories(tempExtractDir);
            if (subDirs.Length == 1 && Directory.GetFiles(tempExtractDir).Length == 0)
            {
                sourceDir = subDirs[0];
            }

            if (Directory.Exists(modelInfo.LocalPath))
            {
                Directory.Delete(modelInfo.LocalPath, true);
            }
            Directory.CreateDirectory(modelInfo.LocalPath);

            CopyDirectoryRecursively(sourceDir, modelInfo.LocalPath);

            progress?.Report(1.0);
            _logger?.LogInformation("Modelo '{Name}' instalado con éxito en: {Path}", modelInfo.Name, modelInfo.LocalPath);
            return modelInfo.LocalPath;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error al descargar o extraer el modelo Qwen3 '{Name}'.", modelInfo.Name);
            throw;
        }
        finally
        {
            try { if (File.Exists(tempArchive)) File.Delete(tempArchive); } catch { }
            try { if (Directory.Exists(tempExtractDir)) Directory.Delete(tempExtractDir, true); } catch { }
        }
    }

    private async Task<string> DownloadQwenFilesModelAsync(
        TranscriptionModelInfo modelInfo,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        if (modelInfo.DownloadFiles == null || modelInfo.DownloadFiles.Count == 0)
        {
            throw new InvalidOperationException($"No se definieron archivos de descarga para {modelInfo.Name}.");
        }

        Directory.CreateDirectory(modelInfo.LocalPath);
        _logger?.LogInformation("Descargando componentes del modelo '{Name}' ({Count} archivos) en: {Path}...", 
            modelInfo.Name, modelInfo.DownloadFiles.Count, modelInfo.LocalPath);

        long totalExpectedBytes = modelInfo.DownloadFiles.Sum(f => f.ExpectedSizeBytes);
        if (totalExpectedBytes <= 0) totalExpectedBytes = modelInfo.FileSizeBytes;

        // Calcular bytes ya presentes en disco si se está reanudando
        long totalBytesDownloaded = 0;
        foreach (var file in modelInfo.DownloadFiles)
        {
            string dest = Path.Combine(modelInfo.LocalPath, file.RelativePath);
            if (File.Exists(dest))
            {
                long len = new FileInfo(dest).Length;
                if (file.ExpectedSizeBytes > 0 && len >= (long)(file.ExpectedSizeBytes * 0.99))
                {
                    totalBytesDownloaded += len;
                }
            }
        }

        if (totalExpectedBytes > 0)
        {
            progress?.Report(Math.Min(0.99, (double)totalBytesDownloaded / totalExpectedBytes));
        }

        foreach (var file in modelInfo.DownloadFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string dest = Path.Combine(modelInfo.LocalPath, file.RelativePath);
            string? parentDir = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
            {
                Directory.CreateDirectory(parentDir);
            }

            // Si el archivo ya existe y tiene el tamaño esperado, lo saltamos
            if (File.Exists(dest))
            {
                long existingLength = new FileInfo(dest).Length;
                if (file.ExpectedSizeBytes > 0 && existingLength >= (long)(file.ExpectedSizeBytes * 0.99))
                {
                    _logger?.LogInformation("Archivo {File} ya presente y verificado ({Size:N0} bytes).", file.RelativePath, existingLength);
                    continue;
                }
            }

            string tempFile = dest + ".download";
            _logger?.LogInformation("Descargando archivo {File}...", file.RelativePath);

            HttpResponseMessage? response = null;
            try
            {
                try
                {
                    response = await SharedHttpClient.GetAsync(file.PrimaryUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    response.EnsureSuccessStatusCode();
                }
                catch (Exception ex) when (!string.IsNullOrWhiteSpace(file.FallbackUrl) && !cancellationToken.IsCancellationRequested)
                {
                    response?.Dispose();
                    response = null;
                    _logger?.LogWarning(ex, "Fallo al descargar {File} desde URL primaria. Intentando URL alternativa...", file.RelativePath);
                    response = await SharedHttpClient.GetAsync(file.FallbackUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    response.EnsureSuccessStatusCode();
                }

                using (response)
                using (var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken))
                using (var fileStream = File.Create(tempFile))
                {
                    byte[] buffer = new byte[81920];
                    int read;
                    while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, read, cancellationToken);
                        totalBytesDownloaded += read;

                        if (progress != null && totalExpectedBytes > 0)
                        {
                            double p = Math.Min(0.99, (double)totalBytesDownloaded / totalExpectedBytes);
                            progress.Report(p);
                        }
                    }
                }

                if (File.Exists(dest))
                {
                    File.Delete(dest);
                }
                File.Move(tempFile, dest);
                _logger?.LogInformation("Archivo {File} descargado correctamente.", file.RelativePath);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error al descargar el archivo {File} del modelo {Name}.", file.RelativePath, modelInfo.Name);
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
                throw;
            }
        }

        if (!IsQwenDirectoryValid(modelInfo.LocalPath))
        {
            throw new InvalidOperationException($"La descarga finalizó pero faltan componentes obligatorios en {modelInfo.LocalPath}.");
        }

        progress?.Report(1.0);
        _logger?.LogInformation("Modelo '{Name}' descargado e instalado con éxito en: {Path}", modelInfo.Name, modelInfo.LocalPath);
        return modelInfo.LocalPath;
    }

    private static void CopyDirectoryRecursively(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(targetDir, Path.GetFileName(file));
            File.Copy(file, destFile, true);
        }

        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(targetDir, Path.GetFileName(subDir));
            CopyDirectoryRecursively(subDir, destSubDir);
        }
    }

    public bool DeleteModel(ModelSize modelSize)
    {
        var modelInfo = GetAvailableModels().FirstOrDefault(m => m.Size == modelSize);
        if (modelInfo == null) return false;

        try
        {
            if (modelInfo.IsDirectory)
            {
                if (Directory.Exists(modelInfo.LocalPath))
                {
                    Directory.Delete(modelInfo.LocalPath, true);
                    _logger?.LogInformation("Directorio del modelo '{Name}' eliminado del disco.", modelInfo.Name);
                    return true;
                }
            }
            else
            {
                if (File.Exists(modelInfo.LocalPath))
                {
                    File.Delete(modelInfo.LocalPath);
                    _logger?.LogInformation("Modelo '{Name}' eliminado del disco.", modelInfo.Name);
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error al eliminar modelo '{Name}'.", modelInfo.Name);
        }

        return false;
    }

    public bool IsDiarizationModelDownloaded()
    {
        // 1. Carpeta configurada actual (_modelsFolder/diarization)
        string dir = Path.Combine(_modelsFolder, "diarization");
        if (CheckDiarizationDirectory(dir)) return true;

        // 2. Comprobar instalación previa o directorio de la aplicación solo si estamos en la ubicación predeterminada
        if (IsDefaultModelsFolder())
        {
            string defaultDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DictaMeeting", "models", "diarization");
            if (CheckDiarizationDirectory(defaultDir)) return true;

            string baseDir = Path.Combine(AppContext.BaseDirectory, "models", "diarization");
            if (CheckDiarizationDirectory(baseDir)) return true;
        }

        return false;
    }

    private static bool CheckDiarizationDirectory(string dir)
    {
        if (!Directory.Exists(dir)) return false;
        string segPath = Path.Combine(dir, "community1-segmentation.onnx");
        string embPath = Path.Combine(dir, "community1-embedding.onnx");
        string pldaPath = Path.Combine(dir, "plda_community1.bin");

        return File.Exists(segPath) && File.Exists(embPath) && File.Exists(pldaPath) &&
               new FileInfo(segPath).Length > 100_000 &&
               new FileInfo(embPath).Length > 1_000_000 &&
               new FileInfo(pldaPath).Length > 10_000;
    }

    public string GetDiarizationModelPath()
    {
        string dir = Path.Combine(_modelsFolder, "diarization");
        if (CheckDiarizationDirectory(dir)) return Path.Combine(dir, "community1-segmentation.onnx");

        if (IsDefaultModelsFolder())
        {
            string defaultDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DictaMeeting", "models", "diarization");
            if (CheckDiarizationDirectory(defaultDir)) return Path.Combine(defaultDir, "community1-segmentation.onnx");

            string baseDir = Path.Combine(AppContext.BaseDirectory, "models", "diarization");
            if (CheckDiarizationDirectory(baseDir)) return Path.Combine(baseDir, "community1-segmentation.onnx");
        }

        return Path.Combine(dir, "community1-segmentation.onnx");
    }

    public bool IsDiarizationModelIncomplete()
    {
        if (IsDiarizationModelDownloaded()) return false;
        string dir = Path.Combine(_modelsFolder, "diarization");
        if (!Directory.Exists(dir)) return false;
        return Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Length > 0;
    }

    public Task<string> EnsureDiarizationModelDownloadedAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        if (IsDiarizationModelDownloaded())
        {
            progress?.Report(1.0);
            return Task.FromResult(GetDiarizationModelPath());
        }

        return DownloadDiarizationModelAsync(progress, cancellationToken);
    }

    public async Task<string> DownloadDiarizationModelAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        string targetDir = Path.Combine(_modelsFolder, "diarization");
        Directory.CreateDirectory(targetDir);

        string segPath = Path.Combine(targetDir, "community1-segmentation.onnx");
        string embPath = Path.Combine(targetDir, "community1-embedding.onnx");
        string pldaPath = Path.Combine(targetDir, "plda_community1.bin");

        if (IsDiarizationModelDownloaded())
        {
            progress?.Report(1.0);
            return GetDiarizationModelPath();
        }

        _logger?.LogInformation("Descargando modelos de Diarización PyAnnote Community-1...");

        var downloads = new (string FileName, string Url, long ExpectedSize)[]
        {
            ("community1-segmentation.onnx", "https://raw.githubusercontent.com/jcolozzi/cpp-annote-ex/master/artifacts/community1-segmentation.onnx", 5_920_186),
            ("community1-embedding.onnx", "https://raw.githubusercontent.com/jcolozzi/cpp-annote-ex/master/artifacts/community1-embedding.onnx", 26_548_733)
        };

        long totalBytesExpected = 32_600_000;
        long totalBytesDownloaded = 0;

        foreach (var item in downloads)
        {
            string dest = Path.Combine(targetDir, item.FileName);
            if (File.Exists(dest) && new FileInfo(dest).Length >= item.ExpectedSize * 0.95)
            {
                totalBytesDownloaded += item.ExpectedSize;
                continue;
            }

            string tempFile = dest + ".download";
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                using (var resp = await SharedHttpClient.GetAsync(item.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                {
                    resp.EnsureSuccessStatusCode();
                    using var contentStream = await resp.Content.ReadAsStreamAsync(cancellationToken);
                    using var fileStream = File.Create(tempFile);

                    byte[] buffer = new byte[81920];
                    int read;
                    while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, read, cancellationToken);
                        totalBytesDownloaded += read;
                        progress?.Report(Math.Min(0.99, (double)totalBytesDownloaded / totalBytesExpected));
                    }
                }

                if (File.Exists(dest)) File.Delete(dest);
                File.Move(tempFile, dest);
            }
            catch
            {
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
                throw;
            }
        }

        // Si falta plda_community1.bin, copiar de la distribución local o de instalación previa
        if (!File.Exists(pldaPath))
        {
            string basePlda = Path.Combine(AppContext.BaseDirectory, "models", "diarization", "plda_community1.bin");
            if (File.Exists(basePlda))
            {
                File.Copy(basePlda, pldaPath, true);
            }
            else
            {
                string defaultPlda = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DictaMeeting", "models", "diarization", "plda_community1.bin");
                if (File.Exists(defaultPlda))
                {
                    File.Copy(defaultPlda, pldaPath, true);
                }
            }
        }

        if (!CheckDiarizationDirectory(targetDir))
        {
            throw new InvalidOperationException("La descarga de los modelos de diarización finalizó pero faltan componentes obligatorios.");
        }

        progress?.Report(1.0);
        _logger?.LogInformation("Modelos PyAnnote Community-1 descargados y verificados con éxito.");
        return targetDir;
    }

    public bool IsVadModelDownloaded()
    {
        string p1 = Path.Combine(_modelsFolder, "silero", "silero_vad.onnx");
        if (File.Exists(p1) && new FileInfo(p1).Length > 100_000) return true;

        if (IsDefaultModelsFolder())
        {
            string defaultVad = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DictaMeeting", "models", "silero", "silero_vad.onnx");
            if (File.Exists(defaultVad) && new FileInfo(defaultVad).Length > 100_000) return true;

            string baseVad = Path.Combine(AppContext.BaseDirectory, "models", "silero", "silero_vad.onnx");
            if (File.Exists(baseVad) && new FileInfo(baseVad).Length > 100_000) return true;
        }

        return false;
    }

    public string GetVadModelPath()
    {
        string p1 = Path.Combine(_modelsFolder, "silero", "silero_vad.onnx");
        if (File.Exists(p1) && new FileInfo(p1).Length > 100_000) return p1;

        if (IsDefaultModelsFolder())
        {
            string defaultVad = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DictaMeeting", "models", "silero", "silero_vad.onnx");
            if (File.Exists(defaultVad) && new FileInfo(defaultVad).Length > 100_000) return defaultVad;

            string baseVad = Path.Combine(AppContext.BaseDirectory, "models", "silero", "silero_vad.onnx");
            if (File.Exists(baseVad) && new FileInfo(baseVad).Length > 100_000) return baseVad;
        }

        return p1;
    }

    public bool IsVadModelIncomplete()
    {
        if (IsVadModelDownloaded()) return false;
        string temp = Path.Combine(_modelsFolder, "silero", "silero_vad.onnx.download");
        string p1 = Path.Combine(_modelsFolder, "silero", "silero_vad.onnx");
        return File.Exists(temp) || (File.Exists(p1) && new FileInfo(p1).Length > 0 && new FileInfo(p1).Length <= 500_000);
    }

    public async Task<string> DownloadVadModelAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        string targetDir = Path.Combine(_modelsFolder, "silero");
        Directory.CreateDirectory(targetDir);

        if (IsVadModelDownloaded())
        {
            progress?.Report(1.0);
            return GetVadModelPath();
        }

        string dest = Path.Combine(targetDir, "silero_vad.onnx");
        string temp = dest + ".download";
        string url = "https://raw.githubusercontent.com/snakers4/silero-vad/master/src/silero_vad/data/silero_vad.onnx";
        long expectedSize = 2_327_524;

        _logger?.LogInformation("Descargando modelo Silero VAD v5...");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (var resp = await SharedHttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                resp.EnsureSuccessStatusCode();
                using var contentStream = await resp.Content.ReadAsStreamAsync(cancellationToken);
                using var fileStream = File.Create(temp);

                byte[] buffer = new byte[81920];
                int read;
                long totalRead = 0;
                while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, read, cancellationToken);
                    totalRead += read;
                    progress?.Report(Math.Min(0.99, (double)totalRead / expectedSize));
                }
            }

            if (File.Exists(dest)) File.Delete(dest);
            File.Move(temp, dest);

            progress?.Report(1.0);
            _logger?.LogInformation("Modelo Silero VAD v5 descargado y verificado con éxito.");
            return dest;
        }
        catch
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            throw;
        }
    }
}

