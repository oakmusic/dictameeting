using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using DictaMeeting.Meetings.Enums;
using DictaMeeting.Meetings.Interfaces;
using DictaMeeting.Meetings.Models;
using Microsoft.Extensions.Logging;

namespace DictaMeeting.Infrastructure.Persistence;

/// <summary>
/// Repositorio basado en sistema de archivos local para persistencia de reuniones.
/// Guarda cada reunión en una carpeta independiente: Meetings/YYYY-MM-DD_Titulo/
/// con meeting.json, transcript.md y transcript.txt.
/// </summary>
public class LocalFileMeetingRepository : IMeetingRepository
{
    private readonly string _baseDirectory;
    private readonly IDocumentExporter _exporter;
    private readonly ILogger<LocalFileMeetingRepository>? _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _dirLocks = new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public string BaseDirectory => _baseDirectory;

    public LocalFileMeetingRepository(
        IDocumentExporter exporter,
        string? baseDirectory = null,
        ILogger<LocalFileMeetingRepository>? logger = null)
    {
        _exporter = exporter;
        _logger = logger;
        _baseDirectory = baseDirectory ?? ResolveDefaultMeetingsDirectory();

        if (!Directory.Exists(_baseDirectory))
        {
            Directory.CreateDirectory(_baseDirectory);
        }
        else
        {
            CleanupPendingDeletions();
        }
    }

    /// <summary>
    /// Resuelve la ruta del directorio de reuniones. Si la aplicación está ejecutándose
    /// desde un subdirectorio 'data' (estructura con DLLs aisladas), ubica 'Meetings'
    /// en el directorio raíz de la aplicación junto a 'data'.
    /// </summary>
    public static string ResolveDefaultMeetingsDirectory(string? currentBaseDir = null)
    {
        var rawBase = currentBaseDir ?? AppDomain.CurrentDomain.BaseDirectory;
        var normalized = rawBase.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var dirName = Path.GetFileName(normalized);

        if (string.Equals(dirName, "data", StringComparison.OrdinalIgnoreCase))
        {
            var parent = Path.GetDirectoryName(normalized);
            if (!string.IsNullOrEmpty(parent))
            {
                return Path.Combine(parent, "Meetings");
            }
        }

        return Path.Combine(normalized, "Meetings");
    }

    public string GetMeetingDirectoryPath(Meeting meeting)
    {
        var safeTitle = MakeValidFileName(meeting.Title);
        var time = meeting.StartTime?.LocalDateTime ?? meeting.Date;
        var datePrefix = time.ToString("yyyy-MM-dd");
        var folderName = $"{datePrefix}_{safeTitle}_{meeting.Id[..6]}";
        return Path.Combine(_baseDirectory, folderName);
    }

    public string GetMeetingFileBaseName(Meeting meeting)
    {
        var safeTitle = MakeValidFileName(meeting.Title);
        var time = meeting.StartTime?.LocalDateTime ?? meeting.Date;
        var dateFormatted = time.ToString("yyyy-MM-dd_HH-mm");
        return $"{safeTitle}_{dateFormatted}";
    }

    public string GetAudioFilePath(Meeting meeting) => GetAudioFilePath(meeting, null);

    public string GetAudioFilePath(Meeting meeting, string? extension)
    {
        var dirPath = GetMeetingDirectoryPath(meeting);
        var baseName = GetMeetingFileBaseName(meeting);
        var ext = string.IsNullOrWhiteSpace(extension)
            ? ".mp3"
            : (extension.StartsWith('.') ? extension.ToLowerInvariant() : "." + extension.ToLowerInvariant());
        return Path.Combine(dirPath, $"{baseName}_audio{ext}");
    }

    private SemaphoreSlim GetLockForDir(string dirPath)
    {
        return _dirLocks.GetOrAdd(dirPath, _ => new SemaphoreSlim(1, 1));
    }

