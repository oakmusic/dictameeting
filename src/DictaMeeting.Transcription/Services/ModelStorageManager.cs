using System.IO;
using DictaMeeting.Transcription.Models;
using Microsoft.Extensions.Logging;

namespace DictaMeeting.Transcription.Services;

/// <summary>
/// Representa un modelo o componente de modelo reconocido en el almacenamiento local.
/// </summary>
public class ModelStorageItem
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public long TotalSizeBytes { get; set; }
    public bool IsValid { get; set; }
}

/// <summary>
/// Progreso detallado durante la migración segura de modelos entre unidades/carpetas.
/// </summary>
public class ModelMigrationProgress
{
    public string CurrentModelName { get; set; } = string.Empty;
    public string CurrentOperation { get; set; } = string.Empty;
    public double OverallProgress { get; set; } // 0.0 a 1.0
    public long TotalBytesCopied { get; set; }
    public long TotalBytesExpected { get; set; }
}

/// <summary>
/// Servicio centralizado de resolución de almacenamiento, validación, detección y migración segura de modelos locales.
/// </summary>
public static class ModelStorageManager
{
    /// <summary>
    /// Escanea una carpeta y detecta todos los modelos válidos reconocidos por DictaMeeting.
    /// No incluye archivos temporales, logs ni elementos no reconocidos.
    /// </summary>
    public static List<ModelStorageItem> ScanModels(string directory)
    {
        var items = new List<ModelStorageItem>();
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return items;
        }

        try
        {
            // 1. Qwen3-ASR 0.6B
            var qwen06Path = Path.Combine(directory, "qwen3-asr-0.6b-int8");
            if (Directory.Exists(qwen06Path))
            {
                bool valid = ModelManager.IsQwenDirectoryValid(qwen06Path);
                long size = CalculateDirectorySize(qwen06Path);
                items.Add(new ModelStorageItem
                {
                    Id = "qwen3_06b",
                    DisplayName = "Qwen3-ASR 0.6B",
                    RelativePath = "qwen3-asr-0.6b-int8",
                    IsDirectory = true,
                    TotalSizeBytes = size,
                    IsValid = valid
                });
            }

            // 2. Qwen3-ASR 1.7B
            var qwen17Path = Path.Combine(directory, "qwen3-asr-1.7b-int8");
            if (Directory.Exists(qwen17Path))
            {
                bool valid = ModelManager.IsQwenDirectoryValid(qwen17Path);
                long size = CalculateDirectorySize(qwen17Path);
                items.Add(new ModelStorageItem
                {
                    Id = "qwen3_17b",
                    DisplayName = "Qwen3-ASR 1.7B",
                    RelativePath = "qwen3-asr-1.7b-int8",
                    IsDirectory = true,
                    TotalSizeBytes = size,
                    IsValid = valid
                });
            }

            // 3. Modelos Whisper (ggml-*.bin)
            foreach (var file in Directory.GetFiles(directory, "ggml-*.bin"))
            {
                var fi = new FileInfo(file);
                if (fi.Length > 1024 * 1024) // Mayor a 1 MB
                {
                    var fileName = Path.GetFileName(file);
                    items.Add(new ModelStorageItem
                    {
                        Id = fileName,
                        DisplayName = GetWhisperDisplayName(fileName),
                        RelativePath = fileName,
                        IsDirectory = false,
                        TotalSizeBytes = fi.Length,
                        IsValid = true
                    });
                }
            }

            // 4. Modelos de Diarización (diarization/)
            var diarPath = Path.Combine(directory, "diarization");
            if (Directory.Exists(diarPath))
            {
                string seg = Path.Combine(diarPath, "community1-segmentation.onnx");
                string emb = Path.Combine(diarPath, "community1-embedding.onnx");
                string plda = Path.Combine(diarPath, "plda_community1.bin");

                bool hasSeg = File.Exists(seg) && new FileInfo(seg).Length > 100_000;
                bool hasEmb = File.Exists(emb) && new FileInfo(emb).Length > 1_000_000;
                bool hasPlda = File.Exists(plda) && new FileInfo(plda).Length > 10_000;
                bool valid = hasSeg && hasEmb && hasPlda;

                if (hasSeg || hasEmb || hasPlda)
                {
                    items.Add(new ModelStorageItem
                    {
                        Id = "diarization",
                        DisplayName = "PyAnnote Diarization",
                        RelativePath = "diarization",
                        IsDirectory = true,
                        TotalSizeBytes = CalculateDirectorySize(diarPath),
                        IsValid = valid
                    });
                }
            }

            // 5. Modelos de Resumen (summary/)
            var summaryPath = Path.Combine(directory, "summary");
            if (Directory.Exists(summaryPath))
            {
                var ggufFiles = Directory.GetFiles(summaryPath, "*.gguf");
                if (ggufFiles.Length > 0)
                {
                    long totalSummarySize = 0;
                    bool anyValid = false;
                    foreach (var gguf in ggufFiles)
                    {
                        var fi = new FileInfo(gguf);
                        totalSummarySize += fi.Length;
                        if (fi.Length >= 900L * 1024 * 1024)
                        {
                            anyValid = true;
                        }
                    }

                    items.Add(new ModelStorageItem
                    {
                        Id = "summary",
                        DisplayName = "Resumen Local (Qwen2.5 GGUF)",
                        RelativePath = "summary",
                        IsDirectory = true,
                        TotalSizeBytes = totalSummarySize,
                        IsValid = anyValid
                    });
                }
            }

            // 6. Silero VAD (silero/)
            var sileroPath = Path.Combine(directory, "silero");
            if (Directory.Exists(sileroPath))
            {
                var vadFile = Path.Combine(sileroPath, "silero_vad.onnx");
                if (File.Exists(vadFile) && new FileInfo(vadFile).Length > 500_000)
                {
                    items.Add(new ModelStorageItem
                    {
                        Id = "silero",
                        DisplayName = "Silero VAD",
                        RelativePath = "silero",
                        IsDirectory = true,
                        TotalSizeBytes = CalculateDirectorySize(sileroPath),
                        IsValid = true
                    });
                }
            }

            // 7. Punctuation (punctuation/)
            var puncPath = Path.Combine(directory, "punctuation");
            if (Directory.Exists(puncPath))
            {
                var puncModel = Path.Combine(puncPath, "punctuate-all", "model.onnx");
                if (File.Exists(puncModel) && new FileInfo(puncModel).Length > 10_000_000)
                {
                    items.Add(new ModelStorageItem
                    {
                        Id = "punctuation",
                        DisplayName = "Puntuación ONNX",
                        RelativePath = "punctuation",
                        IsDirectory = true,
                        TotalSizeBytes = CalculateDirectorySize(puncPath),
                        IsValid = true
                    });
                }
            }
        }
        catch
        {
            // Ignorar errores de acceso durante escaneo rápido
        }

