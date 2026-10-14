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
}
