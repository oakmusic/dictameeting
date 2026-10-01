using System.Text.Json;
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

    public Task SaveMeetingAsync(Meeting meeting, CancellationToken cancellationToken = default)
    {
        return SaveMeetingAsync(meeting, parameters: null, cancellationToken);
    }

    public async Task SaveMeetingAsync(Meeting meeting, MeetingSessionParameters? parameters, CancellationToken cancellationToken = default)
    {
        try
        {
            var dirPath = GetMeetingDirectoryPath(meeting);
            if (!Directory.Exists(dirPath))
            {
                Directory.CreateDirectory(dirPath);
            }

            var baseName = GetMeetingFileBaseName(meeting);

            // 1. Guardar JSON estructurado
            var jsonFileName = $"{baseName}_meeting.json";
            var jsonPath = Path.Combine(dirPath, jsonFileName);
            var jsonContent = _exporter.ExportToJson(meeting);
            await File.WriteAllTextAsync(jsonPath, jsonContent, cancellationToken);

            // 2. Guardar Markdown profesional
            var mdFileName = $"{baseName}_transcript.md";
            var mdPath = Path.Combine(dirPath, mdFileName);
            var mdContent = _exporter.ExportToMarkdown(meeting);
            await File.WriteAllTextAsync(mdPath, mdContent, cancellationToken);

            // 3. Guardar texto plano
            var txtFileName = $"{baseName}_transcript.txt";
            var txtPath = Path.Combine(dirPath, txtFileName);
            var txtContent = _exporter.ExportToPlainText(meeting);
            await File.WriteAllTextAsync(txtPath, txtContent, cancellationToken);

            string? resumenFileName = null;
            // 4. Guardar Resumen en texto plano si ha sido generado
            if ((meeting.SummarySegments != null && meeting.SummarySegments.Count > 0) || !string.IsNullOrWhiteSpace(meeting.SummaryText))
            {
                resumenFileName = $"{baseName}_resumen.txt";
                var resumenPath = Path.Combine(dirPath, resumenFileName);
                var resumenContent = _exporter.ExportSummaryToPlainText(meeting);
                await File.WriteAllTextAsync(resumenPath, resumenContent, cancellationToken);
            }

            string? actaFileName = null;
            // 5. Guardar Acta de la reunión si ha sido generada
            if (!string.IsNullOrWhiteSpace(meeting.ActaMarkdown))
            {
                actaFileName = $"{baseName}_acta.md";
                var actaPath = Path.Combine(dirPath, actaFileName);
                await File.WriteAllTextAsync(actaPath, meeting.ActaMarkdown, cancellationToken);
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
            await File.WriteAllTextAsync(paramsPath, paramsJson, cancellationToken);

            _logger?.LogInformation("Reunión '{Title}' ({Id}) guardada con éxito en {Path}", meeting.Title, meeting.Id, dirPath);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error al guardar la reunión '{Title}' en disco.", meeting.Title);
            throw;
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
                    var json = await File.ReadAllTextAsync(jsonFile, cancellationToken);
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

    public static string MakeValidFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = string.Join("_", name.Split(invalid, StringSplitOptions.RemoveEmptyEntries)).Trim();
        clean = clean.Replace(' ', '-');
        return string.IsNullOrWhiteSpace(clean) ? "Reunion" : clean;
    }
}
