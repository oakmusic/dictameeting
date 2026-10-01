using DictaMeeting.Meetings.Enums;
using DictaMeeting.Meetings.Exporters;
using DictaMeeting.Meetings.Interfaces;
using DictaMeeting.Meetings.Models;
using DictaMeeting.Meetings.Services;
using Xunit;

namespace DictaMeeting.Tests;

/// <summary>
/// Tests para la funcionalidad de importación de transcripciones de texto.
/// Cubre: formatos soportados, parseo de distintos formatos, creación de reunión,
/// integración con el repositorio, y validaciones de error.
/// </summary>
public class TranscriptImportTests : IDisposable
{
    private readonly string _testTempDir;

    public TranscriptImportTests()
    {
        _testTempDir = Path.Combine(Path.GetTempPath(), $"DictaMeeting_TranscriptImportTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testTempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testTempDir))
                Directory.Delete(_testTempDir, true);
        }
        catch { }
    }

    // ── Stub repositorio ─────────────────────────────────────────────────────

    private class StubRepository : IMeetingRepository
    {
        private readonly string _baseDir;
        public List<Meeting> SavedMeetings { get; } = new();

        public StubRepository(string baseDir) { _baseDir = baseDir; }

        public Task SaveMeetingAsync(Meeting meeting, CancellationToken _ = default)
        {
            SavedMeetings.RemoveAll(m => m.Id == meeting.Id);
            SavedMeetings.Add(meeting);
            return Task.CompletedTask;
        }

        public Task SaveMeetingAsync(Meeting meeting, MeetingSessionParameters? _, CancellationToken __ = default)
        {
            SavedMeetings.RemoveAll(m => m.Id == meeting.Id);
            SavedMeetings.Add(meeting);
            return Task.CompletedTask;
        }

        public Task<Meeting?> GetMeetingAsync(string id, CancellationToken _ = default)
            => Task.FromResult(SavedMeetings.FirstOrDefault(m => m.Id == id));

        public Task<IReadOnlyList<Meeting>> GetAllMeetingsAsync(CancellationToken _ = default)
            => Task.FromResult<IReadOnlyList<Meeting>>(SavedMeetings);

        public string GetMeetingDirectoryPath(Meeting meeting)
        {
            var dir = Path.Combine(_baseDir, meeting.Id);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return dir;
        }

        public string GetMeetingFileBaseName(Meeting meeting)
            => $"{meeting.Title}_{meeting.Date:yyyy-MM-dd_HH-mm}";

        public string GetAudioFilePath(Meeting meeting) => GetAudioFilePath(meeting, ".mp3");
        public string GetAudioFilePath(Meeting meeting, string? extension)
            => Path.Combine(GetMeetingDirectoryPath(meeting), $"{meeting.Title}_audio{extension ?? ".mp3"}");
    }

    // =========================================================================
    // 1. TESTS DE FORMATOS SOPORTADOS
    // =========================================================================

