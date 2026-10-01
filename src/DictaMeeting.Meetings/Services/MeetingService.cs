using DictaMeeting.Meetings.Enums;
using DictaMeeting.Meetings.Interfaces;
using DictaMeeting.Meetings.Models;

namespace DictaMeeting.Meetings.Services;

public class MeetingService : IMeetingService
{
    private readonly IMeetingRepository _repository;
    private readonly Dictionary<string, string> _speakerAliases = new(StringComparer.OrdinalIgnoreCase);
    private Meeting? _currentMeeting;
    private MeetingState _currentState = MeetingState.Idle;

    public MeetingService(IMeetingRepository repository)
    {
        _repository = repository;
    }

    public Meeting? CurrentMeeting => _currentMeeting;
    public MeetingState CurrentState => _currentState;

    public event EventHandler<MeetingState>? StateChanged;
    public event EventHandler<TranscriptSegment>? SegmentAppended;
#pragma warning disable CS0067
    public event EventHandler<TranscriptSegment>? SegmentUpdated;
#pragma warning restore CS0067
    public event EventHandler<Speaker>? SpeakerUpdated;
    public event EventHandler<(string MergedSpeakerId, string TargetSpeakerId)>? SpeakerMerged;



    public Task StartMeetingAsync(string title, string organizer = "", string company = "", CancellationToken cancellationToken = default)
    {
        if (_currentState == MeetingState.Recording || _currentState == MeetingState.Preparing)
        {
            throw new InvalidOperationException("Ya hay una reunión en curso o en preparación.");
        }

        SetState(MeetingState.Preparing);
        _speakerAliases.Clear();

        var now = DateTimeOffset.Now;
        _currentMeeting = new Meeting
        {
            Title = string.IsNullOrWhiteSpace(title) ? $"Reunión {now:yyyy-MM-dd HH:mm}" : title.Trim(),
            Organizer = organizer.Trim(),
            Company = company.Trim(),
            Date = now.LocalDateTime,
            StartTime = now,
            State = MeetingState.Recording
        };

        SetState(MeetingState.Recording);
        return Task.CompletedTask;
    }

    public async Task<Meeting> CreateImportedMeetingAsync(
        string title,
        string sourceAudioFilePath,
        TimeSpan? audioDuration = null,
        string? language = null,
        CancellationToken cancellationToken = default)
    {
        if (_currentState == MeetingState.Recording || _currentState == MeetingState.Preparing)
        {
            throw new InvalidOperationException("Ya hay una reunión en curso o en preparación.");
        }

        if (string.IsNullOrWhiteSpace(sourceAudioFilePath) || !File.Exists(sourceAudioFilePath))
        {
            throw new FileNotFoundException($"El archivo de audio de origen no existe: {sourceAudioFilePath}");
        }

        Reset();

        var cleanTitle = string.IsNullOrWhiteSpace(title)
            ? Path.GetFileNameWithoutExtension(sourceAudioFilePath)
            : title.Trim();

        var now = DateTimeOffset.Now;
        var duration = audioDuration.HasValue && audioDuration.Value > TimeSpan.Zero
            ? audioDuration.Value
            : TimeSpan.Zero;

        var startTime = duration > TimeSpan.Zero ? now - duration : now;

        var meeting = new Meeting
        {
            Title = string.IsNullOrWhiteSpace(cleanTitle) ? $"Reunión {now:yyyy-MM-dd HH:mm}" : cleanTitle,
            Date = now.LocalDateTime,
            StartTime = startTime,
            EndTime = now,
            State = MeetingState.Completed,
            ProcessingStatus = ProcessingStatus.Pending,
            ProcessingStatusText = "Pendiente de procesamiento final...",
            Language = language ?? "Spanish"
        };

        // Crear carpeta de reunión exactamente igual que para una grabada normalmente
        var meetingDir = _repository.GetMeetingDirectoryPath(meeting);
        if (!Directory.Exists(meetingDir))
        {
            Directory.CreateDirectory(meetingDir);
        }

        // Obtener ruta destino del audio dentro de la carpeta de reunión y copiar sin modificar el original
        var ext = Path.GetExtension(sourceAudioFilePath);
        var destAudioPath = _repository.GetAudioFilePath(meeting, ext);

        await Task.Run(() => File.Copy(sourceAudioFilePath, destAudioPath, overwrite: true), cancellationToken);

        meeting.AudioFilePath = destAudioPath;

        var sessionParams = new MeetingSessionParameters
        {
            LiveModel = "Audio Importado",
            MicrophoneDevice = "N/A (Importado)",
            SystemAudioDevice = "N/A (Importado)",
            Language = language ?? "Spanish"
        };

        await _repository.SaveMeetingAsync(meeting, sessionParams, cancellationToken);

        _currentMeeting = meeting;
        SetState(MeetingState.Completed);

        return meeting;
    }

