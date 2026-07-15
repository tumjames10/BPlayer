using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using BPlayer.Models;
using BPlayer.Services;
using LibVLCSharp.Shared;
using VlcMediaPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace BPlayer.Pages;

public partial class DetailPage : Page
{
    private readonly VideoItem _video;
    private LibVLC _libVlc = null!;
    private VlcMediaPlayer _vlcPlayer = null!;
    private Media _media = null!;
    private bool _isPlaying;
    private readonly DispatcherTimer _updateTimer;
    private bool _ready;
    private bool _disposed;
    private readonly PlaybackStateService _playbackState = PlaybackStateService.Instance;
    private readonly ConfigService _configService = new();
    private double _currentRate = 1.0;
    private List<VideoItem>? _playlistVideos;
    private int _playlistIndex;
    private bool _autoPlayNext = true;
    private bool _hasEnded;
    private bool _isFullscreen;
    private readonly DispatcherTimer _hideControlsTimer;
    private DispatcherTimer? _hideFullscreenTimer;
    private const int CONTROLS_SHOW_ZONE = 60;
    private string _subtitleFontFamily = "Arial";
    private int _subtitleFontSize = 16;
    private int _savedSpuIndex = -1;
    private string? _externalSubtitlePath;
    private long? _pointA;
    private long? _pointB;
    private const int UpdateIntervalMs = 250;
    private const int HideControlsDelaySec = 3;
    private const int SeekStepMs = 10000;
    private const int LoadingTimeoutMs = 10000;
    private const int VolumeStep = 5;
    private const int MaxVolume = 200;
    private const int DefaultVolume = 100;
    private static readonly double[] PlaybackRates = { 0.5, 0.75, 1.0, 1.25, 1.5, 2.0 };

    private bool _isLoopingAB;

