using DictaMeeting.App.ViewModels;

namespace DictaMeeting.App.Services;

/// <summary>
/// Implementación de alto rendimiento para <see cref="ITranscriptAudioSyncService"/>.
/// Utiliza verificación en tiempo constante O(1) para reproducción secuencial continua
/// y búsqueda binaria O(log N) para saltos/seek instantáneos en reuniones de larga duración.
/// </summary>
public sealed class TranscriptAudioSyncService : ITranscriptAudioSyncService
{
    private readonly List<TranscriptSegmentViewModel> _segments = new();
    private TranscriptSegmentViewModel? _activeSegment;
    private int _lastIndex = -1;

    public TranscriptSegmentViewModel? ActiveSegment => _activeSegment;

    public event EventHandler<TranscriptSegmentViewModel?>? ActiveSegmentChanged;

    public void SetSegments(IEnumerable<TranscriptSegmentViewModel> segments)
    {
        Clear();
        if (segments == null) return;

        foreach (var seg in segments)
        {
            if (seg == null) continue;
            // Asegurar timestamps válidos por si proceden de fuentes con formato de texto
            EnsureValidTimestamps(seg);
            _segments.Add(seg);
        }

        // Ordenar por tiempo de inicio para garantizar búsqueda binaria determinista
        _segments.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
        _lastIndex = -1;
    }

    public void Clear()
    {
        if (_activeSegment != null)
        {
            _activeSegment.IsActive = false;
            _activeSegment = null;
        }

        _segments.Clear();
        _lastIndex = -1;
        ActiveSegmentChanged?.Invoke(this, null);
    }

    public void SetActiveSegment(TranscriptSegmentViewModel? segment)
    {
        if (ReferenceEquals(_activeSegment, segment)) return;

        if (_activeSegment != null)
        {
            _activeSegment.IsActive = false;
        }

        _activeSegment = segment;

        if (_activeSegment != null)
        {
            _activeSegment.IsActive = true;
            _lastIndex = _segments.IndexOf(_activeSegment);
        }
        else
        {
            _lastIndex = -1;
        }

        ActiveSegmentChanged?.Invoke(this, _activeSegment);
    }

    public bool UpdateActiveSegment(TimeSpan position)
    {
        var target = FindSegmentAt(position);
        if (!ReferenceEquals(_activeSegment, target))
        {
            SetActiveSegment(target);
            return true;
        }

        return false;
    }

    public TranscriptSegmentViewModel? FindSegmentAt(TimeSpan position)
    {
        if (_segments.Count == 0) return null;

        // 1. Verificación O(1): mismo segmento actual (caso más común durante reproducción continua)
        if (_lastIndex >= 0 && _lastIndex < _segments.Count)
        {
            var current = _segments[_lastIndex];
            if (IsPositionInSegment(position, current))
            {
                return current;
            }

            // 2. Verificación O(1): avance secuencial al siguiente segmento contiguo
            int nextIndex = _lastIndex + 1;
            if (nextIndex < _segments.Count)
            {
                var next = _segments[nextIndex];
                if (IsPositionInSegment(position, next))
                {
                    _lastIndex = nextIndex;
                    return next;
                }
            }
        }

        // 3. Búsqueda binaria O(log N) para saltos (seek) en la línea de tiempo
        int low = 0;
        int high = _segments.Count - 1;
        int candidateIndex = -1;

        while (low <= high)
        {
            int mid = low + ((high - low) / 2);
            if (_segments[mid].StartTime <= position)
            {
                candidateIndex = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        if (candidateIndex >= 0)
        {
            var candidate = _segments[candidateIndex];
            if (IsPositionInSegment(position, candidate))
            {
                _lastIndex = candidateIndex;
                return candidate;
            }
        }

        _lastIndex = -1;
        return null;
    }

    public TimeSpan GetPositionForSegment(TranscriptSegmentViewModel segment)
    {
        EnsureValidTimestamps(segment);
        return segment.StartTime;
    }

    private static bool IsPositionInSegment(TimeSpan position, TranscriptSegmentViewModel segment)
    {
        var start = segment.StartTime;
        var end = segment.EndTime > segment.StartTime
            ? segment.EndTime
            : segment.StartTime + TimeSpan.FromSeconds(3); // respaldo si falta EndTime

        return position >= start && position < end;
    }

    private static void EnsureValidTimestamps(TranscriptSegmentViewModel segment)
    {
        if (segment.StartTime == TimeSpan.Zero && segment.EndTime == TimeSpan.Zero &&
            !string.IsNullOrWhiteSpace(segment.FormattedTime) &&
            segment.FormattedTime != "00:00:00")
        {
            if (TimeSpan.TryParse(segment.FormattedTime, out var parsed))
            {
                segment.StartTime = parsed;
                segment.EndTime = parsed + TimeSpan.FromSeconds(3);
            }
        }
    }
}
