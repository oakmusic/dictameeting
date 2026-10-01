using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace DictaMeeting.App.Controls;

/// <summary>
/// Control de visualización en tiempo real de ondas de audio (waveform bars).
/// Diseñado con barras verticales redondeadas, reactivas a los niveles de señal reales
/// de micrófono y audio del sistema, con soporte de decaimiento suave y estado en reposo.
/// </summary>
public class AudioWaveformVisualizer : FrameworkElement
{
    public static readonly DependencyProperty AudioLevelProperty =
        DependencyProperty.Register(
            nameof(AudioLevel),
            typeof(double),
            typeof(AudioWaveformVisualizer),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender, OnAudioLevelChanged));

    public static readonly DependencyProperty BarBrushProperty =
        DependencyProperty.Register(
            nameof(BarBrush),
            typeof(Brush),
            typeof(AudioWaveformVisualizer),
            new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BarCountProperty =
        DependencyProperty.Register(
            nameof(BarCount),
            typeof(int),
            typeof(AudioWaveformVisualizer),
            new FrameworkPropertyMetadata(14, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BarWidthProperty =
        DependencyProperty.Register(
            nameof(BarWidth),
            typeof(double),
            typeof(AudioWaveformVisualizer),
            new FrameworkPropertyMetadata(2.8, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BarGapProperty =
        DependencyProperty.Register(
            nameof(BarGap),
            typeof(double),
            typeof(AudioWaveformVisualizer),
            new FrameworkPropertyMetadata(3.2, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MinBarHeightProperty =
        DependencyProperty.Register(
            nameof(MinBarHeight),
            typeof(double),
            typeof(AudioWaveformVisualizer),
            new FrameworkPropertyMetadata(3.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaxBarHeightProperty =
        DependencyProperty.Register(
            nameof(MaxBarHeight),
            typeof(double),
            typeof(AudioWaveformVisualizer),
            new FrameworkPropertyMetadata(24.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsRestingProperty =
        DependencyProperty.Register(
            nameof(IsResting),
            typeof(bool),
            typeof(AudioWaveformVisualizer),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender, OnIsRestingChanged));

    public double AudioLevel
    {
        get => (double)GetValue(AudioLevelProperty);
        set => SetValue(AudioLevelProperty, value);
    }

    public Brush BarBrush
    {
        get => (Brush)GetValue(BarBrushProperty);
        set => SetValue(BarBrushProperty, value);
    }

    public int BarCount
    {
        get => (int)GetValue(BarCountProperty);
        set => SetValue(BarCountProperty, value);
    }

    public double BarWidth
    {
        get => (double)GetValue(BarWidthProperty);
        set => SetValue(BarWidthProperty, value);
    }

    public double BarGap
    {
        get => (double)GetValue(BarGapProperty);
        set => SetValue(BarGapProperty, value);
    }

    public double MinBarHeight
    {
        get => (double)GetValue(MinBarHeightProperty);
        set => SetValue(MinBarHeightProperty, value);
    }

    public double MaxBarHeight
    {
        get => (double)GetValue(MaxBarHeightProperty);
        set => SetValue(MaxBarHeightProperty, value);
    }

    public bool IsResting
    {
        get => (bool)GetValue(IsRestingProperty);
        set => SetValue(IsRestingProperty, value);
    }

    private double _smoothedLevel = 0.0;
    private double _phase = 0.0;
    private double _peakReference = 0.08; // Referencia adaptativa inicial para boost inmediato
    private DispatcherTimer? _decayTimer;

    public AudioWaveformVisualizer()
    {
        Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _decayTimer?.Stop();
        _decayTimer = null;
    }

    private static void OnAudioLevelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AudioWaveformVisualizer visualizer)
        {
            visualizer.HandleAudioLevelChanged();
        }
    }

    private static void OnIsRestingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AudioWaveformVisualizer visualizer && (bool)e.NewValue)
        {
            visualizer._smoothedLevel = 0.0;
            visualizer._peakReference = 0.08;
            visualizer._decayTimer?.Stop();
            visualizer.InvalidateVisual();
        }
    }

    private double ComputeNormalizedTarget(double raw)
    {
        // Puerta de silencio muy baja para evitar amplificar ruido de fondo imperceptible
        if (raw <= 0.005)
        {
            return 0.0;
        }

        // Si se detecta un pico mayor, expandir el techo adaptativo
        if (raw > _peakReference)
        {
            _peakReference = Math.Min(1.0, raw);
        }
        else
        {
            // Decaimiento muy lento de la referencia para permitir re-adaptación si se habla más bajo
            _peakReference = Math.Max(0.06, _peakReference * 0.998);
        }

        // Normalización adaptativa: proyecta el rango de audio detectable a [0 .. 1]
        double range = Math.Max(0.035, _peakReference - 0.005);
        double normalized = Math.Clamp((raw - 0.005) / range, 0.0, 1.0);

        // Curva perceptual de sonoridad (potencia 0.42): eleva con amplitud generosa
        // las frases de nivel medio o bajo para que el usuario aprecie claramente la captura
        return Math.Pow(normalized, 0.42);
    }

    private void HandleAudioLevelChanged()
    {
        if (IsResting)
        {
            InvalidateVisual();
            return;
        }

        double raw = Math.Clamp(AudioLevel, 0.0, 1.0);
        double target = ComputeNormalizedTarget(raw);

        // Ataque rápido instantáneo si sube la señal
        if (target > _smoothedLevel)
        {
            _smoothedLevel = target;
            _phase += 0.35;
        }

        if (_decayTimer == null)
        {
            _decayTimer = new DispatcherTimer(
                TimeSpan.FromMilliseconds(33),
                DispatcherPriority.Render,
                OnDecayTick,
                Dispatcher);
        }

        if (!_decayTimer.IsEnabled)
        {
            _decayTimer.Start();
        }

        InvalidateVisual();
    }

    private void OnDecayTick(object? sender, EventArgs e)
    {
        if (IsResting)
        {
            _decayTimer?.Stop();
            return;
        }

        double raw = Math.Clamp(AudioLevel, 0.0, 1.0);
        double target = ComputeNormalizedTarget(raw);

        if (_smoothedLevel > target)
        {
            // Caída suave y natural tipo vúmetro profesional
            _smoothedLevel = Math.Max(target, _smoothedLevel * 0.85 - 0.01);
            if (_smoothedLevel < 0.01)
            {
                _smoothedLevel = 0.0;
            }
            _phase += 0.12;
            InvalidateVisual();
        }
        else if (Math.Abs(_smoothedLevel - target) < 0.003)
        {
            if (_smoothedLevel <= 0.01)
            {
                _smoothedLevel = 0.0;
                _decayTimer?.Stop();
                InvalidateVisual();
            }
        }
        else
        {
            _smoothedLevel = target;
            InvalidateVisual();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        int count = Math.Max(3, BarCount);
        double totalWidth = count * BarWidth + (count - 1) * BarGap;
        double desiredHeight = Math.Max(MaxBarHeight, 26.0);
        return new Size(totalWidth, desiredHeight);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        int count = Math.Max(3, BarCount);
        double width = BarWidth;
        double gap = BarGap;
        double totalWidth = count * width + (count - 1) * gap;

        // Centrado horizontal
        double startX = Math.Max(0, (ActualWidth - totalWidth) / 2.0);
        double centerY = ActualHeight / 2.0;
        double minH = MinBarHeight;
        double maxH = Math.Min(MaxBarHeight, ActualHeight > 0 ? ActualHeight : MaxBarHeight);
        double rangeH = Math.Max(1.0, maxH - minH);

        Brush brush = BarBrush ?? Brushes.DodgerBlue;
        double radius = width / 2.0;

        if (IsResting)
        {
            // Patrón de onda en reposo estática elegante
            for (int i = 0; i < count; i++)
            {
                double t = count > 1 ? (double)i / (count - 1) : 0.5;
                // Silueta de doble onda armónica suave
                double env = Math.Sin(Math.PI * t);
                double ripple = 0.65 + 0.35 * Math.Sin(t * Math.PI * 3.5);
                double profile = Math.Clamp(env * ripple, 0.15, 1.0);

                double h = minH + rangeH * profile;
                double x = startX + i * (width + gap);
                double y = centerY - h / 2.0;

                dc.DrawRoundedRectangle(brush, null, new Rect(x, y, width, h), radius, radius);
            }
            return;
        }

        // Estado activo en tiempo real
        double level = Math.Clamp(_smoothedLevel, 0.0, 1.0);

        for (int i = 0; i < count; i++)
        {
            double h;
            if (level <= 0.005)
            {
                h = minH;
            }
            else
            {
                // Envolvente de arco centrada (más alta en el medio, elegante hacia los extremos)
                double t = count > 1 ? (double)i / (count - 1) : 0.5;
                double env = 0.22 + 0.78 * Math.Sin(Math.PI * t);

                // Modulación dinámica de frecuencias para sensación orgánica de señal de voz
                double wave = 0.40 + 0.60 * Math.Abs(Math.Sin(i * 1.8 + _phase));
                double scale = env * wave;

                h = minH + rangeH * scale * level;
            }

            double x = startX + i * (width + gap);
            double y = centerY - h / 2.0;

            dc.DrawRoundedRectangle(brush, null, new Rect(x, y, width, h), radius, radius);
        }
    }
}