    [Theory]
    [InlineData(".txt", true)]
    [InlineData(".md", true)]
    [InlineData(".markdown", true)]
    [InlineData(".text", true)]
    [InlineData(".log", true)]
    [InlineData(".srt", true)]
    [InlineData(".vtt", true)]
    [InlineData(".rst", true)]
    [InlineData(".org", true)]
    [InlineData("TXT", true)]         // extensión con mayúsculas
    [InlineData("transcript.MD", true)]
    [InlineData(".mp3", false)]
    [InlineData(".pdf", false)]
    [InlineData(".exe", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void SupportedTranscriptFormats_ValidatesExtensions(string? ext, bool expected)
    {
        Assert.Equal(expected, SupportedTranscriptFormats.IsSupported(ext));
    }

    [Fact]
    public void SupportedTranscriptFormats_FileDialogFilter_ContainsTxt()
    {
        Assert.Contains("*.txt", SupportedTranscriptFormats.FileDialogFilter);
    }

    [Fact]
    public void SupportedTranscriptFormats_DisplayList_ContainsMainFormats()
    {
        var list = SupportedTranscriptFormats.DisplayList;
        Assert.Contains("TXT", list);
        Assert.Contains("MD", list);
        Assert.Contains("SRT", list);
    }

    // =========================================================================
    // 2. TESTS DE PARSEO — TranscriptTextParser
    // =========================================================================

    [Fact]
    public void Parser_PlainText_CreatesSegmentsWithNoSpeaker()
    {
        var content = "Buenos días a todos.\n\nEmpezamos con el orden del día.";
        var segments = TranscriptTextParser.Parse(content);

        Assert.Equal(2, segments.Count);
        Assert.All(segments, s => Assert.True(string.IsNullOrEmpty(s.SpeakerId)));
        Assert.Equal("Buenos días a todos.", segments[0].Text);
        Assert.Equal("Empezamos con el orden del día.", segments[1].Text);
    }

    [Fact]
    public void Parser_TimestampSpeakerFormat_PreservesSpeakerAndTimestamp()
    {
        var content =
            "[00:01:23] SPEAKER_00: Buenos días.\n" +
            "[00:01:27] SPEAKER_01: Buenos días a todos.";

        var segments = TranscriptTextParser.Parse(content);

        Assert.Equal(2, segments.Count);

        Assert.Equal(new TimeSpan(0, 1, 23), segments[0].StartTime);
        Assert.Equal("SPEAKER_00", segments[0].SpeakerId);
        Assert.Equal("Buenos días.", segments[0].Text);

        Assert.Equal(new TimeSpan(0, 1, 27), segments[1].StartTime);
        Assert.Equal("SPEAKER_01", segments[1].SpeakerId);
        Assert.Equal("Buenos días a todos.", segments[1].Text);
    }

    [Fact]
    public void Parser_MarkdownTranscript_PreservesSpeakerAndTimestamp()
    {
        var content =
            "# Reunión de Equipo\n\n" +
            "## Participantes\n\n" +
            "- Aritz\n\n" +
            "## Transcripción\n\n" +
            "### 00:01:23 — Aritz\n\n" +
            "Buenos días a todos.\n\n" +
            "### 00:01:27 — María\n\n" +
            "Gracias por asistir.";

        var segments = TranscriptTextParser.Parse(content);

        Assert.True(segments.Count >= 2);
        var aritz = segments.FirstOrDefault(s => s.SpeakerId == "Aritz");
        Assert.NotNull(aritz);
        Assert.Equal(new TimeSpan(0, 1, 23), aritz!.StartTime);
    }

    [Fact]
    public void Parser_SrtFormat_ParsesTimestampsAndText()
    {
        var content =
            "1\n" +
            "00:00:01,000 --> 00:00:04,000\n" +
            "Buenos días.\n\n" +
            "2\n" +
            "00:00:05,000 --> 00:00:08,500\n" +
            "Hola a todos.\n";

        var segments = TranscriptTextParser.Parse(content);

        Assert.Equal(2, segments.Count);
        Assert.Equal(TimeSpan.FromSeconds(1), segments[0].StartTime);
        Assert.Equal(TimeSpan.FromSeconds(4), segments[0].EndTime);
        Assert.Equal("Buenos días.", segments[0].Text);
    }

    [Fact]
    public void Parser_SrtWithSpeakerTag_ExtractsSpeaker()
    {
        var content =
            "1\n" +
            "00:00:01,000 --> 00:00:04,000\n" +
            "[SPEAKER_00] Buenos días.\n\n" +
            "2\n" +
            "00:00:05,000 --> 00:00:08,000\n" +
            "[SPEAKER_01] Hola.\n";

        var segments = TranscriptTextParser.Parse(content);

        Assert.Equal(2, segments.Count);
        Assert.Equal("SPEAKER_00", segments[0].SpeakerId);
        Assert.Equal("Buenos días.", segments[0].Text);
    }

    [Fact]
    public void Parser_VttFormat_ParsesTimestampsAndText()
    {
        var content =
            "WEBVTT\n\n" +
            "00:00:01.000 --> 00:00:04.000\n" +
            "Buenos días.\n\n" +
            "00:00:05.000 --> 00:00:08.500\n" +
            "Hola a todos.\n";

        var segments = TranscriptTextParser.Parse(content);

        Assert.Equal(2, segments.Count);
        Assert.Equal(TimeSpan.FromSeconds(1), segments[0].StartTime);
        Assert.Equal("Buenos días.", segments[0].Text);
    }

    [Fact]
    public void Parser_EmptyContent_ReturnsEmptyList()
    {
        Assert.Empty(TranscriptTextParser.Parse(""));
        Assert.Empty(TranscriptTextParser.Parse("   \n  "));
    }

    [Fact]
    public void Parser_AllSegmentsAreFinal()
    {
        var content = "[00:00:10] SPEAKER_00: Texto de prueba.";
        var segments = TranscriptTextParser.Parse(content);
        Assert.All(segments, s => Assert.True(s.IsFinal));
    }

    // =========================================================================
    // 3. TESTS DE MeetingService.CreateMeetingFromTranscriptAsync
    // =========================================================================

    [Fact]
    public async Task CreateMeetingFromTranscriptAsync_CreatesCompletedMeetingWithoutAudio()
    {
        var repo = new StubRepository(_testTempDir);
        var service = new MeetingService(repo);

        var filePath = Path.Combine(_testTempDir, "Reunion_Comite.txt");
        await File.WriteAllTextAsync(filePath,
            "[00:01:00] SPEAKER_00: Buenos días.\n" +
            "[00:01:05] SPEAKER_01: Buenos días a todos.");

        var meeting = await service.CreateMeetingFromTranscriptAsync("", filePath);

        // Debe tener el nombre del archivo como título
        Assert.Equal("Reunion_Comite", meeting.Title);

        // Sin audio
        Assert.Null(meeting.AudioFilePath);

        // Estado completado sin pendiente de procesamiento
        Assert.Equal(MeetingState.Completed, meeting.State);
        Assert.Equal(ProcessingStatus.Completed, meeting.ProcessingStatus);

        // Con segmentos parseados
        Assert.Equal(2, meeting.Transcript.Count);
        Assert.Equal("SPEAKER_00", meeting.Transcript[0].SpeakerId);

        // Participantes sincronizados
        Assert.Equal(2, meeting.Participants.Count);

        // Guardado en repositorio
        Assert.Single(repo.SavedMeetings);
    }

    [Fact]
    public async Task CreateMeetingFromTranscriptAsync_UsesProvidedTitle()
    {
        var repo = new StubRepository(_testTempDir);
        var service = new MeetingService(repo);

        var filePath = Path.Combine(_testTempDir, "archivo.txt");
        await File.WriteAllTextAsync(filePath, "Texto simple de la reunión.");

        var meeting = await service.CreateMeetingFromTranscriptAsync("Mi Reunión Personalizada", filePath);

        Assert.Equal("Mi Reunión Personalizada", meeting.Title);
    }

    [Fact]
    public async Task CreateMeetingFromTranscriptAsync_CopiesOriginalFileToMeetingFolder()
    {
        var repo = new StubRepository(_testTempDir);
        var service = new MeetingService(repo);

        var filePath = Path.Combine(_testTempDir, "Transcripcion_Original.txt");
        await File.WriteAllTextAsync(filePath, "Texto de prueba.");

        var meeting = await service.CreateMeetingFromTranscriptAsync("", filePath);

        // El archivo original no fue eliminado
        Assert.True(File.Exists(filePath));

        // La carpeta de reunión fue creada
        var meetingDir = repo.GetMeetingDirectoryPath(meeting);
        Assert.True(Directory.Exists(meetingDir));

        // Hay una copia del archivo dentro
        var copiedFile = Directory.GetFiles(meetingDir, "*_imported.txt").FirstOrDefault();
        Assert.NotNull(copiedFile);
        var copiedContent = await File.ReadAllTextAsync(copiedFile!);
        Assert.Equal("Texto de prueba.", copiedContent);
    }

    [Fact]
    public async Task CreateMeetingFromTranscriptAsync_ThrowsFileNotFoundException_WhenFileDoesNotExist()
    {
        var repo = new StubRepository(_testTempDir);
        var service = new MeetingService(repo);
        var nonExistent = Path.Combine(_testTempDir, "ghost.txt");

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            service.CreateMeetingFromTranscriptAsync("Ghost", nonExistent));
    }

    [Fact]
    public async Task CreateMeetingFromTranscriptAsync_ThrowsInvalidDataException_WhenFileIsEmpty()
    {
        var repo = new StubRepository(_testTempDir);
        var service = new MeetingService(repo);

        var emptyPath = Path.Combine(_testTempDir, "empty.txt");
        await File.WriteAllTextAsync(emptyPath, "   \n   ");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.CreateMeetingFromTranscriptAsync("Empty", emptyPath));
    }