    public DetailPage(VideoItem video, List<VideoItem>? playlistVideos = null, int playlistIndex = 0)
    {
        InitializeComponent();
        _video = video;
        _playlistVideos = playlistVideos;
        _playlistIndex = playlistIndex;
        DataContext = _video;
        _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(UpdateIntervalMs) };
        _updateTimer.Tick += OnUpdateTick;
        _hideControlsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(HideControlsDelaySec) };
        _hideControlsTimer.Tick += (_, _) => { _hideControlsTimer.Stop(); if (_isFullscreen) ControlsOverlay.Visibility = Visibility.Collapsed; };
        _pointA = null;
        _pointB = null;
        _isLoopingAB = false;
        StartSpinnerAnimation();

        bool hasNext = _playlistVideos != null && _playlistIndex + 1 < _playlistVideos.Count;
        if (NextBtn != null) NextBtn.Visibility = hasNext ? Visibility.Visible : Visibility.Collapsed;
        if (AutoPlayBtn != null)
        {
            AutoPlayBtn.Visibility = _playlistVideos != null ? Visibility.Visible : Visibility.Collapsed;
            UpdateAutoPlayUI();
        }

        Loaded += OnLoaded;
        Unloaded += (_, _) => Cleanup();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _disposed = false;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                if (!InitializeVlc()) return;
                StartPlaybackAndResume();
                HookVideoWndProc();
            }
            catch (Exception ex)
            {
                Logger.Error($"Init: {ex.Message}");
                ShowError($"VLC init failed:\n{ex.Message}");
            }
        }));
    }

    private bool InitializeVlc()
    {
        Logger.Info("=== VLC Init ===");

        var dir = AppDomain.CurrentDomain.BaseDirectory;
        var vlcDir = Path.Combine(dir, "libvlc", "win-x64");
        var pluginPath = Path.Combine(vlcDir, "plugins");

        Logger.Info($"libvlc.dll: {File.Exists(Path.Combine(vlcDir, "libvlc.dll"))}");
        Logger.Info($"Video exists: {File.Exists(_video.FilePath)}");

        if (!File.Exists(_video.FilePath))
        { ShowError("Video file not found."); return false; }

        _libVlc = new LibVLC("--quiet", "--no-osd", "--plugin-path=" + pluginPath);
        _vlcPlayer = new VlcMediaPlayer(_libVlc);
        _vlcPlayer.Volume = DefaultVolume;
        VlcPlayer.MediaPlayer = _vlcPlayer;

        _ = LoadSubtitleFontSettingsAsync();
        _media = BuildMediaWithSubtitleOptions();

        _vlcPlayer.Media = _media;
        _vlcPlayer.EndReached += OnMediaEnded;
        _vlcPlayer.Playing += OnVlcPlaying;
        _vlcPlayer.Stopped += OnVlcStopped;
        _vlcPlayer.Buffering += OnVlcBuffering;

        _ready = true;
        Logger.Info("VLC Ready");
        _vlcPlayer.Volume = DefaultVolume;
        UpdateVolumeUI(100);
        UpdateAudioLabel();
        UpdateSubtitleLabel();
        _ = LoadRatingAsync();
        return true;
    }

    private void StartPlaybackAndResume()
    {
        if (!_vlcPlayer.Play()) { ShowError("Failed to play."); return; }
        _isPlaying = true;

        ShowLoadingOverlay("Loading video…");

        var resumePos = _playbackState.GetResumePosition(_video.FilePath);
        if (resumePos.HasValue)
            _vlcPlayer.Time = (long)(resumePos.Value * 1000);

        PlayBtn.Content = "⏸";
        _updateTimer.Start();
    }

    private void ShowLoadingOverlay(string text)
    {
        LoadingText.Text = text;
        LoadingOverlay.Visibility = Visibility.Visible;
        _loadingTimer?.Stop();
        _loadingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(LoadingTimeoutMs) };
        _loadingTimer.Tick += OnLoadingTimeout;
        _loadingTimer.Start();
    }

    private void HideLoadingOverlay()
    {
        LoadingOverlay.Visibility = Visibility.Collapsed;
        _loadingTimer?.Stop();
        _loadingTimer = null;
    }

    private void OnVlcPlaying(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() => HideLoadingOverlay());
    }

    private void OnVlcStopped(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() => HideLoadingOverlay());
    }

    private void OnVlcBuffering(object? sender, MediaPlayerBufferingEventArgs e)
    {
        if (e.Cache < 100)
        {
            Dispatcher.Invoke(() => LoadingText.Text = $"Buffering… {e.Cache}%");
        }
        else
        {
            Dispatcher.Invoke(() => HideLoadingOverlay());
        }
    }

    private void OnLoadingTimeout(object? sender, EventArgs e)
    {
        _loadingTimer?.Stop();
        Dispatcher.Invoke(() => HideLoadingOverlay());
    }

    private DispatcherTimer? _loadingTimer;
    private DispatcherTimer? _spinnerTimer;

    private void StartSpinnerAnimation()
    {
        _spinnerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        _spinnerTimer.Tick += (_, _) =>
        {
            if (SpinnerRing != null && SpinnerRotation != null)
                SpinnerRotation.Angle = (SpinnerRotation.Angle + 6) % 360;
        };
        _spinnerTimer.Start();
    }

    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_MOUSEWHEEL = 0x020A;
    private const int WH_MOUSE_LL = 14;
    private const int HC_ACTION = 0;

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x; public int y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private IntPtr _mouseHook;
    private HookProc? _mouseHookProc;
    private uint _lastClickTime;
    private POINT _lastClickPt;
    private IntPtr _keyboardHook;
    private HookProc? _keyboardHookProc;

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int VK_SPACE = 0x20;
    private const int VK_ESCAPE = 0x1B;
    private const int VK_LEFT = 0x25;
    private const int VK_UP = 0x26;
    private const int VK_RIGHT = 0x27;
    private const int VK_DOWN = 0x28;

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private void Cleanup()
    {
        if (_disposed) return;
        _disposed = true;
        if (_vlcPlayer != null && _vlcPlayer.Length > 0 && !_hasEnded)
            _playbackState.SavePosition(_video.FilePath, _vlcPlayer.Time / 1000.0);
        Logger.Info("Cleanup");

        CloseFullscreenControlsWindow();

        if (_mouseHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
            Logger.Info("Mouse hook removed");
        }
        if (_keyboardHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
            Logger.Info("Keyboard hook removed");
        }
        HideLoadingOverlay();
        _updateTimer.Stop();
        _loadingTimer?.Stop();
        _spinnerTimer?.Stop();
        _ready = false;
        _isPlaying = false;
        VlcPlayer.MediaPlayer = null;
        _media?.Dispose();
        _vlcPlayer?.Dispose();
        _libVlc?.Dispose();
    }

    private void ShowError(string msg) =>
        MessageBox.Show(msg, "Error", MessageBoxButton.OK, MessageBoxImage.Error);

    private void OnUpdateTick(object? sender, EventArgs e)
    {
        if (!_ready || _vlcPlayer == null || _vlcPlayer.Length <= 0) return;
        var len = _vlcPlayer.Length;
        var pos = _vlcPlayer.Time;
        var fraction = Math.Max(0, Math.Min(1, (double)pos / len));
        ProgressFill.Width = fraction * SeekBar.ActualWidth;
        TimeCurrent.Text = FormatTime(TimeSpan.FromMilliseconds(pos));
        TimeTotal.Text = FormatTime(TimeSpan.FromMilliseconds(len));
        UpdateVolumeUI(_vlcPlayer.Volume);

        if (_isLoopingAB && _pointA.HasValue && _pointB.HasValue && pos >= _pointB.Value)
        {
            _vlcPlayer.Time = _pointA.Value;
        }
    }

    private async System.Threading.Tasks.Task LoadRatingAsync()
    {
        try
        {
            if (_video.Rating > 0) { RatingText.Text = $"★ {_video.Rating:F1}"; return; }
            var svc = new RatingService(new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) });
            var r = await svc.FetchRatingAsync(_video.Title);
            if (r > 0) { _video.Rating = r; RatingText.Text = $"★ {r:F1}"; }
            else RatingText.Text = "N/A";
        }
        catch (Exception ex) { Logger.Warn($"Rating fetch failed: {ex.Message}"); RatingText.Text = "N/A"; }
    }

    private void OnMediaEnded(object? sender, EventArgs e)
    {
        _hasEnded = true;
        _playbackState.MarkWatched(_video.FilePath);
        Dispatcher.Invoke(() =>
        {
            if (_autoPlayNext && _playlistVideos != null && _playlistIndex + 1 < _playlistVideos.Count)
            {
                var nextVideo = _playlistVideos[_playlistIndex + 1];
                Cleanup();
                NavigationService?.Navigate(new DetailPage(nextVideo, _playlistVideos, _playlistIndex + 1));
            }
        });
    }

    // ── Playback controls ──

    private void TogglePlayback()
    {
        if (!_ready || _vlcPlayer == null) return;
        try
        {
            if (_isPlaying)
            {
                _vlcPlayer.Pause();
                _isPlaying = false;
                PlayBtn.Content = "▶";
                _updateTimer.Stop();
                if (_vlcPlayer.Length > 0)
                    _playbackState.SavePosition(_video.FilePath, _vlcPlayer.Time / 1000.0);
            }
            else
            {
                if (!_vlcPlayer.Play()) { ShowError("Failed to play."); return; }
                _isPlaying = true;
                PlayBtn.Content = "⏸";
                _updateTimer.Start();
            }
        }
        catch (Exception ex) { Logger.Error($"Toggle: {ex.Message}"); }
    }

    private void StopPlayback()
    {
        if (!_ready || _vlcPlayer == null) return;
        _vlcPlayer.Stop();
        _isPlaying = false;
        PlayBtn.Content = "▶";
        _updateTimer.Stop();
        ProgressFill.Width = 0;
        TimeCurrent.Text = "0:00";
        if (_vlcPlayer.Length > 0)
        {
            _playbackState.SavePosition(_video.FilePath, _vlcPlayer.Time / 1000.0);
            _playbackState.MarkWatched(_video.FilePath);
        }
    }

    private void OnSpeedBtnClick(object sender, MouseButtonEventArgs e)
    {
        if (!_ready || _vlcPlayer == null) return;
        var rates = PlaybackRates;
        var menu = new ContextMenu();
        foreach (var rate in rates)
        {
            var mark = Math.Abs(rate - _currentRate) < 0.01 ? "✓ " : "  ";
            var item = new MenuItem
            {
                Header = mark + rate + "x", FontSize = 13,
                Padding = new System.Windows.Thickness(12, 8, 20, 8)
            };
            var r = rate;
            item.Click += (_, _) => SetSpeed(r);
            menu.Items.Add(item);
        }
        menu.PlacementTarget = sender as UIElement;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void SetSpeed(double rate)
    {
        if (_vlcPlayer == null) return;
        _currentRate = rate;
        _vlcPlayer.SetRate((float)rate);
        SpeedLabel.Text = rate + "x";
    }

    // ── Low-level mouse hook (WH_MOUSE_LL) — synchronous Invoke to open menu before VLC processes the click ──

    private void HookVideoWndProc()
    {
        try
        {
            var mod = Marshal.GetHINSTANCE(typeof(DetailPage).Module);
            _mouseHookProc = MouseHookCallback;
            _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseHookProc, mod, 0);
            if (_mouseHook == IntPtr.Zero)
                Logger.Error($"SetWindowsHookEx(WH_MOUSE_LL) failed, error={Marshal.GetLastWin32Error()}");
            else
                Logger.Info($"WH_MOUSE_LL hook: {_mouseHook}");

            _keyboardHookProc = KeyboardHookCallback;
            _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardHookProc, mod, 0);
            if (_keyboardHook == IntPtr.Zero)
                Logger.Error($"SetWindowsHookEx(WH_KEYBOARD_LL) failed, error={Marshal.GetLastWin32Error()}");
            else
                Logger.Info($"WH_KEYBOARD_LL hook: {_keyboardHook}");
        }
        catch (Exception ex)
        {
            Logger.Error($"HookVideoWndProc: {ex.Message}");
        }
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= HC_ACTION && IsPlayerWindowActive())
        {
            var msg = (int)wParam;
            var hs = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

            if (msg == WM_MOUSEMOVE && _isFullscreen)
            {
                if (hs.pt.y >= NativeHelper.PrimaryScreenHeight - CONTROLS_SHOW_ZONE)
                {
                    CloseFullscreenControlsWindow();
                    ShowFullscreenControlsWindow();
                }
            }
            else if (msg == WM_MOUSEWHEEL && _isPlaying)
            {
                var delta = (short)((hs.mouseData >> 16) & 0xFFFF);
                Dispatcher.Invoke(() =>
                {
                    var vol = _vlcPlayer?.Volume ?? 100;
                    vol = Math.Max(0, Math.Min(200, vol + (delta > 0 ? 5 : -5)));
                    if (_vlcPlayer != null) _vlcPlayer.Volume = vol;
                    UpdateVolumeUI(vol);
                    Logger.Debug($"Mouse wheel volume: {vol}");
                });
            }
            else if (msg == WM_RBUTTONDOWN)
            {
                Logger.Info($"WM_RBUTTONDOWN at ({hs.pt.x},{hs.pt.y})");
                Dispatcher.Invoke(new Action(ShowRightClickMenuWindow));
            }
            else if (msg == WM_LBUTTONDOWN)
            {
                // Skip double-click if fullscreen controls window is open
                if (_isFullscreen && _fullscreenControlsWindow != null && _fullscreenControlsWindow.IsVisible)
                {
                    if (hs.pt.y >= NativeHelper.PrimaryScreenHeight - 120)
                    {
                        _lastClickTime = 0;
                        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
                    }
                }

                var dclickTime = GetDoubleClickTime();
                var dx = hs.pt.x - _lastClickPt.x;
                var dy = hs.pt.y - _lastClickPt.y;
                var dist = dx * dx + dy * dy;
                if (hs.time - _lastClickTime <= dclickTime && dist < 100)
                {
                    Logger.Info($"Double-click detected at ({hs.pt.x},{hs.pt.y})");
                    Dispatcher.Invoke(new Action(ToggleFullscreen));
                    _lastClickTime = 0;
                }
                else
                {
                    _lastClickTime = hs.time;
                    _lastClickPt = hs.pt;
                }
            }
        }
        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private bool IsPlayerWindowActive()
    {
        var win = Window.GetWindow(this);
        if (win == null) return false;
        var hwnd = new WindowInteropHelper(win).Handle;
        return GetForegroundWindow() == hwnd;
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= HC_ACTION && (int)wParam == WM_KEYDOWN && IsPlayerWindowActive())
        {
            var hs = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if (DispatchKey((int)hs.vkCode))
                return (IntPtr)1;
        }
        return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private bool DispatchKey(int vkCode)
    {
        if (vkCode == VK_SPACE)
            return DispatchOnUI(TogglePlayback);
        if (vkCode == VK_ESCAPE)
        {
            var win = Window.GetWindow(this);
            if (win != null && win.WindowStyle == WindowStyle.None)
                return DispatchOnUI(ToggleFullscreen);
            return false;
        }
        if (vkCode == VK_LEFT)
            return DispatchOnUI(() => { if (_vlcPlayer != null) _vlcPlayer.Time = Math.Max(0, _vlcPlayer.Time - 10000); });
        if (vkCode == VK_RIGHT)
            return DispatchOnUI(() => { if (_vlcPlayer != null) _vlcPlayer.Time = Math.Min(_vlcPlayer.Length, _vlcPlayer.Time + 10000); });
        if (vkCode == VK_UP)
            return DispatchOnUI(() => { if (_vlcPlayer != null) { var v = Math.Min(MaxVolume, _vlcPlayer.Volume + VolumeStep); _vlcPlayer.Volume = v; UpdateVolumeUI(v); } });
        if (vkCode == VK_DOWN)
            return DispatchOnUI(() => { if (_vlcPlayer != null) { var v = Math.Max(0, _vlcPlayer.Volume - VolumeStep); _vlcPlayer.Volume = v; UpdateVolumeUI(v); } });
        if (vkCode == 0x4E) // N key - next in playlist
            return DispatchOnUI(PlayNextInPlaylist);
        if (vkCode == 0xDB) // [ key - speed down
            return DispatchOnUI(() => StepSpeed(-1));
        if (vkCode == 0xDD) // ] key - speed up
            return DispatchOnUI(() => StepSpeed(1));
        if (vkCode == 0x41) // A key - set point A
            return DispatchOnUI(SetPointA);
        if (vkCode == 0x42) // B key - set point B
            return DispatchOnUI(SetPointB);
        if (vkCode == 0x53) // S key - screenshot
            return DispatchOnUI(TakeScreenshot);
        return false;
    }

    private bool DispatchOnUI(Action action)
    {
        Dispatcher.Invoke(action);
        return true;
    }

    private void PlayNextInPlaylist()
    {
        if (_playlistVideos != null && _playlistIndex + 1 < _playlistVideos.Count)
        {
            Cleanup();
            NavigationService?.Navigate(new DetailPage(_playlistVideos[_playlistIndex + 1], _playlistVideos, _playlistIndex + 1));
        }
    }

    private void OnNextClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        PlayNextInPlaylist();
    }

    private void OnAutoPlayToggleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _autoPlayNext = !_autoPlayNext;
        UpdateAutoPlayUI();
    }

    private void UpdateAutoPlayUI()
    {
        if (AutoPlayBtn == null) return;
        if (_autoPlayNext)
        {
            AutoPlayLabel.Text = "▶▶";
            AutoPlayLabel.Foreground = System.Windows.Media.Brushes.LimeGreen;
        }
        else
        {
            AutoPlayLabel.Text = "▶▶";
            AutoPlayLabel.Foreground = System.Windows.Media.Brushes.Gray;
        }
    }

    private void StepSpeed(int direction)
    {
        var rates = PlaybackRates;
        var idx = Array.IndexOf(rates, _currentRate);
        var newIdx = idx + direction;
        if (newIdx >= 0 && newIdx < rates.Length)
            SetSpeed(rates[newIdx]);
    }

    // ── A-B Repeat ──

    private void SetPointA()
    {
        if (_vlcPlayer == null) return;
        _pointA = _vlcPlayer.Time;
        ABtn.Foreground = System.Windows.Media.Brushes.Orange;
        if (_pointA.HasValue && _pointB.HasValue && !_isLoopingAB)
        {
            _isLoopingAB = true;
            ABRepeatBorder.Visibility = Visibility.Visible;
            ABRepeatLabel.Text = "▶AB";
            ABRepeatLabel.Foreground = System.Windows.Media.Brushes.Lime;
        }
    }

    private void SetPointB()
    {
        if (_vlcPlayer == null) return;
        _pointB = _vlcPlayer.Time;
        BBtn.Foreground = System.Windows.Media.Brushes.Orange;
        if (_pointA.HasValue && _pointB.HasValue && !_isLoopingAB)
        {
            _isLoopingAB = true;
            ABRepeatBorder.Visibility = Visibility.Visible;
            ABRepeatLabel.Text = "▶AB";
            ABRepeatLabel.Foreground = System.Windows.Media.Brushes.Lime;
        }
    }

    private void ToggleABRepeat()
    {
        if (!_pointA.HasValue || !_pointB.HasValue) return;
        _isLoopingAB = !_isLoopingAB;
        if (_isLoopingAB)
        {
            ABRepeatLabel.Text = "▶AB";
            ABRepeatLabel.Foreground = System.Windows.Media.Brushes.Lime;
            if (_vlcPlayer != null && _vlcPlayer.Time >= _pointB.Value)
                _vlcPlayer.Time = _pointA.Value;
        }
        else
        {
            ABRepeatLabel.Text = "AB";
            ABRepeatLabel.Foreground = System.Windows.Media.Brushes.Orange;
        }
    }

    private void TakeScreenshot()
    {
        if (_vlcPlayer == null || !_ready) return;

        try
        {
            var screenshotDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                "BPlayer Screenshots");
            Directory.CreateDirectory(screenshotDir);

            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var filename = $"{Path.GetFileNameWithoutExtension(_video.FilePath)}_{timestamp}.png";
            var filePath = Path.Combine(screenshotDir, filename);

            var success = _vlcPlayer.TakeSnapshot(0, filePath, 0, 0);
            if (success)
            {
                ScreenshotNotif.Text = $"📷 Screenshot saved: {filename}";
                ScreenshotNotif.Visibility = Visibility.Visible;
                Logger.Info($"Screenshot saved: {filePath}");

                var timer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(2.5),
                    IsEnabled = true
                };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    ScreenshotNotif.Visibility = Visibility.Collapsed;
                };
                timer.Start();
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Screenshot failed: {ex.Message}");
        }
    }

    private void OnAButtonClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_pointA.HasValue && _pointB.HasValue)
        {
            _pointA = null;
            _pointB = null;
            _isLoopingAB = false;
            ABRepeatBorder.Visibility = Visibility.Collapsed;
            ABtn.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x6b, 0x72, 0x80));
            BBtn.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x6b, 0x72, 0x80));
            return;
        }
        SetPointA();
    }

    private void OnBButtonClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        SetPointB();
    }

    private void OnABRepeatClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        ToggleABRepeat();
    }

    // ── Window-based popup (no airspace issues with VLC HWND) ──

    private Window? _menuWindow;

    private void CloseMenuWindow()
    {
        try
        {
            if (_menuWindow != null)
            {
                if (_menuWindow.IsVisible)
                    _menuWindow.Close();
                _menuWindow = null;
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"CloseMenuWindow: {ex}");
            _menuWindow = null;
        }
    }

    private Border MakeMenuItem(string header, Action click)
    {
        var border = new Border
        {
            Padding = new System.Windows.Thickness(16, 10, 24, 10),
            Background = System.Windows.Media.Brushes.Transparent,
            Cursor = Cursors.Hand
        };
        border.MouseEnter += (_, _) => border.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(233, 69, 96));
        border.MouseLeave += (_, _) => border.Background = System.Windows.Media.Brushes.Transparent;
        border.MouseLeftButtonUp += (_, _) => { try { click(); } catch (Exception ex) { Logger.Error($"Menu click: {ex}"); } CloseMenuWindow(); };

        var text = new TextBlock
        {
            Text = header,
            Foreground = System.Windows.Media.Brushes.White,
            FontSize = 13
        };
        border.Child = text;
        return border;
    }

    private void ShowRightClickMenuWindow()
    {
        try
        {
            var (cursorX, cursorY) = NativeHelper.GetCursorPosition();
            var panel = new StackPanel();

            panel.Children.Add(MakeMenuItem(_isPlaying ? "⏸  Pause" : "▶  Play", TogglePlayback));
            if (_isPlaying)
                panel.Children.Add(MakeMenuItem("⏹  Stop", StopPlayback));

            panel.Children.Add(MakeSeparator());

            AddMenuSection(panel, "Audio", () => BuildAudioMenu(panel.Children, () => CloseMenuWindow()));
            AddMenuSection(panel, "Subtitles", () => BuildSubtitleMenu(panel.Children, () => CloseMenuWindow()));
            AddMenuSection(panel, "Subtitle Size",
                () => BuildSubtitleFontSizeMenu(panel.Children, () => CloseMenuWindow()));
            AddMenuSection(panel, "Subtitle Font",
                () => BuildSubtitleFontFamilyMenu(panel.Children, () => CloseMenuWindow()));
            panel.Children.Add(MakeSeparator());

            panel.Children.Add(MakeMenuItem("⛶  Fullscreen", ToggleFullscreen));

            ShowPopupWindow(panel, cursorX, cursorY);
        }
        catch (Exception ex)
        {
            Logger.Error($"ShowRightClickMenuWindow: {ex.Message}");
        }
    }

    private static Border MakeSeparator()
    {
        return new Border
        {
            Height = 1,
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(50, 50, 70)),
            Margin = new System.Windows.Thickness(8, 0, 8, 0)
        };
    }

    private static void AddMenuSection(StackPanel panel, string title, Action buildContent)
    {
        panel.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(180, 180, 200)),
            FontSize = 12,
            Padding = new System.Windows.Thickness(16, 8, 24, 4)
        });
        buildContent();
    }

    private void ShowPopupWindow(StackPanel panel, int x, int y)
    {
        var bg = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(20, 20, 32));
        var outerBorder = new Border
        {
            Child = panel,
            Background = bg,
            BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(50, 50, 70)),
            BorderThickness = new System.Windows.Thickness(1),
            CornerRadius = new CornerRadius(8),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = System.Windows.Media.Colors.Black,
                BlurRadius = 12,
                Opacity = 0.6,
                Direction = 0,
                ShadowDepth = 4
            }
        };

        var win = new Window
        {
            Content = outerBorder,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = null,
            Topmost = true,
            ShowInTaskbar = false,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = x,
            Top = y,
            Owner = Window.GetWindow(this)
        };

        win.Deactivated += (_, _) => CloseMenuWindow();
        win.Loaded += (_, _) => { win.Focus(); Keyboard.Focus(win); };
        win.Show();
        _menuWindow = win;
    }

    // ── Audio track support ──

    private void OnAudioBtnClick(object sender, MouseButtonEventArgs e)
    {
        if (!_ready || _vlcPlayer == null) return;

        var menu = new ContextMenu();
        BuildAudioMenu(menu.Items);

        menu.StaysOpen = true;
        menu.PlacementTarget = sender as UIElement;
        if (menu.Style == null)
            menu.Style = TryFindResource(typeof(ContextMenu)) as Style;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void UpdateAudioLabel()
    {
        if (_vlcPlayer == null) return;
        var active = _vlcPlayer.AudioTrack;
        if (active <= 0)
        {
            AudioLabel.Text = " 1";
        }
        else
        {
            try
            {
                var descs = _vlcPlayer.AudioTrackDescription;
                if (descs != null)
                {
                    foreach (var d in descs)
                    {
                        if (d.Id == active)
                        {
                            AudioLabel.Text = " " + d.Name;
                            return;
                        }
                    }
                }
                AudioLabel.Text = " Track " + active;
            }
            catch (Exception ex) { Logger.Warn($"UpdateAudioLabel: {ex.Message}"); AudioLabel.Text = " Track " + active; }
        }
    }

    private void BuildAudioMenu(ItemCollection items)
    {
        var activeTrack = _vlcPlayer.AudioTrack;

        try
        {
            var descs = _vlcPlayer.AudioTrackDescription;
            if (descs != null)
            {
                foreach (var d in descs)
                {
                    if (d.Id < 0) continue;
                    var mark = d.Id == activeTrack ? "✓ " : "  ";
                    var desc = d.Name ?? "Track " + d.Id;
                    var item = new MenuItem
                    {
                        Header = mark + desc, FontSize = 12,
                        Padding = new System.Windows.Thickness(16, 6, 20, 6)
                    };
                    var id = d.Id;
                    item.Click += (_, _) => { _vlcPlayer.SetAudioTrack(id); UpdateAudioLabel(); };
                    items.Add(item);
                }
            }
        }
        catch (Exception ex) { Logger.Warn($"BuildAudioMenu (UI) failed: {ex.Message}"); }
    }

    private void BuildAudioMenu(UIElementCollection items, Action close)
    {
        var activeTrack = _vlcPlayer.AudioTrack;
        try
        {
            var descs = _vlcPlayer.AudioTrackDescription;
            if (descs != null)
            {
                foreach (var d in descs)
                {
                    if (d.Id < 0) continue;
                    var mark = d.Id == activeTrack ? "✓ " : "  ";
                    var desc = d.Name ?? "Track " + d.Id;
                    var border = new Border { Padding = new System.Windows.Thickness(24, 6, 24, 6), Background = System.Windows.Media.Brushes.Transparent, Cursor = Cursors.Hand };
                    var id = d.Id;
                    border.MouseEnter += (_, _) => border.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(233, 69, 96));
                    border.MouseLeave += (_, _) => border.Background = System.Windows.Media.Brushes.Transparent;
                    border.MouseLeftButtonUp += (_, _) => { _vlcPlayer.SetAudioTrack(id); UpdateAudioLabel(); close(); };
                    border.Child = new TextBlock { Text = mark + desc, Foreground = System.Windows.Media.Brushes.White, FontSize = 12, Padding = new System.Windows.Thickness(8, 0, 0, 0) };
                    items.Add(border);
                }
            }
        }
        catch (Exception ex) { Logger.Warn($"BuildAudioMenu (popup) failed: {ex.Message}"); }
    }

    // ── Subtitle track support ──

    private void OnSubtitleBtnClick(object sender, MouseButtonEventArgs e)
    {
        if (!_ready || _vlcPlayer == null) return;

        var menu = new ContextMenu();
        BuildSubtitleMenu(menu.Items);

        menu.StaysOpen = true;
        menu.PlacementTarget = sender as UIElement;
        if (menu.Style == null)
            menu.Style = TryFindResource(typeof(ContextMenu)) as Style;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void UpdateSubtitleLabel()
    {
        if (_vlcPlayer == null) return;
        var active = _vlcPlayer.Spu;
        if (active < 0)
        {
            SubLabel.Text = " Off";
        }
        else
        {
            try
            {
                var descs = _vlcPlayer.SpuDescription;
                if (descs != null && active < descs.Length)
                    SubLabel.Text = " " + descs[active].Name;
                else
                    SubLabel.Text = " Track " + (active + 1);
            }
            catch (Exception ex) { Logger.Warn($"UpdateSubtitleLabel: {ex.Message}"); SubLabel.Text = " Track " + (active + 1); }
        }
    }

    private void BuildSubtitleMenu(ItemCollection items)
    {
        var activeTrack = _vlcPlayer.Spu;

        try
        {
            var descs = _vlcPlayer.SpuDescription;
            for (int i = 0; i < _vlcPlayer.SpuCount && i < (descs?.Length ?? 0); i++)
            {
                var idx = i;
                var desc = (descs != null && idx < descs.Length ? descs[idx].Name : null) ?? "Track " + (idx + 1);
                var mark = idx == activeTrack ? "✓ " : "  ";
                var item = new MenuItem
                {
                    Header = mark + desc, FontSize = 12,
                    Padding = new System.Windows.Thickness(16, 6, 20, 6)
                };
                item.Click += (_, _) => { _vlcPlayer.SetSpu(idx); _savedSpuIndex = idx; UpdateSubtitleLabel(); };
                items.Add(item);
            }
            if (items.Count > 0)
                items.Add(new Separator());
        }
        catch (Exception ex) { Logger.Warn($"BuildSubtitleMenu failed: {ex.Message}"); }

        var off = activeTrack == -1 ? "✓ " : "  ";
        var offItem = new MenuItem
        {
            Header = off + "Off", FontSize = 12,
            Padding = new System.Windows.Thickness(16, 6, 20, 6)
        };
        offItem.Click += (_, _) => { _vlcPlayer.SetSpu(-1); _savedSpuIndex = -1; UpdateSubtitleLabel(); };
        items.Add(offItem);

        // External subtitle
        items.Add(new Separator());
        var extItem = new MenuItem
        {
            Header = "  Load subtitle file...", FontSize = 12,
            Padding = new System.Windows.Thickness(16, 6, 20, 6)
        };
        extItem.Click += (_, _) => LoadExternalSubtitle();
        items.Add(extItem);
    }

    private void BuildSubtitleMenu(UIElementCollection items, Action close)
    {
        var activeTrack = _vlcPlayer.Spu;

        try
        {
            var descs = _vlcPlayer.SpuDescription;
            for (int i = 0; i < _vlcPlayer.SpuCount && i < (descs?.Length ?? 0); i++)
            {
                var idx = i;
                var desc = (descs != null && idx < descs.Length ? descs[idx].Name : null) ?? "Track " + (idx + 1);
                var mark = idx == activeTrack ? "✓ " : "  ";
                var border = new Border { Padding = new System.Windows.Thickness(24, 6, 24, 6), Background = System.Windows.Media.Brushes.Transparent, Cursor = Cursors.Hand };
                border.MouseEnter += (_, _) => border.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(233, 69, 96));
                border.MouseLeave += (_, _) => border.Background = System.Windows.Media.Brushes.Transparent;
                border.MouseLeftButtonUp += (_, _) => { _vlcPlayer.SetSpu(idx); _savedSpuIndex = idx; UpdateSubtitleLabel(); close(); };
                border.Child = new TextBlock { Text = mark + desc, Foreground = System.Windows.Media.Brushes.White, FontSize = 12, Padding = new System.Windows.Thickness(8, 0, 0, 0) };
                items.Add(border);
            }
        }
        catch (Exception ex) { Logger.Warn($"BuildSubtitleMenu (UI) failed: {ex.Message}"); }

        var off = activeTrack == -1 ? "✓ " : "  ";
        var offItem = new Border { Padding = new System.Windows.Thickness(24, 6, 24, 6), Background = System.Windows.Media.Brushes.Transparent, Cursor = Cursors.Hand };
        offItem.MouseEnter += (_, _) => offItem.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(233, 69, 96));
        offItem.MouseLeave += (_, _) => offItem.Background = System.Windows.Media.Brushes.Transparent;
        offItem.MouseLeftButtonUp += (_, _) => { _vlcPlayer.SetSpu(-1); _savedSpuIndex = -1; UpdateSubtitleLabel(); close(); };
        offItem.Child = new TextBlock { Text = off + "Off", Foreground = System.Windows.Media.Brushes.White, FontSize = 12, Padding = new System.Windows.Thickness(8, 0, 0, 0) };
        items.Add(offItem);

        // External subtitle
        var extItem = new Border { Padding = new System.Windows.Thickness(24, 6, 24, 6), Background = System.Windows.Media.Brushes.Transparent, Cursor = Cursors.Hand };
        extItem.MouseEnter += (_, _) => extItem.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(233, 69, 96));
        extItem.MouseLeave += (_, _) => extItem.Background = System.Windows.Media.Brushes.Transparent;
        extItem.MouseLeftButtonUp += (_, _) => { close(); LoadExternalSubtitle(); };
        extItem.Child = new TextBlock { Text = "  Load subtitle file...", Foreground = System.Windows.Media.Brushes.White, FontSize = 12, Padding = new System.Windows.Thickness(8, 0, 0, 0) };
        items.Add(extItem);
    }

    private void BuildSubtitleFontSizeMenu(UIElementCollection items, Action close)
    {
        int[] sizes = { 10, 12, 16, 20, 24, 28, 36, 48 };
        foreach (var size in sizes)
        {
            var mark = size == _subtitleFontSize ? "✓ " : "  ";
            var border = MakeMenuBorder(mark + size + "px", close, () => ApplySubtitleFont(_subtitleFontFamily, size));
            items.Add(border);
        }
    }

    private void BuildSubtitleFontFamilyMenu(UIElementCollection items, Action close)
    {
        string[] fonts = { "Arial", "Verdana", "Tahoma", "Times New Roman", "Courier New", "Segoe UI", "Consolas" };
        foreach (var font in fonts)
        {
            var mark = font == _subtitleFontFamily ? "✓ " : "  ";
            var border = MakeMenuBorder(mark + font, close, () => ApplySubtitleFont(font, _subtitleFontSize));
            items.Add(border);
        }
    }

    private Border MakeMenuBorder(string text, Action close, Action action)
    {
        var border = new Border
        {
            Padding = new System.Windows.Thickness(24, 6, 24, 6),
            Background = System.Windows.Media.Brushes.Transparent,
            Cursor = Cursors.Hand
        };
        border.MouseEnter += (_, _) => border.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(233, 69, 96));
        border.MouseLeave += (_, _) => border.Background = System.Windows.Media.Brushes.Transparent;
        border.MouseLeftButtonUp += (_, _) => { close(); try { action(); } catch (Exception ex) { Logger.Error($"Menu: {ex}"); } };
        border.Child = new TextBlock { Text = text, Foreground = System.Windows.Media.Brushes.White, FontSize = 12, Padding = new System.Windows.Thickness(8, 0, 0, 0) };
        return border;
    }

    private void LoadExternalSubtitle()
    {
        try
        {
            var videoDir = Path.GetDirectoryName(_video.FilePath);
            var videoName = Path.GetFileNameWithoutExtension(_video.FilePath);
            var srtPath = Path.Combine(videoDir!, videoName + ".srt");

            if (File.Exists(srtPath))
            {
                _vlcPlayer.AddSlave(MediaSlaveType.Subtitle, srtPath, true);
                _externalSubtitlePath = srtPath;
                _savedSpuIndex = _vlcPlayer.Spu;
                Logger.Info($"Loaded subtitle: {srtPath}");
                UpdateSubtitleLabel();
                return;
            }

            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select subtitle file",
                Filter = "Subtitle files (*.srt;*.sub;*.vtt)|*.srt;*.sub;*.vtt|All files (*.*)|*.*",
                InitialDirectory = videoDir
            };
            if (dialog.ShowDialog() == true)
            {
                _vlcPlayer.AddSlave(MediaSlaveType.Subtitle, dialog.FileName, true);
                _externalSubtitlePath = dialog.FileName;
                _savedSpuIndex = _vlcPlayer.Spu;
                Logger.Info($"Loaded subtitle: {dialog.FileName}");
                UpdateSubtitleLabel();
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Subtitle: {ex.Message}");
            ShowError($"Failed to load subtitle:\n{ex.Message}");
        }
    }

    // ── Subtitle font settings ──

    private async System.Threading.Tasks.Task LoadSubtitleFontSettingsAsync()
    {
        try
        {
            var config = await _configService.LoadAsync();
            _subtitleFontFamily = config.SubtitleFontFamily ?? "Arial";
            _subtitleFontSize = config.SubtitleFontSize > 0 ? config.SubtitleFontSize : 16;
        }
        catch (Exception ex) { Logger.Warn($"LoadSubtitleFontSettings failed: {ex.Message}"); }
    }

    private void ApplySubtitleFont(string fontFamily, int fontSize)
    {
        _subtitleFontFamily = fontFamily;
        _subtitleFontSize = fontSize;
        _ = SaveSubtitleFontSettingsAsync();
        ShowLoadingOverlay("Applying subtitle font…");
        RestartPlayback();
    }

    private async System.Threading.Tasks.Task SaveSubtitleFontSettingsAsync()
    {
        try
        {
            var config = await _configService.LoadAsync();
            config.SubtitleFontFamily = _subtitleFontFamily;
            config.SubtitleFontSize = _subtitleFontSize;
            await _configService.SaveAsync(config);
        }
        catch (Exception ex) { Logger.Warn($"SaveSubtitleFontSettings failed: {ex.Message}"); }
    }

    private void RestartPlayback()
    {
        if (!_ready || _vlcPlayer == null) return;
        var position = _vlcPlayer.Time;
        var wasPlaying = _isPlaying;
        var volume = _vlcPlayer.Volume;
        var rate = _vlcPlayer.Rate;
        var audioTrack = _vlcPlayer.AudioTrack;
        var spu = _vlcPlayer.Spu;

        _vlcPlayer.Stop();
        _media?.Dispose();

        _media = BuildMediaWithSubtitleOptions();
        _vlcPlayer.Media = _media;

        if (wasPlaying)
        {
            _vlcPlayer.Play();
            _vlcPlayer.Volume = volume;
            if (Math.Abs(rate - 1.0f) > 0.01f) _vlcPlayer.SetRate(rate);
            if (audioTrack > 0) _vlcPlayer.SetAudioTrack(audioTrack);
            if (spu >= 0) _vlcPlayer.SetSpu(spu);
            _vlcPlayer.Time = position;
        }
    }

    private static string SanitizeVlcOption(string value)
    {
        // Allow only safe characters for VLC string options
        return (value ?? "").Replace("\"", "").Replace(":", "").Replace("\n", "").Replace("\r", "");
    }

    private Media BuildMediaWithSubtitleOptions()
    {
        var m = new Media(_libVlc, _video.FilePath);
        var safeFont = SanitizeVlcOption(_subtitleFontFamily);
        m.AddOption(":freetype-font=" + safeFont);
        m.AddOption(":freetype-fontsize=" + _subtitleFontSize);
        if (_savedSpuIndex >= 0)
            m.AddOption(":sub-track=" + _savedSpuIndex);
        if (_externalSubtitlePath != null)
            m.AddOption(":sub-file=" + SanitizeVlcOption(_externalSubtitlePath));
        else
        {
            var srtPath = Path.Combine(Path.GetDirectoryName(_video.FilePath) ?? "", Path.GetFileNameWithoutExtension(_video.FilePath) + ".srt");
            if (File.Exists(srtPath))
            {
                m.AddOption(":sub-file=" + srtPath);
                _externalSubtitlePath = srtPath;
            }
        }
        return m;
    }

    // ── UI event handlers ──

    private void OnPlayClick(object sender, RoutedEventArgs e) => TogglePlayback();

    private void OnProgressClick(object sender, MouseButtonEventArgs e)
    {
        if (_vlcPlayer == null || _vlcPlayer.Length <= 0) return;
        var bar = (Border)sender;
        var pos = e.GetPosition(bar);
        var fraction = Math.Max(0, Math.Min(1, pos.X / bar.ActualWidth));
        _vlcPlayer.Position = (float)fraction;
        ProgressFill.Width = fraction * bar.ActualWidth;
        Logger.Info($"Seek to {fraction * 100:F1}%");
    }

    private void OnVolumeBarClick(object sender, MouseButtonEventArgs e)
    {
        if (_vlcPlayer == null) return;
        var bar = (Border)sender;
        var pos = e.GetPosition(bar);
        var fraction = Math.Max(0, Math.Min(1, pos.X / bar.ActualWidth));
        var vol = (int)(fraction * 200);
        _vlcPlayer.Volume = vol;
        UpdateVolumeUI(vol);
        Logger.Info($"Volume set to {vol} ({fraction * 100:F0}%)");
    }

    private void OnFullscreenBtnClick(object sender, MouseButtonEventArgs e) => ToggleFullscreen();

    private void UpdateVolumeUI(int volume)
    {
        var pct = Math.Max(0, Math.Min(100, (int)(volume / 2.0)));
        if (VolumeBar.ActualWidth > 0)
            VolumeFill.Width = pct / 100.0 * VolumeBar.ActualWidth;
        VolumeLabel.Text = volume.ToString();
    }

    private void OnGridKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && Window.GetWindow(this) is { WindowStyle: WindowStyle.None })
        {
            ToggleFullscreen();
            e.Handled = true;
        }
    }

    // ── Fullscreen ──

    private Window? _fullscreenControlsWindow;

    private void ToggleFullscreen()
    {
        var win = Window.GetWindow(this);
        if (win == null) return;
        if (win.WindowStyle == WindowStyle.None && win.WindowState == WindowState.Maximized)
        {
            _isFullscreen = false;
            _hideControlsTimer.Stop();
            CloseFullscreenControlsWindow();
            win.WindowStyle = WindowStyle.SingleBorderWindow;
            win.WindowState = WindowState.Normal;
            ControlsOverlay.Visibility = Visibility.Visible;
            Logger.Info("Exited fullscreen");
        }
        else
        {
            _isFullscreen = true;
            win.WindowStyle = WindowStyle.None;
            win.WindowState = WindowState.Maximized;
            ControlsOverlay.Visibility = Visibility.Collapsed;
            ShowFullscreenControlsWindow();
            Logger.Info("Entered fullscreen");
        }
    }

    private void CloseFullscreenControlsWindow()
    {
        try
        {
            _hideFullscreenTimer?.Stop();
            if (_fullscreenControlsWindow != null)
            {
                if (_fullscreenControlsWindow.IsVisible)
                    _fullscreenControlsWindow.Close();
                _fullscreenControlsWindow = null;
            }
        }
        catch (Exception ex) { Logger.Warn($"Fullscreen controls failed: {ex.Message}"); _fullscreenControlsWindow = null; }
    }

    private void ShowFullscreenControlsWindow()
    {
        try
        {
            CloseFullscreenControlsWindow();
            _fullscreenControlsWindow = BuildFullscreenControlsWindow();
            _fullscreenControlsWindow.Show();

            // Auto-hide after 3 seconds of inactivity
            if (_hideFullscreenTimer == null)
            {
                _hideFullscreenTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                _hideFullscreenTimer.Tick += (_, _) =>
                {
                    _hideFullscreenTimer.Stop();
                    CloseFullscreenControlsWindow();
                };
            }
            _hideFullscreenTimer.Stop();
            _hideFullscreenTimer.Start();
        }
        catch (Exception ex)
        {
            Logger.Error($"ShowFullscreenControlsWindow: {ex.Message}");
        }
    }

    private Window BuildFullscreenControlsWindow()
    {
        var screenWidth = (int)SystemParameters.PrimaryScreenWidth;

        var trackBg = Application.Current.Resources["ButtonBgBrush"] as System.Windows.Media.Brush
            ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2a, 0x2a, 0x3e));
        var accentFill = Application.Current.Resources["AccentBrush"] as System.Windows.Media.Brush
            ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xe9, 0x45, 0x60));

        var seekBar = new Border
        {
            Height = 12,
            Background = trackBg,
            CornerRadius = new CornerRadius(6),
            Margin = new System.Windows.Thickness(28, 4, 28, 8),
            Cursor = Cursors.Hand,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch
        };
        var progressFill = new Border
        {
            Width = 0,
            Background = accentFill,
            CornerRadius = new CornerRadius(6),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left
        };
        seekBar.Child = progressFill;
        seekBar.MouseLeftButtonDown += (s, e) =>
        {
            if (_vlcPlayer == null || _vlcPlayer.Length <= 0) return;
            var bar = (Border)s!;
            var pos = e.GetPosition(bar);
            var fraction = bar.ActualWidth > 0 ? Math.Max(0, Math.Min(1, pos.X / bar.ActualWidth)) : 0;
            _vlcPlayer.Position = (float)fraction;
            progressFill.Width = fraction * bar.ActualWidth;
        };

        // Keep progress fill in sync
        var updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        updateTimer.Tick += (_, _) =>
        {
            if (_vlcPlayer == null || _vlcPlayer.Length <= 0) return;
            var frac = Math.Max(0, Math.Min(1, (double)_vlcPlayer.Time / _vlcPlayer.Length));
            progressFill.Width = frac * seekBar.ActualWidth;
        };
        updateTimer.Start();

        var panel = new StackPanel();
        panel.Children.Add(seekBar);

        var btnRow = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            Margin = new System.Windows.Thickness(0, 0, 0, 8)
        };

        var playBtn = new Button
        {
            Content = _isPlaying ? "⏸" : "▶",
            Background = System.Windows.Media.Brushes.Transparent,
            Foreground = System.Windows.Media.Brushes.White,
            BorderThickness = new System.Windows.Thickness(0),
            FontSize = 18,
            Cursor = Cursors.Hand,
            Margin = new System.Windows.Thickness(0, 0, 20, 0)
        };
        playBtn.Click += (_, _) => { TogglePlayback(); playBtn.Content = _isPlaying ? "⏸" : "▶"; };
        btnRow.Children.Add(playBtn);

        var timeText = new TextBlock
        {
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x9c, 0xa3, 0xaf)),
            FontSize = 12,
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        };
        // Update time periodically
        var timeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timeTimer.Tick += (_, _) =>
        {
            if (_vlcPlayer != null && _vlcPlayer.Length > 0)
                timeText.Text = FormatTime(TimeSpan.FromMilliseconds(_vlcPlayer.Time)) + " / " + FormatTime(TimeSpan.FromMilliseconds(_vlcPlayer.Length));
        };
        timeTimer.Start();
        btnRow.Children.Add(timeText);

        var volBorder = new Border
        {
            Width = 100,
            Height = 8,
            Background = trackBg,
            CornerRadius = new CornerRadius(4),
            Cursor = Cursors.Hand,
            Margin = new System.Windows.Thickness(20, 0, 0, 0),
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        };
        var volFill = new Border
        {
            Width = _vlcPlayer?.Volume ?? 100,
            Background = accentFill,
            CornerRadius = new CornerRadius(4),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left
        };
        volBorder.Child = volFill;
        volBorder.MouseLeftButtonDown += (s, e) =>
        {
            if (_vlcPlayer == null) return;
            var bar = (Border)s!;
            var pos = e.GetPosition(bar);
            var fraction = bar.ActualWidth > 0 ? Math.Max(0, Math.Min(1, pos.X / bar.ActualWidth)) : 0;
            var vol = (int)(fraction * 200);
            _vlcPlayer.Volume = vol;
            volFill.Width = fraction * bar.ActualWidth;
        };
        btnRow.Children.Add(volBorder);

        var exitFsBtn = new Button
        {
            Content = "⛶",
            Background = System.Windows.Media.Brushes.Transparent,
            Foreground = System.Windows.Media.Brushes.White,
            BorderThickness = new System.Windows.Thickness(0),
            FontSize = 16,
            Cursor = Cursors.Hand,
            Margin = new System.Windows.Thickness(20, 0, 28, 0)
        };
        exitFsBtn.Click += (_, _) => ToggleFullscreen();
        btnRow.Children.Add(exitFsBtn);

        panel.Children.Add(btnRow);

        var outerBorder = new Border
        {
            Child = panel,
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(204, 0, 0, 0))
        };

        var win = new Window
        {
            Content = outerBorder,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = null,
            Topmost = true,
            ShowInTaskbar = false,
            ResizeMode = ResizeMode.NoResize,
            Width = screenWidth,
            Height = 80,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Owner = Window.GetWindow(this),
            Left = 0,
            Top = SystemParameters.PrimaryScreenHeight - 80
        };
        win.Loaded += (_, _) => { win.Focus(); Keyboard.Focus(win); };

        return win;
    }

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Cleanup();
            if (NavigationService?.CanGoBack == true)
                NavigationService.GoBack();
        }
        catch (Exception ex) { Logger.Error($"Back: {ex.Message}"); }
    }

    private static string FormatTime(TimeSpan ts) => FormattingUtils.FormatTime(ts);
}