    public async Task<Meeting> CreateMeetingFromTranscriptAsync(
        string title,
        string sourceTranscriptFilePath,
        CancellationToken cancellationToken = default)
    {
        if (_currentState == MeetingState.Recording || _currentState == MeetingState.Preparing)
        {
            throw new InvalidOperationException("Ya hay una reunión en curso o en preparación.");
        }

        if (string.IsNullOrWhiteSpace(sourceTranscriptFilePath) || !File.Exists(sourceTranscriptFilePath))
        {
            throw new FileNotFoundException($"El archivo de transcripción de origen no existe: {sourceTranscriptFilePath}");
        }

        Reset();

        var cleanTitle = string.IsNullOrWhiteSpace(title)
            ? Path.GetFileNameWithoutExtension(sourceTranscriptFilePath)
            : title.Trim();

        // Leer y parsear el contenido del archivo de transcripción
        var rawContent = await File.ReadAllTextAsync(sourceTranscriptFilePath, cancellationToken);
        if (string.IsNullOrWhiteSpace(rawContent))
        {
            throw new InvalidDataException("El archivo de transcripción está vacío o no contiene texto legible.");
        }

        var segments = TranscriptTextParser.Parse(rawContent);
        if (segments.Count == 0)
        {
            throw new InvalidDataException("No se pudo extraer contenido de texto del archivo de transcripción.");
        }

        var now = DateTimeOffset.Now;
        var meeting = new Meeting
        {
            Title = string.IsNullOrWhiteSpace(cleanTitle) ? $"Reunión {now:yyyy-MM-dd HH:mm}" : cleanTitle,
            Date = now.LocalDateTime,
            StartTime = now,
            EndTime = now,
            State = MeetingState.Completed,
            ProcessingStatus = ProcessingStatus.Completed,
            ProcessingStatusText = "Transcripción importada",
            AudioFilePath = null,  // Sin audio asociado
            Transcript = segments
        };

        // Sincronizar participantes desde los segmentos parseados
        meeting.SyncParticipantsFromTranscript();

        // Crear carpeta de reunión con la misma estructura que las demás reuniones
        var meetingDir = _repository.GetMeetingDirectoryPath(meeting);
        if (!Directory.Exists(meetingDir))
        {
            Directory.CreateDirectory(meetingDir);
        }

        // Copiar el archivo de transcripción original dentro de la carpeta de reunión (sin sobrescribir el original)
        var originalExt = Path.GetExtension(sourceTranscriptFilePath);
        var baseName = _repository.GetMeetingFileBaseName(meeting);
        var importedFileDest = Path.Combine(meetingDir, $"{baseName}_imported{originalExt}");
        await Task.Run(() => File.Copy(sourceTranscriptFilePath, importedFileDest, overwrite: false), cancellationToken);

        var sessionParams = new MeetingSessionParameters
        {
            LiveModel = "Transcripción Importada",
            MicrophoneDevice = "N/A (Importado)",
            SystemAudioDevice = "N/A (Importado)",
            Language = "Auto"
        };

        await _repository.SaveMeetingAsync(meeting, sessionParams, cancellationToken);

        _currentMeeting = meeting;
        SetState(MeetingState.Completed);

        return meeting;
    }


    public void OpenMeeting(Meeting meeting)
    {
        _currentMeeting = meeting ?? throw new ArgumentNullException(nameof(meeting));
        _speakerAliases.Clear();
        SetState(meeting.State == MeetingState.Idle ? MeetingState.Completed : meeting.State);
    }

    public void Reset()
    {
        _currentMeeting = null;
        _speakerAliases.Clear();
        SetState(MeetingState.Idle);
    }

