using DictaMeeting.Diarization.Models;
using DictaMeeting.Diarization.Services;
using DictaMeeting.Infrastructure.Persistence;
using DictaMeeting.Meetings.Exporters;
using DictaMeeting.Meetings.Models;
using Xunit;

namespace DictaMeeting.Tests;

public class PostProcessingAndExportIntegrationTests : IDisposable
{
    private readonly string _tempMeetingDir;

    public PostProcessingAndExportIntegrationTests()
    {
        _tempMeetingDir = Path.Combine(Path.GetTempPath(), "DictaMeeting_Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempMeetingDir);
    }

    [Fact]
    public async Task PostProcessingAndExport_GeneratesUnifiedMeetingFolderWithAllFormats()
    {
        // 1. Arrange: Modelo de reunión con metadatos y participantes
        var exporter = new DocumentExporter();
        var repository = new LocalFileMeetingRepository(exporter, _tempMeetingDir);

        var meeting = new Meeting
        {
            Title = "Reunión Estratégica Q4",
            Organizer = "Aritz Villodas",
            Company = "Tech Corp",
            Date = new DateTime(2026, 9, 21),
            StartTime = new DateTimeOffset(2026, 9, 21, 10, 0, 0, TimeSpan.FromHours(2)),
            EndTime = new DateTimeOffset(2026, 9, 21, 10, 45, 0, TimeSpan.FromHours(2))
        };

        // Participante renombrado en vivo durante la reunión
        meeting.Participants.Add(new Speaker { Id = "SPEAKER_00", DisplayName = "Aritz Villodas" });
        meeting.Participants.Add(new Speaker { Id = "SPEAKER_01", DisplayName = "Maria Garcia" });

        // Segmentos provisionales capturados en vivo
        var liveSegments = new List<TranscriptSegment>
        {
            new()
            {
                Id = "seg-1",
                StartTime = TimeSpan.FromSeconds(0),
                EndTime = TimeSpan.FromSeconds(5),
                SpeakerId = "SPEAKER_00",
                SpeakerDisplayName = "Aritz Villodas",
                Text = "Buenos días a todos, empezamos la sesión estratégica.",
                IsFinal = false
            },
            new()
            {
                Id = "seg-2",
                StartTime = TimeSpan.FromSeconds(6),
                EndTime = TimeSpan.FromSeconds(12),
                SpeakerId = "SPEAKER_01",
                SpeakerDisplayName = "Maria Garcia",
                Text = "Revisamos los puntos acordados y las metas del trimestre.",
                IsFinal = false
            }
        };

        // 2. Act: Reconciliación con diarización final
        var diarizationResult = new DiarizationResult
        {
            Duration = TimeSpan.FromSeconds(13),
            DetectedSpeakerCount = 2,
            ExclusiveSegments = new List<ExclusiveSpeakerSegment>
            {
                new() { StartTime = TimeSpan.FromSeconds(0), EndTime = TimeSpan.FromSeconds(5.2), SpeakerId = "SPEAKER_00" },
                new() { StartTime = TimeSpan.FromSeconds(5.8), EndTime = TimeSpan.FromSeconds(12.5), SpeakerId = "SPEAKER_01" }
            }
        };

        var aligner = new DictaMeeting.Diarization.Pipeline.SpeakerTranscriptAligner();
        var reconciledResult = aligner.Align(liveSegments, diarizationResult, meeting.Participants);

        meeting.Transcript.AddRange(reconciledResult.ReconciledSegments);

        // 3. Act: Guardado unificado de la sesión en disco
        await repository.SaveMeetingAsync(meeting);

        // 4. Assert: Verificar la carpeta y los archivos generados con formato descriptivo
        var meetingFolder = repository.GetMeetingDirectoryPath(meeting);
        Assert.True(Directory.Exists(meetingFolder), "La carpeta unificada de la reunión debe existir.");

        var baseName = repository.GetMeetingFileBaseName(meeting);
        Assert.Contains("Reunión-Estratégica-Q4", baseName);
        Assert.Contains("2026-09-21", baseName);

        var jsonPath = Path.Combine(meetingFolder, $"{baseName}_meeting.json");
        var mdPath = Path.Combine(meetingFolder, $"{baseName}_transcript.md");
        var txtPath = Path.Combine(meetingFolder, $"{baseName}_transcript.txt");
        var paramsPath = Path.Combine(meetingFolder, $"{baseName}_params.json");

        Assert.True(File.Exists(jsonPath), $"{baseName}_meeting.json debe existir en la carpeta de la reunión.");
        Assert.True(File.Exists(mdPath), $"{baseName}_transcript.md debe existir en la carpeta de la reunión.");
        Assert.True(File.Exists(txtPath), $"{baseName}_transcript.txt debe existir en la carpeta de la reunión.");
        Assert.True(File.Exists(paramsPath), $"{baseName}_params.json debe existir en la carpeta de la reunión.");

        // Verificar contenido del Markdown estructurado
        var mdContent = await File.ReadAllTextAsync(mdPath);
        Assert.Contains("# Reunión Estratégica Q4", mdContent);
        Assert.Contains("**Organizador:** Aritz Villodas", mdContent);
        Assert.Contains("**Empresa:** Tech Corp", mdContent);
        Assert.Contains("## Participantes", mdContent);
        Assert.Contains("Aritz Villodas", mdContent);
        Assert.Contains("Maria Garcia", mdContent);
        Assert.Contains("Buenos días a todos, empezamos la sesión estratégica.", mdContent);

        // Verificar contenido del texto plano
        var txtContent = await File.ReadAllTextAsync(txtPath);
        Assert.Contains("Reunión Estratégica Q4", txtContent);
        Assert.Contains("Aritz Villodas", txtContent);

        // Verificar contenido del JSON estructurado
        var jsonContent = await File.ReadAllTextAsync(jsonPath);
        Assert.Contains("\"title\": \"Reunión Estratégica Q4\"", jsonContent);
        Assert.Contains("\"speakerDisplayName\": \"Aritz Villodas\"", jsonContent);
        Assert.Contains("\"speakerId\": \"SPEAKER_00\"", jsonContent); // Preserva ID técnico intacto
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempMeetingDir))
            {
                Directory.Delete(_tempMeetingDir, recursive: true);
            }
        }
        catch { }
    }
}
