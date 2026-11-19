using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace SpotifyLyricsOverlay;

public partial class MainWindow : Window
{
    private readonly AppSettingsService _settings = new();
    private readonly WindowsMediaClient _media = new();
    private readonly LyricsService _lyrics = new();
    private readonly DispatcherTimer _timer;

    private LyricsResult? _currentLyrics;
    private string? _trackId;
    private bool _polling;
    private bool _isPositionLocked;
    private int _manualOffsetMs;
    private bool _showingPlainLyrics;

    public MainWindow()
    {
        InitializeComponent();

        ApplySettings(_settings.Load());

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };

        _timer.Tick += async (_, _) => await PollAsync();
        Loaded += async (_, _) => await StartAsync();
        Closing += (_, _) => SaveSettings();

        SetStatus("Starting...");
    }

    private void ApplySettings(AppSettings settings)
    {
        Width = Clamp(settings.Width, MinWidth, SystemParameters.VirtualScreenWidth) ?? Width;
        Height = Clamp(settings.Height, MinHeight, SystemParameters.VirtualScreenHeight) ?? Height;

        var left = settings.Left;
        var top = settings.Top;

        if (left.HasValue &&
            top.HasValue &&
            IsReasonablyVisible(left.Value, top.Value, Width, Height))
        {
            Left = left.Value;
            Top = top.Value;
        }
        else
        {
            Left = (SystemParameters.WorkArea.Width - Width) / 2;
            Top = SystemParameters.WorkArea.Height - Height - 70;
        }

        var fontSize = Clamp(
            settings.FontSize,
            FontSizeSlider.Minimum,
            FontSizeSlider.Maximum);

        if (fontSize.HasValue)
            FontSizeSlider.Value = fontSize.Value;

        _lyrics.GeniusToken = settings.GeniusToken;
    }

    private void SaveSettings()
    {
        _settings.Save(new AppSettings
        {
            Left = RestoreBounds.Left,
            Top = RestoreBounds.Top,
            Width = RestoreBounds.Width,
            Height = RestoreBounds.Height,
            FontSize = FontSizeSlider.Value
        });
    }

    private static double? Clamp(double? value, double minimum, double maximum)
    {
        if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value))
            return null;

        return Math.Clamp(value.Value, minimum, maximum);
    }

    private static bool IsReasonablyVisible(
        double left,
        double top,
        double width,
        double height)
    {
        var right = left + width;
        var bottom = top + height;

        return right >= SystemParameters.VirtualScreenLeft + 80 &&
            bottom >= SystemParameters.VirtualScreenTop + 80 &&
            left <= SystemParameters.VirtualScreenLeft +
                SystemParameters.VirtualScreenWidth - 80 &&
            top <= SystemParameters.VirtualScreenTop +
                SystemParameters.VirtualScreenHeight - 80;
    }

    private async Task StartAsync()
    {
        try
        {
            SetStatus("Connecting to Windows media session...");
            await _media.InitializeAsync();
            SetStatus("Media session connected. Waiting for Spotify playback...");
            _timer.Start();
        }
        catch (Exception ex)
        {
            SetStatus($"Media session error: {ex.Message}");
        }
    }

    private async Task PollAsync()
    {
        if (_polling)
            return;

        _polling = true;

        try
        {
            var playback = await _media.GetCurrentPlaybackAsync();

            if (playback is null)
            {
                SetStatus("Media session connected. No track is currently playing.");
                return;
            }

            TrackText.Text = $"{playback.Artist} - {playback.Title}";

            if (!string.Equals(_trackId, playback.TrackId, StringComparison.Ordinal))
            {
                _trackId = playback.TrackId;
                _currentLyrics = null;

                SetCurrentLyric("Searching lyrics...");
                SetNextLyric("");

                try
                {
                    _currentLyrics = await _lyrics.GetAsync(
                        playback.Title,
                        playback.Artist,
                        playback.Album,
                        playback.DurationMs / 1000.0);

                    if (_currentLyrics is not null)
                    {
                        SetStatus(
                            $"Media: {playback.SourceApp} - " +
                            $"Lyrics: {_currentLyrics.Provider} - " +
                            $"{_currentLyrics.Lines.Count} lines");

                        // Switch display mode based on synced vs unsynced
                        if (_currentLyrics.IsSynced)
                        {
                            ShowSyncedMode();
                        }
                        else
                        {
                            ShowPlainMode(_currentLyrics.PlainText, _currentLyrics.Lines);
                        }
                    }
                    else
                    {
                        ShowSyncedMode();
                        SetStatus(
                            $"Media: {playback.SourceApp} - Lyrics not found - " +
                            $"{playback.Title}");
                    }
                }
                catch (Exception ex)
                {
                    SetStatus($"Lyrics error: {ex.Message}");
                }
            }

            Render(Math.Max(0, playback.ProgressMs + _manualOffsetMs));
        }
        catch (Exception ex)
        {
            SetStatus($"Media playback error: {ex.Message}");
        }
        finally
        {
            _polling = false;
        }
    }

    private void Render(long progressMs)
    {
        if (_currentLyrics?.Lines.Count is not > 0)
        {
            if (!_showingPlainLyrics)
            {
                SetCurrentLyric("Lyrics not found");
                SetNextLyric("");
            }
            return;
        }

        // For plain/unsynced lyrics, auto-scroll the ScrollViewer based on progress
        if (_showingPlainLyrics && _currentLyrics is not null)
        {
            AutoScrollPlainLyrics(progressMs, _currentLyrics);
            return;
        }

        var index = 0;

        for (var i = 0; i < _currentLyrics.Lines.Count; i++)
        {
            if (_currentLyrics.Lines[i].TimeMs <= progressMs)
                index = i;
            else
                break;
        }

        SetCurrentLyric(_currentLyrics.Lines[index].Text);
        SetNextLyric(
            index + 1 < _currentLyrics.Lines.Count
                ? _currentLyrics.Lines[index + 1].Text
                : "");
    }

    private void ShowSyncedMode()
    {
        _showingPlainLyrics = false;
        SyncedLyricsPanel.Visibility = Visibility.Visible;
        PlainLyricsScroller.Visibility = Visibility.Collapsed;
    }

    private void ShowPlainMode(string? plainText, IReadOnlyList<LyricsLine> lines)
    {
        _showingPlainLyrics = true;
        SyncedLyricsPanel.Visibility = Visibility.Collapsed;
        PlainLyricsScroller.Visibility = Visibility.Visible;

        // Build display text from PlainText or fall back to Lines
        var displayText = !string.IsNullOrWhiteSpace(plainText)
            ? plainText.Trim()
            : string.Join("\n", lines.Select(l => l.Text));

        PlainLyricsText.Text = displayText;
        PlainLyricsStroke.Text = displayText;

        // Apply font sizes
        var fontSize = Math.Max(12, FontSizeSlider.Value * 0.55);
        PlainLyricsText.FontSize = fontSize;
        PlainLyricsStroke.FontSize = fontSize;
        PlainLyricsText.LineHeight = fontSize * 1.6;
        PlainLyricsStroke.LineHeight = fontSize * 1.6;
    }

    private void AutoScrollPlainLyrics(long progressMs, LyricsResult lyrics)
    {
        if (PlainLyricsScroller.ScrollableHeight <= 0)
            return;

        var firstTime = lyrics.Lines[0].TimeMs;
        var lastTime = lyrics.Lines[^1].TimeMs;
        var totalRange = lastTime - firstTime;

        if (totalRange <= 0)
            return;

        var progress = Math.Clamp(
            (double)(progressMs - firstTime) / totalRange,
            0.0, 1.0);

        PlainLyricsScroller.ScrollToVerticalOffset(
            progress * PlainLyricsScroller.ScrollableHeight);
    }

    private void FontSizeSlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsInitialized)
            return;

        SetCurrentLyricFontSize(e.NewValue);
        SetNextLyricFontSize(Math.Max(12, e.NewValue * 0.48));

        // Update plain lyrics font if visible
        if (_showingPlainLyrics)
        {
            var plainSize = Math.Max(12, e.NewValue * 0.55);
            PlainLyricsText.FontSize = plainSize;
            PlainLyricsStroke.FontSize = plainSize;
            PlainLyricsText.LineHeight = plainSize * 1.6;
            PlainLyricsStroke.LineHeight = plainSize * 1.6;
        }
    }

    private void SetCurrentLyric(string text)
    {
        CurrentLyric.Text = text;
        CurrentLyricStrokeLeft.Text = text;
        CurrentLyricStrokeRight.Text = text;
        CurrentLyricStrokeTop.Text = text;
        CurrentLyricStrokeBottom.Text = text;
    }

    private void SetNextLyric(string text)
    {
        NextLyric.Text = text;
        NextLyricStrokeLeft.Text = text;
        NextLyricStrokeRight.Text = text;
        NextLyricStrokeTop.Text = text;
        NextLyricStrokeBottom.Text = text;
    }

    private void SetCurrentLyricFontSize(double fontSize)
    {
        CurrentLyric.FontSize = fontSize;
        CurrentLyricStrokeLeft.FontSize = fontSize;
        CurrentLyricStrokeRight.FontSize = fontSize;
        CurrentLyricStrokeTop.FontSize = fontSize;
        CurrentLyricStrokeBottom.FontSize = fontSize;
    }

    private void SetNextLyricFontSize(double fontSize)
    {
        NextLyric.FontSize = fontSize;
        NextLyricStrokeLeft.FontSize = fontSize;
        NextLyricStrokeRight.FontSize = fontSize;
        NextLyricStrokeTop.FontSize = fontSize;
        NextLyricStrokeBottom.FontSize = fontSize;
    }

    private void SetStatus(string message)
    {
        StatusText.Text = message;
    }

    private void PositionLockButton_Click(object sender, RoutedEventArgs e)
    {
        _isPositionLocked = !_isPositionLocked;

        ResizeMode = _isPositionLocked
            ? ResizeMode.NoResize
            : ResizeMode.CanResizeWithGrip;

        Chrome.ResizeBorderThickness = _isPositionLocked
            ? new Thickness(0)
            : new Thickness(10);

        WindowResizeGrip.Visibility = _isPositionLocked
            ? Visibility.Collapsed
            : Visibility.Visible;

        PositionLockButton.Content = _isPositionLocked
            ? "\uE785"
            : "\uE72E";

        PositionLockButton.ToolTip = _isPositionLocked
            ? "Unlock position"
            : "Lock position";
    }

    private void DragWindow(object sender, MouseButtonEventArgs e)
    {
        if (!_isPositionLocked && e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }
}