    private static async Task SafeWriteAllTextAsync(string path, string content, CancellationToken cancellationToken)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var tempPath = Path.Combine(dir ?? string.Empty, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        for (int attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                var utf8WithoutBom = new UTF8Encoding(false);
                using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
                using (var writer = new StreamWriter(stream, utf8WithoutBom))
                {
                    await writer.WriteAsync(content.AsMemory(), cancellationToken);
                    await writer.FlushAsync(cancellationToken);
                }

                File.Move(tempPath, path, overwrite: true);
                return;
            }
            catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException) && attempt < 3 && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(50 * (attempt + 1), cancellationToken);
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath)) File.Delete(tempPath);
                }
                catch { }
            }
        }
    }

    private static async Task<string> SafeReadAllTextAsync(string path, CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, useAsync: true);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                var text = await reader.ReadToEndAsync(cancellationToken);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
                if (attempt < 3 && !cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(50 * (attempt + 1), cancellationToken);
                    continue;
                }
                return text;
            }
            catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException) && attempt < 3 && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(50 * (attempt + 1), cancellationToken);
            }
        }

        return await File.ReadAllTextAsync(path, cancellationToken);
    }

    public Task SaveMeetingAsync(Meeting meeting, CancellationToken cancellationToken = default)
    {
        return SaveMeetingAsync(meeting, parameters: null, cancellationToken);
    }

    public async Task SaveMeetingAsync(Meeting meeting, MeetingSessionParameters? parameters, CancellationToken cancellationToken = default)
    {
        var dirPath = GetMeetingDirectoryPath(meeting);
        var dirLock = GetLockForDir(dirPath);
        await dirLock.WaitAsync(cancellationToken);

        try
        {
            if (!Directory.Exists(dirPath))
            {
                Directory.CreateDirectory(dirPath);
            }

            var baseName = GetMeetingFileBaseName(meeting);

            // 1. Guardar JSON estructurado
            var jsonFileName = $"{baseName}_meeting.json";
            var jsonPath = Path.Combine(dirPath, jsonFileName);
            var jsonContent = _exporter.ExportToJson(meeting);
            await SafeWriteAllTextAsync(jsonPath, jsonContent, cancellationToken);

            // 2. Guardar Markdown profesional
            var mdFileName = $"{baseName}_transcript.md";
            var mdPath = Path.Combine(dirPath, mdFileName);
            var mdContent = _exporter.ExportToMarkdown(meeting);
            await SafeWriteAllTextAsync(mdPath, mdContent, cancellationToken);

            // 3. Guardar texto plano
            var txtFileName = $"{baseName}_transcript.txt";
            var txtPath = Path.Combine(dirPath, txtFileName);
            var txtContent = _exporter.ExportToPlainText(meeting);
            await SafeWriteAllTextAsync(txtPath, txtContent, cancellationToken);

            string? resumenFileName = null;
            // 4. Guardar Resumen en texto plano si ha sido generado
            if ((meeting.SummarySegments != null && meeting.SummarySegments.Count > 0) || !string.IsNullOrWhiteSpace(meeting.SummaryText))
            {
                resumenFileName = $"{baseName}_resumen.txt";
                var resumenPath = Path.Combine(dirPath, resumenFileName);
                var resumenContent = _exporter.ExportSummaryToPlainText(meeting);
                await SafeWriteAllTextAsync(resumenPath, resumenContent, cancellationToken);
            }

            string? actaFileName = null;
            // 5. Guardar Actas versionadas de la reunión si existen
            if (meeting.ActaVersions != null && meeting.ActaVersions.Count > 0)
            {
                var actasDir = Path.Combine(dirPath, "actas");
                if (!Directory.Exists(actasDir))
                {
                    Directory.CreateDirectory(actasDir);
                }

                var activeVersion = meeting.GetActiveActaVersion() ?? meeting.ActaVersions.Last();
                meeting.SelectedActaVersionId = activeVersion.Id;
                meeting.ActaMarkdown = activeVersion.MarkdownContent;

                foreach (var ver in meeting.ActaVersions)
                {
                    if (string.IsNullOrWhiteSpace(ver.FileName))
                    {
                        var dateStr = ver.CreatedAt.ToString("yyyyMMdd_HHmmss");
                        var langStr = ver.Language.ToString().ToLowerInvariant();
                        var detailStr = ver.DetailLevel.ToString().ToLowerInvariant();
                        var shortId = ver.Id[..Math.Min(6, ver.Id.Length)];
                        ver.FileName = $"acta_{dateStr}_{langStr}_{detailStr}_{shortId}.md";
                    }

                    var verPath = Path.Combine(actasDir, ver.FileName);
                    await SafeWriteAllTextAsync(verPath, ver.MarkdownContent ?? string.Empty, cancellationToken);
                }

                var indexObj = new MeetingActaIndex
                {
                    MeetingId = meeting.Id,
                    ActiveVersionId = meeting.SelectedActaVersionId,
                    Versions = meeting.ActaVersions.Select(v => new MeetingActaVersionMetadata
                    {
                        Id = v.Id,
                        MeetingId = v.MeetingId,
                        FileName = v.FileName,
                        Language = v.Language,
                        DetailLevel = v.DetailLevel,
                        CreatedAt = v.CreatedAt,
                        LastModifiedAt = v.LastModifiedAt,
                        ModelUsed = v.ModelUsed,
                        IsLegacyMigrated = v.IsLegacyMigrated,
                        OriginalAiMarkdownContent = v.OriginalAiMarkdownContent ?? v.MarkdownContent
                    }).ToList()
                };

                var indexPath = Path.Combine(actasDir, "index.json");
                var indexJson = JsonSerializer.Serialize(indexObj, JsonOptions);
                await SafeWriteAllTextAsync(indexPath, indexJson, cancellationToken);

                // Guardar también archivo de acta principal en la raíz de la reunión para compatibilidad 100%
                actaFileName = $"{baseName}_acta.md";
                var actaPath = Path.Combine(dirPath, actaFileName);
                await SafeWriteAllTextAsync(actaPath, meeting.ActaMarkdown ?? string.Empty, cancellationToken);
            }
            else if (!string.IsNullOrWhiteSpace(meeting.ActaMarkdown))
            {
                // Fallback para acta directa legacy sin versiones en memoria todavía
                actaFileName = $"{baseName}_acta.md";
                var actaPath = Path.Combine(dirPath, actaFileName);
                await SafeWriteAllTextAsync(actaPath, meeting.ActaMarkdown, cancellationToken);

                // Crear e indexar versión automáticamente
                await LoadActaVersionsAsync(meeting, dirPath, cancellationToken);
            }

            // 6. Guardar archivo de parámetros y metadatos de la sesión
            var paramsObj = parameters ?? new MeetingSessionParameters();
            paramsObj.MeetingId = meeting.Id;
            paramsObj.Title = meeting.Title;
            paramsObj.Date = meeting.Date;
            paramsObj.StartTime = meeting.StartTime;
            paramsObj.EndTime = meeting.EndTime;
            paramsObj.Duration = meeting.Duration;

            paramsObj.GeneratedFiles["MeetingJson"] = jsonFileName;
            paramsObj.GeneratedFiles["TranscriptMarkdown"] = mdFileName;
            paramsObj.GeneratedFiles["TranscriptText"] = txtFileName;
            if (resumenFileName != null)
            {
                paramsObj.GeneratedFiles["SummaryText"] = resumenFileName;
            }
            if (!string.IsNullOrEmpty(meeting.AudioFilePath))
            {
                paramsObj.GeneratedFiles["Audio"] = Path.GetFileName(meeting.AudioFilePath);
            }
            if (actaFileName != null)
            {
                paramsObj.GeneratedFiles["ActaMarkdown"] = actaFileName;
            }

            var paramsPath = Path.Combine(dirPath, $"{baseName}_params.json");
            var paramsJson = JsonSerializer.Serialize(paramsObj, JsonOptions);
            await SafeWriteAllTextAsync(paramsPath, paramsJson, cancellationToken);

            _logger?.LogInformation("Reunión '{Title}' ({Id}) guardada con éxito en {Path}", meeting.Title, meeting.Id, dirPath);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error al guardar la reunión '{Title}' en disco.", meeting.Title);
            throw;
        }
        finally
        {
            dirLock.Release();
        }
    }

    public async Task<Meeting?> GetMeetingAsync(string id, CancellationToken cancellationToken = default)
    {
        var all = await GetAllMeetingsAsync(cancellationToken);
        return all.FirstOrDefault(m => m.Id == id);
    }

    public async Task<IReadOnlyList<Meeting>> GetAllMeetingsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<Meeting>();

        if (!Directory.Exists(_baseDirectory))
        {
            return result;
        }

        var directories = Directory.GetDirectories(_baseDirectory);
        foreach (var dir in directories)
        {
            if (Path.GetFileName(dir).StartsWith(".deleting_", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Buscar prioritariamente *_meeting.json, con fallback a meeting.json tradicional
            string? jsonFile = Directory.GetFiles(dir, "*_meeting.json").FirstOrDefault();
            if (jsonFile == null)
            {
                var legacyPath = Path.Combine(dir, "meeting.json");
                if (File.Exists(legacyPath))
                {
                    jsonFile = legacyPath;
                }
                else
                {
                    jsonFile = Directory.GetFiles(dir, "*.json")
                        .FirstOrDefault(f => !f.EndsWith("_params.json", StringComparison.OrdinalIgnoreCase) &&
                                             !f.EndsWith("parameters.json", StringComparison.OrdinalIgnoreCase));
                }
            }

            if (jsonFile != null && File.Exists(jsonFile))
            {
                try
                {
                    var json = await SafeReadAllTextAsync(jsonFile, cancellationToken);
                    var meeting = JsonSerializer.Deserialize<Meeting>(json, JsonOptions);
                    if (meeting != null)
                    {
                        meeting.SyncParticipantsFromTranscript();
                        meeting.EnsureCompatibility();

                        // Asegurar resolución de AudioFilePath relativo/local
                        if (string.IsNullOrWhiteSpace(meeting.AudioFilePath) || !File.Exists(meeting.AudioFilePath))
                        {
                            var audio = Directory.GetFiles(dir, "*.*")
                                .FirstOrDefault(f => SupportedAudioFormats.IsSupported(Path.GetExtension(f)));
                            if (audio != null)
                            {
                                meeting.AudioFilePath = audio;
                            }
                        }

                        // Cargar o migrar historial de actas versionadas
                        await LoadActaVersionsAsync(meeting, dir, cancellationToken);

                        result.Add(meeting);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "No se pudo leer la reunión del archivo: {File}", jsonFile);
                }
            }
        }

        return result.OrderByDescending(m => m.Date).ThenByDescending(m => m.StartTime).ToList();
    }

    /// <summary>
    /// Carga el historial de versiones de actas desde la subcarpeta actas/index.json,
    /// o realiza una migración retrocompatible e idempotente si existe un acta legacy.
    /// </summary>
    public async Task LoadActaVersionsAsync(Meeting meeting, string dirPath, CancellationToken cancellationToken = default)
    {
        var actasDir = Path.Combine(dirPath, "actas");
        var indexPath = Path.Combine(actasDir, "index.json");

        if (File.Exists(indexPath))
        {
            try
            {
                var indexJson = await SafeReadAllTextAsync(indexPath, cancellationToken);
                var indexObj = JsonSerializer.Deserialize<MeetingActaIndex>(indexJson, JsonOptions);
                if (indexObj != null && indexObj.Versions != null && indexObj.Versions.Count > 0)
                {
                    var loadedVersions = new List<MeetingActaVersion>();
                    foreach (var meta in indexObj.Versions)
                    {
                        var verFilePath = Path.Combine(actasDir, meta.FileName);
                        string content = string.Empty;
                        if (File.Exists(verFilePath))
                        {
                            content = await SafeReadAllTextAsync(verFilePath, cancellationToken);
                        }
                        else if (!string.IsNullOrWhiteSpace(meeting.ActaMarkdown))
                        {
                            content = meeting.ActaMarkdown;
                        }

                        loadedVersions.Add(new MeetingActaVersion
                        {
                            Id = meta.Id,
                            MeetingId = meta.MeetingId,
                            FileName = meta.FileName,
                            Language = meta.Language,
                            DetailLevel = meta.DetailLevel,
                            CreatedAt = meta.CreatedAt,
                            LastModifiedAt = meta.LastModifiedAt,
                            ModelUsed = meta.ModelUsed,
                            IsLegacyMigrated = meta.IsLegacyMigrated,
                            MarkdownContent = content,
                            OriginalAiMarkdownContent = meta.OriginalAiMarkdownContent ?? content
                        });
                    }

                    if (loadedVersions.Count > 0)
                    {
                        meeting.ActaVersions = loadedVersions;
                        meeting.SelectedActaVersionId = indexObj.ActiveVersionId ?? loadedVersions.Last().Id;
                        var active = meeting.GetActiveActaVersion();
                        if (active != null && !string.IsNullOrWhiteSpace(active.MarkdownContent))
                        {
                            meeting.ActaMarkdown = active.MarkdownContent;
                        }
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error al leer actas/index.json en {Dir}", dirPath);
            }
        }

        // Si no había index.json o estaba vacío, comprobar si existe un acta legacy (*_acta.md) o en meeting.ActaMarkdown
        string? legacyContent = null;
        DateTimeOffset legacyCreatedAt = meeting.StartTime?.LocalDateTime ?? meeting.Date;
        string? legacyActaFile = Directory.GetFiles(dirPath, "*_acta.md").FirstOrDefault();
        if (legacyActaFile != null && File.Exists(legacyActaFile))
        {
            try
            {
                legacyContent = await SafeReadAllTextAsync(legacyActaFile, cancellationToken);
                legacyCreatedAt = File.GetCreationTime(legacyActaFile);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error al leer acta previa de {File}", legacyActaFile);
            }
        }

        if (string.IsNullOrWhiteSpace(legacyContent) && !string.IsNullOrWhiteSpace(meeting.ActaMarkdown))
        {
            legacyContent = meeting.ActaMarkdown;
        }

        if (!string.IsNullOrWhiteSpace(legacyContent))
        {
            // Migración idempotente a la nueva estructura actas/
            var shortId = meeting.Id[..Math.Min(6, meeting.Id.Length)];
            var legacyVersion = new MeetingActaVersion
            {
                Id = Guid.NewGuid().ToString("N"),
                MeetingId = meeting.Id,
                FileName = $"acta_legacy_{shortId}.md",
                Language = LanguageMode.Auto,
                DetailLevel = ActaDetailLevel.Normal,
                CreatedAt = legacyCreatedAt,
                IsLegacyMigrated = true,
                MarkdownContent = legacyContent,
                OriginalAiMarkdownContent = legacyContent
            };

            meeting.ActaVersions = new List<MeetingActaVersion> { legacyVersion };
            meeting.SelectedActaVersionId = legacyVersion.Id;
            meeting.ActaMarkdown = legacyContent;

            try
            {
                if (!Directory.Exists(actasDir))
                {
                    Directory.CreateDirectory(actasDir);
                }
                var verPath = Path.Combine(actasDir, legacyVersion.FileName);
                if (!File.Exists(verPath))
                {
                    await SafeWriteAllTextAsync(verPath, legacyContent, cancellationToken);
                }

                var indexObj = new MeetingActaIndex
                {
                    MeetingId = meeting.Id,
                    ActiveVersionId = legacyVersion.Id,
                    Versions = new List<MeetingActaVersionMetadata>
                    {
                        new()
                        {
                            Id = legacyVersion.Id,
                            MeetingId = legacyVersion.MeetingId,
                            FileName = legacyVersion.FileName,
                            Language = legacyVersion.Language,
                            DetailLevel = legacyVersion.DetailLevel,
                            CreatedAt = legacyVersion.CreatedAt,
                            IsLegacyMigrated = true
                        }
                    }
                };
                var indexJson = JsonSerializer.Serialize(indexObj, JsonOptions);
                await SafeWriteAllTextAsync(Path.Combine(actasDir, "index.json"), indexJson, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error al persistir la migración del acta legacy en {ActasDir}", actasDir);
            }
        }
    }

    public static string MakeValidFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = string.Join("_", name.Split(invalid, StringSplitOptions.RemoveEmptyEntries)).Trim();
        clean = clean.Replace(' ', '-');
        return string.IsNullOrWhiteSpace(clean) ? "Reunion" : clean;
    }

    /// <summary>
    /// Limpia los directorios temporales de borrado pendiente (.deleting_*) que hayan podido quedar de sesiones anteriores.
    /// </summary>
    private void CleanupPendingDeletions()
    {
        try
        {
            if (!Directory.Exists(_baseDirectory)) return;

            var pendingDirs = Directory.GetDirectories(_baseDirectory, ".deleting_*");
            foreach (var dir in pendingDirs)
            {
                try
                {
                    DeleteDirectoryWithRetries(dir, maxRetries: 2, delayMs: 50);
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "No se pudo limpiar el directorio temporal de borrado pendiente: {Dir}", dir);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Error al comprobar directorios de borrado pendiente en {BaseDir}", _baseDirectory);
        }
    }

    /// <summary>
    /// Localiza de forma segura el directorio en disco de una reunión a partir de su ID único.
    /// </summary>
    public string? FindMeetingDirectory(string meetingId)
    {
        if (string.IsNullOrWhiteSpace(meetingId) || !Directory.Exists(_baseDirectory)) return null;

        var directories = Directory.GetDirectories(_baseDirectory);
        var shortId = meetingId.Length >= 6 ? meetingId[..6] : meetingId;

        // 1. Intento rápido: directorio que finaliza con _{shortId}
        foreach (var dir in directories)
        {
            var dirName = Path.GetFileName(dir);
            if (dirName.StartsWith(".deleting_", StringComparison.OrdinalIgnoreCase)) continue;

            if (dirName.EndsWith($"_{shortId}", StringComparison.OrdinalIgnoreCase))
            {
                if (DirectoryMatchesMeetingId(dir, meetingId))
                {
                    return dir;
                }
            }
        }

        // 2. Búsqueda exhaustiva en todos los subdirectorios si la carpeta fue renombrada
        foreach (var dir in directories)
        {
            var dirName = Path.GetFileName(dir);
            if (dirName.StartsWith(".deleting_", StringComparison.OrdinalIgnoreCase)) continue;

            if (DirectoryMatchesMeetingId(dir, meetingId))
            {
                return dir;
            }
        }

        return null;
    }

    private static bool DirectoryMatchesMeetingId(string dir, string meetingId)
    {
        var jsonFile = Directory.GetFiles(dir, "*_meeting.json").FirstOrDefault()
                       ?? (File.Exists(Path.Combine(dir, "meeting.json")) ? Path.Combine(dir, "meeting.json") : null);

        if (jsonFile != null && File.Exists(jsonFile))
        {
            try
            {
                using var stream = new FileStream(jsonFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var doc = JsonDocument.Parse(stream);
                if (doc.RootElement.TryGetProperty("id", out var idProp) &&
                    string.Equals(idProp.GetString(), meetingId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch
            {
                // Ignorar archivos temporalmente no legibles
            }
        }
        return false;
    }

    /// <summary>
    /// Valida que el directorio pertenezca estrictamente al almacenamiento administrado de reuniones y no sea el directorio raíz.
    /// </summary>
    private void ValidateSafeMeetingDirectory(string dirPath)
    {
        if (string.IsNullOrWhiteSpace(dirPath))
            throw new ArgumentException("La ruta del directorio de la reunión no puede estar vacía.", nameof(dirPath));

        var fullBase = Path.GetFullPath(_baseDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullDir = Path.GetFullPath(dirPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.Equals(fullBase, fullDir, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Operación denegada: no se permite eliminar el directorio raíz de reuniones.");
        }

        if (!fullDir.StartsWith(fullBase + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Operación denegada: el directorio '{fullDir}' no pertenece al almacenamiento de reuniones '{fullBase}'.");
        }
    }

    /// <summary>
    /// Elimina una reunión completa, incluyendo todos sus archivos asociados (JSON, audios, transcripciones, actas).
    /// </summary>
    public async Task<bool> DeleteMeetingAsync(string meetingId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(meetingId))
        {
            throw new ArgumentException("El identificador de la reunión no puede estar vacío.", nameof(meetingId));
        }

        var dirPath = FindMeetingDirectory(meetingId);
        if (dirPath == null || !Directory.Exists(dirPath))
        {
            _logger?.LogWarning("No se encontró el directorio de la reunión con ID {MeetingId} para eliminar.", meetingId);
            return false;
        }

        ValidateSafeMeetingDirectory(dirPath);

        var dirLock = GetLockForDir(dirPath);
        await dirLock.WaitAsync(cancellationToken);

        try
        {
            var shortId = meetingId.Length >= 6 ? meetingId[..6] : meetingId;
            var tempDeletingDir = Path.Combine(_baseDirectory, $".deleting_{shortId}_{Guid.NewGuid():N}");

            // Fase 1: Mover atómicamente a directorio temporal de borrado pendiente
            Directory.Move(dirPath, tempDeletingDir);

            // Fase 2: Borrado físico con reintentos
            DeleteDirectoryWithRetries(tempDeletingDir);

            _dirLocks.TryRemove(dirPath, out _);
            _logger?.LogInformation("Reunión {MeetingId} y directorio {Path} eliminados correctamente.", meetingId, dirPath);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error al eliminar la reunión {MeetingId} en {Path}", meetingId, dirPath);
            throw;
        }
        finally
        {
            try { dirLock.Release(); } catch { }
        }
    }

    /// <summary>
    /// Elimina una versión individual del acta de una reunión, conservando el resto de los datos y versiones.
    /// </summary>
    public async Task<bool> DeleteActaVersionAsync(string meetingId, string versionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(meetingId))
            throw new ArgumentException("El ID de reunión no puede estar vacío.", nameof(meetingId));
        if (string.IsNullOrWhiteSpace(versionId))
            throw new ArgumentException("El ID de versión no puede estar vacío.", nameof(versionId));

        var dirPath = FindMeetingDirectory(meetingId);
        if (dirPath == null || !Directory.Exists(dirPath))
        {
            _logger?.LogWarning("No se encontró el directorio de la reunión {MeetingId} para eliminar versión {VersionId}.", meetingId, versionId);
            return false;
        }

        ValidateSafeMeetingDirectory(dirPath);

        var dirLock = GetLockForDir(dirPath);
        await dirLock.WaitAsync(cancellationToken);

        try
        {
            // 1. Localizar y cargar el JSON de la reunión
            string? jsonFile = Directory.GetFiles(dirPath, "*_meeting.json").FirstOrDefault();
            if (jsonFile == null)
            {
                var legacyPath = Path.Combine(dirPath, "meeting.json");
                if (File.Exists(legacyPath))
                {
                    jsonFile = legacyPath;
                }
            }

            if (jsonFile == null || !File.Exists(jsonFile))
            {
                _logger?.LogWarning("No se encontró el archivo JSON de la reunión {MeetingId}.", meetingId);
                return false;
            }

            var json = await SafeReadAllTextAsync(jsonFile, cancellationToken);
            var meeting = JsonSerializer.Deserialize<Meeting>(json, JsonOptions);
            if (meeting == null) return false;

            meeting.SyncParticipantsFromTranscript();
            meeting.EnsureCompatibility();

            // Cargar versiones completas de actas desde actas/ o legacy
            await LoadActaVersionsAsync(meeting, dirPath, cancellationToken);

            var versionToDelete = meeting.ActaVersions.FirstOrDefault(v => string.Equals(v.Id, versionId, StringComparison.OrdinalIgnoreCase));
            if (versionToDelete == null)
            {
                _logger?.LogWarning("No se encontró la versión de acta {VersionId} en la reunión {MeetingId}.", versionId, meetingId);
                return false;
            }

            // 2. Eliminar el archivo Markdown individual en la carpeta actas/ si existe
            var actasDir = Path.Combine(dirPath, "actas");
            if (!string.IsNullOrWhiteSpace(versionToDelete.FileName))
            {
                var cleanFileName = Path.GetFileName(versionToDelete.FileName);
                var verFilePath = Path.Combine(actasDir, cleanFileName);
                if (File.Exists(verFilePath))
                {
                    try
                    {
                        File.Delete(verFilePath);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "No se pudo eliminar el archivo de versión {Path}", verFilePath);
                    }
                }
            }

            // 3. Eliminar de la lista de versiones en el modelo y actualizar versión activa
            meeting.RemoveActaVersion(versionId);

            var baseName = GetMeetingFileBaseName(meeting);
            var rootActaFile = Path.Combine(dirPath, $"{baseName}_acta.md");

            if (meeting.ActaVersions.Count > 0)
            {
                var activeVersion = meeting.GetActiveActaVersion() ?? meeting.ActaVersions.OrderByDescending(v => v.CreatedAt).First();
                meeting.SelectedActaVersionId = activeVersion.Id;
                meeting.ActaMarkdown = activeVersion.MarkdownContent;

                // Actualizar archivo de acta en la raíz
                await SafeWriteAllTextAsync(rootActaFile, meeting.ActaMarkdown ?? string.Empty, cancellationToken);

                // Actualizar index.json
                var indexObj = new MeetingActaIndex
                {
                    MeetingId = meeting.Id,
                    ActiveVersionId = meeting.SelectedActaVersionId,
                    Versions = meeting.ActaVersions.Select(v => new MeetingActaVersionMetadata
                    {
                        Id = v.Id,
                        MeetingId = v.MeetingId,
                        FileName = v.FileName,
                        Language = v.Language,
                        DetailLevel = v.DetailLevel,
                        CreatedAt = v.CreatedAt,
                        LastModifiedAt = v.LastModifiedAt,
                        ModelUsed = v.ModelUsed,
                        IsLegacyMigrated = v.IsLegacyMigrated,
                        OriginalAiMarkdownContent = v.OriginalAiMarkdownContent ?? v.MarkdownContent
                    }).ToList()
                };

                var indexJson = JsonSerializer.Serialize(indexObj, JsonOptions);
                await SafeWriteAllTextAsync(Path.Combine(actasDir, "index.json"), indexJson, cancellationToken);
            }
            else
            {
                // Se eliminó la última versión disponible
                meeting.SelectedActaVersionId = null;
                meeting.ActaMarkdown = null;

                if (File.Exists(rootActaFile))
                {
                    try { File.Delete(rootActaFile); } catch { }
                }

                // Guardar index.json vacío
                var indexObj = new MeetingActaIndex
                {
                    MeetingId = meeting.Id,
                    ActiveVersionId = null,
                    Versions = new List<MeetingActaVersionMetadata>()
                };
                var indexJson = JsonSerializer.Serialize(indexObj, JsonOptions);
                await SafeWriteAllTextAsync(Path.Combine(actasDir, "index.json"), indexJson, cancellationToken);
            }

            // 4. Guardar JSON principal de la reunión
            var updatedJson = _exporter.ExportToJson(meeting);
            await SafeWriteAllTextAsync(jsonFile, updatedJson, cancellationToken);

            // 5. Actualizar _params.json si existe
            var paramsFile = Path.Combine(dirPath, $"{baseName}_params.json");
            if (File.Exists(paramsFile))
            {
                try
                {
                    var paramsJson = await SafeReadAllTextAsync(paramsFile, cancellationToken);
                    var paramsObj = JsonSerializer.Deserialize<MeetingSessionParameters>(paramsJson, JsonOptions);
                    if (paramsObj != null)
                    {
                        if (meeting.ActaVersions.Count == 0)
                        {
                            paramsObj.GeneratedFiles.Remove("ActaMarkdown");
                        }
                        var updatedParamsJson = JsonSerializer.Serialize(paramsObj, JsonOptions);
                        await SafeWriteAllTextAsync(paramsFile, updatedParamsJson, cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Fallo no crítico al actualizar _params.json tras borrar versión de acta.");
                }
            }

            _logger?.LogInformation("Versión {VersionId} eliminada con éxito de la reunión {MeetingId}.", versionId, meetingId);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error al eliminar la versión de acta {VersionId} de la reunión {MeetingId}.", versionId, meetingId);
            throw;
        }
        finally
        {
            dirLock.Release();
        }
    }

    private static void DeleteDirectoryWithRetries(string path, int maxRetries = 5, int delayMs = 100)
    {
        if (!Directory.Exists(path)) return;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                var dirInfo = new DirectoryInfo(path);
                foreach (var file in dirInfo.GetFiles("*", SearchOption.AllDirectories))
                {
                    try
                    {
                        file.Attributes = FileAttributes.Normal;
                    }
                    catch { }
                }

                Directory.Delete(path, recursive: true);
                return;
            }
            catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException) && attempt < maxRetries)
            {
                Thread.Sleep(delayMs * attempt);
            }
        }

        Directory.Delete(path, recursive: true);
    }
}