    [Fact]
    public async Task CreateMeetingFromTranscriptAsync_ThrowsIfMeetingAlreadyRecording()
    {
        var repo = new StubRepository(_testTempDir);
        var service = new MeetingService(repo);

        // Iniciar una reunión
        await service.StartMeetingAsync("Reunión en curso");

        var filePath = Path.Combine(_testTempDir, "test.txt");
        await File.WriteAllTextAsync(filePath, "Texto.");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateMeetingFromTranscriptAsync("", filePath));
    }

    [Fact]
    public async Task CreateMeetingFromTranscriptAsync_PlainText_CreatesSingleParticipant_WhenNoSpeakers()
    {
        var repo = new StubRepository(_testTempDir);
        var service = new MeetingService(repo);

        var filePath = Path.Combine(_testTempDir, "sinAtribucion.md");
        await File.WriteAllTextAsync(filePath, "Párrafo uno sin speaker.\n\nPárrafo dos sin speaker.");

        var meeting = await service.CreateMeetingFromTranscriptAsync("", filePath);

        // Sin hablantes identificados => Participants vacío (ningún SpeakerId en segmentos)
        Assert.All(meeting.Transcript, s => Assert.Empty(s.SpeakerId));
        Assert.Empty(meeting.Participants);
        Assert.Equal(2, meeting.Transcript.Count);
    }

    // =========================================================================
    // 4. TEST DE EXPORTACIÓN: la reunión importada se puede exportar igual
    // =========================================================================

    [Fact]
    public async Task ImportedTranscriptMeeting_CanBeExportedToMarkdown()
    {
        var repo = new StubRepository(_testTempDir);
        var service = new MeetingService(repo);
        var exporter = new DocumentExporter();

        var filePath = Path.Combine(_testTempDir, "Exportable.txt");
        await File.WriteAllTextAsync(filePath,
            "[00:00:05] SPEAKER_00: Primera intervención.\n" +
            "[00:00:10] SPEAKER_01: Segunda intervención.");

        var meeting = await service.CreateMeetingFromTranscriptAsync("ReunionExportable", filePath);

        var markdown = exporter.ExportToMarkdown(meeting);
        var plainText = exporter.ExportToPlainText(meeting);
        var json = exporter.ExportToJson(meeting);

        Assert.Contains("ReunionExportable", markdown);
        Assert.Contains("SPEAKER_00", markdown);
        Assert.Contains("Primera intervención.", markdown);

        Assert.Contains("SPEAKER_01", plainText);
        Assert.Contains("Segunda intervención.", plainText);

        Assert.Contains("\"transcript\"", json);
    }
}
