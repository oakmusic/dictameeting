using CommunityToolkit.Mvvm.ComponentModel;

namespace DictaMeeting.App.ViewModels;

public partial class TranscriptSegmentViewModel : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _formattedTime = "00:00:00";

    [ObservableProperty]
    private TimeSpan _startTime = TimeSpan.Zero;

    [ObservableProperty]
    private TimeSpan _endTime = TimeSpan.Zero;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSpeakerLabel))]
    private string _speakerId = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSpeakerLabel))]
    private string _speakerDisplayName = string.Empty;

    [ObservableProperty]
    private string _text = string.Empty;

    [ObservableProperty]
    private string _colorHex = "#6366F1";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSpeakerLabel))]
    private bool _showSpeakerLabel = true;

    /// <summary>
    /// Indica si se debe mostrar el badge/etiqueta del interlocutor en la tarjeta.
    /// Solo se muestra si hay un nombre de interlocutor no vacío y corresponde al inicio de su intervención
    /// (se oculta en frases consecutivas del mismo interlocutor).
    /// </summary>
    public bool HasSpeakerLabel => ShowSpeakerLabel && !string.IsNullOrWhiteSpace(SpeakerDisplayName);

    /// <summary>
    /// Actualiza la visibilidad de las etiquetas de interlocutor en una secuencia ordenada de segmentos,
    /// garantizando que cuando un participante hable con varias frases seguidas, solo aparezca su etiqueta
    /// en la primera de ellas (al inicio de la intervención) y no se repita hasta que hable otro participante.
    /// </summary>
    public static void UpdateSpeakerInterventionHeaders(IEnumerable<TranscriptSegmentViewModel> segments)
    {
        string? lastSpeaker = null;

        foreach (var seg in segments)
        {
            var currentSpeaker = !string.IsNullOrWhiteSpace(seg.SpeakerId)
                ? seg.SpeakerId
                : (!string.IsNullOrWhiteSpace(seg.SpeakerDisplayName) ? seg.SpeakerDisplayName : null);

            if (string.IsNullOrWhiteSpace(currentSpeaker))
            {
                seg.ShowSpeakerLabel = false;
                lastSpeaker = null;
            }
            else
            {
                bool isNewIntervention = !string.Equals(currentSpeaker, lastSpeaker, StringComparison.OrdinalIgnoreCase);
                seg.ShowSpeakerLabel = isNewIntervention;
                lastSpeaker = currentSpeaker;
            }
        }
    }
}
