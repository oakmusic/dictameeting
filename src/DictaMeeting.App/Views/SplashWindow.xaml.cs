using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Animation;

namespace DictaMeeting.App.Views;

public partial class SplashWindow : Window
{
    private bool _isClosing;

    public SplashWindow()
    {
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Las ondas sutiles de fondo se mueven suavemente en bucle continuo
        var waveStoryboard = (Storyboard)Resources["WaveStoryboard"];
        waveStoryboard.Begin(this);
    }

    /// <summary>
    /// Ejecuta la animación de entrada del logotipo una vez que la aplicación ha completado su precarga.
    /// </summary>
    public async Task PlayIntroAnimationAsync()
    {
        var introStoryboard = (Storyboard)Resources["IntroStoryboard"];
        var tcs = new TaskCompletionSource<bool>();

        EventHandler? completedHandler = null;
        completedHandler = (s, e) =>
        {
            introStoryboard.Completed -= completedHandler;
            tcs.TrySetResult(true);
        };

        introStoryboard.Completed += completedHandler;
        introStoryboard.Begin(this);

        await tcs.Task;
    }

    /// <summary>
    /// Ejecuta el fade-out final y cierra la ventana del splash.
    /// </summary>
    public async Task FadeOutAndCloseAsync()
    {
        if (_isClosing)
            return;

        _isClosing = true;

        if (!IsLoaded)
        {
            Close();
            return;
        }

        var exitStoryboard = (Storyboard)Resources["ExitStoryboard"];
        var tcs = new TaskCompletionSource<bool>();

        EventHandler? completedHandler = null;
        completedHandler = (sender, args) =>
        {
            exitStoryboard.Completed -= completedHandler;
            tcs.TrySetResult(true);
        };

        exitStoryboard.Completed += completedHandler;
        exitStoryboard.Begin(this);

        await tcs.Task;

        Close();
    }
}