    public async Task StopMeetingAsync(CancellationToken cancellationToken = default)
    {
        if (_currentMeeting == null || _currentState != MeetingState.Recording)
        {
            return;
        }

        SetState(MeetingState.Processing);
        _currentMeeting.EndTime = DateTimeOffset.Now;

        SetState(MeetingState.Finalizing);
        await _repository.SaveMeetingAsync(_currentMeeting, cancellationToken);

        SetState(MeetingState.Completed);
    }

    public void UpdateSpeakerDisplayName(string speakerId, string displayName)
    {
        if (_currentMeeting == null) return;

        var resolvedSpeakerId = ResolveSpeakerId(speakerId);
        var targetId = _currentMeeting.UpdateSpeakerDisplayName(resolvedSpeakerId, displayName);

        if (!string.Equals(targetId, resolvedSpeakerId, StringComparison.OrdinalIgnoreCase))
        {
            // Fusión automática de participantes con nombres idénticos
            _speakerAliases[resolvedSpeakerId] = targetId;
            _speakerAliases[speakerId] = targetId;
            SpeakerMerged?.Invoke(this, (resolvedSpeakerId, targetId));
        }
        else
        {
            var speaker = _currentMeeting.Participants.FirstOrDefault(p => string.Equals(p.Id, targetId, StringComparison.OrdinalIgnoreCase));
            if (speaker != null)
            {
                SpeakerUpdated?.Invoke(this, speaker);
            }
        }
    }

    public void AppendSegment(TranscriptSegment segment)
    {
        if (_currentMeeting == null) return;

        if (!string.IsNullOrWhiteSpace(segment.SpeakerId))
        {
            // 1. Resolver alias si el hablante fue fusionado previamente
            segment.SpeakerId = ResolveSpeakerId(segment.SpeakerId);

            // 2. Asociar o registrar participante (comprobando coincidencia de Id o de DisplayName)
            var existing = _currentMeeting.Participants.FirstOrDefault(p =>
                string.Equals(p.Id, segment.SpeakerId, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(segment.SpeakerDisplayName) &&
                 string.Equals(p.DisplayName, segment.SpeakerDisplayName, StringComparison.OrdinalIgnoreCase)));

            if (existing != null)
            {
                if (!string.Equals(existing.Id, segment.SpeakerId, StringComparison.OrdinalIgnoreCase))
                {
                    _speakerAliases[segment.SpeakerId] = existing.Id;
                    segment.SpeakerId = existing.Id;
                }
                segment.SpeakerDisplayName = existing.DisplayName;
            }
            else
            {
                var initialName = !string.IsNullOrWhiteSpace(segment.SpeakerDisplayName)
                    ? segment.SpeakerDisplayName
                    : segment.SpeakerId;

                var speakerColor = Meeting.SpeakerColorPalette[_currentMeeting.Participants.Count % Meeting.SpeakerColorPalette.Length];
                _currentMeeting.Participants.Add(new Speaker
                {
                    Id = segment.SpeakerId,
                    DisplayName = initialName,
                    ColorHex = speakerColor
                });
                segment.SpeakerDisplayName = initialName;
            }
        }
        else
        {
            segment.SpeakerId = string.Empty;
            segment.SpeakerDisplayName = string.Empty;
        }

        // 3. Comprobar si este segmento es una continuación natural del segmento previo (evitar tarjetas cortadas)
        var last = _currentMeeting.Transcript.LastOrDefault();
        if (last != null && TranscriptSegmentConsolidator.ShouldMerge(last, segment))
        {
            last.Text = TranscriptSegmentConsolidator.CombineText(last.Text, segment.Text);
            if (segment.EndTime > last.EndTime)
            {
                last.EndTime = segment.EndTime;
            }
            SegmentUpdated?.Invoke(this, last);
            return;
        }

        // 4. Si no se fusiona, añadir como nuevo segmento
        _currentMeeting.Transcript.Add(segment);
        SegmentAppended?.Invoke(this, segment);
    }

    private string ResolveSpeakerId(string speakerId)
    {
        var current = speakerId;
        while (_speakerAliases.TryGetValue(current, out var next) && !string.Equals(current, next, StringComparison.OrdinalIgnoreCase))
        {
            current = next;
        }
        return current;
    }

    private void SetState(MeetingState newState)
    {
        _currentState = newState;
        if (_currentMeeting != null)
        {
            _currentMeeting.State = newState;
        }
        StateChanged?.Invoke(this, newState);
    }
}