        return items;
    }

    /// <summary>
    /// Comprueba si hay al menos un modelo válido instalado en el directorio dado.
    /// </summary>
    public static bool HasAnyModels(string directory)
    {
        return ScanModels(directory).Any(m => m.IsValid);
    }

    /// <summary>
    /// Comprueba si la ruta de almacenamiento de modelos es accesible (disco conectado y montado).
    /// </summary>
    public static bool IsDirectoryAccessible(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return false;

        try
        {
            string? root = Path.GetPathRoot(directory);
            if (string.IsNullOrEmpty(root)) return false;

            var drive = new DriveInfo(root);
            if (!drive.IsReady) return false;

            return Directory.Exists(directory);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Valida que la carpeta de destino sea apta para albergar modelos.
    /// </summary>
    public static (bool IsValid, string? ErrorMessage) ValidateDestination(string sourceDirectory, string destinationDirectory)
    {
        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            return (false, "Debe seleccionar una carpeta válida.");
        }

        string fullSource;
        string fullDest;
        try
        {
            fullSource = Path.GetFullPath(sourceDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            fullDest = Path.GetFullPath(destinationDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception ex)
        {
            return (false, $"Ruta no válida: {ex.Message}");
        }

        if (string.Equals(fullSource, fullDest, StringComparison.OrdinalIgnoreCase))
        {
            return (false, "La carpeta seleccionada es la misma que la ubicación actual de los modelos.");
        }

        var sourceWithSep = fullSource + Path.DirectorySeparatorChar;
        var destWithSep = fullDest + Path.DirectorySeparatorChar;

        if (destWithSep.StartsWith(sourceWithSep, StringComparison.OrdinalIgnoreCase))
        {
            return (false, "La carpeta de destino no puede estar dentro de la carpeta actual de modelos.");
        }

        if (sourceWithSep.StartsWith(destWithSep, StringComparison.OrdinalIgnoreCase))
        {
            return (false, "La carpeta de destino no puede contener a la carpeta actual de modelos.");
        }

        string? root = Path.GetPathRoot(fullDest);
        if (string.IsNullOrEmpty(root))
        {
            return (false, "La carpeta seleccionada no pertenece a una unidad de almacenamiento válida.");
        }

        try
        {
            var drive = new DriveInfo(root);
            if (!drive.IsReady)
            {
                return (false, $"La unidad {drive.Name} no está disponible o no está lista para operaciones.");
            }
        }
        catch (Exception ex)
        {
            return (false, $"No se pudo verificar la unidad de disco: {ex.Message}");
        }

        try
        {
            if (!Directory.Exists(fullDest))
            {
                Directory.CreateDirectory(fullDest);
            }

            // Probar permisos de escritura creando y borrando un archivo efímero
            var testFilePath = Path.Combine(fullDest, $".dictameeting_test_{Guid.NewGuid():N}.tmp");
            File.WriteAllText(testFilePath, "DictaMeeting write test");
            File.Delete(testFilePath);
        }
        catch (UnauthorizedAccessException)
        {
            return (false, "No se tienen permisos suficientes de lectura y escritura en la carpeta seleccionada.");
        }
        catch (Exception ex)
        {
            return (false, $"Error al verificar permisos en la carpeta: {ex.Message}");
        }

        return (true, null);
    }

    /// <summary>
    /// Comprueba el espacio libre disponible en la unidad de destino comparado con el tamaño requerido.
    /// Añade un margen de seguridad de 300 MB.
    /// </summary>
    public static (bool HasSpace, long RequiredBytes, long AvailableBytes) CheckFreeSpace(string destinationDirectory, long requiredBytes)
    {
        try
        {
            string? root = Path.GetPathRoot(destinationDirectory);
            if (string.IsNullOrEmpty(root)) return (false, requiredBytes, 0);

            var drive = new DriveInfo(root);
            long available = drive.AvailableFreeSpace;
            long safetyMargin = 300L * 1024 * 1024; // 300 MB margen
            bool hasSpace = available >= (requiredBytes + safetyMargin);
            return (hasSpace, requiredBytes, available);
        }
        catch
        {
            return (true, requiredBytes, 0);
        }
    }

    /// <summary>
    /// Ejecuta la migración segura paso a paso de los modelos:
    /// 1. Comprobaciones previas (espacio, permisos).
    /// 2. Copia segura de modelos válidos omitiendo duplicados íntegros ya existentes.
    /// 3. Verificación de integridad en destino.
    /// 4. Eliminación de originales ÚNICAMENTE tras verificación exitosa.
    /// </summary>
    public static async Task MigrateModelsAsync(
        string sourceDirectory,
        string destinationDirectory,
        IProgress<ModelMigrationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateDestination(sourceDirectory, destinationDirectory);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(validation.ErrorMessage);
        }

        var sourceModels = ScanModels(sourceDirectory).Where(m => m.IsValid).ToList();
        if (sourceModels.Count == 0)
        {
            // No hay modelos para migrar
            return;
        }

        var destModels = ScanModels(destinationDirectory);

        // Identificar qué modelos necesitan ser copiados y cuáles ya existen íntegros en destino
        var modelsToCopy = new List<ModelStorageItem>();
        long totalBytesToCopy = 0;

        foreach (var src in sourceModels)
        {
            var existingInDest = destModels.FirstOrDefault(d => d.RelativePath.Equals(src.RelativePath, StringComparison.OrdinalIgnoreCase) && d.IsValid);
            if (existingInDest == null)
            {
                modelsToCopy.Add(src);
                totalBytesToCopy += src.TotalSizeBytes;
            }
        }

        // Comprobación de espacio libre
        if (totalBytesToCopy > 0)
        {
            var spaceCheck = CheckFreeSpace(destinationDirectory, totalBytesToCopy);
            if (!spaceCheck.HasSpace)
            {
                var reqMb = totalBytesToCopy / (1024.0 * 1024.0);
                var availMb = spaceCheck.AvailableBytes / (1024.0 * 1024.0);
                throw new IOException($"No hay suficiente espacio libre en la unidad seleccionada. Se requieren {reqMb:F1} MB y solo hay {availMb:F1} MB disponibles.");
            }
        }

        var copiedNewFiles = new List<string>();
        var copiedNewDirs = new List<string>();
        long totalBytesCopiedSoFar = 0;

        try
        {
            // 2. COPIA MODELO A MODELO
            foreach (var model in modelsToCopy)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var srcPath = Path.Combine(sourceDirectory, model.RelativePath);
                var dstPath = Path.Combine(destinationDirectory, model.RelativePath);

                progress?.Report(new ModelMigrationProgress
                {
                    CurrentModelName = model.DisplayName,
                    CurrentOperation = $"Copiando {model.DisplayName}...",
                    OverallProgress = totalBytesToCopy > 0 ? (double)totalBytesCopiedSoFar / totalBytesToCopy : 0.0,
                    TotalBytesCopied = totalBytesCopiedSoFar,
                    TotalBytesExpected = totalBytesToCopy
                });

                if (model.IsDirectory)
                {
                    await CopyDirectoryWithProgressAsync(
                        srcPath,
                        dstPath,
                        copiedNewFiles,
                        copiedNewDirs,
                        bytesRead =>
                        {
                            totalBytesCopiedSoFar += bytesRead;
                            progress?.Report(new ModelMigrationProgress
                            {
                                CurrentModelName = model.DisplayName,
                                CurrentOperation = $"Copiando {model.DisplayName}...",
                                OverallProgress = totalBytesToCopy > 0 ? Math.Min(0.99, (double)totalBytesCopiedSoFar / totalBytesToCopy) : 0.0,
                                TotalBytesCopied = totalBytesCopiedSoFar,
                                TotalBytesExpected = totalBytesToCopy
                            });
                        },
                        cancellationToken);
                }
                else
                {
                    await CopyFileWithProgressAsync(
                        srcPath,
                        dstPath,
                        copiedNewFiles,
                        bytesRead =>
                        {
                            totalBytesCopiedSoFar += bytesRead;
                            progress?.Report(new ModelMigrationProgress
                            {
                                CurrentModelName = model.DisplayName,
                                CurrentOperation = $"Copiando {model.DisplayName}...",
                                OverallProgress = totalBytesToCopy > 0 ? Math.Min(0.99, (double)totalBytesCopiedSoFar / totalBytesToCopy) : 0.0,
                                TotalBytesCopied = totalBytesCopiedSoFar,
                                TotalBytesExpected = totalBytesToCopy
                            });
                        },
                        cancellationToken);
                }
            }

            // 3. VERIFICACIÓN DE INTEGRIDAD EN DESTINO
            progress?.Report(new ModelMigrationProgress
            {
                CurrentModelName = "Todos los modelos",
                CurrentOperation = "Verificando integridad en la nueva ubicación...",
                OverallProgress = 0.99,
                TotalBytesCopied = totalBytesCopiedSoFar,
                TotalBytesExpected = totalBytesToCopy
            });

            var verifiedDest = ScanModels(destinationDirectory);
            foreach (var srcModel in sourceModels)
            {
                var verifiedItem = verifiedDest.FirstOrDefault(d => d.RelativePath.Equals(srcModel.RelativePath, StringComparison.OrdinalIgnoreCase));
                if (verifiedItem == null || !verifiedItem.IsValid)
                {
                    throw new InvalidDataException($"La verificación de integridad del modelo '{srcModel.DisplayName}' falló en el destino.");
                }
            }

            // 4. ELIMINACIÓN SEGURA DE ORIGINALES (ÚNICAMENTE SI LA VERIFICACIÓN FUE 100% EXITOSA)
            progress?.Report(new ModelMigrationProgress
            {
                CurrentModelName = "Originales",
                CurrentOperation = "Liberando espacio en la ubicación anterior...",
                OverallProgress = 1.0,
                TotalBytesCopied = totalBytesToCopy,
                TotalBytesExpected = totalBytesToCopy
            });

            foreach (var srcModel in sourceModels)
            {
                var pathToDelete = Path.Combine(sourceDirectory, srcModel.RelativePath);
                try
                {
                    if (srcModel.IsDirectory)
                    {
                        if (Directory.Exists(pathToDelete))
                        {
                            Directory.Delete(pathToDelete, recursive: true);
                        }
                    }
                    else
                    {
                        if (File.Exists(pathToDelete))
                        {
                            File.Delete(pathToDelete);
                        }
                    }
                }
                catch
                {
                    // Si algún archivo no se puede borrar inmediatamente por permisos, no invalidar la migración ya completada en destino.
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelado: Limpiar archivos parciales copiados en destino y conservar intacto origen
            RollbackPartialDestinationFiles(copiedNewFiles, copiedNewDirs);
            throw;
        }
        catch (Exception)
        {
            // Error: Limpiar archivos parciales copiados en destino y conservar intacto origen
            RollbackPartialDestinationFiles(copiedNewFiles, copiedNewDirs);
            throw;
        }
    }

    private static void RollbackPartialDestinationFiles(List<string> createdFiles, List<string> createdDirs)
    {
        foreach (var file in createdFiles)
        {
            try
            {
                if (File.Exists(file)) File.Delete(file);
            }
            catch { }
        }

        // Eliminar directorios creados en orden inverso (más profundos primero)
        foreach (var dir in createdDirs.OrderByDescending(d => d.Length))
        {
            try
            {
                if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    Directory.Delete(dir);
                }
            }
            catch { }
        }
    }

    private static async Task CopyFileWithProgressAsync(
        string sourceFile,
        string destinationFile,
        List<string> trackedCreatedFiles,
        Action<int> onBytesCopied,
        CancellationToken cancellationToken)
    {
        var tempDest = destinationFile + ".migrating.tmp";
        var destDir = Path.GetDirectoryName(destinationFile);
        if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        trackedCreatedFiles.Add(tempDest);
        trackedCreatedFiles.Add(destinationFile);

        byte[] buffer = new byte[128 * 1024]; // 128 KB
        await using (var sourceStream = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, useAsync: true))
        await using (var destStream = new FileStream(tempDest, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, useAsync: true))
        {
            int bytesRead;
            while ((bytesRead = await sourceStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
            {
                await destStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                onBytesCopied(bytesRead);
            }
        }

        if (File.Exists(destinationFile))
        {
            File.Delete(destinationFile);
        }
        File.Move(tempDest, destinationFile);
    }

    private static async Task CopyDirectoryWithProgressAsync(
        string sourceDir,
        string destinationDir,
        List<string> trackedCreatedFiles,
        List<string> trackedCreatedDirs,
        Action<int> onBytesCopied,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(destinationDir))
        {
            Directory.CreateDirectory(destinationDir);
            trackedCreatedDirs.Add(destinationDir);
        }

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = Path.GetFileName(file);
            var destFile = Path.Combine(destinationDir, fileName);

            // Omitir temporales residuales en origen
            if (fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(".download", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            await CopyFileWithProgressAsync(file, destFile, trackedCreatedFiles, onBytesCopied, cancellationToken);
        }

        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var subDirName = Path.GetFileName(subDir);
            var destSubDir = Path.Combine(destinationDir, subDirName);
            await CopyDirectoryWithProgressAsync(subDir, destSubDir, trackedCreatedFiles, trackedCreatedDirs, onBytesCopied, cancellationToken);
        }
    }

    private static long CalculateDirectorySize(string directory)
    {
        try
        {
            long size = 0;
            foreach (var file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
            {
                try
                {
                    size += new FileInfo(file).Length;
                }
                catch { }
            }
            return size;
        }
        catch
        {
            return 0;
        }
    }

    private static string GetWhisperDisplayName(string fileName)
    {
        return fileName.ToLowerInvariant() switch
        {
            "ggml-tiny.bin" => "Whisper Tiny",
            "ggml-base.bin" => "Whisper Base",
            "ggml-small.bin" => "Whisper Small",
            "ggml-medium.bin" => "Whisper Medium",
            "ggml-large-v3-turbo.bin" => "Whisper Large-v3 Turbo",
            _ => fileName
        };
    }
}
