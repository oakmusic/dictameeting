using System.Windows;
using System.Windows.Controls;

namespace DictaMeeting.App.Behaviors;

/// <summary>
/// Comportamiento adjunto para ScrollViewer en transcripciones en vivo:
/// - Hace scroll automático hacia abajo cuando se añaden nuevos fragmentos, SIEMPRE que el usuario se encuentre al final.
/// - Si el usuario hace scroll hacia arriba para inspeccionar fragmentos anteriores, suspende el auto-scroll.
/// - Cuando el usuario vuelve a bajar al final, reanuda automáticamente el auto-scroll.
/// </summary>
public static class SmartScrollBehavior
{
    public static readonly DependencyProperty EnableAutoScrollProperty =
        DependencyProperty.RegisterAttached(
            "EnableAutoScroll",
            typeof(bool),
            typeof(SmartScrollBehavior),
            new PropertyMetadata(false, OnEnableAutoScrollChanged));

    public static bool GetEnableAutoScroll(DependencyObject obj) => (bool)obj.GetValue(EnableAutoScrollProperty);
    public static void SetEnableAutoScroll(DependencyObject obj, bool value) => obj.SetValue(EnableAutoScrollProperty, value);

    private static readonly DependencyProperty ScrollStateProperty =
        DependencyProperty.RegisterAttached(
            "ScrollState",
            typeof(ScrollStateHolder),
            typeof(SmartScrollBehavior),
            new PropertyMetadata(null));

    private class ScrollStateHolder
    {
        public bool AutoScrollToEnd = true;
    }

    private static void OnEnableAutoScrollChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer scrollViewer) return;

        if ((bool)e.NewValue)
        {
            var state = new ScrollStateHolder();
            scrollViewer.SetValue(ScrollStateProperty, state);
            scrollViewer.ScrollChanged += OnScrollChanged;
        }
        else
        {
            scrollViewer.ScrollChanged -= OnScrollChanged;
            scrollViewer.ClearValue(ScrollStateProperty);
        }
    }

    private static void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer) return;

        var state = (ScrollStateHolder)scrollViewer.GetValue(ScrollStateProperty);
        if (state == null) return;

        // Si el usuario desplazó manualmente la barra o la rueda (VerticalChange != 0 pero sin cambio de altura de contenido)
        if (e.ExtentHeightChange == 0 && Math.Abs(e.VerticalChange) > 0.001)
        {
            // Verificamos si la posición actual está muy cerca del final (umbral de 30 px)
            bool isNearBottom = scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight - 30;
            state.AutoScrollToEnd = isNearBottom;
        }

        // Si se ha añadido contenido nuevo (ExtentHeight aumentó)
        if (e.ExtentHeightChange > 0)
        {
            if (state.AutoScrollToEnd)
            {
                scrollViewer.ScrollToEnd();
            }
        }
    }
}
