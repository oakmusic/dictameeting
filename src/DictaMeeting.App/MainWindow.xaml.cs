using System.Windows;
using DictaMeeting.App.ViewModels;

namespace DictaMeeting.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _isUserDraggingSlider;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        _viewModel.RequestScrollToSegment += OnRequestScrollToSegment;

        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsAiConfigModalOpen) && _viewModel.IsAiConfigModalOpen)
            {
                if (OpenRouterPasswordBox != null) OpenRouterPasswordBox.Password = string.Empty;
                if (CustomServerPasswordBox != null) CustomServerPasswordBox.Password = string.Empty;
            }
            else if (e.PropertyName == nameof(MainViewModel.ApiKeyInput) && string.IsNullOrEmpty(_viewModel.ApiKeyInput))
            {
                if (OpenRouterPasswordBox != null && !string.IsNullOrEmpty(OpenRouterPasswordBox.Password))
                {
                    OpenRouterPasswordBox.Password = string.Empty;
                }
            }
            else if (e.PropertyName == nameof(MainViewModel.CustomServerApiKeyInput) && string.IsNullOrEmpty(_viewModel.CustomServerApiKeyInput))
            {
                if (CustomServerPasswordBox != null && !string.IsNullOrEmpty(CustomServerPasswordBox.Password))
                {
                    CustomServerPasswordBox.Password = string.Empty;
                }
            }
        };

        Loaded += async (_, _) =>
        {
            await _viewModel.CheckFirstRunExperienceAsync();
        };
    }

    private void NewMeetingDropdownButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.ContextMenu != null)
        {
            element.ContextMenu.PlacementTarget = element;
            element.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            element.ContextMenu.IsOpen = true;
        }
    }

    private void AudioDropZone_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            if (AudioDropBorder != null)
            {
                AudioDropBorder.BorderBrush = (System.Windows.Media.Brush)FindResource("BorderFocusBrush");
                AudioDropBorder.Background = (System.Windows.Media.Brush)FindResource("AccentLightBrush");
            }
            e.Handled = true;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
    }

    private void AudioDropZone_DragLeave(object sender, DragEventArgs e)
    {
        if (AudioDropBorder != null)
        {
            AudioDropBorder.BorderBrush = (System.Windows.Media.Brush)FindResource("BorderSubtleBrush");
            AudioDropBorder.Background = (System.Windows.Media.Brush)FindResource("BgInputBrush");
        }
    }

    private void AudioDropZone_Drop(object sender, DragEventArgs e)
    {
        if (AudioDropBorder != null)
        {
            AudioDropBorder.BorderBrush = (System.Windows.Media.Brush)FindResource("BorderSubtleBrush");
            AudioDropBorder.Background = (System.Windows.Media.Brush)FindResource("BgInputBrush");
        }

        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length > 0)
            {
                if (DataContext is MainViewModel vm)
                {
                    vm.HandleDroppedAudioFile(files[0]);
                }
            }
            e.Handled = true;
        }
    }

    private void AudioDropZone_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm && !vm.HasSelectedImportAudioFile)
        {
            vm.BrowseAudioFileCommand.Execute(null);
        }
    }

    private void TranscriptDropZone_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            if (TranscriptDropBorder != null)
            {
                TranscriptDropBorder.BorderBrush = (System.Windows.Media.Brush)FindResource("BorderFocusBrush");
                TranscriptDropBorder.Background = (System.Windows.Media.Brush)FindResource("AccentLightBrush");
            }
            e.Handled = true;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
    }

    private void TranscriptDropZone_DragLeave(object sender, DragEventArgs e)
    {
        if (TranscriptDropBorder != null)
        {
            TranscriptDropBorder.BorderBrush = (System.Windows.Media.Brush)FindResource("BorderSubtleBrush");
            TranscriptDropBorder.Background = (System.Windows.Media.Brush)FindResource("BgInputBrush");
        }
    }

    private void TranscriptDropZone_Drop(object sender, DragEventArgs e)
    {
        if (TranscriptDropBorder != null)
        {
            TranscriptDropBorder.BorderBrush = (System.Windows.Media.Brush)FindResource("BorderSubtleBrush");
            TranscriptDropBorder.Background = (System.Windows.Media.Brush)FindResource("BgInputBrush");
        }

        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length > 0)
            {
                if (DataContext is MainViewModel vm)
                {
                    vm.HandleDroppedTranscriptFile(files[0]);
                }
            }
            e.Handled = true;
        }
    }

    private void TranscriptDropZone_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm && !vm.HasSelectedImportTranscriptFile)
        {
            vm.BrowseTranscriptFileCommand.Execute(null);
        }
    }

    private void ExportActiveTranscriptButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.ContextMenu != null)
        {
            element.ContextMenu.PlacementTarget = element;
            element.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            element.ContextMenu.IsOpen = true;
        }
    }


    private void ReprocessHistoricalMeetingButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.ContextMenu != null)
        {
            element.ContextMenu.PlacementTarget = element;
            element.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            element.ContextMenu.IsOpen = true;
        }
    }

    private void TitleTextBlock_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.CanEditMeetingTitle)
        {
            vm.StartEditMeetingTitleCommand.Execute(null);
            Dispatcher.BeginInvoke(new Action(() =>
            {
                MeetingTitleEditBox.Focus();
                MeetingTitleEditBox.SelectAll();
            }), System.Windows.Threading.DispatcherPriority.Input);
        }
    }

    private void HistoryListBoxItem_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm && sender is System.Windows.Controls.ListBoxItem item && item.DataContext is MeetingHistoryItemViewModel histVm)
        {
            vm.OpenMeetingCommand.Execute(histVm);
        }
    }

    private void EditMeetingTitleButton_Click(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            MeetingTitleEditBox.Focus();
            MeetingTitleEditBox.SelectAll();
        }), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void MeetingTitleEditBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                vm.FinishEditMeetingTitleCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.Escape)
            {
                vm.CancelEditMeetingTitleCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    private void MeetingTitleEditBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.IsEditingMeetingTitle)
        {
            vm.FinishEditMeetingTitleCommand.Execute(null);
        }
    }

    private void SpeakerNameTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter || e.Key == System.Windows.Input.Key.Escape)
        {
            if (sender is FrameworkElement element && element.DataContext is SpeakerViewModel speakerVm)
            {
                speakerVm.CommitDisplayName();
            }

            if (sender is UIElement uiElem)
            {
                var scope = System.Windows.Input.FocusManager.GetFocusScope(uiElem);
                System.Windows.Input.FocusManager.SetFocusedElement(scope, null);
                System.Windows.Input.Keyboard.ClearFocus();
            }

            e.Handled = true;
        }
    }

    private void SpeakerNameTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is SpeakerViewModel speakerVm)
        {
            speakerVm.CommitDisplayName();
        }
    }

    private void NewRealParticipantTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter && DataContext is MainViewModel vm)
        {
            vm.AddRealParticipantCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void NewHistoricalRealParticipantTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter && DataContext is MainViewModel vm)
        {
            vm.AddHistoricalRealParticipantCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void VocabularyTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter && DataContext is MainViewModel vm)
        {
            if (vm.VocabularyManager.AddOrUpdateTermCommand.CanExecute(null))
            {
                vm.VocabularyManager.AddOrUpdateTermCommand.Execute(null);
            }
            VocabularyNewWordTextBox.Focus();
            e.Handled = true;
        }
    }

    private void VocabularyModal_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                VocabularyNewWordTextBox.Focus();
            }), System.Windows.Threading.DispatcherPriority.Input);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (_viewModel != null)
        {
            _viewModel.RequestScrollToSegment -= OnRequestScrollToSegment;
        }
    }

    private void AudioProgressSlider_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton != System.Windows.Input.MouseButton.Left)
            return;

        if (sender is System.Windows.Controls.Slider slider)
        {
            _isUserDraggingSlider = true;
            if (_viewModel != null)
            {
                _viewModel.IsUserSeekingAudio = true;
            }

            // Si el usuario hace clic sobre el Thumb (cursor), mantenemos el comportamiento actual de arrastrar el cursor
            bool isOverThumb = IsChildOf<System.Windows.Controls.Primitives.Thumb>(e.OriginalSource as DependencyObject);

            if (!isOverThumb)
            {
                // Clic en cualquier punto de la barra: calcular posición proporcional y saltar inmediatamente a ese tiempo
                var point = e.GetPosition(slider);
                double width = slider.ActualWidth;
                if (width > 0 && slider.Maximum > slider.Minimum)
                {
                    double ratio = Math.Clamp(point.X / width, 0.0, 1.0);
                    double targetSeconds = slider.Minimum + ratio * (slider.Maximum - slider.Minimum);
                    slider.Value = targetSeconds;
                    _viewModel?.OnAudioSliderSeek(targetSeconds);
                }

                slider.CaptureMouse();
                e.Handled = true;
            }
        }
    }

    private void AudioProgressSlider_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is System.Windows.Controls.Slider slider && slider.IsMouseCaptured && e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
        {
            var point = e.GetPosition(slider);
            double width = slider.ActualWidth;
            if (width > 0 && slider.Maximum > slider.Minimum)
            {
                double ratio = Math.Clamp(point.X / width, 0.0, 1.0);
                double targetSeconds = slider.Minimum + ratio * (slider.Maximum - slider.Minimum);
                slider.Value = targetSeconds;
                _viewModel?.OnAudioSliderSeek(targetSeconds);
            }
        }
    }

    private void AudioProgressSlider_PreviewMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _isUserDraggingSlider = false;
        if (_viewModel != null)
        {
            _viewModel.IsUserSeekingAudio = false;
        }

        if (sender is System.Windows.Controls.Slider slider)
        {
            if (slider.IsMouseCaptured)
            {
                slider.ReleaseMouseCapture();
            }
            _viewModel?.OnAudioSliderSeek(slider.Value);
        }
    }

    private void AudioProgressSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUserDraggingSlider && sender is System.Windows.Controls.Slider slider)
        {
            _viewModel?.OnAudioSliderSeek(slider.Value);
        }
    }

    private static bool IsChildOf<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element != null)
        {
            if (element is T)
                return true;
            if (element is System.Windows.Media.Visual || element is System.Windows.Media.Media3D.Visual3D)
                element = System.Windows.Media.VisualTreeHelper.GetParent(element);
            else
                element = System.Windows.LogicalTreeHelper.GetParent(element);
        }
        return false;
    }

    private void TranscriptCard_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is TranscriptSegmentViewModel segment)
        {
            _viewModel?.OnTranscriptSegmentClicked(segment);
        }
    }

    private void OnRequestScrollToSegment(object? sender, TranscriptSegmentViewModel segment)
    {
        if (segment == null || TranscriptScrollViewer == null || TranscriptItemsControl == null)
            return;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                var container = TranscriptItemsControl.ItemContainerGenerator.ContainerFromItem(segment) as FrameworkElement;
                if (container != null)
                {
                    var transform = container.TransformToAncestor(TranscriptScrollViewer);
                    var point = transform.Transform(new Point(0, 0));
                    double containerTop = point.Y;
                    double containerBottom = containerTop + container.ActualHeight;
                    double viewportHeight = TranscriptScrollViewer.ViewportHeight;

                    // If not comfortably visible within viewport margins, scroll smoothly into view
                    if (containerTop < 10 || containerBottom > viewportHeight - 10)
                    {
                        double currentOffset = TranscriptScrollViewer.VerticalOffset;
                        double targetOffset = currentOffset + containerTop - (viewportHeight * 0.25);
                        if (targetOffset < 0) targetOffset = 0;
                        if (targetOffset > TranscriptScrollViewer.ScrollableHeight) targetOffset = TranscriptScrollViewer.ScrollableHeight;

                        TranscriptScrollViewer.ScrollToVerticalOffset(targetOffset);
                    }
                }
            }
            catch
            {
                // Non-critical UI helper, suppress layout measure exceptions
            }
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void OpenRouterRadio_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.SelectedAiConfigProvider = "OpenRouter";
        }
    }

    private void CustomServerRadio_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.SelectedAiConfigProvider = "CustomServer";
        }
    }

    private void OpenRouterPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && sender is System.Windows.Controls.PasswordBox pb)
        {
            vm.ApiKeyInput = pb.Password;
        }
    }

    private void CustomServerPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && sender is System.Windows.Controls.PasswordBox pb)
        {
            vm.CustomServerApiKeyInput = pb.Password;
        }
    }
}