using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;

using WpfApp            = System.Windows.Application;
using WpfMsgBox         = System.Windows.MessageBox;
using WpfDataFormats    = System.Windows.DataFormats;
using WpfDragDropFx     = System.Windows.DragDropEffects;
using WpfDragEventArgs  = System.Windows.DragEventArgs;
using WpfKeyEventArgs   = System.Windows.Input.KeyEventArgs;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;
using AnniPlayer.Services;
using AnniPlayer.Models;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Media;
using System.Linq;

namespace AnniPlayer
{
    public partial class MainWindow : Window
    {
        // ── MPV state ──────────────────────────────────────────────────────
        private IntPtr _mpv;
        private System.Windows.Forms.Panel _videoPanel = null!;
        private System.Windows.Threading.DispatcherTimer _timer = null!;
        private System.Windows.Threading.DispatcherTimer _mousePollTimer = null!;
        private System.Windows.Threading.DispatcherTimer _clickTimer = null!;
        private System.Windows.Threading.DispatcherTimer _osdTimer = null!;

        private bool _isPlaying        = false;
        private bool _isMuted          = false;
        private bool _hasMedia         = false;
        private bool _isFullscreen     = false;
        private bool _isTogglingFullscreen = false;
        // Cancels long-lived background listeners when the WPF window is closed.
        // Without this, the named-pipe loop can survive shutdown and block on Dispatcher.Invoke.
        private readonly System.Threading.CancellationTokenSource _windowLifetimeCts = new();
        private string _lastMpvBackgroundColor = "";
        // Cached reflection method for Popup.UpdatePosition (avoids repeated GetMethod lookups per window event)
        private static readonly System.Reflection.MethodInfo? _popupUpdatePositionMethod =
            typeof(System.Windows.Controls.Primitives.Popup)
                .GetMethod("UpdatePosition", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        private bool _timerUpdating    = false;  // prevents seek feedback loop
        private bool _draggingTimeline = false;
        private System.Drawing.Point _lastMousePos;
        private DateTime _lastMouseMoveTime;
        private DateTime _keepUiAliveUntil = DateTime.MinValue;
        private bool _videoSizeSet     = true;
        private string _pendingMediaDimensionsPath = "";

        private System.Drawing.Point _videoPanelMouseDownPos;
        private bool _videoPanelIsMouseDown = false;
        private bool _videoPanelIsDraggingWindow = false;
        private bool _ignoreNextPlayPauseClick = false;
        private long _lastActivatedByClickTick = 0;
        private const int WM_MOUSEACTIVATE = 0x0021;
        
        private bool _isDrawerOpen = false;
        private bool _drawerAnimating = false;
        private bool _isDrawerPinned = false;
        private bool _isDialogOpen = false;
        private string _smartFillMode = "none";
        private bool _smartFillEnabled { get => _smartFillMode != "none"; set => _smartFillMode = value ? (_smartFillMode == "none" ? "normal" : _smartFillMode) : "none"; }
        private string _autoCropMode = "none";
        private System.Drawing.Size _lastVideoPanelSize;
        private bool _isTransitioning = false;
        private bool _isCurrentImage = false;
        private string _lastVfString = "";   // cache: skip MPV vf rebuild if unchanged
        private static readonly System.Windows.Media.Brush CyanTransitionBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 229, 255));
        private static readonly System.Windows.Media.Brush GrayTransitionBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(102, 102, 102));
        private System.Windows.Threading.DispatcherTimer? _imageMotionTimer;
        private DateTime _imageStartTime;
        private int _imageMotionMode = 0;
        private double _imageElapsedSec = 0.0;
        private double _pendingResumePosition = 0.0;
        private bool _isDraggingSize = false;
        private bool _currentMediaHasVideoTrack = false;
        
        // Anti-deadlock serialization queue
        private bool _isMediaLoading = false;
        private System.Collections.Concurrent.ConcurrentQueue<string> _loadQueue = new();

        // Dedup guard: prevent same file triggering PlayFile twice within 800ms
        // (happens when drag-to-exe-icon fires both Pipe IPC and WM_DROPFILES simultaneously)
        private string _lastPlayedPath = "";
        private DateTime _lastPlayedTime = DateTime.MinValue;
        private const int PlayDedupMs = 800;

        // self-test & perf-debug
        private bool   _selfTest      = false;
        private string _selfTestMedia = "";
        private bool   _perfDebug     = false;
        private string _perfDebugMedia = "";

        // Explicit stream save trigger from Ctrl+U dialog
        private bool _explicitSaveStreamRequested = false;
        private string _explicitSaveStreamDir = "";
        private string _explicitSaveStreamUrl = "";

        // ─────────────────────────────────────────────────────────────────
        public MainWindow()
        {
            InitializeComponent();
            System.Windows.Input.InputMethod.SetIsInputMethodEnabled(this, false);

            var cmdArgs = Environment.GetCommandLineArgs();
            if (cmdArgs.Any(a => a == "--self-test"))
            {
                _selfTest = true;
                int idx = Array.IndexOf(cmdArgs, "--self-test");
                if (idx + 1 < cmdArgs.Length) _selfTestMedia = cmdArgs[idx + 1];
            }
            else if (cmdArgs.Any(a => a == "--perf-debug" || a == "--benchmark"))
            {
                _perfDebug = true;
                int idx = Array.FindIndex(cmdArgs, a => a == "--perf-debug" || a == "--benchmark");
                if (idx + 1 < cmdArgs.Length) _perfDebugMedia = cmdArgs[idx + 1];
            }
            try { File.WriteAllText(@"E:\Winnie-history\Anni player\perf_main_ctor.txt", $"_perfDebug={_perfDebug}, _selfTest={_selfTest}, args={string.Join(" ", cmdArgs)}"); } catch {}

            // Attempt to force rounded corners for Windows 11
            this.Loaded += (s, e) =>
            {
                try
                {
                    var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                    int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
                    int DWMWCP_ROUND = 2;
                    DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref DWMWCP_ROUND, sizeof(int));
                }
                catch { }
            };

            // File Drag & Drop support on WPF window
            this.Drop += Window_Drop;
            this.DragOver += Window_DragOver;

            popupTop.CustomPopupPlacementCallback = (popupSize, targetSize, offset) =>
            {
                return new[]
                {
                    new System.Windows.Controls.Primitives.CustomPopupPlacement(
                        new System.Windows.Point(0, 0),
                        System.Windows.Controls.Primitives.PopupPrimaryAxis.Horizontal)
                };
            };

            popupBottom.CustomPopupPlacementCallback = (popupSize, targetSize, offset) =>
            {
                double targetH = targetSize.Height > 0 ? targetSize.Height : ActualHeight;
                // Extend 3px bleed past the bottom of the screen to eliminate any 1px gap from DPI/subpixel rounding
                double y = Math.Max(0, targetH - 115);
                return new[]
                {
                    new System.Windows.Controls.Primitives.CustomPopupPlacement(
                        new System.Windows.Point(0, y),
                        System.Windows.Controls.Primitives.PopupPrimaryAxis.Horizontal)
                };
            };

            I18nService.Instance.PropertyChanged += (s, e) => UpdateControlBarTooltips();

            UpdateControlBarTooltips();

            this.Loaded += Window_Loaded;
            this.Activated += (s, e) =>
            {
                RestoreTopmostState();
                EnsureMainCanvasFocusAndDisableIme();
            };

            this.Deactivated += (s, e) =>
            {
                if (!IsCurrentAppActive())
                {
                    if (_isFullscreen && !SettingsService.Instance.Config.AlwaysOnTop)
                    {
                        this.Topmost = false;
                        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                        if (hwnd != IntPtr.Zero)
                        {
                            SetWindowPos(hwnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
                        }
                    }
                    else if (SettingsService.Instance.Config.AlwaysOnTop || _isPipMode)
                    {
                        // Explicitly enforce Topmost when losing focus so other windows do NOT cover AniPlayer
                        RestoreTopmostState();
                    }

                    if (_isFullscreen)
                    {
                        _keepUiAliveUntil = DateTime.MinValue;
                        AnimateTopBar(false);
                        AnimateBottomBar(false);
                    }
                }
            };

            // _hideTimer logic now integrated into _mousePollTimer

            _clickTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(System.Windows.Forms.SystemInformation.DoubleClickTime)
            };
            _clickTimer.Tick += (s, ev) =>
            {
                _clickTimer.Stop();
                if (SettingsService.Instance.Config.ClickToActivateOnly && _ignoreNextPlayPauseClick)
                {
                    _ignoreNextPlayPauseClick = false;
                    return;
                }
                BtnPlay_Click(null, new RoutedEventArgs());
            };

            _osdTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            _osdTimer.Tick += (s, ev) =>
            {
                _osdTimer.Stop();
                popupOsd.IsOpen = false;
            };

            // Record initial mouse position for polling
            _lastMousePos = System.Windows.Forms.Cursor.Position;
            
            // Stream Download Service Event Wiring
            StreamDownloadService.Instance.Started += (savePath) =>
            {
                Dispatcher.Invoke(() =>
                {
                    string fName = Path.GetFileName(savePath);
                    ShowOsd(string.Format(I18nService.Instance["OsdStreamRecording"], fName));
                });
            };
            StreamDownloadService.Instance.Completed += (savePath) =>
            {
                Dispatcher.Invoke(() =>
                {
                    string fName = Path.GetFileName(savePath);
                    ShowOsd(string.Format(I18nService.Instance["OsdStreamDownloadCompleted"], fName));
                });
            };
            StreamDownloadService.Instance.Failed += (err) =>
            {
                Dispatcher.Invoke(() =>
                {
                    ShowOsd(string.Format(I18nService.Instance["OsdStreamDownloadFailed"], err));
                });
            };

            _mousePollTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _mousePollTimer.Tick += MousePollTimer_Tick;
            _mousePollTimer.Start();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var source = System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(this).Handle);
            source?.AddHook(WndProc);
        }

        private void ApplyControlBarMode(bool isPip)
        {
            if (isPip)
            {
                ApplyButtonSizes(28, 18, 34, 22);
                controlBar.Height = 52;
                txtTime.FontSize = 11;
                txtTime.FontWeight = FontWeights.Normal;
                txtClock.FontSize = 11;
                txtClock.FontWeight = FontWeights.Normal;
                if (gridControlBarButtons != null) gridControlBarButtons.Margin = new Thickness(2, -4, 2, 2);
                if (btnPip != null) btnPip.Margin = new Thickness(0, 0, 8, 0);
                if (btnTracks != null) btnTracks.Margin = new Thickness(0, 0, 8, 0);
                if (panelMuteContainer != null) panelMuteContainer.Margin = new Thickness(0, 0, 8, 0);
            }
            else
            {
                ApplyButtonSizes(42, 24, 54, 30);
                controlBar.Height = 105;
                txtTime.FontSize = 18;
                txtTime.FontWeight = FontWeights.Bold;
                txtClock.FontSize = 18;
                txtClock.FontWeight = FontWeights.Bold;
                if (gridControlBarButtons != null) gridControlBarButtons.Margin = new Thickness(12, -4, 12, 2);
                if (btnPip != null) btnPip.Margin = new Thickness(0, 0, 10, 0);
                if (btnTracks != null) btnTracks.Margin = new Thickness(0, 0, 16, 0);
                if (panelMuteContainer != null) panelMuteContainer.Margin = new Thickness(0, 0, 16, 0);
                UpdateResponsiveControlBar(this.ActualWidth > 0 ? this.ActualWidth : this.Width);
            }
        }

        private double _lastResponsiveWidth = -1;
        private void UpdateResponsiveControlBar(double width)
        {
            if (_isPipMode) return; // In PiP mode, ApplyControlBarMode(true) controls button visibility
            if (Math.Abs(width - _lastResponsiveWidth) < 1.0) return;
            _lastResponsiveWidth = width;

            // Progressive button-by-button collapsing with generous safety margins:
            // 1. panelBrightness (Brightness slider popup, width ~58px): Collapse below 1040px
            if (panelBrightness != null)
                panelBrightness.Visibility = (width >= 1040) ? Visibility.Visible : Visibility.Collapsed;
            if (panelBrightnessFS != null)
                panelBrightnessFS.Visibility = (width >= 1040) ? Visibility.Visible : Visibility.Collapsed;

            // 2. btnScreenshot (Screenshot, width ~52px): Collapse below 980px
            if (btnScreenshot != null)
                btnScreenshot.Visibility = (width >= 980) ? Visibility.Visible : Visibility.Collapsed;
            if (btnScreenshotFS != null)
                btnScreenshotFS.Visibility = (width >= 980) ? Visibility.Visible : Visibility.Collapsed;

            // 3. btnAutoCrop (AutoCrop 去黑边, width ~52px): Collapse below 920px
            if (btnAutoCrop != null)
                btnAutoCrop.Visibility = (width >= 920) ? Visibility.Visible : Visibility.Collapsed;
            if (btnAutoCropFS != null)
                btnAutoCropFS.Visibility = (width >= 920) ? Visibility.Visible : Visibility.Collapsed;

            // 4. btnSmartFill (SmartFill 虚化填充, width ~52px): Collapse below 860px
            if (btnSmartFill != null)
                btnSmartFill.Visibility = (width >= 860) ? Visibility.Visible : Visibility.Collapsed;
            if (btnSmartFillFS != null)
                btnSmartFillFS.Visibility = (width >= 860) ? Visibility.Visible : Visibility.Collapsed;

            // 5. btnSpeed (Speed 1.0x button, width ~72px): Collapse below 800px
            if (btnSpeed != null)
                btnSpeed.Visibility = (width >= 800) ? Visibility.Visible : Visibility.Collapsed;
            if (btnSpeedFS != null)
                btnSpeedFS.Visibility = (width >= 800) ? Visibility.Visible : Visibility.Collapsed;

            // 6. btnTracks (Audio/Sub tracks, width ~58px): Collapse below 740px
            if (btnTracks != null)
                btnTracks.Visibility = (width >= 740) ? Visibility.Visible : Visibility.Collapsed;
            if (btnTracksFS != null)
                btnTracksFS.Visibility = (width >= 740) ? Visibility.Visible : Visibility.Collapsed;

            // 7. btnLibrary (Media Library, width ~52px): Collapse below 680px
            if (btnLibrary != null)
                btnLibrary.Visibility = (width >= 680) ? Visibility.Visible : Visibility.Collapsed;
            if (btnLibraryFS != null)
                btnLibraryFS.Visibility = (width >= 680) ? Visibility.Visible : Visibility.Collapsed;

            // 8. btnPip (PiP button, width ~52px): Collapse below 620px
            if (btnPip != null)
                btnPip.Visibility = (width >= 620) ? Visibility.Visible : Visibility.Collapsed;
            if (btnPipFS != null)
                btnPipFS.Visibility = (width >= 620) ? Visibility.Visible : Visibility.Collapsed;

            // 9. btnOpen (Open File text button, width ~110px): Priority essential button, preserved down to 540px!
            if (btnOpen != null)
                btnOpen.Visibility = (width >= 540) ? Visibility.Visible : Visibility.Collapsed;
            if (btnOpenFS != null)
                btnOpenFS.Visibility = (width >= 540) ? Visibility.Visible : Visibility.Collapsed;

            // Always keep: btnPrev, btnPlay, btnNext, txtTime, btnMute, btnFullscreen

            // Time label font scaling
            if (txtTime != null)
            {
                if (width >= 900) txtTime.FontSize = 18;
                else if (width >= 720) txtTime.FontSize = 16;
                else if (width >= 580) txtTime.FontSize = 14;
                else txtTime.FontSize = 12;
            }
            if (txtTimeFS != null)
            {
                if (width >= 900) txtTimeFS.FontSize = 18;
                else if (width >= 720) txtTimeFS.FontSize = 16;
                else if (width >= 580) txtTimeFS.FontSize = 14;
                else txtTimeFS.FontSize = 12;
            }
        }

        private void ApplyButtonSizes(double btnSize, double fontPx, double playSize, double playFontPx)
        {
            var transportBtns = new[] { btnLibrary, btnPrev, btnNext, btnScreenshot, btnSmartFill, btnAutoCrop, btnMute, btnFullscreen, btnPip, btnTracks };
            foreach (var btn in transportBtns)
            {
                if (btn != null)
                {
                    btn.Width = btnSize;
                    btn.Height = btnSize;
                    btn.FontSize = fontPx;
                }
            }
            if (btnPlay != null)
            {
                btnPlay.Width = playSize;
                btnPlay.Height = playSize;
                btnPlay.FontSize = playFontPx;
            }
            if (btnPlayFS != null)
            {
                btnPlayFS.Width = playSize;
                btnPlayFS.Height = playSize;
                btnPlayFS.FontSize = playFontPx;
            }
            if (btnSpeed != null)
            {
                btnSpeed.Width = _isPipMode ? 38 : 62;
                btnSpeed.Height = _isPipMode ? 28 : 42;
                btnSpeed.FontSize = _isPipMode ? 11 : 17;
                btnSpeed.FontWeight = _isPipMode ? FontWeights.Normal : FontWeights.Bold;
            }
            if (btnSpeedFS != null)
            {
                btnSpeedFS.Width = 62;
                btnSpeedFS.Height = 42;
                btnSpeedFS.FontSize = 17;
                btnSpeedFS.FontWeight = FontWeights.Bold;
            }
        }

        private void UpdatePipClosePosition()
        {
            if (_isPipMode && popupPipClose != null && popupPipClose.IsOpen)
            {
                popupPipClose.HorizontalOffset = Math.Max(0, this.Width - 70);
                popupPipClose.VerticalOffset = 10;
                _popupUpdatePositionMethod?.Invoke(popupPipClose, null);
            }
        }

        private DateTime _lastEscPressTime = DateTime.MinValue;
        private double _abLoopA = -1;
        private double _abLoopB = -1;

        private void UpdateAbMarkers()
        {
            if (canvasAbMarkers == null && canvasAbMarkersFS == null) return;

            if (canvasAbMarkers != null) canvasAbMarkers.Children.Clear();
            if (canvasAbMarkersFS != null) canvasAbMarkersFS.Children.Clear();

            _abLoopA = -1;
            _abLoopB = -1;

            double dur = MpvGetDouble("duration");
            if (dur <= 0) return;

            string loopA = MpvGet("ab-loop-a");
            string loopB = MpvGet("ab-loop-b");

            double posA = -1;
            double posB = -1;

            if (double.TryParse(loopA, System.Globalization.CultureInfo.InvariantCulture, out double valA) && valA >= 0)
                posA = valA;
            if (double.TryParse(loopB, System.Globalization.CultureInfo.InvariantCulture, out double valB) && valB >= 0)
                posB = valB;

            if (posA >= 0 && posB > posA)
            {
                _abLoopA = posA;
                _abLoopB = posB;
            }

            DrawAbDotOnCanvas(canvasAbMarkers, posA, dur, "#00F0FF", "A");
            DrawAbDotOnCanvas(canvasAbMarkers, posB, dur, "#FFD700", "B");

            DrawAbDotOnCanvas(canvasAbMarkersFS, posA, dur, "#00F0FF", "A");
            DrawAbDotOnCanvas(canvasAbMarkersFS, posB, dur, "#FFD700", "B");
        }

        private void DrawAbDotOnCanvas(Canvas? canvas, double pos, double dur, string colorHex, string label)
        {
            if (canvas == null || pos < 0 || dur <= 0) return;
            double width = canvas.ActualWidth;
            if (width <= 0) width = sliderTimeline != null ? sliderTimeline.ActualWidth : 0;
            if (width <= 0) return;

            double ratio = Math.Clamp(pos / dur, 0.0, 1.0);
            double x = ratio * width;

            var border = new System.Windows.Controls.Border
            {
                Width = 8,
                Height = 8,
                CornerRadius = new CornerRadius(4),
                Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(colorHex)!,
                BorderBrush = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(1),
                ToolTip = $"AB Loop {label}: {Fmt(pos)}"
            };

            Canvas.SetLeft(border, Math.Max(0, x - 4));
            Canvas.SetTop(border, Math.Max(0, (canvas.ActualHeight > 0 ? canvas.ActualHeight : 14) / 2 - 4));
            canvas.Children.Add(border);
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            if (_isTogglingFullscreen) return;  // Skip redundant work during fullscreen transition
            UpdateResponsiveControlBar(sizeInfo.NewSize.Width);
            UpdatePipClosePosition();
            UpdateAbMarkers();
            if (!_isDraggingSize) UpdateSmartFill();
        }

        private const int WM_SIZING = 0x0214;
        private const int WM_GETMINMAXINFO = 0x0024;
        private const int WM_ENTERSIZEMOVE = 0x0231;
        private const int WM_EXITSIZEMOVE = 0x0232;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private bool GetSourceVideoDimensions(out double vw, out double vh)
        {
            vw = 0;
            vh = 0;
            if (_mpv == IntPtr.Zero) return false;

            // Read raw stream dimensions (independent of any active vf filter)
            double rawW = MpvGetDouble("video-params/w");
            double rawH = MpvGetDouble("video-params/h");
            if (rawW <= 0 || rawH <= 0)
            {
                rawW = MpvGetDouble("video-dec-params/w");
                rawH = MpvGetDouble("video-dec-params/h");
            }
            if (rawW <= 0 || rawH <= 0)
            {
                if (string.IsNullOrEmpty(_lastVfString))
                {
                    rawW = MpvGetDouble("width");
                    rawH = MpvGetDouble("height");
                }
            }

            if (rawW <= 0 || rawH <= 0) return false;

            double aspect = MpvGetDouble("video-params/aspect");
            double rotate = MpvGetDouble("video-params/rotate");

            // Handle 90/270 degree rotation metadata (e.g. smartphone recordings)
            if (Math.Abs(rotate - 90) < 1 || Math.Abs(rotate - 270) < 1)
            {
                double temp = rawW;
                rawW = rawH;
                rawH = temp;
                if (aspect > 0) aspect = 1.0 / aspect;
            }

            // Factor in Display Aspect Ratio (DAR) for anamorphic videos (e.g. 1440x1080 16:9 or DVD 720x576 16:9)
            if (aspect > 0.05 && Math.Abs((rawW / rawH) - aspect) > 0.02)
            {
                vw = Math.Round(rawH * aspect);
                vh = rawH;
            }
            else
            {
                vw = rawW;
                vh = rawH;
            }

            return vw > 0 && vh > 0;
        }

        private double GetTargetAspectRatio()
        {
            if (_isPipMode)
            {
                if (PlaylistManager.IsAudioFile(_currentPlayingFilePath)) return 16.0 / 9.0;
                return _pipAspectRatio > 0 ? _pipAspectRatio : (16.0 / 9.0);
            }

            // Normal window mode: pure audio defaults to 16:9 standard ratio
            if (PlaylistManager.IsAudioFile(_currentPlayingFilePath)) return 16.0 / 9.0;

            if (_currentAspectRatio == "4:3") return 4.0 / 3.0;
            if (_currentAspectRatio == "16:9") return 16.0 / 9.0;
            if (_currentAspectRatio == "16:10") return 16.0 / 10.0;
            if (_currentAspectRatio == "2.35:1") return 2.35;
            if (_currentAspectRatio == "stretch") return -1;

            if (GetSourceVideoDimensions(out double vw, out double vh))
            {
                double r = vw / vh;
                if (_isCurrentImage) return 16.0 / 9.0;
                if (r < 1.0) return Math.Max(r, 4.0 / 3.0);
                return r;
            }

            return 16.0 / 9.0;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_MOUSEACTIVATE)
            {
                if (!_isWindowActive || !this.IsActive || !IsCurrentAppActive())
                {
                    _lastActivatedByClickTick = Environment.TickCount64;
                    if (SettingsService.Instance.Config.ClickToActivateOnly)
                    {
                        _ignoreNextPlayPauseClick = true;
                    }
                }
            }

            if (msg == 0x001C /* WM_ACTIVATEAPP */ || msg == 0x0006 /* WM_ACTIVATE */)
            {
                bool isActivating = (msg == 0x001C && wParam != IntPtr.Zero) || (msg == 0x0006 && wParam != IntPtr.Zero);
                if (isActivating && !_isWindowActive)
                {
                    _lastActivatedByClickTick = Environment.TickCount64;
                }

                if (SettingsService.Instance.Config.AlwaysOnTop || _isPipMode)
                {
                    Dispatcher.BeginInvoke(new Action(RestoreTopmostState), System.Windows.Threading.DispatcherPriority.Normal);
                }
            }

            if (msg == WM_GETMINMAXINFO)
            {
                var mmi = System.Runtime.InteropServices.Marshal.PtrToStructure<MINMAXINFO>(lParam);
                IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
                if (monitor != IntPtr.Zero)
                {
                    MONITORINFO monitorInfo = new MONITORINFO();
                    monitorInfo.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(MONITORINFO));
                    if (GetMonitorInfo(monitor, ref monitorInfo))
                    {
                        RECT rcMonitor = monitorInfo.rcMonitor;
                        RECT rcWork = monitorInfo.rcWork;

                        if (_isFullscreen)
                        {
                            // True Fullscreen: Set max position and size to full physical monitor (covering taskbar 100%)
                            mmi.ptMaxPosition.X = 0;
                            mmi.ptMaxPosition.Y = 0;
                            mmi.ptMaxSize.X = rcMonitor.Right - rcMonitor.Left;
                            mmi.ptMaxSize.Y = rcMonitor.Bottom - rcMonitor.Top;
                            mmi.ptMaxTrackSize.X = rcMonitor.Right - rcMonitor.Left;
                            mmi.ptMaxTrackSize.Y = rcMonitor.Bottom - rcMonitor.Top;
                        }
                        else if (_isPipMode)
                        {
                            double ratio = GetTargetAspectRatio();
                            if (ratio <= 0) ratio = 16.0 / 9.0;
                            double controlH = 46.0;

                            double minPipW = 260.0;
                            double minPipH = Math.Round(minPipW / ratio + controlH);
                            double maxPipW = 854.0;
                            double maxPipH = Math.Round(maxPipW / ratio + controlH);

                            var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice ?? System.Windows.Media.Matrix.Identity;
                            mmi.ptMinTrackSize.X = (int)Math.Round(minPipW * transform.M11);
                            mmi.ptMinTrackSize.Y = (int)Math.Round(minPipH * transform.M22);
                            mmi.ptMaxTrackSize.X = (int)Math.Round(maxPipW * transform.M11);
                            mmi.ptMaxTrackSize.Y = (int)Math.Round(maxPipH * transform.M22);
                        }
                        else
                        {
                            // Windowed Mode Maximized: Respect Work Area (do not cover taskbar)
                            mmi.ptMaxPosition.X = rcWork.Left - rcMonitor.Left;
                            mmi.ptMaxPosition.Y = rcWork.Top - rcMonitor.Top;
                            mmi.ptMaxSize.X = rcWork.Right - rcWork.Left;
                            mmi.ptMaxSize.Y = rcWork.Bottom - rcWork.Top;

                            var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice ?? System.Windows.Media.Matrix.Identity;
                            mmi.ptMinTrackSize.X = (int)Math.Round(480.0 * transform.M11);
                            mmi.ptMinTrackSize.Y = (int)Math.Round(320.0 * transform.M22);
                        }
                    }
                }
                System.Runtime.InteropServices.Marshal.StructureToPtr(mmi, lParam, true);
                handled = true;
                return IntPtr.Zero;
            }

            if (msg == WM_ENTERSIZEMOVE)
            {
                _isDraggingSize = true;
                return IntPtr.Zero;
            }

            if (msg == WM_EXITSIZEMOVE && !_isFullscreen)
            {
                _isDraggingSize = false;
                UpdateResponsiveControlBar(this.ActualWidth > 0 ? this.ActualWidth : this.Width);
                UpdateSmartFill();
                if (!_hasMedia || (PlaylistManager.IsAudioFile(_currentPlayingFilePath) && !_currentMediaHasVideoTrack))
                {
                    _videoPanel?.Invalidate();
                }
                return IntPtr.Zero;
            }

            if (msg == WM_SIZING && !_isFullscreen)
            {
                double ratio = GetTargetAspectRatio();
                if (ratio > 0)
                {
                    RECT rect = System.Runtime.InteropServices.Marshal.PtrToStructure<RECT>(lParam);
                    
                    int w = rect.Right - rect.Left;
                    int h = rect.Bottom - rect.Top;
                    double nonVideoH = _isPipMode ? 46.0 : ((rowTitle?.ActualHeight ?? 40) + (rowControls?.ActualHeight ?? 105));
                    if (nonVideoH <= 0) nonVideoH = _isPipMode ? 46.0 : 145.0;

                    double minW = _isPipMode ? 260.0 : 480.0;
                    double maxW = _isPipMode ? 854.0 : (SystemParameters.WorkArea.Width * 0.95);

                    int edge = wParam.ToInt32();
                    // 1: WMSZ_LEFT, 2: WMSZ_RIGHT, 3: WMSZ_TOP, 4: WMSZ_TOPLEFT, 5: WMSZ_TOPRIGHT
                    // 6: WMSZ_BOTTOM, 7: WMSZ_BOTTOMLEFT, 8: WMSZ_BOTTOMRIGHT

                    if (edge == 3 || edge == 6) // Pure vertical drag (Top or Bottom border): adjust width according to dragged height
                    {
                        double minH = Math.Round(minW / ratio + nonVideoH);
                        double maxH = Math.Round(maxW / ratio + nonVideoH);
                        h = (int)Math.Max(minH, Math.Min(maxH, h));

                        if (edge == 3)
                            rect.Top = rect.Bottom - h;
                        else
                            rect.Bottom = rect.Top + h;

                        double videoH = Math.Max(30, h - nonVideoH);
                        int targetW = (int)Math.Round(videoH * ratio);
                        targetW = (int)Math.Max(minW, Math.Min(maxW, targetW));
                        rect.Right = rect.Left + targetW;
                    }
                    else // Corner or horizontal drag: adjust height according to width
                    {
                        w = (int)Math.Max(minW, Math.Min(maxW, w));
                        if (edge == 1 || edge == 4 || edge == 7) // Left edge dragged
                        {
                            rect.Left = rect.Right - w;
                        }
                        else
                        {
                            rect.Right = rect.Left + w;
                        }

                        double videoH = w / ratio;
                        int targetH = (int)Math.Round(videoH + nonVideoH);

                        var workArea = SystemParameters.WorkArea;
                        if (targetH > workArea.Height - 40 && !_isPipMode)
                        {
                            targetH = (int)(workArea.Height - 40);
                            int clampedW = (int)Math.Round((targetH - nonVideoH) * ratio);
                            if (clampedW >= minW)
                            {
                                rect.Right = rect.Left + clampedW;
                            }
                        }

                        if (edge == 3 || edge == 4 || edge == 5) // WMSZ_TOP, WMSZ_TOPLEFT, WMSZ_TOPRIGHT
                        {
                            rect.Top = rect.Bottom - targetH;
                        }
                        else
                        {
                            rect.Bottom = rect.Top + targetH;
                        }
                    }

                    System.Runtime.InteropServices.Marshal.StructureToPtr(rect, lParam, true);
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        // ── WinForms Double-Buffered Panel & Background Cache ─────────────
        private System.Drawing.Bitmap? _cachedIdleBgBitmap = null;
        private string? _cachedIdleBgPath = null;

        private void InvalidateIdleBgCache()
        {
            try
            {
                _cachedIdleBgBitmap?.Dispose();
                _cachedIdleBgBitmap = null;
                _cachedIdleBgPath = null;
            }
            catch { }
        }

        // ── Window Loaded ─────────────────────────────────────────────────
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // WinForms double-buffered panel hosts the MPV HWND (flicker-free GDI+ rendering)
            _videoPanel = new DoubleBufferedPanel
            {
                BackColor  = System.Drawing.Color.FromArgb(22, 32, 50),
                Dock       = System.Windows.Forms.DockStyle.Fill,
                AllowDrop  = true
            };
            _videoPanel.Paint     += VideoPanel_Paint;
            _videoPanel.Resize    += (s, ev) => { if (!_isTogglingFullscreen && !_isDraggingSize) UpdateSmartFill(); };
            ThemeService.Instance.PropertyChanged += (s, ev) =>
            {
                InvalidateIdleBgCache();
                if (!_hasMedia) _videoPanel?.Invalidate();
            };
            _videoPanel.PreviewKeyDown += (s, ev) =>
            {
                ev.IsInputKey = true;
            };
            _videoPanel.KeyDown += (s, ev) =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (popupSettings.IsOpen || popupLibrary.IsOpen || popupOpenUrl?.IsOpen == true) return;
                    var wpfKey = System.Windows.Input.KeyInterop.KeyFromVirtualKey((int)ev.KeyCode);
                    var modifiers = System.Windows.Input.ModifierKeys.None;
                    if (ev.Control) modifiers |= System.Windows.Input.ModifierKeys.Control;
                    if (ev.Alt) modifiers |= System.Windows.Input.ModifierKeys.Alt;
                    if (ev.Shift) modifiers |= System.Windows.Input.ModifierKeys.Shift;
                    ExecutePlayerHotkey(wpfKey, modifiers);
                });
            };
            
            // Allow drag and drop on the video panel
            _videoPanel.DragEnter += (s, ev) =>
            {
                if (ev.Data?.GetDataPresent(System.Windows.Forms.DataFormats.FileDrop) == true)
                    ev.Effect = System.Windows.Forms.DragDropEffects.Copy;
            };
            _videoPanel.DragDrop += (s, ev) =>
            {
                if (ev.Data?.GetData(System.Windows.Forms.DataFormats.FileDrop) is string[] files && files.Length > 0)
                {
                    // Use RequestPlayFile to deduplicate concurrent Pipe + WM_DROPFILES triggers
                    Dispatcher.Invoke(() => HandleDropPaths(files, fromDrop: true));
                }
            };

            lbPlaylist.ItemsSource = PlaylistManager.Instance.Items;

            UpdatePlaylistCountText();
            PlaylistManager.Instance.Items.CollectionChanged += (s, ev) => UpdatePlaylistCountText();
            I18nService.Instance.PropertyChanged += (s, ev) => UpdatePlaylistCountText();

            overlayLibrary.CloseRequested += (s, ev) => { CloseLibraryOverlay(); };
            overlayLibrary.PlayRequested += (s, playlistName) => 
            {
                CloseLibraryOverlay();
                _ = PlaylistManager.Instance.LoadPlaylistAsync(playlistName, null, onPlayTarget: (targetPath) =>
                {
                    PlayFile(targetPath);
                });
            };
            overlayLibrary.PlaySpecificFileRequested += (playlistName, filePath) =>
            {
                CloseLibraryOverlay();
                if (IsShiftKeyDown() && File.Exists(filePath))
                {
                    HandleDropPaths(new[] { filePath });
                }
                else
                {
                    _ = PlaylistManager.Instance.LoadPlaylistAsync(playlistName, filePath, onPlayTarget: (targetPath) =>
                    {
                        PlayFile(targetPath);
                    });
                }
            };

            overlaySettings.Closed += (s, ev) => { CloseSettingsOverlay(); };
            overlaySettings.OpenLibraryRequested += (s, ev) =>
            {
                CloseSettingsOverlay();
                ShowLibraryOverlay();
            };
            overlaySettings.SponsorRequested += (s, ev) =>
            {
                CloseSettingsOverlay();
                ShowSponsorOverlay();
            };
            if (overlaySponsor != null)
            {
                overlaySponsor.CloseRequested += (s, ev) =>
                {
                    CloseSponsorOverlay();
                    ShowSettingsOverlay(selectAbout: true);
                };
            }
            if (overlayTracks != null)
            {
                overlayTracks.Closed += (s, ev) => { popupTracks.IsOpen = false; EnsureMainCanvasFocusAndDisableIme(); };
                overlayTracks.SubTrackSelected += (s, id) => SelectSubTrack(id);
                overlayTracks.AudioTrackSelected += (s, id) => SelectAudioTrack(id);
                overlayTracks.SubDelayChanged += (s, delay) => SetSubDelay(delay);
                overlayTracks.AudioDelayChanged += (s, delay) => SetAudioDelay(delay);
                overlayTracks.SubPosChanged += (s, pos) => SetSubPos(pos);
                overlayTracks.LoadExternalSubRequested += (s, ev) => OpenExternalSubDialog();
                overlayTracks.LoadExternalAudioRequested += (s, ev) => OpenExternalAudioDialog();
                overlayTracks.NightModeToggled += (s, enabled) => ApplyAudioNightMode(enabled);
            }
            if (overlayOpenUrl != null)
            {
                overlayOpenUrl.Closed += (s, ev) => CloseOpenUrlOverlay();
                overlayOpenUrl.PlayRequested += (s, args) =>
                {
                    CloseOpenUrlOverlay();
                    if (!string.IsNullOrWhiteSpace(args?.Url))
                    {
                        string targetUrl = args.Url.Trim();
                        _explicitSaveStreamRequested = args.SaveToLocal;
                        _explicitSaveStreamDir = args.SaveDir;
                        _explicitSaveStreamUrl = targetUrl;
                        PlayFileWithTransition(targetUrl);
                    }
                };
            }
            if (popupTracks != null) popupTracks.Closed += (s, ev) => EnsureMainCanvasFocusAndDisableIme();
            if (popupOpenUrl != null) popupOpenUrl.Closed += (s, ev) => { ApplyBaseBrightness(); EnsureMainCanvasFocusAndDisableIme(); };
            if (popupSponsor != null) popupSponsor.Closed += (s, ev) => { ApplyBaseBrightness(); EnsureMainCanvasFocusAndDisableIme(); };
            popupSettings.Closed += (s, ev) => EnsureMainCanvasFocusAndDisableIme();
            popupLibrary.Closed += (s, ev) => EnsureMainCanvasFocusAndDisableIme();
            if (popupSideDrawer != null) popupSideDrawer.Closed += (s, ev) => EnsureMainCanvasFocusAndDisableIme();
            if (this.Resources["VideoContextMenu"] is System.Windows.Controls.ContextMenu ctxMenuRes)
            {
                ctxMenuRes.Closed += (s, ev) => EnsureMainCanvasFocusAndDisableIme();
            }
            if (btnOpen.ContextMenu != null) btnOpen.ContextMenu.Closed += (s, ev) => EnsureMainCanvasFocusAndDisableIme();
            if (btnOpenFS.ContextMenu != null) btnOpenFS.ContextMenu.Closed += (s, ev) => EnsureMainCanvasFocusAndDisableIme();

            overlaySettings.SettingsSaved += (s, ev) =>
            {
                var cfg = SettingsService.Instance.Config;
                RestoreTopmostState();
                I18nService.Instance.ChangeLanguage(cfg.Language);
                ThemeService.Instance.CurrentThemeKey = cfg.Theme;
                ThemeService.Instance.ActiveSkinKey = cfg.ActiveSkin;
                ThemeService.Instance.ApplyActiveSkinOrTheme();
                UpdateThemeBackgrounds();
                UpdateSmartFillUI();
                UpdateHardwareDecodingMode();
                UpdateControlBarTooltips();

                // Force refresh SmartFill pipeline because changing hwdec or saving settings causes MPV to reset vf
                _lastVfString = "";
                UpdateSmartFill();
            };

            // Apply loaded settings on startup
            var startCfg = SettingsService.Instance.Config;
            this.Topmost = startCfg.AlwaysOnTop;
            I18nService.Instance.ChangeLanguage(startCfg.Language);
            ThemeService.Instance.CurrentThemeKey = startCfg.Theme;
            ThemeService.Instance.ActiveSkinKey = startCfg.ActiveSkin;
            _smartFillMode = "none";
            _autoCropMode = "none";
            UpdateSmartFillUI();
            UpdateAutoCropUI();

            // Initialize Sort Menu checkmarks
            if (startCfg.PlaylistSortMode != -1 && Enum.IsDefined(typeof(PlaylistSortOption), startCfg.PlaylistSortMode))
            {
                var option = (PlaylistSortOption)startCfg.PlaylistSortMode;
                menuSortNameAsc.IsChecked = option == PlaylistSortOption.NameAscending;
                menuSortNameDesc.IsChecked = option == PlaylistSortOption.NameDescending;
                menuSortDateAsc.IsChecked = option == PlaylistSortOption.DateAscending;
                menuSortDateDesc.IsChecked = option == PlaylistSortOption.DateDescending;
            }

            UpdateOuterBorder();
            this.LocationChanged += (s, e) => UpdateIdleHintOverlay();
            this.SizeChanged += (s, e) => UpdateIdleHintOverlay();
            ThemeService.Instance.PropertyChanged += (s, ev) =>
            {
                UpdateThemeBackgrounds();
                UpdateOuterBorder();
                UpdateSkinOrSlideshowBgm();
                UpdateSkinIdleVideo();
                UpdateIdleHintOverlay();
            };
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
            {
                UpdateSkinOrSlideshowBgm();
                UpdateSkinIdleVideo();
                UpdateIdleHintOverlay();
            }));

            // Click or Drag video to play/pause/drag window or double click for fullscreen
            _videoPanel.MouseDown += (s, ev) =>
            {
                if (ev.Button == System.Windows.Forms.MouseButtons.Left)
                {
                    long now = Environment.TickCount64;
                    bool activatedRecently = (now - _lastActivatedByClickTick < 800);
                    bool wasInactive = !_isWindowActive || !this.IsActive || !IsCurrentAppActive() || activatedRecently || _ignoreNextPlayPauseClick;
                    if (wasInactive && SettingsService.Instance.Config.ClickToActivateOnly)
                    {
                        _ignoreNextPlayPauseClick = true;
                    }

                    Dispatcher.Invoke(() => EnsureMainCanvasFocusAndDisableIme());
                    if (!_isFullscreen)
                    {
                        _videoPanelIsMouseDown = true;
                        _videoPanelIsDraggingWindow = false;
                        _videoPanelMouseDownPos = System.Windows.Forms.Cursor.Position;
                    }
                }
            };

            _videoPanel.MouseMove += (s, ev) =>
            {
                if (_videoPanelIsMouseDown && !_videoPanelIsDraggingWindow && !_isFullscreen)
                {
                    var currentPos = System.Windows.Forms.Cursor.Position;
                    int dx = Math.Abs(currentPos.X - _videoPanelMouseDownPos.X);
                    int dy = Math.Abs(currentPos.Y - _videoPanelMouseDownPos.Y);

                    if (dx > 4 || dy > 4)
                    {
                        _videoPanelIsDraggingWindow = true;
                        _ignoreNextPlayPauseClick = false;
                        Dispatcher.Invoke(() =>
                        {
                            _clickTimer.Stop(); // Cancel single-click play/pause on drag
                            IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                            if (hwnd != IntPtr.Zero)
                            {
                                ReleaseCapture();
                                SendMessage(hwnd, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
                            }
                        });
                    }
                }
            };

            _videoPanel.MouseUp += (s, ev) =>
            {
                if (ev.Button == System.Windows.Forms.MouseButtons.Left)
                {
                    _videoPanelIsMouseDown = false;
                }
            };

            _videoPanel.MouseClick += (s, ev) =>
            {
                if (ev.Button == System.Windows.Forms.MouseButtons.Left)
                {
                    long now = Environment.TickCount64;
                    bool activatedRecently = (now - _lastActivatedByClickTick < 800);
                    if (SettingsService.Instance.Config.ClickToActivateOnly && (_ignoreNextPlayPauseClick || activatedRecently))
                    {
                        _ignoreNextPlayPauseClick = false;
                        _lastActivatedByClickTick = 0;
                        return; // Consume the click: Window is activated without toggling play/pause!
                    }
                    _ignoreNextPlayPauseClick = false;
                    if (_videoPanelIsDraggingWindow)
                    {
                        _videoPanelIsDraggingWindow = false;
                        return; // Ignore play/pause single-click if user was dragging window!
                    }
                    if (!_isSkinIdleVideoPlaying && (string.IsNullOrEmpty(_currentPlayingFilePath) || _mpv == IntPtr.Zero))
                    {
                        return; // Ignore single-click on blank home screen to avoid delayed pause when subsequently opening/pasting files
                    }
                    if (PlaylistManager.IsAudioFile(_currentPlayingFilePath))
                    {
                        return;
                    }
                    Dispatcher.Invoke(() => _clickTimer.Start());
                }
                else if (ev.Button == System.Windows.Forms.MouseButtons.Right)
                {
                    Dispatcher.Invoke(() =>
                    {
                        var menu = this.Resources["VideoContextMenu"] as System.Windows.Controls.ContextMenu;
                        if (menu != null)
                        {
                            ApplyThemeToMenu(menu);
                            menu.PlacementTarget = this;
                            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
                            menu.IsOpen = true;
                        }
                    });
                }
            };
            _videoPanel.MouseDoubleClick += (s, ev) =>
            {
                if (ev.Button == System.Windows.Forms.MouseButtons.Left && !_isPipMode)
                {
                    _ignoreNextPlayPauseClick = false;
                    Dispatcher.Invoke(() =>
                    {
                        _clickTimer.Stop();
                        ToggleFullscreen();
                    });
                }
            };

            // Removed _videoPanel.MouseMove because mpv HWND intercepts it.
            // We use global mouse polling in Timer_Tick instead.

            // Middle mouse scroll on video = volume control
            // WinForms host intercepts wheel before WPF, so bind here too
            _videoPanel.MouseWheel += (s, ev) =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (_mpv == IntPtr.Zero) return;
                    AdjustVolume(ev.Delta > 0);
                });
            };

            videoHost.Child = _videoPanel;

            // Init MPV
            if (!InitMpv()) return;

            // Polling timer – 200 ms
            _timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            _timer.Tick += Timer_Tick;
            _timer.Start();

            // Init Theme & Settings from config
            ThemeService.Instance.ApplyActiveSkinOrTheme();
            LoadSettingsFromConfig();
            UpdateThemeBackgrounds();

            // Named-pipe server for single-instance IPC
            StartPipeServer();

            // Self-test
            // Wait until panel is ready before trying to init mpv
            if (_videoPanel.ClientSize.Width > 0 && _videoPanel.ClientSize.Height > 0)
            {
                _lastVideoPanelSize = _videoPanel.ClientSize;
            }
            this.LocationChanged += (s, e) => UpdatePopupPlacements();
            this.SizeChanged += (s, e) => UpdatePopupPlacements();
            this.StateChanged += (s, e) =>
            {
                if (this.WindowState == WindowState.Minimized)
                {
                    if (popupOsd != null && popupOsd.IsOpen)
                    {
                        popupOsd.IsOpen = false;
                    }
                }
                UpdatePopupPlacements();
            };
            this.Deactivated += (s, e) =>
            {
                _isWindowActive = false;
                if (popupOsd != null && popupOsd.IsOpen)
                {
                    popupOsd.IsOpen = false;
                }
            };
            this.Activated += (s, e) =>
            {
                _isWindowActive = true;
                if (_isNetworkBuffering && !string.IsNullOrEmpty(_cachedBufferingText))
                {
                    ShowBufferingOsd(_cachedBufferingText);
                }
            };
            if (popupOsd != null)
            {
                popupOsd.Opened += (s, e) => EnsurePopupZOrder(popupOsd);
            }
            this.ContentRendered += Window_ContentRendered;
        }

        private bool _hasContentRendered = false;
        private void Window_ContentRendered(object? sender, EventArgs e)
        {
            if (_hasContentRendered) return;
            _hasContentRendered = true;

            if (_selfTest) { RunSelfTestAsync(); return; }
            if (_perfDebug) { RunPerfDebugAsync(); return; }

            // Open file from command-line args when UI is fully painted and sized (fixes blank screen)
            if (App.StartArgs.Length > 0)
            {
                string argPath = App.ParseFilePathFromArgs(App.StartArgs);
                if (File.Exists(argPath) || Directory.Exists(argPath))
                {
                    bool isShift = App.WasShiftHeldOnLaunch || IsShiftKeyDown();
                    App.WasShiftHeldOnLaunch = false;
                    RequestPlayFile(argPath, isShift);
                }
            }
            ForceForeground();
        }

        private void UpdatePopupPlacements()
        {
            // Skip all Popup repositioning while fullscreen toggle is in progress
            if (_isTogglingFullscreen) return;
            if (_isPipMode && popupPipClose != null && popupPipClose.IsOpen)
            {
                UpdatePipClosePosition();
            }
            if (popupSideDrawer != null && popupSideDrawer.IsOpen && popupSideDrawer.Child != null)
            {
                try
                {
                    var source = System.Windows.PresentationSource.FromVisual(popupSideDrawer.Child) as System.Windows.Interop.HwndSource;
                    if (source != null && source.Handle != IntPtr.Zero)
                    {
                        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
                        System.Windows.Point screenPos = mainGrid.PointToScreen(new System.Windows.Point(0, 0));

                        int x = (int)(screenPos.X + (mainGrid.ActualWidth - 450) * dpi.DpiScaleX);
                        int y = (int)(screenPos.Y);
                        int w = (int)(450 * dpi.DpiScaleX);
                        int h = (int)(mainGrid.ActualHeight * dpi.DpiScaleY);

                        SetWindowPos(source.Handle, HWND_TOP, x, y, w, h, SWP_NOACTIVATE);
                    }
                    else
                    {
                        _popupUpdatePositionMethod?.Invoke(popupSideDrawer, null);
                    }
                }
                catch
                {
                    _popupUpdatePositionMethod?.Invoke(popupSideDrawer, null);
                }
            }
            if (popupTop != null && popupTop.IsOpen && popupTop.Child != null)
            {
                try
                {
                    double w = mainGrid.ActualWidth > 0 ? mainGrid.ActualWidth : ActualWidth;
                    if (w > 0)
                    {
                        popupTop.Width = w;
                        if (fsTopBar != null) fsTopBar.Width = w;
                    }
                    _popupUpdatePositionMethod?.Invoke(popupTop, null);
                }
                catch { }
            }
            if (popupBottom != null && popupBottom.IsOpen && popupBottom.Child != null)
            {
                try
                {
                    double w = mainGrid.ActualWidth > 0 ? mainGrid.ActualWidth : ActualWidth;
                    if (w > 0)
                    {
                        popupBottom.Width = w;
                        if (fsBottomBar != null) fsBottomBar.Width = w;
                    }
                    _popupUpdatePositionMethod?.Invoke(popupBottom, null);
                }
                catch { }
            }
            if (popupLibrary != null && popupLibrary.IsOpen)
            {
                UpdateLibraryPopupSize();
                _popupUpdatePositionMethod?.Invoke(popupLibrary, null);
            }
            if (popupSettings != null && popupSettings.IsOpen)
            {
                UpdateSettingsPopupSize();
                _popupUpdatePositionMethod?.Invoke(popupSettings, null);
            }
            if (popupOpenUrl != null && popupOpenUrl.IsOpen)
            {
                UpdateOpenUrlPopupSize();
                _popupUpdatePositionMethod?.Invoke(popupOpenUrl, null);
            }
            if (popupSponsor != null && popupSponsor.IsOpen)
            {
                UpdateSponsorPopupSize();
                _popupUpdatePositionMethod?.Invoke(popupSponsor, null);
            }
            if (popupAudioBanner != null && popupAudioBanner.IsOpen)
            {
                UpdateAudioBannerView();
                _popupUpdatePositionMethod?.Invoke(popupAudioBanner, null);
            }
            if (popupOsd != null && popupOsd.IsOpen)
            {
                _popupUpdatePositionMethod?.Invoke(popupOsd, null);
            }
        }

        private bool _isWindowActive = true;

        private void EnsurePopupZOrder(System.Windows.Controls.Primitives.Popup? popup)
        {
            try
            {
                if (popup?.Child == null) return;
                var source = System.Windows.PresentationSource.FromVisual(popup.Child) as System.Windows.Interop.HwndSource;
                if (source != null && source.Handle != IntPtr.Zero)
                {
                    IntPtr popupHwnd = source.Handle;
                    IntPtr mainHwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                    if (mainHwnd != IntPtr.Zero)
                    {
                        bool shouldBeTopmost = _isFullscreen || _isPipMode || SettingsService.Instance.Config.AlwaysOnTop;
                        if (_isFullscreen && !SettingsService.Instance.Config.AlwaysOnTop && !IsCurrentAppActive())
                        {
                            shouldBeTopmost = false;
                        }

                        // 1. Set owner HWND to MainWindow so Windows manages Z-order and minimizes popup automatically with MainWindow
                        SetWindowLongPtr(popupHwnd, GWLP_HWNDPARENT, mainHwnd);

                        // 2. Align popup WS_EX_TOPMOST with main window without demoting main window
                        long exStyle = GetWindowLongPtr(popupHwnd, GWL_EXSTYLE).ToInt64();
                        if (shouldBeTopmost)
                        {
                            if ((exStyle & WS_EX_TOPMOST) == 0)
                            {
                                SetWindowLongPtr(popupHwnd, GWL_EXSTYLE, new IntPtr(exStyle | WS_EX_TOPMOST));
                            }
                            SetWindowPos(popupHwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_FRAMECHANGED);
                        }
                        else
                        {
                            if ((exStyle & WS_EX_TOPMOST) != 0)
                            {
                                SetWindowLongPtr(popupHwnd, GWL_EXSTYLE, new IntPtr(exStyle & ~WS_EX_TOPMOST));
                            }
                            SetWindowPos(popupHwnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_FRAMECHANGED);
                        }

                        // 3. Always ensure MainWindow maintains its desired topmost state
                        RestoreTopmostState();
                    }
                }
            }
            catch { }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int ShowCursor(bool bShow);

        private bool _isCursorHiddenSafe = false;

        private void HideCursorSafe()
        {
            if (!_isCursorHiddenSafe)
            {
                _isCursorHiddenSafe = true;
                ShowCursor(false);
            }
        }

        private void ShowCursorSafe()
        {
            if (_isCursorHiddenSafe)
            {
                _isCursorHiddenSafe = false;
                ShowCursor(true);
            }
        }

        private bool _isTopBarOpen = false;
        private bool _isBottomBarOpen = false;

        private void AnimateTopBar(bool open)
        {
            if (transTopBar == null || popupTop == null) return;
            if (_isTopBarOpen == open) return;
            _isTopBarOpen = open;

            if (open)
            {
                double targetW = mainGrid.ActualWidth > 0 ? mainGrid.ActualWidth : ActualWidth;
                popupTop.Width = targetW;
                if (fsTopBar != null) fsTopBar.Width = targetW;

                popupTop.IsOpen = true;
                popupTop.UpdateLayout();
                UpdatePopupPlacements();
                var da = new DoubleAnimation
                {
                    To = 0,
                    Duration = TimeSpan.FromMilliseconds(300),
                    EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
                };
                transTopBar.BeginAnimation(TranslateTransform.YProperty, da);
            }
            else
            {
                var da = new DoubleAnimation
                {
                    To = -50,
                    Duration = TimeSpan.FromMilliseconds(300),
                    EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseIn }
                };
                da.Completed += (s, e) =>
                {
                    if (!_isTopBarOpen && popupTop != null)
                    {
                        popupTop.IsOpen = false;
                    }
                };
                transTopBar.BeginAnimation(TranslateTransform.YProperty, da);
            }
        }

        private void AnimateBottomBar(bool open)
        {
            if (transBottomBar == null || popupBottom == null) return;
            if (_isBottomBarOpen == open) return;
            _isBottomBarOpen = open;

            if (open)
            {
                double targetW = mainGrid.ActualWidth > 0 ? mainGrid.ActualWidth : ActualWidth;
                popupBottom.Width = targetW;
                if (fsBottomBar != null) fsBottomBar.Width = targetW;
                UpdateResponsiveControlBar(targetW);

                popupBottom.IsOpen = true;
                popupBottom.UpdateLayout();
                UpdatePopupPlacements();
                var da = new DoubleAnimation
                {
                    To = 0,
                    Duration = TimeSpan.FromMilliseconds(300),
                    EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
                };
                transBottomBar.BeginAnimation(TranslateTransform.YProperty, da);
            }
            else
            {
                var da = new DoubleAnimation
                {
                    To = 125,
                    Duration = TimeSpan.FromMilliseconds(300),
                    EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseIn }
                };
                da.Completed += (s, e) =>
                {
                    if (!_isBottomBarOpen && popupBottom != null)
                    {
                        popupBottom.IsOpen = false;
                    }
                };
                transBottomBar.BeginAnimation(TranslateTransform.YProperty, da);
            }
        }

        private void SnapTopBar(bool open)
        {
            if (transTopBar == null || popupTop == null) return;
            _isTopBarOpen = open;
            transTopBar.BeginAnimation(TranslateTransform.YProperty, null);
            transTopBar.Y = open ? 0 : -50;
            popupTop.IsOpen = open;
            if (open) UpdatePopupPlacements();
        }

        private void SnapBottomBar(bool open)
        {
            if (transBottomBar == null || popupBottom == null) return;
            _isBottomBarOpen = open;
            transBottomBar.BeginAnimation(TranslateTransform.YProperty, null);
            transBottomBar.Y = open ? 0 : 125;
            popupBottom.IsOpen = open;
            if (open) UpdatePopupPlacements();
        }

        // ── Fullscreen UI Auto-Hide Logic ─────────────────────────────────
        private void MousePollTimer_Tick(object? sender, EventArgs e)
        {
            if (!_isFullscreen) return;

            // When window is not active / focused, do not trigger hover zones
            if (!IsCurrentAppActive())
            {
                AnimateTopBar(false);
                AnimateBottomBar(false);
                return;
            }

            var currentPos = System.Windows.Forms.Cursor.Position;
            bool mouseMoved = currentPos != _lastMousePos;

            if (mouseMoved)
            {
                _lastMousePos = currentPos;
                _lastMouseMoveTime = DateTime.Now;
                ShowCursorSafe();
            }

            try
            {
                // Check if VideoContextMenu or any modal overlay dialog is open
                bool isCtxMenuOpen = (this.Resources["VideoContextMenu"] is System.Windows.Controls.ContextMenu ctx && ctx.IsOpen);
                bool isAnyModalOpen = (popupLibrary != null && popupLibrary.IsOpen) || 
                                      (popupSettings != null && popupSettings.IsOpen) || 
                                      (popupOpenUrl != null && popupOpenUrl.IsOpen) ||
                                      (popupTracks != null && popupTracks.IsOpen) ||
                                      (popupSponsor != null && popupSponsor.IsOpen);

                if (isCtxMenuOpen || isAnyModalOpen)
                {
                    // While right-click context menu or modal dialog is open, keep cursor visible and do NOT pop up top/bottom bars!
                    ShowCursorSafe();
                    return;
                }

                var winPos = PointFromScreen(new System.Windows.Point(_lastMousePos.X, _lastMousePos.Y));
                double h = ActualHeight;
                bool inTopZone = winPos.Y <= 80;
                bool inBottomZone = winPos.Y >= h - 120;
                
                // Check if user is interacting with controls/menus/popups belonging to top/bottom bars
                bool isPopupActive = IsMouseNearPopup(popupVolumeFS, 8) ||
                                     IsMouseNearPopup(popupBrightnessFS, 8) ||
                                     IsMouseNearPopup(popupVolume, 8) ||
                                     IsMouseNearPopup(popupBrightness, 8);

                if (!isPopupActive)
                {
                    CloseInactiveHoverPopups();
                }

                bool isTopBarControlActive = (btnOpenFS?.ContextMenu?.IsOpen == true);
                bool isBottomBarControlActive = (btnAutoCropFS?.ContextMenu?.IsOpen == true) ||
                                                (btnSpeedFS?.ContextMenu?.IsOpen == true) ||
                                                isPopupActive;

                if (isPopupActive)
                {
                    _keepUiAliveUntil = DateTime.Now.AddSeconds(1.5);
                }

                bool keepAlive = DateTime.Now < _keepUiAliveUntil;
                bool shouldShowBars = inTopZone || inBottomZone || keepAlive || isTopBarControlActive || isBottomBarControlActive;

                AnimateTopBar(shouldShowBars);
                AnimateBottomBar(shouldShowBars);

                // Hide cursor if idle for 1s AND NOT in hot zones / showing bars
                if (!mouseMoved && (DateTime.Now - _lastMouseMoveTime).TotalMilliseconds > 1000)
                {
                    if (!shouldShowBars)
                    {
                        HideCursorSafe();
                    }
                }
            }
            catch { }
        }

        private void WakeFullscreenUI()
        {
            if (!_isFullscreen) return;
            ShowCursorSafe();
            _keepUiAliveUntil = DateTime.Now.AddSeconds(2);
            AnimateTopBar(true);
            AnimateBottomBar(true);
        }

        private void Window_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            // Hover zones handled entirely by MousePollTimer_Tick now
        }

        // ── MPV Init ──────────────────────────────────────────────────────
        private bool InitMpv()
        {
            _mpv = MpvNative.mpv_create();
            if (_mpv == IntPtr.Zero)
            {
                WpfMsgBox.Show(
                    I18nService.Instance["ErrMpvInit"],
                    I18nService.Instance["AppTitle"], System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                WpfApp.Current.Shutdown(1);
                return false;
            }

            MpvNative.mpv_set_option_string(_mpv, "wid",        _videoPanel.Handle.ToString());
            MpvNative.mpv_set_option_string(_mpv, "vo",         "gpu");
            MpvNative.mpv_set_option_string(_mpv, "gpu-api",    "d3d11");
            MpvNative.mpv_set_option_string(_mpv, "gpu-context", "d3d11");
            MpvNative.mpv_set_option_string(_mpv, "d3d11-exclusive-fs", "no");
            MpvNative.mpv_set_option_string(_mpv, "d3d11-flip", "yes");
            MpvNative.mpv_set_option_string(_mpv, "demuxer-thread", "yes");
            MpvNative.mpv_set_option_string(_mpv, "input-default-bindings", "no"); // Disable mpv internal key/mouse bindings
            MpvNative.mpv_set_option_string(_mpv, "input-cursor", "no");           // Disable mpv mouse tracking
            MpvNative.mpv_set_option_string(_mpv, "input-vo-keyboard", "no");     // Allow keys to bubble up to WinForms
            MpvNative.mpv_set_option_string(_mpv, "osd-level",  "0");   // hide MPV OSD
            MpvNative.mpv_set_option_string(_mpv, "osd-bar",    "no");  // hide seek bar
            MpvNative.mpv_set_option_string(_mpv, "hwdec",      "auto-copy");
            MpvNative.mpv_set_option_string(_mpv, "volume",     "80");
            MpvNative.mpv_set_option_string(_mpv, "volume-max", "200");
            MpvNative.mpv_set_option_string(_mpv, "audio-buffer", "0.25");
            MpvNative.mpv_set_option_string(_mpv, "keep-open",  "yes"); // don't close on EOF
            MpvNative.mpv_set_option_string(_mpv, "working-directory", AppDomain.CurrentDomain.BaseDirectory);
            MpvNative.mpv_set_option_string(_mpv, "hr-seek",    "always"); // exact high-resolution seeking
            MpvNative.mpv_set_option_string(_mpv, "cursor-autohide", "1000");
            MpvNative.mpv_set_option_string(_mpv, "cursor-autohide-fs-only", "yes");

            // Adaptive High-Efficiency RAM Caching & I/O Tuning
            // Default to lean memory profile for instant local playback, automatically expanded for network URLs
            MpvNative.mpv_set_option_string(_mpv, "cache", "yes");
            MpvNative.mpv_set_option_string(_mpv, "cache-on-disk", "no");
            MpvNative.mpv_set_option_string(_mpv, "cache-secs", "20");
            MpvNative.mpv_set_option_string(_mpv, "demuxer-readahead-secs", "20");
            MpvNative.mpv_set_option_string(_mpv, "demuxer-max-bytes", "50331648"); // 48 MiB max RAM cache for local files
            MpvNative.mpv_set_option_string(_mpv, "demuxer-max-back-bytes", "25165824"); // 24 MiB back cache
            MpvNative.mpv_set_option_string(_mpv, "demuxer-seekable-cache", "yes");
            MpvNative.mpv_set_option_string(_mpv, "demuxer-hysteresis-secs", "5");
            MpvNative.mpv_set_option_string(_mpv, "stream-buffer-size", "2048k");
            MpvNative.mpv_set_option_string(_mpv, "cache-pause", "yes");
            MpvNative.mpv_set_option_string(_mpv, "cache-pause-wait", "0.5");
            MpvNative.mpv_set_option_string(_mpv, "network-timeout", "30");
            MpvNative.mpv_set_option_string(_mpv, "force-seekable", "yes");
            MpvNative.mpv_set_option_string(_mpv, "demuxer-lavf-o", "reconnect=1,reconnect_streamed=1,reconnect_delay_max=5");
            MpvNative.mpv_set_option_string(_mpv, "user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");

            MpvNative.mpv_initialize(_mpv);

            ApplyVideoSharpening(SettingsService.Instance.Config.VideoSharpening);
            UpdateHardwareDecodingMode();

            string scriptPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "autocrop.lua");
            if (System.IO.File.Exists(scriptPath))
            {
                MpvNative.mpv_command_string(_mpv, $"load-script \"{scriptPath.Replace("\\", "/")}\"");
            }

            int savedVol = SettingsService.Instance.Config.LastVolume;
            if (savedVol < 0 || savedVol > 200) savedVol = 80;
            sliderVolume.Value = savedVol;
            if (sliderVolumeFS != null) sliderVolumeFS.Value = savedVol;
            MpvSetPropertyString("volume", savedVol.ToString());

            if (SettingsService.Instance.Config.AudioNightMode)
            {
                MpvSetPropertyString("af", "lavfi=[dynaudnorm=f=150:g=15:maxgain=12:m=4.0]");
            }

            // Initialize independent background audio MPV player (no video output, completely decoupled from image flips)
            try
            {
                _mpvBgm = MpvNative.mpv_create();
                if (_mpvBgm != IntPtr.Zero)
                {
                    MpvNative.mpv_set_option_string(_mpvBgm, "vo", "null");
                    MpvNative.mpv_set_option_string(_mpvBgm, "video", "no");
                    MpvNative.mpv_set_option_string(_mpvBgm, "keep-open", "always");
                    MpvNative.mpv_set_option_string(_mpvBgm, "audio-pitch-correction", "yes");
                    MpvNative.mpv_set_option_string(_mpvBgm, "working-directory", AppDomain.CurrentDomain.BaseDirectory);
                    MpvNative.mpv_initialize(_mpvBgm);
                    MpvNative.mpv_set_property_string(_mpvBgm, "volume", savedVol.ToString());
                    MpvNative.mpv_set_property_string(_mpvBgm, "loop-file", "inf");
                }
            }
            catch { }

            return true;
        }

        // ── WinForms Panel Paint (placeholder when no media or pure audio without cover) ──────────────
        private void VideoPanel_Paint(object? sender, System.Windows.Forms.PaintEventArgs e)
        {
            bool isAudioWithoutCover = _hasMedia && 
                                       PlaylistManager.IsAudioFile(_currentPlayingFilePath) && 
                                       !_currentMediaHasVideoTrack;

            if ((_hasMedia && !isAudioWithoutCover) || _isSkinIdleVideoPlaying) return;
            var idleVideos = ThemeService.Instance.ResolvedSkinIdleVideos;
            if (idleVideos.Count > 0) return; // If skin has idle videos configured, ignore static picture interface
            var g = e.Graphics;

            var theme = ThemeService.Instance.CurrentTheme;

            // Background color fallback or theme window background
            System.Drawing.Color bgClr = System.Drawing.Color.FromArgb(5, 16, 36);
            if (theme != null && !string.IsNullOrEmpty(theme.WindowBgHex))
            {
                try
                {
                    var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(theme.WindowBgHex);
                    bgClr = System.Drawing.Color.FromArgb(c.R, c.G, c.B);
                }
                catch { }
            }
            g.Clear(bgClr);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            // 1. Draw Idle Background Wallpaper if present in skin
            string? idleBgPath = null;
            try
            {
                string? folder = theme?.SkinFolderPath;
                if (!string.IsNullOrWhiteSpace(theme?.IdleBg) && !string.IsNullOrEmpty(folder))
                {
                    string candidate = Path.IsPathRooted(theme.IdleBg) ? theme.IdleBg : Path.Combine(folder, theme.IdleBg);
                    if (File.Exists(candidate)) idleBgPath = candidate;
                }
                if (idleBgPath == null && !string.IsNullOrEmpty(folder))
                {
                    if (!string.IsNullOrWhiteSpace(theme?.ThemeBg))
                    {
                        string cThemeBg = Path.IsPathRooted(theme.ThemeBg) ? theme.ThemeBg : Path.Combine(folder, theme.ThemeBg);
                        if (File.Exists(cThemeBg)) idleBgPath = cThemeBg;
                    }
                    if (idleBgPath == null && !string.IsNullOrWhiteSpace(theme?.BackgroundImage))
                    {
                        string cBg = Path.IsPathRooted(theme.BackgroundImage) ? theme.BackgroundImage : Path.Combine(folder, theme.BackgroundImage);
                        if (File.Exists(cBg)) idleBgPath = cBg;
                    }
                }
                if (idleBgPath == null && !string.IsNullOrEmpty(folder))
                {
                    string[] candidates = new[] {
                        "blue marble.jpg", "teal_gold.jpg", "black gold.jpg",
                        "blue_marble_1k.jpg", "teal_gold_1k.jpg", "black_gold_1k.jpg",
                        "idle_bg.png", "idle.jpg", "wallpaper.png", "wallpaper.jpg", "background.jpg", "background.png"
                    };
                    foreach (var cand in candidates)
                    {
                        string p = Path.Combine(folder, cand);
                        if (File.Exists(p)) { idleBgPath = p; break; }
                    }
                }

                if (idleBgPath != null && File.Exists(idleBgPath))
                {
                    if (_cachedIdleBgBitmap == null || _cachedIdleBgPath != idleBgPath)
                    {
                        try
                        {
                            _cachedIdleBgBitmap?.Dispose();
                            using var fs = new FileStream(idleBgPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                            using var tempImg = System.Drawing.Image.FromStream(fs);
                            _cachedIdleBgBitmap = new System.Drawing.Bitmap(tempImg);
                            _cachedIdleBgPath = idleBgPath;
                        }
                        catch
                        {
                            _cachedIdleBgBitmap = null;
                            _cachedIdleBgPath = null;
                        }
                    }

                    if (_cachedIdleBgBitmap != null)
                    {
                        float pw = _videoPanel.Width;
                        float ph = _videoPanel.Height;
                        if (pw > 0 && ph > 0 && _cachedIdleBgBitmap.Width > 0 && _cachedIdleBgBitmap.Height > 0)
                        {
                            float scale = Math.Max(pw / _cachedIdleBgBitmap.Width, ph / _cachedIdleBgBitmap.Height);
                            float dw = _cachedIdleBgBitmap.Width * scale;
                            float dh = _cachedIdleBgBitmap.Height * scale;
                            float dx = (pw - dw) / 2f;
                            float dy = (ph - dh) / 2f;
                            g.InterpolationMode = _isDraggingSize 
                                ? System.Drawing.Drawing2D.InterpolationMode.Low 
                                : System.Drawing.Drawing2D.InterpolationMode.Bilinear;
                            g.DrawImage(_cachedIdleBgBitmap, dx, dy, dw, dh);
                        }
                    }
                }
            }
            catch { }

            // 2. Custom Typography, Font Sizes & Colors from Skin (XAML / JSON)
            // If media (audio) is loaded or playing, do NOT draw the open file prompts over the skin wallpaper!
            if (_hasMedia || !string.IsNullOrEmpty(_currentPlayingFilePath))
            {
                return;
            }

            string fontFam = "Microsoft YaHei";
            if (!string.IsNullOrWhiteSpace(theme?.IdleHintFontFamily))
            {
                fontFam = theme.IdleHintFontFamily.Split(',')[0].Trim();
            }
            else if (!string.IsNullOrWhiteSpace(theme?.FontFamily))
            {
                fontFam = theme.FontFamily.Split(',')[0].Trim();
            }
            if (System.Windows.Application.Current.Resources["ThemeIdleHintFontFamily"] is System.Windows.Media.FontFamily xamlFont)
            {
                fontFam = xamlFont.Source.Split(',')[0].Trim();
            }

            float fontSizeTitle = (float)(theme?.IdleHintTitleSize ?? 22.0);
            if (System.Windows.Application.Current.Resources["ThemeIdleHintTitleSize"] is double xamlTSize)
            {
                fontSizeTitle = (float)xamlTSize;
            }

            float fontSizeSub = (float)(theme?.IdleHintSubtitleSize ?? 14.5);
            if (System.Windows.Application.Current.Resources["ThemeIdleHintSubtitleSize"] is double xamlSSize)
            {
                fontSizeSub = (float)xamlSSize;
            }

            System.Drawing.Color c1 = System.Drawing.Color.FromArgb(245, 215, 127);
            System.Drawing.Color c2 = System.Drawing.Color.FromArgb(216, 181, 104);
            
            if (System.Windows.Application.Current.Resources["ThemeIdleHintTitleBrush"] is System.Windows.Media.SolidColorBrush tb)
            {
                c1 = System.Drawing.Color.FromArgb(tb.Color.R, tb.Color.G, tb.Color.B);
            }
            else if (!string.IsNullOrEmpty(theme?.IdleHintTitleHex))
            {
                try
                {
                    var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(theme.IdleHintTitleHex);
                    c1 = System.Drawing.Color.FromArgb(c.R, c.G, c.B);
                } catch { }
            }
            else if (!string.IsNullOrEmpty(theme?.AccentHex))
            {
                try
                {
                    var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(theme.AccentHex);
                    c1 = System.Drawing.Color.FromArgb(c.R, c.G, c.B);
                } catch { }
            }

            if (System.Windows.Application.Current.Resources["ThemeIdleHintSubtitleBrush"] is System.Windows.Media.SolidColorBrush sb)
            {
                c2 = System.Drawing.Color.FromArgb(sb.Color.R, sb.Color.G, sb.Color.B);
            }
            else if (!string.IsNullOrEmpty(theme?.IdleHintSubtitleHex))
            {
                try
                {
                    var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(theme.IdleHintSubtitleHex);
                    c2 = System.Drawing.Color.FromArgb(c.R, c.G, c.B);
                } catch { }
            }
            else if (!string.IsNullOrEmpty(theme?.TextHex))
            {
                try
                {
                    var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(theme.TextHex);
                    c2 = System.Drawing.Color.FromArgb(c.R, c.G, c.B);
                } catch { }
            }

            bool isSkin = ThemeService.Instance.IsSkinActive;
            bool isBold = theme?.IdleHintBold == true || (System.Windows.Application.Current.Resources["ThemeIdleHintBold"] is bool bBold && bBold);
            var fontStyle = isBold ? System.Drawing.FontStyle.Bold : System.Drawing.FontStyle.Regular;

            System.Drawing.Font f1;
            try { f1 = new System.Drawing.Font(fontFam, fontSizeTitle, fontStyle); }
            catch { f1 = new System.Drawing.Font("Microsoft YaHei", fontSizeTitle, fontStyle); }

            System.Drawing.Font f2;
            try { f2 = new System.Drawing.Font(fontFam, fontSizeSub, System.Drawing.FontStyle.Regular); }
            catch { f2 = new System.Drawing.Font("Microsoft YaHei", fontSizeSub, System.Drawing.FontStyle.Regular); }

            using (f1)
            using (f2)
            {
                using var b1 = new System.Drawing.SolidBrush(c1);
                using var b2 = new System.Drawing.SolidBrush(c2);
                using var shadowBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(140, 0, 0, 0));

                bool drawShadow = isSkin && (idleBgPath != null && File.Exists(idleBgPath));

                var hk = SettingsService.Instance.Config.Hotkeys ??= new HotkeyConfig();

                // Line 1: Main Title prompt (skin custom or built-in i18n fallback)
                string l1 = !string.IsNullOrWhiteSpace(theme?.IdleHintTitle)
                    ? theme.IdleHintTitle
                    : I18nService.Instance["DragHintLine1"];

                // Line 2: Subtitle prompt (skin custom or built-in i18n fallback with hotkey formatting)
                string rawL2 = !string.IsNullOrWhiteSpace(theme?.IdleHintSubtitle)
                    ? theme.IdleHintSubtitle
                    : I18nService.Instance["DragHintLine2"];
                string l2;
                try
                {
                    l2 = string.Format(rawL2, hk.OpenFile, hk.OpenFolder, hk.OpenUrl);
                }
                catch
                {
                    l2 = rawL2;
                }

                // Line 3: SubText prompt (skin custom or built-in i18n fallback)
                string l3 = !string.IsNullOrWhiteSpace(theme?.IdleHintSubText)
                    ? theme.IdleHintSubText
                    : I18nService.Instance["DragHintLine3"];

                var s1 = g.MeasureString(l1, f1);
                var s2 = g.MeasureString(l2, f2);
                var s3 = g.MeasureString(l3, f2);

                float totalH = s1.Height + s2.Height + s3.Height + 24f;
                float cx = _videoPanel.Width / 2f;
                float startY = (_videoPanel.Height - totalH) / 2f;

                // Draw Title
                float x1 = cx - s1.Width / 2f;
                if (drawShadow) g.DrawString(l1, f1, shadowBrush, x1 + 1.2f, startY + 1.2f);
                g.DrawString(l1, f1, b1, x1, startY);

                // Draw Subtitle
                float y2 = startY + s1.Height + 10f;
                float x2 = cx - s2.Width / 2f;
                if (drawShadow) g.DrawString(l2, f2, shadowBrush, x2 + 1.0f, y2 + 1.0f);
                g.DrawString(l2, f2, b2, x2, y2);

                // Draw SubText
                float y3 = y2 + s2.Height + 8f;
                float x3 = cx - s3.Width / 2f;
                if (drawShadow) g.DrawString(l3, f2, shadowBrush, x3 + 1.0f, y3 + 1.0f);
                g.DrawString(l3, f2, b2, x3, y3);
            }
        }

        public static bool IsNetworkUrl(string? path) => PlaylistManager.IsNetworkUrl(path ?? "");

        // ── PlayFile ──────────────────────────────────────────────────────
        public void PlayFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            bool isUrl = IsNetworkUrl(path);
            if (!isUrl && !File.Exists(path)) return;
            CancelCorruptAutoNext();
            Dispatcher.Invoke(() => _clickTimer?.Stop());
            _loadQueue.Enqueue(path);
            if (!_isMediaLoading)
            {
                _ = ProcessLoadQueueAsync();
            }
        }

        private async System.Threading.Tasks.Task ProcessLoadQueueAsync()
        {
            _isMediaLoading = true;
            Dispatcher.Invoke(() => _clickTimer?.Stop());
            try
            {
                while (_loadQueue.TryDequeue(out string? path))
                {
                    if (string.IsNullOrWhiteSpace(path)) continue;
                    // If there are more items in queue, skip this one (only play the latest)
                    if (!_loadQueue.IsEmpty) continue;

                try
                {
                    StopSkinIdleVideo();
                    _hasMedia = true;
                    _currentPlayingFilePath = path;
                    Dispatcher.Invoke(() =>
                    {
                        videoHost.Width = double.NaN;
                        videoHost.Height = double.NaN;
                        videoHost.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
                        videoHost.VerticalAlignment = System.Windows.VerticalAlignment.Stretch;
                    });
                    _cachedSmartFillGridW = -1;
                    _cachedSmartFillGridH = -1;
                    _cachedSmartFillRawW = -1;
                    _cachedSmartFillRawH = -1;
                    MpvSetPropertyString("keep-open", "yes");
                    MpvSetPropertyString("panscan", "0.0");
                    MpvSetPropertyString("loop-playlist", "no");
                    MpvSetPropertyString("loop-file", "no");
                    MpvSetPropertyString("aid", "auto");
                    MpvSetPropertyString("speed", "1.0");
                    MpvSetPropertyString("mute", _isMuted ? "yes" : "no");
                    _hasAutoSelectedAudioLanguage = false;
                    _recentBufferStalls.Clear();
                    _lastQualityAutoDowngradeTime = DateTime.MinValue;
                    _autoDowngradedFromTrackId = -1;
                    ApplyBaseBrightness();
                    _videoPanel.Invalidate();   // clear placeholder
                    // Prepare for error handling

                    bool isUrl = IsNetworkUrl(path);
                    string posixPath = isUrl ? path : path.Replace("\\", "/");
                    _isCurrentImage = !isUrl && PlaylistManager.IsImageFile(path);
                    _imageElapsedSec = 0.0;

                    if (_isCurrentImage)
                    {
                        // Proactively clear and bypass shaders for images to prevent heavy GPU shader re-runs on motion
                        MpvSetPropertyString("glsl-shaders", "");
                        MpvSetPropertyString("deband", "no");
                        MpvSetPropertyString("scale", "bilinear");
                        MpvSetPropertyString("cscale", "bilinear");
                        MpvSetPropertyString("scale-antiring", "0");
                        MpvSetPropertyString("sharpen", "0");
                        MpvSetPropertyString("image-display-duration", "inf");
                        UpdateSmartFill();
                    }
                    else
                    {
                        StopImageMotion();
                        StopBgmAudio();
                        MpvSetPropertyString("image-display-duration", "inf");
                        UpdateSmartFill();
                    }

                    MpvSetPropertyString("stream-record", "");

                    bool shouldDownload = isUrl && _explicitSaveStreamRequested &&
                        (string.IsNullOrEmpty(_explicitSaveStreamUrl) || string.Equals(_explicitSaveStreamUrl, path, StringComparison.OrdinalIgnoreCase));

                    if (shouldDownload)
                    {
                        string saveDir = !string.IsNullOrWhiteSpace(_explicitSaveStreamDir)
                            ? _explicitSaveStreamDir
                            : SettingsService.Instance.Config.NetworkStreamSaveDir;
                        string mediaTitle = MpvGet("media-title");
                        StreamDownloadService.Instance.StartDownload(path, saveDir, mediaTitle);
                    }
                    else
                    {
                        StreamDownloadService.Instance.StopDownload();
                    }

                    // Reset explicit one-time download trigger
                    _explicitSaveStreamRequested = false;
                    _explicitSaveStreamDir = "";
                    _explicitSaveStreamUrl = "";

                    CancelCorruptAutoNext();
                    _mediaLoadStartTime = DateTime.UtcNow;
                    _mediaLoadFailedHandled = false;
                    _currentMediaLoadedSuccessfully = false;

                    if (isUrl)
                    {
                        _isNetworkBuffering = true;
                        _networkLoadStartTime = DateTime.UtcNow;
                        _networkLoadFailedHandled = false;
                        ShowBuffering("");
                        ApplyAdaptiveNetworkCache();
                    }
                    else
                    {
                        _networkLoadStartTime = DateTime.MinValue;
                        _networkLoadFailedHandled = false;
                        HideBuffering();
                        ApplyLocalFileCache();
                    }

                    if (!isUrl && PlaylistManager.IsAudioFile(path))
                    {
                        // Proactively clear and bypass shaders before MPV loads the audio file
                        MpvSetPropertyString("glsl-shaders", "");
                        MpvSetPropertyString("deband", "no");
                        MpvSetPropertyString("scale", "bilinear");
                        MpvSetPropertyString("cscale", "bilinear");
                        MpvSetPropertyString("scale-antiring", "0");
                        MpvSetPropertyString("sharpen", "0");
                    }

                    double resumePos = 0.0;
                    var cfg = SettingsService.Instance.Config;
                    if (cfg.AutoResume && !_isCurrentImage && !string.IsNullOrEmpty(cfg.LastPlayedFilePath))
                    {
                        try
                        {
                            string curFull = Path.GetFullPath(path);
                            string lastFull = Path.GetFullPath(cfg.LastPlayedFilePath);
                            if (string.Equals(curFull, lastFull, StringComparison.OrdinalIgnoreCase))
                            {
                                // Rewind 3 seconds from last playback position for context, min 0
                                resumePos = Math.Max(0.0, cfg.LastPlayedPosition - 3.0);
                            }
                        }
                        catch { }
                    }

                    if (resumePos > 1.0)
                    {
                        _pendingResumePosition = resumePos;
                        MpvNative.mpv_command_string(_mpv, $"loadfile \"{posixPath}\" replace");
                        ShowOsd(string.Format(I18nService.Instance["OsdResumePlayback"], TimeSpan.FromSeconds(resumePos).ToString(@"mm\:ss")));
                    }
                    else
                    {
                        _pendingResumePosition = 0.0;
                        MpvNative.mpv_command_string(_mpv, $"loadfile \"{posixPath}\" replace");
                    }
                    if (IsAnyModalOverlayOpen())
                    {
                        MpvSetPropertyString("pause", "yes");
                        MpvNative.mpv_command_string(_mpv, "set pause yes");
                    }
                    else
                    {
                        MpvSetPropertyString("pause", "no");
                    }

                    if (_isCurrentImage)
                    {
                        StartImageMotion(path);
                        UpdateBgmAudio(path);
                    }
                    Dispatcher.Invoke(() => EnsureMainCanvasFocusAndDisableIme());
                    if (!isUrl)
                    {
                        AutoMatchLocalSubtitles(path);
                        if (PlaylistManager.IsAudioFile(path))
                        {
                            string mpvLyrics = MpvGet("metadata/by-key/lyrics");
                            if (string.IsNullOrWhiteSpace(mpvLyrics)) mpvLyrics = MpvGet("metadata/by-key/LYRICS");
                            if (string.IsNullOrWhiteSpace(mpvLyrics)) mpvLyrics = MpvGet("metadata/by-key/USLT");
                            if (string.IsNullOrWhiteSpace(mpvLyrics)) mpvLyrics = MpvGet("metadata/by-key/unsyncedlyrics");

                            LyricsService.Instance.LoadLyricsForAudio(path, mpvLyrics);
                            Dispatcher.Invoke(() =>
                            {
                                UpdateAudioBannerView();
                                StartVinylDiscAnimation();
                            });
                        }
                        else
                        {
                            Dispatcher.Invoke(() =>
                            {
                                StopVinylDiscAnimation();
                            });
                        }
                    }

                    _videoSizeSet = false;
                    _currentMediaHasVideoTrack = false;
                    _pendingMediaDimensionsPath = path;

                    PlaylistManager.Instance.AddFile(path, true);
                    Dispatcher.Invoke(() => { if (_isDrawerOpen) ScrollPlaylistToCurrentItem(); });

                    // Reset crop state for the new file
                    MpvNative.mpv_command_string(_mpv, "set user-data/crop-ready false");
                    MpvNative.mpv_command_string(_mpv, "set user-data/crop-w 0");
                    MpvNative.mpv_command_string(_mpv, "set user-data/crop-h 0");
                    MpvNative.mpv_command_string(_mpv, "set user-data/crop-x 0");
                    MpvNative.mpv_command_string(_mpv, "set user-data/crop-y 0");

                    // Trigger autocrop only for videos (skip for images)
                    if (!_isCurrentImage && _autoCropMode != "none")
                    {
                        MpvNative.mpv_command_string(_mpv, $"script-message anni-autocrop-start {_autoCropMode}");
                    }

                    string name = Path.GetFileName(path);
                    string displayName = name;
                    txtTitle.Text = $"{I18nService.Instance["AppTitle"]}  ·  {displayName}";
                    if (txtTitleFS != null) txtTitleFS.Text = txtTitle.Text;
                    Title = $"{I18nService.Instance["AppTitle"]} - {displayName}";

                    // Safety Timeout Guard (1.5 seconds)
                    int timeout = 0;
                    while (timeout < 15)
                    {
                        await System.Threading.Tasks.Task.Delay(100);
                        if (MpvGetDouble("width") > 0 || MpvGetDouble("duration") > 0)
                        {
                            break;
                        }
                        timeout++;
                    }
                    if (IsAnyModalOverlayOpen())
                    {
                        MpvSetPropertyString("pause", "yes");
                        MpvNative.mpv_command_string(_mpv, "set pause yes");
                    }
                    else
                    {
                        MpvSetPropertyString("pause", "no");
                    }
                    _lastVfString = "";
                    UpdateSmartFill();
                }
                catch { }
            }
            }
            finally
            {
                _isMediaLoading = false;
            }
        }

        // ── MPV property helpers ──────────────────────────────────────────
        private string MpvGet(string name)
        {
            if (_mpv == IntPtr.Zero) return "";
            IntPtr p = MpvNative.mpv_get_property_string(_mpv, name);
            if (p == IntPtr.Zero) return "";
            string v = Marshal.PtrToStringUTF8(p) ?? "";
            MpvNative.mpv_free(p);
            return v;
        }

        private string MpvGetBgm(string name)
        {
            if (_mpvBgm == IntPtr.Zero) return "";
            IntPtr p = MpvNative.mpv_get_property_string(_mpvBgm, name);
            if (p == IntPtr.Zero) return "";
            string v = Marshal.PtrToStringUTF8(p) ?? "";
            MpvNative.mpv_free(p);
            return v;
        }

        private double MpvGetDouble(string name)
        {
            string s = MpvGet(name);
            return double.TryParse(s,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out double v) ? v : 0;
        }
        
        private void MpvSetPropertyString(string name, string value)
        {
            if (_mpv != IntPtr.Zero) MpvNative.mpv_set_property_string(_mpv, name, value);
        }

        // ── Polling Timer ────────────────────────────────────────────────
        private void Timer_Tick(object? sender, EventArgs e)
        {
            if (_mpv == IntPtr.Zero) return;
            
            // Update Clock
            string clock = DateTime.Now.ToString("HH:mm:ss");
            if (txtClock.Text != clock)
            {
                txtClock.Text = clock;
                if (txtClockFS != null) txtClockFS.Text = clock;
            }

            // Robust Topmost Enforcement: If AlwaysOnTop or PiP is enabled, ensure WS_EX_TOPMOST was not stripped by Windows OS / other apps
            if (SettingsService.Instance.Config.AlwaysOnTop || _isPipMode)
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    long exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
                    if ((exStyle & WS_EX_TOPMOST) == 0)
                    {
                        RestoreTopmostState();
                    }
                }
            }

            if (_isSkinIdleVideoPlaying)
            {
                if (MpvGet("idle-active") == "yes")
                {
                    var idleVideos = ThemeService.Instance.ResolvedSkinIdleVideos;
                    if (idleVideos.Count > 0)
                    {
                        PlaySkinIdleVideos(idleVideos);
                    }
                }

                bool paused = MpvGet("pause") == "yes";
                txtTime.Text = "--:-- / --:--";
                btnPlay.Content = paused ? "\uE102" : "\uE103";
                if (btnPlayFS != null) btnPlayFS.Content = btnPlay.Content;
                return;
            }

            if (_pendingResumePosition > 0.0 && _hasMedia && _mpv != IntPtr.Zero)
            {
                double dur = MpvGetDouble("duration");
                double pos = MpvGetDouble("time-pos");
                if (dur > 0 || pos >= 0)
                {
                    double target = _pendingResumePosition;
                    _pendingResumePosition = 0.0;
                    MpvNative.mpv_command_string(_mpv, $"seek {target.ToString(System.Globalization.CultureInfo.InvariantCulture)} absolute exact");
                }
            }
            
            if (_videoSizeSet && (_smartFillEnabled || _autoCropMode != "none"))
            {
                if (_lastVideoPanelSize != _videoPanel.ClientSize)
                {
                    _lastVideoPanelSize = _videoPanel.ClientSize;
                    UpdateSmartFill();
                }
            }

            // Auto resize window & update SmartFill when new media dimensions are ready
            if (!_videoSizeSet)
            {
                string currentMpvPath = MpvGet("path");
                string posixPending = _pendingMediaDimensionsPath.Replace("\\", "/");
                bool isNewFileActive = string.IsNullOrEmpty(_pendingMediaDimensionsPath) ||
                                       string.Equals(currentMpvPath, posixPending, StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(currentMpvPath?.Replace("/", "\\"), _pendingMediaDimensionsPath, StringComparison.OrdinalIgnoreCase);

                if (isNewFileActive && GetSourceVideoDimensions(out double vw, out double vh))
                {
                    _videoSizeSet = true;
                    _currentMediaHasVideoTrack = (vw > 0 && vh > 0);
                    _pendingMediaDimensionsPath = "";
                    if (IsNetworkUrl(_currentPlayingFilePath))
                    {
                        ApplyAdaptiveNetworkCache(vw, vh);
                    }
                    ApplyVideoSharpening(SettingsService.Instance.Config.VideoSharpening);

                    if (_isPipMode)
                    {
                        double controlH = 46;
                        if (PlaylistManager.IsAudioFile(_currentPlayingFilePath))
                        {
                            _pipAspectRatio = 16.0 / 9.0;
                            this.Height = Math.Round(this.Width / _pipAspectRatio) + controlH;
                        }
                        else
                        {
                            double r = vw / vh;
                            _pipAspectRatio = (r < 1.0) ? Math.Max(r, 4.0 / 3.0) : r;
                            this.Height = Math.Round(this.Width / _pipAspectRatio) + controlH;
                        }
                    }
                    else if (!_isFullscreen && WindowState == WindowState.Normal)
                    {
                        double chromeH = (rowTitle?.ActualHeight > 0 ? rowTitle.ActualHeight : 40) + 
                                         (rowControls?.ActualHeight > 0 ? rowControls.ActualHeight : 105);
                        var workArea = SystemParameters.WorkArea;
                        double maxW = workArea.Width * 0.85;
                        double maxH = workArea.Height - 40;
                        double maxVideoH = Math.Max(200, maxH - chromeH);

                        double ar = vw / vh;
                        bool isAudioOrImg = _isCurrentImage || PlaylistManager.IsAudioFile(_currentPlayingFilePath);

                        // Exact video aspect ratio (for portrait r < 1.0, clamp container to >= 4:3 so transport controls fit)
                        double effAr = isAudioOrImg ? (16.0 / 9.0) : (ar < 1.0 ? Math.Max(ar, 4.0 / 3.0) : ar);

                        double targetVideoW = Math.Max(820, Math.Min(vw, maxW));
                        double targetVideoH = targetVideoW / effAr;

                        if (targetVideoH > maxVideoH)
                        {
                            targetVideoH = maxVideoH;
                            targetVideoW = Math.Max(820, targetVideoH * effAr);
                            targetVideoH = targetVideoW / effAr;
                        }

                        double targetW = Math.Round(targetVideoW);
                        double targetH = Math.Round(targetVideoH + chromeH);

                        this.Width = targetW;
                        this.Height = targetH;
                        this.Left = workArea.Left + Math.Max(0, (workArea.Width - this.Width) / 2);
                        this.Top = workArea.Top + Math.Max(0, (workArea.Height - this.Height) / 2);
                    }

                    _cachedSmartFillGridW = -1;
                    _cachedSmartFillGridH = -1;
                    Dispatcher.InvokeAsync(() => UpdateSmartFill(), System.Windows.Threading.DispatcherPriority.Loaded);
                }
            }

            try
            {
                double pos  = MpvGetDouble("time-pos");
                double dur  = MpvGetDouble("duration");
                bool paused = MpvGet("pause") == "yes";
                bool muted  = MpvGet("mute")  == "yes";

                // Update Audio Metadata, Lyrics & Rotating Vinyl Disc in real-time
                if (PlaylistManager.IsAudioFile(_currentPlayingFilePath))
                {
                    if (IsAnyModalOverlayOpen() || _isDialogOpen)
                    {
                        if (popupAudioBanner != null && popupAudioBanner.IsOpen)
                        {
                            popupAudioBanner.IsOpen = false;
                        }
                    }
                    else
                    {
                        if (popupAudioBanner != null && !popupAudioBanner.IsOpen)
                        {
                            popupAudioBanner.IsOpen = true;
                            UpdateAudioBannerView();
                            StartVinylDiscAnimation();
                        }
                    }

                    var (curLyric, nextLyric) = LyricsService.Instance.GetLyricsAt(TimeSpan.FromSeconds(pos));
                    if (curLyric != null)
                    {
                        if (txtAudioCurrentLyric != null && txtAudioCurrentLyric.Text != curLyric.Text)
                        {
                            txtAudioCurrentLyric.Text = curLyric.Text;
                            var anim = new System.Windows.Media.Animation.DoubleAnimation(0.3, 1.0, TimeSpan.FromMilliseconds(180));
                            txtAudioCurrentLyric.BeginAnimation(OpacityProperty, anim);
                        }
                    }
                    else
                    {
                        if (txtAudioCurrentLyric != null)
                        {
                            txtAudioCurrentLyric.Text = (LyricsService.Instance.CurrentLyrics.Count > 0) ? ("🎵 " + (txtAudioTrackTitle?.Text ?? "")) : ("🎵 " + I18nService.Instance["AudioNoLyrics"]);
                        }
                    }

                    if (paused)
                    {
                        PauseVinylDiscAnimation();
                    }
                    else
                    {
                        ResumeVinylDiscAnimation();
                    }
                }
                else
                {
                    StopVinylDiscAnimation();
                }

                // Strict A-B Loop boundary check (handles fast-forward / seek past loop end)
                if (_abLoopA >= 0 && _abLoopB > _abLoopA && !_draggingTimeline)
                {
                    if (pos >= _abLoopB - 0.1 || pos < _abLoopA)
                    {
                        MpvNative.mpv_command_string(_mpv, $"seek {_abLoopA.ToString(System.Globalization.CultureInfo.InvariantCulture)} absolute exact");
                        pos = _abLoopA;
                    }
                }

                // Network Stream Buffering OSD, Timeout & Failure Detection
                if (IsNetworkUrl(_currentPlayingFilePath))
                {
                    string idleActive = MpvGet("idle-active");
                    string pausedForCache = MpvGet("paused-for-cache");
                    double cachePercent = MpvGetDouble("cache-buffering-state");
                    double cacheSec = MpvGetDouble("demuxer-cache-duration");
                    string coreIdle = MpvGet("core-idle");
                    string eof = MpvGet("eof-reached");

                    double elapsed = (_networkLoadStartTime > DateTime.MinValue)
                        ? (DateTime.UtcNow - _networkLoadStartTime).TotalSeconds
                        : 0;

                    // 1. Check if playback has actively started (video frame decoded or progress moving)
                    bool hasActivePlayback = (pos > 0.05 || (dur > 0 && pausedForCache != "yes" && coreIdle != "yes") || MpvGetDouble("width") > 0);
                    if (hasActivePlayback)
                    {
                        _hasMedia = true;
                        // If load error was previously shown prematurely, dismiss it immediately
                        if (_networkLoadFailedHandled)
                        {
                            _networkLoadFailedHandled = false;
                            if (popupOsd != null && popupOsd.IsOpen && txtOsd != null && txtOsd.Text.Contains("失败"))
                            {
                                popupOsd.IsOpen = false;
                            }
                        }

                        // Auto-match audio language for multi-audio/demuxed HLS streams
                        EnsureOptimalAudioTrackSelected();
                    }

                    // 2. Robust failure detection:
                    // Only trigger failure if:
                    // - Elapsed > 30.0 seconds without any active playback or data
                    // - OR explicit MPV file abort (eof-reached == "yes" at pos <= 0 after 2.5s)
                    bool isFailed = false;
                    if (!_networkLoadFailedHandled && _networkLoadStartTime > DateTime.MinValue && !paused && !hasActivePlayback)
                    {
                        double totalBytes = MpvGetDouble("demuxer-cache-state/total-bytes");
                        double rawSpeed = MpvGetDouble("demuxer-cache-state/raw-input-rate");
                        bool isActivelyDownloading = (totalBytes > 0 && rawSpeed > 0);

                        if (eof == "yes" && elapsed > 2.5 && pos <= 0 && dur <= 0)
                        {
                            isFailed = true;
                        }
                        else if (elapsed > 30.0 && !isActivelyDownloading && pos <= 0 && dur <= 0 && MpvGetDouble("width") <= 0)
                        {
                            isFailed = true;
                        }
                    }

                    if (isFailed)
                    {
                        _networkLoadFailedHandled = true;
                        _isNetworkBuffering = false;
                        _hasMedia = false;
                        HideBuffering();
                        HandleNetworkStreamLoadFailed(_currentPlayingFilePath);
                    }
                    else if (!_networkLoadFailedHandled)
                    {
                        // 3. Buffering state:
                        // Either waiting for initial network connect/probe (!hasActivePlayback)
                        // OR mid-playback cache pause (pausedForCache == "yes" || coreIdle == "yes")
                        bool isBuffering = !hasActivePlayback || (pausedForCache == "yes") ||
                                           (cachePercent >= 0 && cachePercent < 100 && (coreIdle == "yes" || pos <= 0.3)) ||
                                           (_isNetworkBuffering && (pausedForCache == "yes" || coreIdle == "yes"));

                        if (isBuffering && !paused && eof != "yes")
                        {
                            _isNetworkBuffering = true;
                            string pText = (cachePercent > 0 && cachePercent <= 100) ? $"{(int)cachePercent}%" : (cacheSec > 0.5 ? $"{cacheSec:F1}s" : "");
                            ShowBuffering(pText);

                            if (pausedForCache == "yes" && hasActivePlayback)
                            {
                                CheckAdaptiveBitrateStall();
                            }
                        }
                        else
                        {
                            if (_isNetworkBuffering)
                            {
                                _isNetworkBuffering = false;
                                HideBuffering();
                            }
                        }
                    }
                }
                else
                {
                    if (_isNetworkBuffering)
                    {
                        _isNetworkBuffering = false;
                        HideBuffering();
                    }

                    // Local Media (Video/Audio/Image) Playback Failure Detection
                    if (_hasMedia && !_mediaLoadFailedHandled && _mediaLoadStartTime > DateTime.MinValue && !_isSkinIdleVideoPlaying)
                    {
                        double elapsed = (DateTime.UtcNow - _mediaLoadStartTime).TotalSeconds;

                        if (!_currentMediaLoadedSuccessfully)
                        {
                            bool hasActivePlayback = (pos > 0.05 || (dur > 0 && MpvGet("core-idle") != "yes") || MpvGetDouble("width") > 0 || MpvGetDouble("audio-params/samplerate") > 0);
                            if (hasActivePlayback)
                            {
                                _currentMediaLoadedSuccessfully = true;
                            }
                        }

                        if (!_currentMediaLoadedSuccessfully)
                        {
                            string playbackAbort = MpvGet("playback-abort");
                            string idleActive = MpvGet("idle-active");
                            string eof = MpvGet("eof-reached");
                            double trackCount = MpvGetDouble("track-list/count");

                            bool isFailed = false;
                            if (playbackAbort == "yes" && elapsed >= 0.5)
                            {
                                isFailed = true;
                            }
                            else if ((idleActive == "yes" || eof == "yes") && (trackCount <= 0 || (dur <= 0 && pos <= 0)) && elapsed >= 1.0)
                            {
                                isFailed = true;
                            }
                            else if (elapsed >= 5.0 && dur <= 0 && pos <= 0 && MpvGetDouble("width") <= 0 && trackCount <= 0)
                            {
                                isFailed = true;
                            }

                            if (isFailed)
                            {
                                _mediaLoadFailedHandled = true;
                                HandleUnplayableMedia(_currentPlayingFilePath);
                            }
                        }
                    }
                }

                // Play/Pause icon
                bool playing = !paused && (dur > 0 || _isCurrentImage || IsNetworkUrl(_currentPlayingFilePath));
                if (playing != _isPlaying)
                {
                    _isPlaying = playing;
                    btnPlay.Content = _isPlaying ? "\uE103" : "\uE102"; // pause / play
                    btnPlayFS.Content = btnPlay.Content;
                }

                // Check for end-file (videos only; images handle auto-next in ImageMotionTimer_Tick)
                if (!_isCurrentImage && _hasMedia && !string.IsNullOrEmpty(_currentPlayingFilePath) && _currentMediaLoadedSuccessfully)
                {
                    string eof = MpvGet("eof-reached");
                    if (eof == "yes")
                    {
                        var next = PlaylistManager.Instance.GetNext();
                        if (next != null)
                        {
                            if (string.Equals(next.FilePath, _currentPlayingFilePath, StringComparison.OrdinalIgnoreCase))
                            {
                                // Single item repeat (or single item in playlist with RepeatAll): rewind to 0 and replay
                                MpvNative.mpv_command_string(_mpv, "seek 0 absolute");
                                MpvNative.mpv_command_string(_mpv, "set pause no");
                            }
                            else
                            {
                                PlayFile(next.FilePath);
                                MpvNative.mpv_command_string(_mpv, "set pause no");
                            }
                        }
                        else if (!paused)
                        {
                            MpvNative.mpv_command_string(_mpv, "set pause yes");
                        }
                    }
                }

                // Mute icon
                if (muted != _isMuted)
                {
                    _isMuted = muted;
                    btnMute.Content = _isMuted ? "\uE74F" : "\uE767";  // mute / volume
                }

                // Time label & Timeline slider (Only for video/audio, skip for images to prevent timer flicker)
                if (!_isCurrentImage)
                {
                    bool isLive = IsNetworkUrl(_currentPlayingFilePath) && !IsMediaSeekable();
                    string newTime = isLive ? $"🔴 LIVE · {Fmt(pos)}" : $"{Fmt(pos)} / {Fmt(dur)}";
                    if (txtTime.Text != newTime) txtTime.Text = newTime;

                    if (!_draggingTimeline)
                    {
                        _timerUpdating = true;
                        if (isLive)
                        {
                            sliderTimeline.Maximum = 100;
                            sliderTimeline.Value = 100;
                            if (sliderTimelineFS != null)
                            {
                                sliderTimelineFS.Maximum = 100;
                                sliderTimelineFS.Value = 100;
                            }
                            sliderTimeline.IsEnabled = false;
                            if (sliderTimelineFS != null) sliderTimelineFS.IsEnabled = false;
                        }
                        else
                        {
                            sliderTimeline.IsEnabled = true;
                            if (sliderTimelineFS != null) sliderTimelineFS.IsEnabled = true;
                            sliderTimeline.Maximum = dur;
                            sliderTimeline.Value = pos;
                            if (sliderTimelineFS != null)
                            {
                                sliderTimelineFS.Maximum = dur;
                                sliderTimelineFS.Value = pos;
                            }
                        }
                        _timerUpdating = false;
                    }

                    if (_isPipMode)
                    {


                        if (popupSideDrawer != null && popupSideDrawer.IsOpen)
                        {
                            popupSideDrawer.IsOpen = false;
                            _isDrawerOpen = false;
                            _isDrawerPinned = false;
                        }
                    }
                }

                // Sync UI timelines
                // ... (we'll also handle side drawer here)
                
                // --- Side Drawer Hover Check ---
                bool allowRightHover = SettingsService.Instance.Config.RightEdgeHoverPlaylist;

                if (_drawerAnimating)
                {
                    // Skip hover check while drawer animation is in progress
                }
                else if (this.WindowState == WindowState.Minimized || !this.IsVisible)
                {
                    if (_isDrawerOpen && !_isDrawerPinned) ToggleSideDrawer(false);
                }
                else
                {
                    IntPtr myHwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                    IntPtr foregroundHwnd = GetForegroundWindow();
                    uint currentProcId = (uint)Environment.ProcessId;
                    GetWindowThreadProcessId(foregroundHwnd, out uint fgProcId);

                    bool isWindowOrAppActive = (foregroundHwnd == myHwnd) || (fgProcId == currentProcId) || this.IsActive;

                    if (!isWindowOrAppActive)
                    {
                        if (_isDrawerOpen && !_isDrawerPinned)
                        {
                            ToggleSideDrawer(false);
                        }
                    }
                    else
                    {
                        var globalMouse = System.Windows.Forms.Cursor.Position;
                        IntPtr hwndUnderMouse = WindowFromPoint(new POINT { X = globalMouse.X, Y = globalMouse.Y });
                        GetWindowThreadProcessId(hwndUnderMouse, out uint mouseProcId);
                        bool isMouseOverOurApp = (mouseProcId == currentProcId || hwndUnderMouse == myHwnd);

                        try
                        {
                            var pt = this.PointFromScreen(new System.Windows.Point(globalMouse.X, globalMouse.Y));
                            bool isInsideWindowBounds = (pt.Y >= 0 && pt.Y <= this.ActualHeight && pt.X >= 0 && pt.X <= this.ActualWidth);

                            double topTitleH = _isFullscreen ? 40.0 : ((rowTitle?.ActualHeight > 0) ? rowTitle.ActualHeight : 40.0);
                            double bottomControlsH = _isFullscreen ? 105.0 : ((rowControls?.ActualHeight > 0) ? rowControls.ActualHeight : 105.0);
                            bool isInsideVideoArea = isInsideWindowBounds && (pt.Y >= topTitleH && pt.Y <= (this.ActualHeight - bottomControlsH));

                            bool isAnyOverlayOrDialogOpen = _isDialogOpen || popupSettings?.IsOpen == true || popupLibrary?.IsOpen == true || popupTracks?.IsOpen == true;

                            if (!isAnyOverlayOrDialogOpen && allowRightHover && isInsideVideoArea && isMouseOverOurApp && pt.X >= this.ActualWidth - 80 && !_isDrawerOpen)
                            {
                                ToggleSideDrawer(true);
                            }
                            else if (isAnyOverlayOrDialogOpen && _isDrawerOpen && !_isDrawerPinned)
                            {
                                ToggleSideDrawer(false);
                            }
                            else if (isInsideWindowBounds && pt.X < this.ActualWidth - 360 && _isDrawerOpen && !_isDrawerPinned)
                            {
                                ToggleSideDrawer(false);
                            }
                            else if ((!isInsideWindowBounds || !isMouseOverOurApp) && _isDrawerOpen && !_isDrawerPinned)
                            {
                                ToggleSideDrawer(false);
                            }
                        }
                        catch { }
                    }
                }

                if (dur > 0 && _smartFillEnabled)
                {
                    UpdateSmartFill();
                }
                // Sync FS labels and buttons
                txtTimeFS.Text = txtTime.Text;
                btnPlayFS.Content = btnPlay.Content;
                btnMuteFS.Content = btnMute.Content;
            }
            catch { /* ignore during shutdown */ }
        }

        private static string Fmt(double s)
        {
            if (s <= 0 || double.IsNaN(s) || double.IsInfinity(s)) return "--:--";
            var t = TimeSpan.FromSeconds(s);
            return t.TotalHours >= 1
                ? $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}"
                : $"{t.Minutes:D2}:{t.Seconds:D2}";
        }

        // ── Drag & Drop (WPF side – covers title bar & control bar) ──────
        private void Window_DragOver(object sender, WpfDragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(WpfDataFormats.FileDrop)
                ? WpfDragDropFx.Copy : WpfDragDropFx.None;
            e.Handled = true;
        }

        private void Window_Drop(object sender, WpfDragEventArgs e)
        {
            if (e.Data.GetDataPresent(WpfDataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(WpfDataFormats.FileDrop);
                if (files?.Length > 0) HandleDropPaths(files, fromDrop: true);
            }
            else if (e.Data.GetDataPresent(WpfDataFormats.UnicodeText) || e.Data.GetDataPresent(WpfDataFormats.Text))
            {
                string text = (e.Data.GetData(WpfDataFormats.UnicodeText) as string
                            ?? e.Data.GetData(WpfDataFormats.Text) as string)?.Trim() ?? "";
                if (!string.IsNullOrEmpty(text))
                {
                    if (text.StartsWith("\"") && text.EndsWith("\"") && text.Length > 2)
                        text = text.Substring(1, text.Length - 2);

                    if (IsNetworkUrl(text) || File.Exists(text) || Directory.Exists(text))
                    {
                        HandleDropPaths(new[] { text }, fromDrop: true);
                    }
                }
            }
        }

        /// <summary>
        /// RequestPlayFile: dedup guard — same path within PlayDedupMs is ignored.
        /// Call this from IPC Pipe, StartArgs, and anywhere outside of HandleDropPaths.
        /// </summary>
        private void RequestPlayFile(string path, bool forceAllInFolder = false)
        {
            if (string.IsNullOrEmpty(path) || (!IsNetworkUrl(path) && !File.Exists(path) && !Directory.Exists(path))) return;
            if (path == _lastPlayedPath && (DateTime.UtcNow - _lastPlayedTime).TotalMilliseconds < PlayDedupMs)
                return; // Duplicate trigger within dedup window — ignore
            _lastPlayedPath = path;
            _lastPlayedTime = DateTime.UtcNow;

            HandleDropPaths(new[] { path }, fromDrop: false, forceAllInFolder: forceAllInFolder);
        }

        private void HandleDropPaths(string[] paths, bool fromDrop = false, bool forceAllInFolder = false)
        {
            if (paths == null || paths.Length == 0) return;

            bool isPlaylistNonEmpty = PlaylistManager.Instance.Items.Count > 0;

            // Direct handling for Network URL (e.g. pasted or dropped)
            if (paths.Length == 1 && IsNetworkUrl(paths[0]))
            {
                string url = paths[0];
                if (isPlaylistNonEmpty)
                {
                    PlaylistManager.Instance.AddFilesBatch(new[] { url }, playImmediatelyFirst: false, deferSave: true);
                    var item = PlaylistManager.Instance.Items.FirstOrDefault(i => string.Equals(i.FilePath, url, StringComparison.OrdinalIgnoreCase));
                    if (item != null) PlaylistManager.Instance.SetCurrent(item);
                }
                else
                {
                    PlaylistManager.Instance.Clear();
                    PlaylistManager.Instance.AddFile(url, playImmediately: true);
                }
                PlayFile(url);
                return;
            }

            // ── Dedup gate for Drop events ────────────────────────────────
            // When user drags to exe icon, both NamedPipe and WM_DROPFILES fire.
            // The Pipe handler (RequestPlayFile) runs first and sets _lastPlayedPath.
            // The Drop handler arrives ~50ms later. If the same file was ALREADY
            // dispatched via Pipe, we must skip the ENTIRE function — especially
            // PlaylistManager.Clear() — otherwise Clear() stops MPV and the
            // deduped RequestPlayFile never restarts it → blank screen + audio-only.
            if (fromDrop && paths.Length == 1 && File.Exists(paths[0]))
            {
                string ext = Path.GetExtension(paths[0]).ToLower();
                string[] subExts = new[] { ".srt", ".ass", ".vtt", ".sub", ".sup" };
                string[] audioExts = new[] { ".m4a", ".aac", ".ac3", ".mp3", ".flac", ".wav" };

                if (subExts.Contains(ext))
                {
                    LoadExternalSubFile(paths[0]);
                    return;
                }
                if (audioExts.Contains(ext) && _hasMedia && !_isCurrentImage && !PlaylistManager.IsAudioFile(_currentPlayingFilePath))
                {
                    LoadExternalAudioFile(paths[0]);
                    return;
                }

                if (paths[0] == _lastPlayedPath &&
                    (DateTime.UtcNow - _lastPlayedTime).TotalMilliseconds < PlayDedupMs)
                    return; // Pipe already handled it; do NOT touch the playlist or MPV
            }

            string targetSingle = (paths.Length == 1 && File.Exists(paths[0])) ? paths[0] : "";
            bool isShiftHeld = forceAllInFolder || IsShiftKeyDown() || App.WasShiftHeldOnLaunch;
            string[] finalPaths = paths;
            if (!string.IsNullOrEmpty(targetSingle))
            {
                finalPaths = AutoMatchSeriesInFolder(targetSingle, isShiftHeld);
            }

            if (isPlaylistNonEmpty)
            {
                // Preserve existing playlist in memory: append new items and switch to target (in-memory only, no disk write)
                _ = PlaylistManager.Instance.AppendDirectoryAsync(
                    finalPaths,
                    targetOpenFile: targetSingle,
                    onPlayTarget: (targetPath) =>
                    {
                        PlayFile(targetPath);
                    }
                );
            }
            else
            {
                // Cold start or empty playlist: initialize playlist and save to disk
                _ = PlaylistManager.Instance.LoadDirectoryAsync(
                    finalPaths,
                    targetOpenFile: targetSingle,
                    onPlayTarget: (targetPath) =>
                    {
                        PlayFile(targetPath);
                    }
                );
            }

            if (finalPaths.Length > 1 && !string.IsNullOrEmpty(targetSingle))
            {
                if (isShiftHeld)
                {
                    ShowOsd(string.Format(I18nService.Instance["OsdAllFilesInFolderLoaded"], finalPaths.Length));
                }
                else
                {
                    ShowOsd(string.Format(I18nService.Instance["OsdAutoMatchedSeries"], finalPaths.Length));
                }
            }
        }

        private string[] AutoMatchSeriesInFolder(string targetFilePath, bool forceAllInFolder = false)
        {
            if (string.IsNullOrEmpty(targetFilePath) || !File.Exists(targetFilePath))
                return new[] { targetFilePath };

            string folder = Path.GetDirectoryName(targetFilePath) ?? "";
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                return new[] { targetFilePath };

            if (PlaylistManager.IsImageFile(targetFilePath))
            {
                var imageFiles = Directory.GetFiles(folder, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(PlaylistManager.IsImageFile)
                    .OrderBy(f => f, new NaturalStringComparer())
                    .ToArray();
                return imageFiles.Length > 0 ? imageFiles : new[] { targetFilePath };
            }

            string fileName = Path.GetFileName(targetFilePath);

            var validExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".mp4", ".mkv", ".avi", ".mov", ".flv", ".wmv", ".rmvb", ".rm", ".ts", ".m2ts", ".webm", ".m4v",
                ".mp3", ".flac", ".wav", ".aac", ".m4a", ".ogg", ".opus"
            };

            var allMediaFiles = Directory.GetFiles(folder)
                .Where(f => validExts.Contains(Path.GetExtension(f)))
                .ToList();

            if (allMediaFiles.Count <= 1)
                return new[] { targetFilePath };

            if (forceAllInFolder)
            {
                return allMediaFiles
                    .OrderBy(f => f, new NaturalStringComparer())
                    .ToArray();
            }

            var epRegex = new System.Text.RegularExpressions.Regex(
                @"(?<prefix>.*?)(?:[sS]\d+[eE]\d+|[eE][pP]?\d+|\bCD\d+\b|\bPart\d+\b|第\d+[集话話]|\[\d{1,3}\]|\(\d{1,3}\)|[_\-\s][A-Za-b1-9]\b|(?<=\D)\d{1,3}(?=\D*$))(?<suffix>.*)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            var match = epRegex.Match(fileName);
            if (!match.Success)
            {
                // Standalone file: do NOT dump unrelated files from folder
                return new[] { targetFilePath };
            }

            string rawPrefix = match.Groups["prefix"].Value;
            string prefix = System.Text.RegularExpressions.Regex.Replace(rawPrefix, @"[\.\s_\-]+$", "").Trim();

            List<string> seriesFiles = new List<string>();
            foreach (var file in allMediaFiles)
            {
                string fName = Path.GetFileName(file);
                var m = epRegex.Match(fName);
                if (m.Success)
                {
                    string fRawPrefix = m.Groups["prefix"].Value;
                    string fPrefix = System.Text.RegularExpressions.Regex.Replace(fRawPrefix, @"[\.\s_\-]+$", "").Trim();
                    if (string.Equals(prefix, fPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        seriesFiles.Add(file);
                    }
                }
            }

            if (seriesFiles.Count <= 1)
            {
                return new[] { targetFilePath };
            }

            return seriesFiles
                .OrderBy(f => f, new NaturalStringComparer())
                .ToArray();
        }


        // ── Window Chrome ─────────────────────────────────────────────────
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2) { ToggleMaximize(); return; }
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }
        

        private void Window_StateChanged(object? sender, EventArgs e)
        {
            // Update restore/maximize icon
            btnMax.Content = WindowState == WindowState.Maximized
                ? "\uE923"  // restore icon
                : "\uE922"; // maximize icon

            UpdateOuterBorder();
        }

        private void UpdateOuterBorder()
        {
            if (windowOuterBorder == null) return;
            if (_isPipMode || _isFullscreen || WindowState == WindowState.Maximized)
            {
                windowOuterBorder.BorderThickness = new Thickness(0);
                if (videoGrid != null) videoGrid.Margin = new Thickness(0);
            }
            else
            {
                windowOuterBorder.BorderThickness = new Thickness(1);
                if (videoGrid != null) videoGrid.Margin = new Thickness(1, 0, 1, 0);
            }
        }

        private void BtnMin_Click(object s, RoutedEventArgs e)
            => WindowState = WindowState.Minimized;

        private void BtnMax_Click(object s, RoutedEventArgs e)
            => ToggleMaximize();

        private void BtnClose_Click(object s, RoutedEventArgs e)
            => Close();

        private void ToggleMaximize()
        {
            if (_isFullscreen) return;
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal : WindowState.Maximized;
        }

        // ── Playback Controls ─────────────────────────────────────────────
        private void BtnOpen_Click(object? s, RoutedEventArgs e)
        {
            if (s is System.Windows.Controls.Button btn && btn.ContextMenu != null)
            {
                ApplyThemeToMenu(btn.ContextMenu);
                btn.ContextMenu.PlacementTarget = btn;
                btn.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
                btn.ContextMenu.IsOpen = true;
            }
        }

        private void MenuOpenFile_Click(object? sender, RoutedEventArgs e)
        {
            _isDialogOpen = true;
            try
            {
                var dlg = new WpfOpenFileDialog
                {
                    Title  = "选择要播放的媒体文件",
                    Multiselect = true,
                    Filter = "常用媒体与图片文件|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.flv;*.rmvb;*.ts;*.m2ts;*.webm;*.iso;*.mp3;*.flac;*.aac;*.wav;*.m4a;*.ogg;*.opus;*.jpg;*.jpeg;*.png;*.webp;*.bmp;*.gif;*.tiff;*.jfif|" +
                             "视频文件 (*.mp4;*.mkv;*.avi;*.mov;*.wmv...)|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.flv;*.rmvb;*.ts;*.m2ts;*.webm;*.iso|" +
                             "图片文件 (*.jpg;*.png;*.webp...)|*.jpg;*.jpeg;*.png;*.webp;*.bmp;*.gif;*.tiff;*.jfif|" +
                             "所有文件 (*.*)|*.*"
                };
                if (dlg.ShowDialog() == true && dlg.FileNames.Length > 0)
                {
                    HandleDropPaths(dlg.FileNames);
                }
            }
            finally
            {
                _isDialogOpen = false;
            }
        }

        private void MenuOpenFolder_Click(object? sender, RoutedEventArgs e)
        {
            _isDialogOpen = true;
            try
            {
                var folderDlg = new Microsoft.Win32.OpenFolderDialog
                {
                    Title = I18nService.Instance["DialogLoadFolder"],
                    Multiselect = true
                };
                if (folderDlg.ShowDialog() == true && folderDlg.FolderNames.Length > 0)
                {
                    HandleDropPaths(folderDlg.FolderNames);
                }
            }
            finally
            {
                _isDialogOpen = false;
            }
        }

        private void MenuOpenUrl_Click(object? sender, RoutedEventArgs e)
        {
            if (_isPipMode) return;
            if (popupOpenUrl != null)
            {
                if (popupOpenUrl.IsOpen)
                {
                    CloseOpenUrlOverlay();
                }
                else
                {
                    if (_isFullscreen)
                    {
                        SnapTopBar(false);
                        SnapBottomBar(false);
                    }
                    UpdateOpenUrlPopupSize();
                    popupOpenUrl.IsOpen = true;
                    overlayOpenUrl?.InitializeForOpen();
                    ApplyDimmedBrightness();

                    // Auto pause playback when modal overlay is open
                    PausePlaybackForModal();
                }
            }
        }

        private void MenuSaveStream_Click(object? sender, RoutedEventArgs e)
        {
            if (IsNetworkUrl(_currentPlayingFilePath))
            {
                if (StreamDownloadService.Instance.IsDownloading)
                {
                    string currentFile = Path.GetFileName(StreamDownloadService.Instance.CurrentSavePath);
                    ShowOsd(string.Format(I18nService.Instance["OsdStreamAlreadyDownloading"], currentFile));
                }
                else
                {
                    string saveDir = SettingsService.Instance.Config.NetworkStreamSaveDir;
                    string mediaTitle = MpvGet("media-title");
                    StreamDownloadService.Instance.StartDownload(_currentPlayingFilePath, saveDir, mediaTitle);
                }
            }
            else
            {
                ShowOsd(I18nService.Instance["OsdStreamSaveNotAvailable"]);
            }
        }

        private void TogglePlayPause()
        {
            if (_mpv == IntPtr.Zero) return;

            if (_isSkinIdleVideoPlaying)
            {
                MpvNative.mpv_command_string(_mpv, "cycle pause");
                bool isPaused = MpvGet("pause") == "yes";
                btnPlay.Content = isPaused ? "\uE102" : "\uE103";
                if (btnPlayFS != null) btnPlayFS.Content = btnPlay.Content;
                if (_mpvBgm != IntPtr.Zero && !string.IsNullOrEmpty(_currentBgmFile))
                {
                    MpvNative.mpv_set_property_string(_mpvBgm, "pause", isPaused ? "yes" : "no");
                }
                return;
            }

            string eof = MpvGet("eof-reached");
            double pos = MpvGetDouble("time-pos");
            double dur = MpvGetDouble("duration");

            if (eof == "yes")
            {
                if (dur > 0 && pos >= dur - 1.0)
                {
                    // Video (both local and online) reached true end: replay cleanly from start
                    MpvNative.mpv_command_string(_mpv, "seek 0 absolute");
                    MpvSetPropertyString("pause", "no");
                }
                else if (IsNetworkUrl(_currentPlayingFilePath))
                {
                    // Online stream: temporarily stalled at chunk boundary, kick demuxer to reconnect/fetch
                    MpvNative.mpv_command_string(_mpv, "seek 0 relative");
                    MpvSetPropertyString("pause", "no");
                }
                else
                {
                    MpvNative.mpv_command_string(_mpv, "cycle pause");
                }
            }
            else
            {
                MpvNative.mpv_command_string(_mpv, "cycle pause");
            }

            if (_mpvBgm != IntPtr.Zero && !string.IsNullOrEmpty(_currentBgmFile))
            {
                bool isPaused = MpvGet("pause") == "yes";
                MpvNative.mpv_set_property_string(_mpvBgm, "pause", isPaused ? "yes" : "no");
            }
        }

        private void BtnPlay_Click(object? s, RoutedEventArgs e)
        {
            TogglePlayPause();
        }

        private void MenuPlay_Click(object sender, RoutedEventArgs e)
        {
            TogglePlayPause();
            ShowOsd(MpvGet("pause") == "yes" ? I18nService.Instance["OsdPaused"] : I18nService.Instance["OsdPlaying"]);
        }

        private bool _useTransition = false;
        private void BtnToggleTransition_Click(object? sender, RoutedEventArgs e)
        {
            _useTransition = !_useTransition;
            var brush = _useTransition ? CyanTransitionBrush : GrayTransitionBrush;
            var tooltip = _useTransition ? "转场动画: 开" : "转场动画: 关";
            
            if (btnToggleTransition != null) {
                btnToggleTransition.Foreground = brush;
                btnToggleTransition.ToolTip = tooltip;
            }
            if (btnToggleTransitionFS != null) {
                btnToggleTransitionFS.Foreground = brush;
                btnToggleTransitionFS.ToolTip = tooltip;
            }
        }

        private void BtnPrev_Click(object? sender, RoutedEventArgs e)
        {
            var prev = PlaylistManager.Instance.GetPrev();
            if (prev != null) PlayFileWithTransition(prev.FilePath);
        }

        private void BtnNext_Click(object? sender, RoutedEventArgs e)
        {
            var next = PlaylistManager.Instance.GetNext();
            if (next != null) PlayFileWithTransition(next.FilePath);
        }

        private void BtnMute_Click(object? sender, RoutedEventArgs e)
        {
            if (_mpv == IntPtr.Zero) return;
            _isMuted = !_isMuted;
            MpvSetPropertyString("mute", _isMuted ? "yes" : "no");
            if (_mpvBgm != IntPtr.Zero && !string.IsNullOrEmpty(_currentBgmFile))
            {
                MpvNative.mpv_set_property_string(_mpvBgm, "mute", _isMuted ? "yes" : "no");
            }
            btnMute.Content = _isMuted ? "\xE74F" : "\xE767";
            btnMuteFS.Content = _isMuted ? "\xE74F" : "\xE767";
            ShowOsd(_isMuted ? I18nService.Instance["OsdMuted"] : I18nService.Instance["OsdUnmuted"]);
        }

        private void SmartFillMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.MenuItem mi && mi.Tag is string mode)
            {
                SetSmartFillMode(mode);
            }
        }

        private void BtnSmartFill_Click(object? sender, RoutedEventArgs e)
        {
            string nextMode = _smartFillMode switch
            {
                "none" => "normal",
                "normal" => "feather",
                "feather" => "none",
                _ => "normal"
            };
            SetSmartFillMode(nextMode);
        }

        private void SetSmartFillMode(string mode)
        {
            _smartFillMode = mode;
            if (_smartFillMode != "none" && _currentAspectRatio != "default")
            {
                SetAspectRatio("default");
            }
            UpdateSmartFillUI();
            UpdateSmartFill();
            SaveSettingsToConfig();
            
            string osd = _smartFillMode switch
            {
                "normal" => I18nService.Instance["OsdSmartFillNormal"],
                "feather" => I18nService.Instance["OsdSmartFillFeather"],
                _ => I18nService.Instance["OsdSmartFillOff"]
            };
            ShowOsd(osd);
        }

        private void BtnAutoCrop_Click(object? sender, RoutedEventArgs e)
        {
            if (_autoCropMode == "none") _autoCropMode = "preserve";
            else if (_autoCropMode == "preserve") _autoCropMode = "crop";
            else _autoCropMode = "none";

            OnAutoCropModeChanged();
        }

        // ── Screenshot ───────────────────────────────────────────────────
        private void BtnScreenshot_Click(object? sender, RoutedEventArgs e)
        {
            if (_mpv == IntPtr.Zero) return;
            try
            {
                var cfg = SettingsService.Instance.Config;
                bool saveFile = cfg.SaveScreenshotToFile || cfg.SaveScreenshotToMediaDir;
                bool copyClip = cfg.CopyScreenshotToClipboard;

                if (!saveFile && !copyClip) saveFile = true;

                string targetPath = "";
                if (saveFile)
                {
                    string folder = "";
                    if (cfg.SaveScreenshotToMediaDir)
                    {
                        try
                        {
                            string currentFilePath = !string.IsNullOrEmpty(_currentPlayingFilePath) 
                                ? _currentPlayingFilePath 
                                : PlaylistManager.Instance.GetCurrent()?.FilePath ?? "";

                            if (!string.IsNullOrEmpty(currentFilePath) && System.IO.File.Exists(currentFilePath))
                            {
                                string mediaDir = System.IO.Path.GetDirectoryName(currentFilePath) ?? "";
                                if (System.IO.Directory.Exists(mediaDir))
                                {
                                    string testFile = System.IO.Path.Combine(mediaDir, $".test_access_{Guid.NewGuid():N}.tmp");
                                    System.IO.File.WriteAllText(testFile, "test");
                                    System.IO.File.Delete(testFile);
                                    folder = mediaDir;
                                }
                            }
                        }
                        catch
                        {
                            // Permission denied or read-only drive -> fallback silently
                            folder = "";
                        }
                    }

                    if (string.IsNullOrEmpty(folder))
                    {
                        folder = cfg.ScreenshotPath;
                        if (string.IsNullOrWhiteSpace(folder) || folder.Contains("AnniPlayer", StringComparison.OrdinalIgnoreCase) || !System.IO.Directory.Exists(folder))
                        {
                            folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "AniPlayer");
                        }
                        if (!System.IO.Directory.Exists(folder)) System.IO.Directory.CreateDirectory(folder);
                    }

                    // Automatic naming rule: VideoName + PlaybackProgress (e.g. Movie_01-23-45.png)
                    string mediaFile = !string.IsNullOrEmpty(_currentPlayingFilePath)
                        ? _currentPlayingFilePath
                        : PlaylistManager.Instance.GetCurrent()?.FilePath ?? "";

                    string videoName = "AniPlayer";
                    if (!string.IsNullOrEmpty(mediaFile))
                    {
                        string rawName = System.IO.Path.GetFileNameWithoutExtension(mediaFile);
                        if (!string.IsNullOrWhiteSpace(rawName))
                        {
                            foreach (char c in System.IO.Path.GetInvalidFileNameChars())
                            {
                                rawName = rawName.Replace(c, '_');
                            }
                            videoName = rawName.Trim();
                        }
                    }

                    double currentPos = MpvGetDouble("time-pos");
                    TimeSpan posTs = TimeSpan.FromSeconds(Math.Max(0, currentPos));
                    string timeStampStr = $"{(int)posTs.TotalHours:D2}-{posTs.Minutes:D2}-{posTs.Seconds:D2}";

                    string baseFileName = $"{videoName}_{timeStampStr}";
                    string fileName = $"{baseFileName}.png";
                    targetPath = System.IO.Path.Combine(folder, fileName).Replace("\\", "/");

                    int counter = 1;
                    while (System.IO.File.Exists(targetPath))
                    {
                        fileName = $"{baseFileName}_{counter}.png";
                        targetPath = System.IO.Path.Combine(folder, fileName).Replace("\\", "/");
                        counter++;
                    }
                }
                else
                {
                    string tempFolder = System.IO.Path.GetTempPath();
                    targetPath = System.IO.Path.Combine(tempFolder, $"AniPlayer_temp_{Guid.NewGuid():N}.png").Replace("\\", "/");
                }

                MpvNative.mpv_command_string(_mpv, $"screenshot-to-file \"{targetPath}\"");

                // Wait briefly for mpv to flush the screenshot file to disk
                for (int i = 0; i < 20 && !System.IO.File.Exists(targetPath); i++)
                {
                    System.Threading.Thread.Sleep(15);
                }

                if (copyClip && System.IO.File.Exists(targetPath))
                {
                    try
                    {
                        var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                        bitmap.UriSource = new Uri(targetPath, UriKind.Absolute);
                        bitmap.EndInit();
                        bitmap.Freeze();

                        System.Windows.Clipboard.SetImage(bitmap);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Copy to clipboard failed: {ex.Message}");
                    }
                }

                if (!saveFile && System.IO.File.Exists(targetPath))
                {
                    try { System.IO.File.Delete(targetPath); } catch { }
                }

                if (saveFile && copyClip)
                    ShowOsd($"{I18nService.Instance["OsdScreenshotSavedAndCopied"]}: {targetPath}");
                else if (copyClip)
                    ShowOsd(I18nService.Instance["OsdScreenshotCopied"]);
                else
                    ShowOsd(string.Format(I18nService.Instance["OsdScreenshotSaved"], targetPath));
            }
            catch (Exception ex)
            {
                ShowOsd(string.Format(I18nService.Instance["OsdScreenshotFailed"], ex.Message));
            }
        }

        // ── A-B Loop ─────────────────────────────────────────────────────
        private void BtnAbLoop_Click(object? sender, RoutedEventArgs e)
        {
            ToggleAbLoop();
        }

        private void ToggleAbLoop()
        {
            if (_mpv == IntPtr.Zero) return;
            string loopA = MpvGet("ab-loop-a");
            string loopB = MpvGet("ab-loop-b");

            if (loopA == "no" || string.IsNullOrEmpty(loopA))
            {
                MpvNative.mpv_command_string(_mpv, "ab-loop");
                double pos = MpvGetDouble("time-pos");
                ShowOsd(string.Format(I18nService.Instance["OsdAbLoopA"], Fmt(pos)));
            }
            else if (loopB == "no" || string.IsNullOrEmpty(loopB))
            {
                MpvNative.mpv_command_string(_mpv, "ab-loop");
                double posA = MpvGetDouble("ab-loop-a");
                double posB = MpvGetDouble("time-pos");
                ShowOsd(string.Format(I18nService.Instance["OsdAbLoopAB"], Fmt(posA), Fmt(posB)));
            }
            else
            {
                MpvNative.mpv_command_string(_mpv, "ab-loop");
                ShowOsd(I18nService.Instance["OsdAbLoopClear"]);
            }
            UpdateAbMarkers();
        }

        private async void ExportAbLoopClip(bool promptSaveAs = false)
        {
            if (_mpv == IntPtr.Zero) return;

            string loopA = MpvGet("ab-loop-a");
            string loopB = MpvGet("ab-loop-b");

            if (loopA == "no" || string.IsNullOrEmpty(loopA) || loopB == "no" || string.IsNullOrEmpty(loopB))
            {
                ShowOsd(I18nService.Instance.CurrentLanguage == "en-US" ? "Please set A-B loop points first!" : "请先设定 A-B 循环起点与终点！");
                return;
            }

            if (!double.TryParse(loopA, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double startSec) ||
                !double.TryParse(loopB, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double endSec))
            {
                return;
            }

            if (endSec <= startSec)
            {
                ShowOsd(I18nService.Instance.CurrentLanguage == "en-US" ? "Invalid A-B loop range!" : "A-B 循环时间区间无效！");
                return;
            }

            string inputPath = !string.IsNullOrEmpty(_currentPlayingFilePath) 
                ? _currentPlayingFilePath 
                : PlaylistManager.Instance.GetCurrent()?.FilePath ?? "";
            if (string.IsNullOrEmpty(inputPath) || !File.Exists(inputPath)) return;
            string ext = Path.GetExtension(inputPath);
            if (string.IsNullOrEmpty(ext)) ext = ".mp4";

            string baseName = Path.GetFileNameWithoutExtension(inputPath);
            TimeSpan tsA = TimeSpan.FromSeconds(startSec);
            TimeSpan tsB = TimeSpan.FromSeconds(endSec);
            string timeRangeStr = $"{tsA:hh\\-mm\\-ss}_to_{tsB:hh\\-mm\\-ss}";
            string defaultFileName = $"{baseName}_Clip_{timeRangeStr}{ext}";

            string outPath = "";
            var cfg = SettingsService.Instance.Config;

            if (promptSaveAs)
            {
                string initDir = "";
                if (cfg.SaveScreenshotToMediaDir && !string.IsNullOrEmpty(inputPath) && File.Exists(inputPath))
                {
                    initDir = Path.GetDirectoryName(inputPath) ?? "";
                }
                if (string.IsNullOrEmpty(initDir) || !Directory.Exists(initDir))
                {
                    initDir = cfg.ScreenshotPath;
                    if (string.IsNullOrWhiteSpace(initDir) || !Directory.Exists(initDir))
                    {
                        initDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "AniPlayer");
                    }
                }

                var saveDlg = new Microsoft.Win32.SaveFileDialog
                {
                    Title = I18nService.Instance["MenuExportClipAs"],
                    Filter = $"{ext.TrimStart('.').ToUpper()} 文件|*{ext}|所有文件|*.*",
                    FileName = defaultFileName,
                    InitialDirectory = Directory.Exists(initDir) ? initDir : null
                };
                if (saveDlg.ShowDialog() != true) return;
                outPath = saveDlg.FileName;
            }
            else
            {
                string folder = "";

                if (cfg.SaveScreenshotToMediaDir)
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(inputPath) && File.Exists(inputPath))
                        {
                            string mediaDir = Path.GetDirectoryName(inputPath) ?? "";
                            if (Directory.Exists(mediaDir))
                            {
                                string testFile = Path.Combine(mediaDir, $".test_access_{Guid.NewGuid():N}.tmp");
                                File.WriteAllText(testFile, "test");
                                File.Delete(testFile);
                                folder = mediaDir;
                            }
                        }
                    }
                    catch
                    {
                        folder = "";
                    }
                }

                if (string.IsNullOrEmpty(folder))
                {
                    folder = cfg.ScreenshotPath;
                    if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                    {
                        folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "AniPlayer");
                    }
                    if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                }

                outPath = Path.Combine(folder, defaultFileName);
            }

            ShowOsd(I18nService.Instance["OsdExportClipProgress"]);

            await Task.Run(() =>
            {
                try
                {
                    string ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
                    bool hasFFmpeg = File.Exists(ffmpegPath);
                    if (!hasFFmpeg)
                    {
                        try
                        {
                            using var checkProc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = "ffmpeg",
                                Arguments = "-version",
                                CreateNoWindow = true,
                                UseShellExecute = false
                            });
                            if (checkProc != null && checkProc.WaitForExit(1500) && checkProc.ExitCode == 0)
                            {
                                ffmpegPath = "ffmpeg";
                                hasFFmpeg = true;
                            }
                        }
                        catch { }
                    }

                    string sSec = startSec.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    string eSec = endSec.ToString(System.Globalization.CultureInfo.InvariantCulture);

                    if (hasFFmpeg)
                    {
                        bool isRmvb = ext.Equals(".rmvb", StringComparison.OrdinalIgnoreCase) || ext.Equals(".rm", StringComparison.OrdinalIgnoreCase);
                        string copyArgs = $"-ss {sSec} -to {eSec} -i \"{inputPath}\" -c copy -y \"{outPath}\"";
                        string encodeArgs = $"-ss {sSec} -to {eSec} -i \"{inputPath}\" -c:v libx264 -preset ultrafast -crf 22 -c:a aac -y \"{outPath}\"";

                        var startInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = ffmpegPath,
                            Arguments = isRmvb ? encodeArgs : copyArgs,
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };

                        using (var proc = System.Diagnostics.Process.Start(startInfo))
                        {
                            proc?.WaitForExit(30000);
                        }

                        if (!File.Exists(outPath) || new FileInfo(outPath).Length == 0)
                        {
                            startInfo.Arguments = encodeArgs;
                            using var procFallback = System.Diagnostics.Process.Start(startInfo);
                            procFallback?.WaitForExit(45000);
                        }
                    }
                    else
                    {
                        // ── Pure Native MPV Fallback (No ffmpeg.exe needed) ─────
                        // Uses libmpv-2.dll native dump-cache command directly in C-API
                        string cleanOut = outPath.Replace("\\", "/");
                        Dispatcher.Invoke(() =>
                        {
                            if (_mpv != IntPtr.Zero)
                            {
                                MpvNative.mpv_command_string(_mpv, $"dump-cache {sSec} {eSec} \"{cleanOut}\"");
                            }
                        });
                        System.Threading.Thread.Sleep(1500);
                    }

                    if (File.Exists(outPath) && new FileInfo(outPath).Length > 0)
                    {
                        Dispatcher.Invoke(() => ShowOsd(string.Format(I18nService.Instance["OsdExportClipSuccess"], outPath)));
                    }
                    else
                    {
                        Dispatcher.Invoke(() => ShowOsd(string.Format(I18nService.Instance["OsdExportClipError"], "Export failed")));
                    }
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => ShowOsd(string.Format(I18nService.Instance["OsdExportClipError"], ex.Message)));
                }
            });

            // Automatically cancel A-B loop after triggering save
            MpvSetPropertyString("ab-loop-a", "no");
            MpvSetPropertyString("ab-loop-b", "no");
            UpdateAbMarkers();
        }

        private void MenuExportClip_Click(object sender, RoutedEventArgs e)
        {
            ExportAbLoopClip(promptSaveAs: false);
        }

        private void MenuExportClipAs_Click(object sender, RoutedEventArgs e)
        {
            ExportAbLoopClip(promptSaveAs: true);
        }

        // ── Speed Playback ───────────────────────────────────────────────
        private double _currentSpeed = 1.0;
        private readonly double[] _speedPresetList = new[] { 0.25, 0.5, 0.75, 1.0, 1.25, 1.5, 2.0, 3.0 };

        private void SetPlaybackSpeed(double speed)
        {
            _currentSpeed = Math.Round(speed, 2);
            MpvSetPropertyString("speed", _currentSpeed.ToString(System.Globalization.CultureInfo.InvariantCulture));

            // Enable GPU smooth motion interpolation during slow motion (< 1.0x)
            if (_currentSpeed < 1.0)
            {
                MpvSetPropertyString("video-sync", "display-resample");
                MpvSetPropertyString("interpolation", "yes");
                MpvSetPropertyString("tscale", "oversample");
            }
            else
            {
                MpvSetPropertyString("video-sync", "audio");
                MpvSetPropertyString("interpolation", "no");
            }

            string speedStr = $"{_currentSpeed:0.##}x";
            if (btnSpeed != null) btnSpeed.Content = speedStr;
            if (btnSpeedFS != null) btnSpeedFS.Content = speedStr;
            ShowOsd(string.Format(I18nService.Instance["OsdSpeed"], _currentSpeed.ToString("0.##")));
        }

        private void BtnSpeed_Click(object? sender, RoutedEventArgs e)
        {
            int idx = Array.IndexOf(_speedPresetList, _currentSpeed);
            double nextSpeed = (idx >= 0 && idx < _speedPresetList.Length - 1) ? _speedPresetList[idx + 1] : _speedPresetList[0];
            SetPlaybackSpeed(nextSpeed);
        }

        private void SpeedMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.MenuItem item && double.TryParse(item.Tag?.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double speed))
            {
                SetPlaybackSpeed(speed);
            }
        }

        private string _currentAspectRatio = "default";

        private void AspectRatioMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.MenuItem item && item.Tag is string tag)
            {
                SetAspectRatio(tag);
            }
        }

        private void SetAspectRatio(string tag)
        {
            if (tag != "default" && _smartFillEnabled)
            {
                _smartFillEnabled = false;
                UpdateSmartFillUI();
                UpdateSmartFill();
                SaveSettingsToConfig();
            }
            _currentAspectRatio = tag;
            var i18n = I18nService.Instance;
            if (tag == "default")
            {
                MpvSetPropertyString("keepaspect", "yes");
                MpvSetPropertyString("video-aspect-override", "-1");
                ShowOsd(i18n["AspectDefault"]);
            }
            else if (tag == "stretch")
            {
                MpvSetPropertyString("keepaspect", "no");
                MpvSetPropertyString("video-aspect-override", "-1");
                ShowOsd(i18n["AspectStretch"]);
            }
            else
            {
                MpvSetPropertyString("keepaspect", "yes");
                MpvSetPropertyString("video-aspect-override", tag);
                ShowOsd(i18n["AspectRatioHeader"] + ": " + tag);
            }
            UpdateAspectRatioMenuChecks();

            if (!_isFullscreen && !_isPipMode && tag != "stretch")
            {
                double ratio = GetTargetAspectRatio();
                if (ratio > 0)
                {
                    double nonVideoH = (rowTitle?.ActualHeight ?? 40) + (rowControls?.ActualHeight ?? 105);
                    if (nonVideoH <= 0) nonVideoH = 145.0;
                    double newH = Math.Round((this.Width / ratio) + nonVideoH);
                    this.Height = newH;
                }
            }
        }

        private void UpdateAspectRatioMenuChecks()
        {
            if (this.Resources["VideoContextMenu"] is System.Windows.Controls.ContextMenu menu)
            {
                foreach (var item in menu.Items)
                {
                    if (item is System.Windows.Controls.MenuItem mi && mi.Name == "menuAspectRatio")
                    {
                        foreach (var sub in mi.Items)
                        {
                            if (sub is System.Windows.Controls.MenuItem subItem)
                            {
                                subItem.IsChecked = ((string)subItem.Tag == _currentAspectRatio);
                            }
                        }
                        break;
                    }
                }
            }
        }

        private void ApplyVideoSharpening(bool enable)
        {
            if (_mpv == IntPtr.Zero) return;
            try
            {
                // If disabled, or if playing pure audio/image slideshow, or if no media is loaded, ALWAYS clear and bypass shaders
                bool isAudioOrNoMediaOrImage = string.IsNullOrEmpty(_currentPlayingFilePath)
                                     || PlaylistManager.IsAudioFile(_currentPlayingFilePath)
                                     || _isCurrentImage
                                     || !_hasMedia;

                if (!enable || isAudioOrNoMediaOrImage)
                {
                    // Clear enhancement shaders and restoration options
                    MpvSetPropertyString("glsl-shaders", "");
                    MpvSetPropertyString("deband", "no");
                    MpvSetPropertyString("scale", "bilinear");
                    MpvSetPropertyString("cscale", "bilinear");
                    MpvSetPropertyString("scale-antiring", "0");
                    MpvSetPropertyString("sharpen", "0");
                    return;
                }

                // Check video dimensions
                double vw = MpvGetDouble("video-params/w");
                double vh = MpvGetDouble("video-params/h");
                if (vw <= 0 || vh <= 0)
                {
                    vw = MpvGetDouble("width");
                    vh = MpvGetDouble("height");
                }

                if (vw <= 0 || vh <= 0)
                {
                    // No valid video dimensions yet — clear shaders
                    MpvSetPropertyString("glsl-shaders", "");
                    MpvSetPropertyString("deband", "no");
                    MpvSetPropertyString("scale", "bilinear");
                    MpvSetPropertyString("cscale", "bilinear");
                    MpvSetPropertyString("sharpen", "0");
                    return;
                }

                // Only apply on videos strictly below 720P (< 1280x720 landscape or < 720x1280 portrait)
                double minDim = Math.Min(vw, vh);
                double maxDim = Math.Max(vw, vh);
                bool isBelow720P = (minDim < 720 && maxDim < 1280);
                if (!isBelow720P)
                {
                    // High-res video (>= 720P) — bypass enhancement pipeline to save 100% compute
                    MpvSetPropertyString("glsl-shaders", "");
                    MpvSetPropertyString("deband", "no");
                    MpvSetPropertyString("scale", "bilinear");
                    MpvSetPropertyString("cscale", "bilinear");
                    MpvSetPropertyString("sharpen", "0");
                    return;
                }

                // 3-layer pipeline for <720P: Deblock + FSRCNNX Lite + AMD CAS
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string fsrcnnxPath = System.IO.Path.Combine(baseDir, "shaders", "FSRCNNX_Lite.glsl").Replace("\\", "/");
                string casPath = System.IO.Path.Combine(baseDir, "shaders", "CAS.glsl").Replace("\\", "/");

                var shaderList = new List<string>();
                if (System.IO.File.Exists(fsrcnnxPath)) shaderList.Add(fsrcnnxPath);
                if (System.IO.File.Exists(casPath)) shaderList.Add(casPath);

                string shadersProp = string.Join(";", shaderList);
                if (!string.IsNullOrEmpty(shadersProp))
                {
                    MpvSetPropertyString("glsl-shaders", shadersProp);
                }
                else
                {
                    MpvSetPropertyString("sharpen", "0.35");
                }

                // Step 1: Deblocking + Debanding (smooth compression artifacts and color banding)
                MpvSetPropertyString("deband", "yes");
                MpvSetPropertyString("deband-iterations", "1");
                MpvSetPropertyString("deband-threshold", "32");
                MpvSetPropertyString("deband-range", "16");

                // Step 2 & 3: High quality anti-ringing lanczos/spline36 scalers
                MpvSetPropertyString("scale", "ewa_lanczos");
                MpvSetPropertyString("cscale", "spline36");
                MpvSetPropertyString("scale-antiring", "0.7");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ApplyVideoSharpening] {ex.Message}");
            }
        }

        private void MenuVideoSharpen_Click(object? sender, RoutedEventArgs e)
        {
            bool enable = !SettingsService.Instance.Config.VideoSharpening;
            SettingsService.Instance.Config.VideoSharpening = enable;
            SettingsService.Instance.Save();
            ApplyVideoSharpening(enable);
            var i18n = I18nService.Instance;
            ShowOsd(enable ? i18n["OsdSharpenOn"] : i18n["OsdSharpenOff"]);
        }

        public void ApplyAudioNightMode(bool enable, bool showOsd = true)
        {
            SettingsService.Instance.Config.AudioNightMode = enable;
            SettingsService.Instance.Save();
            try
            {
                if (_mpv != IntPtr.Zero)
                {
                    if (enable)
                    {
                        // FFmpeg Dynamic Audio Normalizer (dynaudnorm):
                        // Amplifies quiet human dialogue and dynamically tames loud gunshots/explosions/sound effects
                        MpvSetPropertyString("af", "lavfi=[dynaudnorm=f=150:g=15:maxgain=12:m=4.0]");
                    }
                    else
                    {
                        MpvSetPropertyString("af", "");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ApplyAudioNightMode] {ex.Message}");
            }

            if (overlayTracks != null)
            {
                overlayTracks.SetNightMode(enable);
            }
            if (showOsd)
            {
                var i18n = I18nService.Instance;
                ShowOsd(enable ? i18n["OsdNightModeOn"] : i18n["OsdNightModeOff"]);
            }
        }


        // ── Volume and Brightness Popup Hover Handlers ──────────────────────
        private System.Windows.Threading.DispatcherTimer? _popupHideTimer;

        private void CloseInactiveHoverPopups()
        {
            try
            {
                // Do not auto-close while user is actively holding down left mouse button (e.g. dragging slider)
                if (System.Windows.Input.Mouse.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
                {
                    return;
                }

                var popups = new[] { popupVolume, popupBrightness, popupVolumeFS, popupBrightnessFS };
                foreach (var p in popups)
                {
                    if (p != null && p.IsOpen)
                    {
                        if (!IsMouseNearPopup(p, 8.0))
                        {
                            p.IsOpen = false;
                        }
                    }
                }
            }
            catch { }
        }

        private bool IsMouseNearPopup(System.Windows.Controls.Primitives.Popup? popup, double tolerance = 8.0)
        {
            if (popup == null || !popup.IsOpen) return false;
            try
            {
                if (popup.Child is System.Windows.FrameworkElement child && child.IsLoaded)
                {
                    if (child.IsMouseOver || child.IsKeyboardFocusWithin) return true;
                    if (child.ActualWidth > 0 && child.ActualHeight > 0)
                    {
                        var screenPt = child.PointToScreen(new System.Windows.Point(0, 0));
                        var cur = System.Windows.Forms.Cursor.Position;
                        var bounds = new System.Windows.Rect(
                            screenPt.X - tolerance,
                            screenPt.Y - tolerance,
                            child.ActualWidth + tolerance * 2,
                            child.ActualHeight + tolerance * 2
                        );
                        if (bounds.Contains(cur.X, cur.Y)) return true;
                    }
                }
                if (popup.PlacementTarget is System.Windows.FrameworkElement target && target.IsLoaded)
                {
                    if (target.IsMouseOver || target.IsKeyboardFocusWithin) return true;
                    if (target.ActualWidth > 0 && target.ActualHeight > 0)
                    {
                        var screenPt = target.PointToScreen(new System.Windows.Point(0, 0));
                        var cur = System.Windows.Forms.Cursor.Position;
                        var bounds = new System.Windows.Rect(
                            screenPt.X - tolerance,
                            screenPt.Y - tolerance,
                            target.ActualWidth + tolerance * 2,
                            target.ActualHeight + tolerance * 2
                        );
                        if (bounds.Contains(cur.X, cur.Y)) return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private void ShowPopup(System.Windows.Controls.Primitives.Popup popup)
        {
            if (popup == null) return;
            // Mutual exclusion: Close sibling popup on the same bar immediately so they never overlap
            if (popup == popupVolume && popupBrightness != null && popupBrightness.IsOpen)
                popupBrightness.IsOpen = false;
            else if (popup == popupBrightness && popupVolume != null && popupVolume.IsOpen)
                popupVolume.IsOpen = false;
            else if (popup == popupVolumeFS && popupBrightnessFS != null && popupBrightnessFS.IsOpen)
                popupBrightnessFS.IsOpen = false;
            else if (popup == popupBrightnessFS && popupVolumeFS != null && popupVolumeFS.IsOpen)
                popupVolumeFS.IsOpen = false;

            popup.IsOpen = true;
        }

        private void HidePopupWithDelay(System.Windows.Controls.Primitives.Popup popup)
        {
            if (_popupHideTimer == null)
            {
                _popupHideTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(400)
                };
                _popupHideTimer.Tick += (s, e) =>
                {
                    _popupHideTimer.Stop();
                    CloseInactiveHoverPopups();
                };
            }

            _popupHideTimer.Stop();
            _popupHideTimer.Start();
        }

        private void Volume_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e) { ShowPopup(popupVolume); }
        private void Volume_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) { HidePopupWithDelay(popupVolume); }
        private void Brightness_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e) { ShowPopup(popupBrightness); }
        private void Brightness_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) { HidePopupWithDelay(popupBrightness); }
        
        private void VolumeFS_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e) { ShowPopup(popupVolumeFS); }
        private void VolumeFS_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) { HidePopupWithDelay(popupVolumeFS); }
        private void BrightnessFS_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e) { ShowPopup(popupBrightnessFS); }
        private void BrightnessFS_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) { HidePopupWithDelay(popupBrightnessFS); }

        private void SliderBrightness_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (txtOsd == null) return;
            if (sender is System.Windows.Controls.Slider slider)
            {
                if (sender == sliderBrightness && sliderBrightnessFS != null && Math.Abs(sliderBrightnessFS.Value - e.NewValue) > 0.01)
                    sliderBrightnessFS.Value = e.NewValue;
                else if (sender == sliderBrightnessFS && sliderBrightness != null && Math.Abs(sliderBrightness.Value - e.NewValue) > 0.01)
                    sliderBrightness.Value = e.NewValue;

                int newBrightness = (int)slider.Value;
                SettingsService.Instance.Config.BaseBrightness = newBrightness;
                
                // Only apply directly if no overlay is currently open
                if ((popupSettings == null || !popupSettings.IsOpen) && (popupLibrary == null || !popupLibrary.IsOpen))
                {
                    ApplyBaseBrightness();
                }

                if (!_timerUpdating && (sender == sliderBrightness || sender == sliderBrightnessFS))
                {
                    int displayPct = 100 + newBrightness;
                    string fmtStr = I18nService.Instance["OsdBrightness"];
                    if (string.IsNullOrEmpty(fmtStr) || fmtStr.StartsWith("[")) fmtStr = "亮度: {0}%";
                    ShowOsd(string.Format(fmtStr, displayPct));
                }
            }
        }

        private void BrightnessReset_Click(object? sender, RoutedEventArgs e)
        {
            if (sliderBrightness != null) sliderBrightness.Value = 0;
            if (sliderBrightnessFS != null) sliderBrightnessFS.Value = 0;
            SettingsService.Instance.Config.BaseBrightness = 0;

            if ((popupSettings == null || !popupSettings.IsOpen) && (popupLibrary == null || !popupLibrary.IsOpen))
            {
                ApplyBaseBrightness();
            }

            string fmtStr = I18nService.Instance["OsdBrightness"];
            if (string.IsNullOrEmpty(fmtStr) || fmtStr.StartsWith("[")) fmtStr = "亮度: {0}%";
            ShowOsd(string.Format(fmtStr, 100));
        }

        private void StepSpeed(bool increase)
        {
            int idx = Array.IndexOf(_speedPresetList, _currentSpeed);
            if (idx < 0) idx = 3;
            if (increase)
            {
                if (idx < _speedPresetList.Length - 1) SetPlaybackSpeed(_speedPresetList[idx + 1]);
            }
            else
            {
                if (idx > 0) SetPlaybackSpeed(_speedPresetList[idx - 1]);
            }
        }



        // ── Picture-in-Picture Mode ───────────────────────────────────────
        private bool _isPipMode = false;
        private bool _isResizingPip = false;
        private System.Windows.Point _pipResizeStartMouse;
        private double _pipResizeStartW;
        private double _pipResizeStartH;
        private double _pipAspectRatio = 16.0 / 9.0;

        private void BtnPip_Click(object? sender, RoutedEventArgs e)
        {
            TogglePipMode();
        }

        private double _prePipW;
        private double _prePipH;
        private double _prePipLeft;
        private double _prePipTop;
        private WindowState _prePipState;

        private void ToggleTopmost()
        {
            var cfg = SettingsService.Instance.Config;
            cfg.AlwaysOnTop = !cfg.AlwaysOnTop;
            SettingsService.Instance.Save();
            RestoreTopmostState();
            ShowOsd(cfg.AlwaysOnTop ? I18nService.Instance["OsdAlwaysOnTopOn"] : I18nService.Instance["OsdAlwaysOnTopOff"]);
        }

        private async void TogglePipMode()
        {
            if (!_isPipMode && _isFullscreen)
            {
                ToggleFullscreen();
                await Task.Delay(150);
            }

            _isPipMode = !_isPipMode;
            if (_isPipMode)
            {
                CloseSettingsOverlay();
                CloseLibraryOverlay();
                if (popupSideDrawer != null) popupSideDrawer.IsOpen = false;
                _isDrawerOpen = false;
                _isDrawerPinned = false;

                // Save previous state
                _prePipState = this.WindowState;
                if (this.WindowState == WindowState.Maximized)
                {
                    this.WindowState = WindowState.Normal;
                }
                _prePipW = this.Width;
                _prePipH = this.Height;
                _prePipLeft = this.Left;
                _prePipTop = this.Top;

                // Hide title bar, keep control bar
                rowTitle.Height = new GridLength(0);
                titleBar.Visibility = Visibility.Collapsed;
                if (popupPipClose != null)
                {
                    popupPipClose.IsOpen = false;
                }
                if (btnPipCloseBar != null) btnPipCloseBar.Visibility = Visibility.Visible;

                // Hide advanced controls for PiP
                btnOpen.Visibility = Visibility.Collapsed;
                btnLibrary.Visibility = Visibility.Collapsed;
                btnScreenshot.Visibility = Visibility.Collapsed;
                btnSpeed.Visibility = Visibility.Collapsed;
                btnSmartFill.Visibility = Visibility.Collapsed;
                btnAutoCrop.Visibility = Visibility.Collapsed;
                txtClock.Visibility = Visibility.Collapsed;
                btnFullscreen.Visibility = Visibility.Collapsed;
                if (panelBrightness != null) panelBrightness.Visibility = Visibility.Collapsed;

                ccControls.LayoutTransform = System.Windows.Media.Transform.Identity;
                ApplyControlBarMode(true);

                this.Topmost = true;

                bool isAudio = PlaylistManager.IsAudioFile(_currentPlayingFilePath);
                double controlH = 46;
                double pipW = 380; // Standard unified PiP default width (380px)

                if (isAudio)
                {
                    _pipAspectRatio = 16.0 / 9.0;
                    double pipH = Math.Round(pipW / _pipAspectRatio) + controlH;

                    this.MinWidth = 220;
                    this.MinHeight = Math.Round(220 / _pipAspectRatio) + controlH;
                    this.MaxWidth = 960;
                    this.MaxHeight = Math.Round(960 / _pipAspectRatio) + controlH;

                    this.Width = pipW;
                    this.Height = pipH;

                    UpdateAudioBannerView();
                    StartVinylDiscAnimation();
                }
                else
                {
                    double vw = MpvGetDouble("width");
                    double vh = MpvGetDouble("height");
                    if (vw > 0 && vh > 0)
                    {
                        double r = vw / vh;
                        _pipAspectRatio = (r < 1.0) ? Math.Max(r, 4.0 / 3.0) : r;
                    }
                    else
                    {
                        _pipAspectRatio = 16.0 / 9.0;
                    }

                    double pipH = Math.Round(pipW / _pipAspectRatio) + controlH;

                    // Restrict MinWidth/MinHeight (220) and MaxWidth/MaxHeight (960)
                    this.MinWidth = 220;
                    this.MinHeight = Math.Round(220 / _pipAspectRatio) + controlH;
                    this.MaxWidth = 960;
                    this.MaxHeight = Math.Round(960 / _pipAspectRatio) + controlH;

                    this.Width = pipW;
                    this.Height = pipH;
                }

                // Move to bottom right
                this.Left = SystemParameters.WorkArea.Right - this.Width - 20;
                this.Top = SystemParameters.WorkArea.Bottom - this.Height - 20;

                RestoreTopmostState();

                if (btnPip != null) btnPip.Content = "\uE8A8";
                if (btnPipFS != null) btnPipFS.Content = "\uE8A8";

                ShowOsd(I18nService.Instance["OsdPipOn"]);
            }
            else
            {
                if (btnPip != null) btnPip.Content = "\uE8A7";
                if (btnPipFS != null) btnPipFS.Content = "\uE8A7";
                rowTitle.Height = GridLength.Auto;
                titleBar.Visibility = Visibility.Visible;
                if (popupPipClose != null) popupPipClose.IsOpen = false;
                if (btnPipCloseBar != null) btnPipCloseBar.Visibility = Visibility.Collapsed;
                if (PlaylistManager.IsAudioFile(_currentPlayingFilePath))
                {
                    UpdateAudioBannerView();
                    StartVinylDiscAnimation();
                }

                // Restore advanced controls
                btnOpen.Visibility = Visibility.Visible;
                btnLibrary.Visibility = Visibility.Visible;
                btnScreenshot.Visibility = Visibility.Visible;
                btnSpeed.Visibility = Visibility.Visible;
                btnSmartFill.Visibility = Visibility.Visible;
                btnAutoCrop.Visibility = Visibility.Visible;
                txtClock.Visibility = Visibility.Visible;
                btnFullscreen.Visibility = Visibility.Visible;
                if (panelBrightness != null) panelBrightness.Visibility = Visibility.Visible;

                ccControls.LayoutTransform = System.Windows.Media.Transform.Identity;
                ApplyControlBarMode(false);

                // Restore MinWidth/MinHeight and MaxWidth/MaxHeight
                this.MinWidth = 480;
                this.MinHeight = 320;
                this.MaxWidth = double.PositiveInfinity;
                this.MaxHeight = double.PositiveInfinity;

                this.WindowState = _prePipState;
                RestoreDefaultWindowSizeAndCenter();

                RestoreTopmostState();

                _videoSizeSet = false;

                ShowOsd(I18nService.Instance["OsdPipOff"]);
            }

            UpdateOuterBorder();
        }

        private void PipResizeGrip_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!_isPipMode) return;
            _isResizingPip = true;
            var pos = System.Windows.Forms.Cursor.Position;
            _pipResizeStartMouse = new System.Windows.Point(pos.X, pos.Y);
            _pipResizeStartW = this.Width;
            _pipResizeStartH = this.Height;
            if (sender is IInputElement element) element.CaptureMouse();
            e.Handled = true;
        }

        // ── Unified Floating Audio Banner (Rotating Pure Circular Vinyl & Full Metadata) ─────────────
        private static readonly System.Windows.Media.Animation.DoubleAnimation _discRotateAnimation = new(0, 360, TimeSpan.FromSeconds(15))
        {
            RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
        };

        private bool _isDiscAnimationRunning = false;

        private void UpdateAudioBannerView()
        {
            if (string.IsNullOrEmpty(_currentPlayingFilePath) || !PlaylistManager.IsAudioFile(_currentPlayingFilePath) || IsAnyModalOverlayOpen() || _isDialogOpen)
            {
                if (popupAudioBanner != null && popupAudioBanner.IsOpen) popupAudioBanner.IsOpen = false;
                return;
            }

            if (popupAudioBanner != null && !popupAudioBanner.IsOpen)
            {
                popupAudioBanner.IsOpen = true;
            }

            double winW = this.ActualWidth > 0 ? this.ActualWidth : this.Width;
            double winH = this.ActualHeight > 0 ? this.ActualHeight : this.Height;

            if (_isPipMode)
            {
                // PiP Mode compact layout
                if (borderAudioBanner != null)
                {
                    borderAudioBanner.Width = Math.Max(180, winW - 20);
                    borderAudioBanner.Margin = new Thickness(10, 0, 10, 10);
                    borderAudioBanner.Padding = new Thickness(10, 6, 10, 6);
                }
                if (viewboxAudioVinyl != null)
                {
                    double discSize = Math.Clamp(Math.Round(Math.Min(winW * 0.18, winH * 0.36)), 38, 64);
                    viewboxAudioVinyl.Width = discSize;
                    viewboxAudioVinyl.Height = discSize;
                    viewboxAudioVinyl.Margin = new Thickness(0, 0, 8, 0);
                }
                if (txtAudioTrackTitle != null) txtAudioTrackTitle.FontSize = 12;
                if (txtAudioArtistAlbum != null) txtAudioArtistAlbum.FontSize = 10.5;
                if (txtAudioCurrentLyric != null) txtAudioCurrentLyric.FontSize = 11;
            }
            else
            {
                // Normal / Fullscreen Window Mode: centered over the album cover with full metadata
                if (borderAudioBanner != null)
                {
                    double targetW = Math.Clamp(Math.Round(winW * 0.58), 380, 720);
                    borderAudioBanner.Width = Math.Min(winW - 32, targetW);
                    double marginB = Math.Clamp(Math.Round(winH * 0.05), 20, 36);
                    borderAudioBanner.Margin = new Thickness(16, 0, 16, marginB);
                    borderAudioBanner.Padding = new Thickness(14, 10, 14, 10);
                }
                if (viewboxAudioVinyl != null)
                {
                    double discSize = Math.Clamp(Math.Round(Math.Min(winH * 0.16, winW * 0.12)), 64, 100);
                    viewboxAudioVinyl.Width = discSize;
                    viewboxAudioVinyl.Height = discSize;
                    viewboxAudioVinyl.Margin = new Thickness(0, 0, 14, 0);
                }
                if (txtAudioTrackTitle != null) txtAudioTrackTitle.FontSize = 14;
                if (txtAudioArtistAlbum != null) txtAudioArtistAlbum.FontSize = 12;
                if (txtAudioCurrentLyric != null) txtAudioCurrentLyric.FontSize = 12.5;
            }

            _popupUpdatePositionMethod?.Invoke(popupAudioBanner, null);
            EnsureSideDrawerOnTop();
            EnsureAudioBannerBelowSideDrawer();

            // 1. Title
            string title = MpvGet("metadata/by-key/title");
            if (string.IsNullOrWhiteSpace(title)) title = MpvGet("metadata/by-key/TITLE");
            if (string.IsNullOrWhiteSpace(title)) title = MpvGet("metadata/by-key/TIT2");
            if (string.IsNullOrWhiteSpace(title)) title = MpvGet("media-title");
            if (string.IsNullOrWhiteSpace(title)) title = Path.GetFileNameWithoutExtension(_currentPlayingFilePath);
            if (string.IsNullOrWhiteSpace(title)) title = I18nService.Instance["AudioUnknownTrack"];

            // 2. Artist & Album
            string artist = MpvGet("metadata/by-key/artist");
            if (string.IsNullOrWhiteSpace(artist)) artist = MpvGet("metadata/by-key/ARTIST");
            if (string.IsNullOrWhiteSpace(artist)) artist = MpvGet("metadata/by-key/TPE1");
            if (string.IsNullOrWhiteSpace(artist)) artist = MpvGet("metadata/by-key/album_artist");

            string album = MpvGet("metadata/by-key/album");
            if (string.IsNullOrWhiteSpace(album)) album = MpvGet("metadata/by-key/ALBUM");
            if (string.IsNullOrWhiteSpace(album)) album = MpvGet("metadata/by-key/TALB");

            if (txtAudioTrackTitle != null) txtAudioTrackTitle.Text = title;

            string artistAlbumText = "";
            if (!string.IsNullOrWhiteSpace(artist) && !string.IsNullOrWhiteSpace(album))
            {
                artistAlbumText = $"{artist} · {album}";
            }
            else if (!string.IsNullOrWhiteSpace(artist))
            {
                artistAlbumText = artist;
            }
            else if (!string.IsNullOrWhiteSpace(album))
            {
                artistAlbumText = album;
            }
            else
            {
                artistAlbumText = I18nService.Instance["AudioUnknownArtist"];
            }
            if (txtAudioArtistAlbum != null) txtAudioArtistAlbum.Text = artistAlbumText;

            // 3. Initial Lyrics preview
            if (LyricsService.Instance.CurrentLyrics.Count == 0)
            {
                string mpvLyrics = MpvGet("metadata/by-key/lyrics");
                if (string.IsNullOrWhiteSpace(mpvLyrics)) mpvLyrics = MpvGet("metadata/by-key/LYRICS");
                if (string.IsNullOrWhiteSpace(mpvLyrics)) mpvLyrics = MpvGet("metadata/by-key/USLT");
                if (string.IsNullOrWhiteSpace(mpvLyrics)) mpvLyrics = MpvGet("metadata/by-key/unsyncedlyrics");
                if (!string.IsNullOrWhiteSpace(mpvLyrics))
                {
                    LyricsService.Instance.ParseLrcContent(mpvLyrics);
                }
            }

            var (cur, next) = LyricsService.Instance.GetLyricsAt(TimeSpan.FromSeconds(MpvGetDouble("time-pos")));
            if (txtAudioCurrentLyric != null)
            {
                txtAudioCurrentLyric.Text = cur?.Text ?? (LyricsService.Instance.CurrentLyrics.Count > 0 ? ("🎵 " + title) : ("🎵 " + I18nService.Instance["AudioNoLyrics"]));
            }
        }

        private void PopupAudioBanner_Opened(object? sender, EventArgs e)
        {
            EnsureSideDrawerOnTop();
            EnsureAudioBannerBelowSideDrawer();
        }

        private void EnsureSideDrawerOnTop()
        {
            try
            {
                if (popupSideDrawer != null && popupSideDrawer.IsOpen && popupSideDrawer.Child != null)
                {
                    var drawerSource = System.Windows.PresentationSource.FromVisual(popupSideDrawer.Child) as System.Windows.Interop.HwndSource;
                    if (drawerSource != null && drawerSource.Handle != IntPtr.Zero)
                    {
                        SetWindowPos(drawerSource.Handle, HWND_TOP, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
                    }
                }
            }
            catch { }
        }

        private void EnsureAudioBannerBelowSideDrawer()
        {
            try
            {
                if (popupAudioBanner != null && popupAudioBanner.IsOpen && popupAudioBanner.Child != null)
                {
                    var bannerSource = System.Windows.PresentationSource.FromVisual(popupAudioBanner.Child) as System.Windows.Interop.HwndSource;
                    if (bannerSource != null && bannerSource.Handle != IntPtr.Zero)
                    {
                        if (popupSideDrawer != null && popupSideDrawer.IsOpen && popupSideDrawer.Child != null)
                        {
                            var drawerSource = System.Windows.PresentationSource.FromVisual(popupSideDrawer.Child) as System.Windows.Interop.HwndSource;
                            if (drawerSource != null && drawerSource.Handle != IntPtr.Zero)
                            {
                                SetWindowPos(bannerSource.Handle, drawerSource.Handle, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private void StartVinylDiscAnimation()
        {
            if (popupAudioBanner != null && !popupAudioBanner.IsOpen)
            {
                popupAudioBanner.IsOpen = true;
            }
            UpdateAudioBannerView();
            if (rotAudioVinylDisc != null)
            {
                rotAudioVinylDisc.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, _discRotateAnimation);
            }
            _isDiscAnimationRunning = true;
        }

        private void PauseVinylDiscAnimation()
        {
            if (!_isDiscAnimationRunning) return;
            if (rotAudioVinylDisc != null)
            {
                double angle = rotAudioVinylDisc.Angle;
                rotAudioVinylDisc.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, null);
                rotAudioVinylDisc.Angle = angle;
            }
            _isDiscAnimationRunning = false;
        }

        private void ResumeVinylDiscAnimation()
        {
            if (_isDiscAnimationRunning) return;
            if (rotAudioVinylDisc != null)
            {
                double cur = rotAudioVinylDisc.Angle % 360.0;
                var resumeAnim = new System.Windows.Media.Animation.DoubleAnimation(cur, cur + 360, TimeSpan.FromSeconds(15))
                {
                    RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
                };
                rotAudioVinylDisc.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, resumeAnim);
            }
            _isDiscAnimationRunning = true;
        }

        private void StopVinylDiscAnimation()
        {
            if (rotAudioVinylDisc != null) rotAudioVinylDisc.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, null);

            if (popupAudioBanner != null && popupAudioBanner.IsOpen)
            {
                popupAudioBanner.IsOpen = false;
            }
            _isDiscAnimationRunning = false;
        }

        private void PipResizeGrip_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_isResizingPip) return;
            var pos = System.Windows.Forms.Cursor.Position;
            double deltaX = pos.X - _pipResizeStartMouse.X;

            bool isAudio = PlaylistManager.IsAudioFile(_currentPlayingFilePath);
            double controlH = 46;
            double minW = isAudio ? 200 : 220;
            double minH = Math.Round(minW / _pipAspectRatio) + controlH;

            double newW = Math.Max(minW, _pipResizeStartW + deltaX);
            double newH = Math.Max(minH, Math.Round(newW / _pipAspectRatio) + controlH);

            this.Width = newW;
            this.Height = newH;
            e.Handled = true;
        }

        private void PipResizeGrip_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isResizingPip)
            {
                _isResizingPip = false;
                if (sender is IInputElement element) element.ReleaseMouseCapture();
                e.Handled = true;
            }
        }

        private void AutoCropMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.MenuItem menuItem)
            {
                _autoCropMode = menuItem.Tag?.ToString() ?? "none";
                OnAutoCropModeChanged();
            }
        }

        private void PopulateLanguageMenu(MenuItem menuLanguageSubmenu)
        {
            menuLanguageSubmenu.Items.Clear();
            string currentLang = I18nService.Instance.CurrentLanguage;
            var availableLangs = I18nService.Instance.GetAvailableLanguages();
            foreach (var langInfo in availableLangs)
            {
                var item = new MenuItem
                {
                    Header = langInfo.DisplayName,
                    Tag = langInfo.Code,
                    IsChecked = langInfo.Code.Equals(currentLang, StringComparison.OrdinalIgnoreCase),
                    Style = (Style)FindResource("SmartFillMenuItemStyle")
                };
                item.Click += LanguageMenuItem_Click;
                menuLanguageSubmenu.Items.Add(item);
            }
        }

        private void LanguageMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.MenuItem menuItem)
            {
                string lang = menuItem.Tag?.ToString() ?? "zh-CN";
                I18nService.Instance.ChangeLanguage(lang);

                if (menuItem.Parent is System.Windows.Controls.MenuItem parentMenu)
                {
                    PopulateLanguageMenu(parentMenu);
                }

                ShowOsd(string.Format(I18nService.Instance["OsdLanguageChanged"] != "[OsdLanguageChanged]" ? I18nService.Instance["OsdLanguageChanged"] : "语言已切换: {0}", lang));
                if (!_hasMedia) _videoPanel?.Invalidate();
            }
        }

        private void ThemeMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.MenuItem menuItem)
            {
                string key = menuItem.Tag?.ToString() ?? "default";
                if (key.Equals("teal_gold", StringComparison.OrdinalIgnoreCase) || ThemeService.Instance.Skins.ContainsKey(key))
                {
                    ThemeService.Instance.ActiveSkinKey = key;
                }
                else
                {
                    ThemeService.Instance.ActiveSkinKey = "none";
                    ThemeService.Instance.CurrentThemeKey = key;
                }
                ThemeService.Instance.ApplyActiveSkinOrTheme();

                var cfg = SettingsService.Instance.Config;
                cfg.Theme = ThemeService.Instance.CurrentThemeKey;
                cfg.ActiveSkin = ThemeService.Instance.ActiveSkinKey;
                SettingsService.Instance.Save();

                if (menuItem.Parent is System.Windows.Controls.MenuItem parentMenu)
                {
                    foreach (var item in parentMenu.Items)
                    {
                        if (item is System.Windows.Controls.MenuItem mi)
                        {
                            mi.IsChecked = (mi.Tag?.ToString() == key);
                        }
                    }
                }

                UpdateThemeBackgrounds();

                string name = key;
                if (ThemeService.Instance.Themes.TryGetValue(key, out var itemTheme))
                {
                    name = I18nService.Instance.CurrentLanguage == "en-US" ? itemTheme.NameEn : itemTheme.NameZh;
                }
                else if (ThemeService.Instance.Skins.TryGetValue(key, out var itemSkin))
                {
                    name = I18nService.Instance.CurrentLanguage == "en-US" ? itemSkin.NameEn : itemSkin.NameZh;
                }
                string fmt = I18nService.Instance["OsdThemeChanged"];
                ShowOsd(string.Format(fmt, name));
            }
        }

        private void UpdatePlaylistCountText()
        {
            if (txtPlaylistCount == null) return;
            int count = PlaylistManager.Instance.Items.Count;
            string fmt = I18nService.Instance["PlaylistCount"];
            txtPlaylistCount.Text = string.Format(fmt, count);
        }

        private void OnAutoCropModeChanged()
        {
            string osd = _autoCropMode switch
            {
                "preserve" => $"{I18nService.Instance["OsdAutoCropPreserve"]}",
                "crop"     => $"{I18nService.Instance["OsdAutoCropAll"]}",
                _          => $"{I18nService.Instance["OsdAutoCropOff"]}"
            };
            ShowOsd(osd);

            UpdateAutoCropUI();
            ApplyAutoCropMode();
            UpdateSmartFill();
            SaveSettingsToConfig();
        }

        private void ApplyAutoCropMode()
        {
            if (_mpv == IntPtr.Zero) return;
            UpdateHardwareDecodingMode();

            if (_autoCropMode == "none")
            {
                MpvNative.mpv_command_string(_mpv, "script-message anni-autocrop-clear");
            }
            else
            {
                MpvNative.mpv_command_string(_mpv, $"script-message anni-autocrop-start {_autoCropMode}");
            }
        }

        private System.Windows.Media.Brush GetActiveThemeBrush()
        {
            var appRes = System.Windows.Application.Current?.Resources;
            if (appRes != null && appRes["ThemeAccentBrush"] is System.Windows.Media.Brush accent)
            {
                return accent;
            }
            return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 229, 255));
        }

        private System.Windows.Media.Brush GetInactiveThemeBrush()
        {
            var appRes = System.Windows.Application.Current?.Resources;
            if (appRes != null && appRes["ThemeInactiveButtonBrush"] is System.Windows.Media.Brush inactive)
            {
                return inactive;
            }
            var theme = ThemeService.Instance.CurrentTheme;
            try
            {
                var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(theme.InactiveButtonHex);
                return new System.Windows.Media.SolidColorBrush(color);
            }
            catch
            {
                return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 200, 200));
            }
        }

        private void UpdateSmartFillUI()
        {
            var activeBrush = GetActiveThemeBrush();
            var inactiveBrush = GetInactiveThemeBrush();

            bool active = _smartFillMode != "none";
            if (btnSmartFill != null) btnSmartFill.Foreground = active ? activeBrush : inactiveBrush;
            if (btnSmartFillFS != null) btnSmartFillFS.Foreground = active ? activeBrush : inactiveBrush;

            UpdateContextMenuChecked(btnSmartFill?.ContextMenu, _smartFillMode);
            UpdateContextMenuChecked(btnSmartFillFS?.ContextMenu, _smartFillMode);

            var videoMenu = this.Resources["VideoContextMenu"] as System.Windows.Controls.ContextMenu;
            if (videoMenu != null)
            {
                foreach (var item in videoMenu.Items)
                {
                    if (item is System.Windows.Controls.MenuItem mi && mi.HasItems)
                    {
                        foreach (var sub in mi.Items)
                        {
                            if (sub is System.Windows.Controls.MenuItem subMi && (subMi.Name?.StartsWith("menuCtxFill") == true || subMi.Name?.StartsWith("menuFill") == true))
                            {
                                subMi.IsChecked = (subMi.Tag?.ToString() == _smartFillMode);
                            }
                        }
                    }
                }
            }
        }

        private void UpdateAutoCropUI()
        {
            var activeBrush = GetActiveThemeBrush();
            var inactiveBrush = GetInactiveThemeBrush();

            bool active = _autoCropMode != "none";
            if (btnAutoCrop != null) btnAutoCrop.Foreground = active ? activeBrush : inactiveBrush;
            if (btnAutoCropFS != null) btnAutoCropFS.Foreground = active ? activeBrush : inactiveBrush;

            UpdateContextMenuChecked(btnAutoCrop?.ContextMenu, _autoCropMode);
            UpdateContextMenuChecked(btnAutoCropFS?.ContextMenu, _autoCropMode);

            var videoMenu = this.Resources["VideoContextMenu"] as System.Windows.Controls.ContextMenu;
            if (videoMenu != null)
            {
                foreach (var item in videoMenu.Items)
                {
                    if (item is System.Windows.Controls.MenuItem mi && mi.HasItems)
                    {
                        foreach (var sub in mi.Items)
                        {
                            if (sub is System.Windows.Controls.MenuItem subMi && (subMi.Name?.StartsWith("menuCtxCrop") == true || subMi.Name?.StartsWith("menuCrop") == true))
                            {
                                subMi.IsChecked = (subMi.Tag?.ToString() == _autoCropMode);
                            }
                        }
                    }
                }
            }
        }

        private void UpdateContextMenuChecked(System.Windows.Controls.ContextMenu? menu, string activeTag)
        {
            if (menu == null) return;
            foreach (var item in menu.Items)
            {
                if (item is System.Windows.Controls.MenuItem mi)
                {
                    mi.IsChecked = (mi.Tag?.ToString() == activeTag);
                }
            }
        }

        private IntPtr _mpvBgm = IntPtr.Zero;
        private string _currentBgmFile = "";

        private void StartImageMotion(string imagePath = "")
        {
            StopImageMotion();
            if (_mpv == IntPtr.Zero) return;

            MpvSetPropertyString("file-local-options/video-aspect-override", "-1");

            _imageMotionMode = Random.Shared.Next(0, 4);
            _imageElapsedSec = 0.0;
            _imageStartTime = DateTime.Now;

            if (_imageMotionTimer == null)
            {
                _imageMotionTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(40) // ~25 fps smooth animation
                };
                _imageMotionTimer.Tick += ImageMotionTimer_Tick;
            }
            _imageMotionTimer.Start();
        }

        private void StopImageMotion()
        {
            _imageMotionTimer?.Stop();
            if (_mpv != IntPtr.Zero)
            {
                MpvSetPropertyString("video-zoom", "0");
                MpvSetPropertyString("video-pan-x", "0");
                MpvSetPropertyString("video-pan-y", "0");
            }
        }

        private double CalculateSkinBgmVolume(double? baseMainVolume = null)
        {
            var currentSkin = ThemeService.Instance.CurrentTheme;
            double skinBgmRatio = (currentSkin?.BgmVolume ?? 70.0) / 100.0;
            double mainVol = baseMainVolume ?? (double)SettingsService.Instance.Config.LastVolume;
            double calculated = mainVol * skinBgmRatio;
            return Math.Clamp(calculated, 0.0, 100.0);
        }

        private void UpdateSkinOrSlideshowBgm()
        {
            try
            {
                if (_mpvBgm == IntPtr.Zero) return;

                // 1. If currently playing a video or audio media file (not an image)
                if (!string.IsNullOrEmpty(_currentPlayingFilePath) && !PlaylistManager.IsImageFile(_currentPlayingFilePath))
                {
                    StopBgmAudio();
                    return;
                }

                // 2. If currently viewing an image slideshow
                if (!string.IsNullOrEmpty(_currentPlayingFilePath) && PlaylistManager.IsImageFile(_currentPlayingFilePath))
                {
                    UpdateBgmAudio(_currentPlayingFilePath);
                    return;
                }

                // 3. Current state: Idle Home Screen (no media playing)
                if (string.IsNullOrEmpty(_currentPlayingFilePath))
                {
                    var currentSkin = ThemeService.Instance.CurrentTheme;
                    string? skinBgmPath = ThemeService.Instance.ResolvedSkinBgmPath;

                    if (!string.IsNullOrEmpty(skinBgmPath) && File.Exists(skinBgmPath) && (currentSkin?.BgmAutoPlayOnIdle ?? true))
                    {
                        double speed = currentSkin?.BgmSpeed ?? 1.0;
                        bool loop = currentSkin?.BgmLoop ?? true;
                        PlayBgmAudio(skinBgmPath, null, speed, loop);
                    }
                    else
                    {
                        StopBgmAudio();
                    }
                }
            }
            catch { }
        }

        private void UpdateBgmAudio(string currentImagePath)
        {
            var cfg = SettingsService.Instance.Config;
            if (cfg.BgmMode == 2) // Disabled
            {
                StopBgmAudio();
                return;
            }

            string bgmFileToPlay = "";
            if (cfg.BgmMode == 1 && !string.IsNullOrEmpty(cfg.ManualBgmPath) && File.Exists(cfg.ManualBgmPath))
            {
                bgmFileToPlay = cfg.ManualBgmPath;
            }
            else if (cfg.BgmMode == 0 && !string.IsNullOrEmpty(currentImagePath)) // Auto same directory search
            {
                try
                {
                    string? dir = Path.GetDirectoryName(currentImagePath);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    {
                        var audioExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ".mp3", ".flac", ".wav", ".aac", ".m4a", ".ogg", ".opus"
                        };
                        var audioFiles = Directory.GetFiles(dir)
                                                  .Where(f => audioExts.Contains(Path.GetExtension(f)))
                                                  .OrderBy(f => f, new NaturalStringComparer())
                                                  .ToList();
                        if (audioFiles.Count > 0)
                        {
                            bgmFileToPlay = audioFiles[0];
                        }
                    }
                }
                catch { }
            }

            if (!string.IsNullOrEmpty(bgmFileToPlay))
            {
                PlayBgmAudio(bgmFileToPlay);
            }
            else
            {
                StopBgmAudio();
            }
        }

        private void PlayBgmAudio(string audioPath, double? customVolume = null, double? customSpeed = null, bool loop = true)
        {
            try
            {
                if (_mpvBgm != IntPtr.Zero && File.Exists(audioPath))
                {
                    double targetVol = customVolume ?? CalculateSkinBgmVolume();
                    double targetSpeed = customSpeed ?? 1.0;

                    // If the same background track is ALREADY playing, DO NOT reload or interrupt!
                    if (string.Equals(_currentBgmFile, audioPath, StringComparison.OrdinalIgnoreCase))
                    {
                        MpvNative.mpv_set_property_string(_mpvBgm, "pause", "no");
                        MpvNative.mpv_set_property_string(_mpvBgm, "volume", targetVol.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        MpvNative.mpv_set_property_string(_mpvBgm, "speed", targetSpeed.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        return;
                    }

                    _currentBgmFile = audioPath;
                    string posix = audioPath.Replace("\\", "/");
                    MpvNative.mpv_command_string(_mpvBgm, $"loadfile \"{posix}\"");
                    MpvNative.mpv_set_property_string(_mpvBgm, "loop-file", loop ? "inf" : "no");
                    MpvNative.mpv_set_property_string(_mpvBgm, "pause", "no");
                    MpvNative.mpv_set_property_string(_mpvBgm, "volume", targetVol.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    MpvNative.mpv_set_property_string(_mpvBgm, "speed", targetSpeed.ToString(System.Globalization.CultureInfo.InvariantCulture));

                    string mute = _isMuted ? "yes" : "no";
                    MpvNative.mpv_set_property_string(_mpvBgm, "mute", mute);
                }
            }
            catch { }
        }

        private void StopBgmAudio()
        {
            try
            {
                if (_mpvBgm != IntPtr.Zero && !string.IsNullOrEmpty(_currentBgmFile))
                {
                    MpvNative.mpv_command_string(_mpvBgm, "stop");
                }
                _currentBgmFile = "";
            }
            catch { }
        }

        private bool _isSkinIdleVideoPlaying = false;

        private void PlaySkinIdleVideos(System.Collections.Generic.List<string> idleVideos)
        {
            try
            {
                if (_mpv == IntPtr.Zero || idleVideos == null || idleVideos.Count == 0) return;

                var theme = ThemeService.Instance.CurrentTheme;
                var appRes = System.Windows.Application.Current?.Resources;

                // 1. Playback Speed: priority XAML (ThemeIdleVideoSpeed) > JSON (idle_video_speed) > 1.0
                double speed = 1.0;
                if (appRes?["ThemeIdleVideoSpeed"] is double xamlSpeed && xamlSpeed > 0.1 && xamlSpeed <= 10.0)
                {
                    speed = xamlSpeed;
                }
                else if (theme?.IdleVideoSpeed is double jsonSpeed && jsonSpeed > 0.1 && jsonSpeed <= 10.0)
                {
                    speed = jsonSpeed;
                }

                // 2. Playback Brightness: priority XAML (ThemeIdleVideoBrightness) > JSON (idle_video_brightness) > 0.0
                double brightness = 0.0;
                if (appRes?["ThemeIdleVideoBrightness"] is double xamlBrightness)
                {
                    brightness = xamlBrightness;
                }
                else if (appRes?["ThemeIdleVideoBrightness"] is int xamlIntBrightness)
                {
                    brightness = xamlIntBrightness;
                }
                else if (theme?.IdleVideoBrightness is double jsonBrightness)
                {
                    brightness = jsonBrightness;
                }
                ApplyImageEnhancement((int)Math.Round(brightness));

                // 3. Audio control: If separate BGM audio is configured, ignore/mute video audio track
                string? skinBgmPath = ThemeService.Instance.ResolvedSkinBgmPath;
                bool hasSeparateBgm = !string.IsNullOrEmpty(skinBgmPath) && File.Exists(skinBgmPath) && (theme?.BgmAutoPlayOnIdle ?? true);

                // Configure MPV options for idle video playback
                MpvSetPropertyString("keep-open", "no");
                MpvSetPropertyString("audio-pitch-correction", "yes");
                MpvSetPropertyString("speed", speed.ToString(System.Globalization.CultureInfo.InvariantCulture));

                // 4. Crop aspect ratio, center and stretch to fill window seamlessly (panscan 1.0)
                _lastVfString = "";
                MpvSetPropertyString("vf", "");
                MpvSetPropertyString("file-local-options/video-crop", "");
                MpvSetPropertyString("panscan", "1.0");
                MpvSetPropertyString("keepaspect", "yes");
                MpvSetPropertyString("video-unscaled", "no");

                if (hasSeparateBgm)
                {
                    // Strip/disable idle video audio track via aid=no so BGM plays without interference, without altering player mute state
                    MpvSetPropertyString("aid", "no");
                }
                else
                {
                    MpvSetPropertyString("aid", "auto");
                }
                MpvSetPropertyString("mute", _isMuted ? "yes" : "no");

                // Loop configuration: infinite playlist loop
                MpvSetPropertyString("loop-playlist", "inf");
                MpvSetPropertyString("loop-file", idleVideos.Count == 1 ? "inf" : "no");

                for (int i = 0; i < idleVideos.Count; i++)
                {
                    string posixPath = idleVideos[i].Replace("\\", "/");
                    if (i == 0)
                    {
                        MpvNative.mpv_command_string(_mpv, $"loadfile \"{posixPath}\" replace");
                    }
                    else
                    {
                        MpvNative.mpv_command_string(_mpv, $"loadfile \"{posixPath}\" append");
                    }
                }

                MpvNative.mpv_command_string(_mpv, "set pause no");

                _isSkinIdleVideoPlaying = true;
                _hasMedia = false; // Keep player in idle state
                _videoPanel.Invalidate();

                Dispatcher.Invoke(() => UpdateIdleHintOverlay());
            }
            catch { }
        }

        private void StopSkinIdleVideo()
        {
            try
            {
                if (_isSkinIdleVideoPlaying)
                {
                    _isSkinIdleVideoPlaying = false;
                    if (_mpv != IntPtr.Zero && !_hasMedia)
                    {
                        MpvNative.mpv_command_string(_mpv, "stop");
                        MpvSetPropertyString("keep-open", "yes");
                        MpvSetPropertyString("aid", "auto");
                        MpvSetPropertyString("speed", "1.0");
                        MpvSetPropertyString("loop-file", "no");
                        MpvSetPropertyString("loop-playlist", "no");
                        MpvSetPropertyString("panscan", "0.0");
                        ApplyBaseBrightness();
                    }
                    _videoPanel.Invalidate();
                    Dispatcher.Invoke(() => UpdateIdleHintOverlay());
                }
            }
            catch { }
        }

        private void UpdateSkinIdleVideo()
        {
            try
            {
                // If user is actively playing media, do not play skin idle video
                if (_hasMedia || !string.IsNullOrEmpty(_currentPlayingFilePath))
                {
                    StopSkinIdleVideo();
                    Dispatcher.Invoke(() => UpdateIdleHintOverlay());
                    return;
                }

                var idleVideos = ThemeService.Instance.ResolvedSkinIdleVideos;
                if (idleVideos.Count > 0)
                {
                    PlaySkinIdleVideos(idleVideos);
                }
                else
                {
                    StopSkinIdleVideo();
                }
                Dispatcher.Invoke(() => UpdateIdleHintOverlay());
            }
            catch { }
        }

        private void UpdateIdleHintOverlay()
        {
            if (popupIdleHint == null) return;

            // Only show WPF floating hint overlay when Skin Idle Video is actively playing AND no modal dialog is open!
            // For built-in themes and static skins, VideoPanel_Paint handles the drawing natively in GDI+.
            if (_hasMedia || !string.IsNullOrEmpty(_currentPlayingFilePath) || !_isSkinIdleVideoPlaying ||
                (popupSettings != null && popupSettings.IsOpen) ||
                (popupLibrary != null && popupLibrary.IsOpen) ||
                (popupOpenUrl != null && popupOpenUrl.IsOpen) ||
                (popupSponsor != null && popupSponsor.IsOpen) ||
                (popupTracks != null && popupTracks.IsOpen))
            {
                if (popupIdleHint.IsOpen) popupIdleHint.IsOpen = false;
                return;
            }

            var theme = ThemeService.Instance.CurrentTheme;
            var hk = SettingsService.Instance.Config.Hotkeys ??= new HotkeyConfig();

            // Line 1: Main Title prompt (skin custom or built-in i18n fallback)
            string l1 = !string.IsNullOrWhiteSpace(theme?.IdleHintTitle)
                ? theme.IdleHintTitle
                : I18nService.Instance["DragHintLine1"];

            // Line 2: Subtitle prompt (skin custom or built-in i18n fallback with hotkey formatting)
            string rawL2 = !string.IsNullOrWhiteSpace(theme?.IdleHintSubtitle)
                ? theme.IdleHintSubtitle
                : I18nService.Instance["DragHintLine2"];
            string l2;
            try { l2 = string.Format(rawL2, hk.OpenFile, hk.OpenFolder, hk.OpenUrl); }
            catch { l2 = rawL2; }

            // Line 3: SubText prompt (skin custom or built-in i18n fallback)
            string l3 = !string.IsNullOrWhiteSpace(theme?.IdleHintSubText)
                ? theme.IdleHintSubText
                : I18nService.Instance["DragHintLine3"];

            txtIdleHintTitle.Text = l1;
            txtIdleHintSub1.Text = l2;
            txtIdleHintSub2.Text = l3;

            // Typography & Colors from Skin/Theme
            var appRes = System.Windows.Application.Current?.Resources;
            if (appRes?["ThemeIdleHintTitleBrush"] is System.Windows.Media.Brush tb)
            {
                txtIdleHintTitle.Foreground = tb;
            }
            else if (!string.IsNullOrEmpty(theme?.IdleHintTitleHex))
            {
                try
                {
                    if (new System.Windows.Media.BrushConverter().ConvertFromString(theme.IdleHintTitleHex) is System.Windows.Media.Brush b)
                        txtIdleHintTitle.Foreground = b;
                } catch { }
            }
            else if (appRes?["ThemeTextBrush"] is System.Windows.Media.Brush textB)
            {
                txtIdleHintTitle.Foreground = textB;
            }

            if (appRes?["ThemeIdleHintSubtitleBrush"] is System.Windows.Media.Brush sb)
            {
                txtIdleHintSub1.Foreground = sb;
                txtIdleHintSub2.Foreground = sb;
            }
            else if (!string.IsNullOrEmpty(theme?.IdleHintSubtitleHex))
            {
                try
                {
                    if (new System.Windows.Media.BrushConverter().ConvertFromString(theme.IdleHintSubtitleHex) is System.Windows.Media.Brush b)
                    {
                        txtIdleHintSub1.Foreground = b;
                        txtIdleHintSub2.Foreground = b;
                    }
                } catch { }
            }
            else if (appRes?["ThemeSubTextBrush"] is System.Windows.Media.Brush subB)
            {
                txtIdleHintSub1.Foreground = subB;
                txtIdleHintSub2.Foreground = subB;
            }

            if (theme?.IdleHintTitleSize is double tSize && tSize > 8) txtIdleHintTitle.FontSize = tSize;
            if (theme?.IdleHintSubtitleSize is double sSize && sSize > 8)
            {
                txtIdleHintSub1.FontSize = sSize;
                txtIdleHintSub2.FontSize = sSize;
            }

            if (!popupIdleHint.IsOpen)
            {
                popupIdleHint.IsOpen = true;
            }
            else
            {
                // Refresh popup position
                double offset = popupIdleHint.HorizontalOffset;
                popupIdleHint.HorizontalOffset = offset + 0.0001;
                popupIdleHint.HorizontalOffset = offset;
            }
        }

        private void ImageMotionTimer_Tick(object? sender, EventArgs e)
        {
            if (!_isCurrentImage || _mpv == IntPtr.Zero)
            {
                StopImageMotion();
                return;
            }

            // Immediately pause motion calculation if player is paused
            if (MpvGet("pause") == "yes")
            {
                return;
            }

            double imgDur = (double)Math.Max(1, SettingsService.Instance.Config.ImageDurationSec);
            _imageElapsedSec += 0.040; // ~40ms per tick
            double progress = Math.Clamp(_imageElapsedSec / imgDur, 0.0, 1.0);

            double zoom = 0.0;
            double panX = 0.0;
            double panY = 0.0;

            switch (_imageMotionMode)
            {
                case 0: // Smooth Slow Zoom In
                    zoom = progress * 0.12;
                    break;
                case 1: // Smooth Slow Zoom Out
                    zoom = 0.12 * (1.0 - progress);
                    break;
                case 2: // Smooth Slow Pan Left to Right
                    zoom = 0.08;
                    panX = -0.04 + progress * 0.08;
                    break;
                default: // Smooth Slow Diagonal Pan & Zoom
                    zoom = progress * 0.10;
                    panX = -0.03 + progress * 0.06;
                    panY = -0.03 + progress * 0.06;
                    break;
            }

            MpvSetPropertyString("video-zoom", zoom.ToString(System.Globalization.CultureInfo.InvariantCulture));
            MpvSetPropertyString("video-pan-x", panX.ToString(System.Globalization.CultureInfo.InvariantCulture));
            MpvSetPropertyString("video-pan-y", panY.ToString(System.Globalization.CultureInfo.InvariantCulture));

            // Update timeline slider and time labels for images
            if (!_draggingTimeline)
            {
                _timerUpdating = true;
                sliderTimeline.Maximum = imgDur;
                sliderTimeline.Value = _imageElapsedSec;
                if (sliderTimelineFS != null)
                {
                    sliderTimelineFS.Maximum = imgDur;
                    sliderTimelineFS.Value = _imageElapsedSec;
                }
                txtTime.Text = $"{Fmt(_imageElapsedSec)} / {Fmt(imgDur)}";
                if (txtTimeFS != null) txtTimeFS.Text = txtTime.Text;
                if (System.Windows.Application.Current?.Resources["ThemeTextBrush"] is System.Windows.Media.Brush textBrush)
                {
                    txtTime.Foreground = textBrush;
                    if (txtTimeFS != null) txtTimeFS.Foreground = textBrush;
                }
                _timerUpdating = false;
            }

            // Auto advance to next file after configured duration
            if (_imageElapsedSec >= imgDur)
            {
                StopImageMotion();
                var next = PlaylistManager.Instance.GetNext();
                if (next != null)
                {
                    PlayFile(next.FilePath);
                    MpvNative.mpv_command_string(_mpv, "set pause no");
                }
            }
        }

        private System.Windows.Threading.DispatcherTimer? _smartFillRetryTimer;
        private System.Windows.Threading.DispatcherTimer? _smartFillDebounceTimer;
        private bool _isUpdatingSmartFill = false;
        private double _cachedSmartFillGridW = -1;
        private double _cachedSmartFillGridH = -1;
        private double _cachedSmartFillRawW = -1;
        private double _cachedSmartFillRawH = -1;
        private bool _cachedSmartFillEnabled = false;
        private string _cachedSmartFillMode = "";
        private bool _cachedSmartFillFullscreen = false;
        private string _cachedSmartFillAutoCrop = "";

        private string _lastHwdec = "";

        private void UpdateHardwareDecodingMode()
        {
            if (_mpv == IntPtr.Zero) return;

            var cfg = SettingsService.Instance.Config;
            if (cfg.HwDec == "no")
            {
                if (_lastHwdec != "no")
                {
                    _lastHwdec = "no";
                    MpvSetPropertyString("hwdec", "no");
                }
                return;
            }

            if (_lastHwdec != "auto-copy")
            {
                _lastHwdec = "auto-copy";
                MpvSetPropertyString("hwdec", "auto-copy");
            }
        }

        private void UpdateSmartFill()
        {
            if (_isUpdatingSmartFill) return;
            _isUpdatingSmartFill = true;
            try
            {
                if (_mpv == IntPtr.Zero) return;
                UpdateHardwareDecodingMode();

                // 1. Strictly isolate Skin Idle Video from user media SmartFill vf/crop.
                // Always ensure clean panscan=1.0 center-crop full fill without any vf shaders.
                if (_isSkinIdleVideoPlaying)
                {
                    if (_lastVfString != "")
                    {
                        _lastVfString = "";
                        MpvSetPropertyString("vf", "");
                    }
                    MpvSetPropertyString("file-local-options/video-crop", "");
                    MpvSetPropertyString("panscan", "1.0");
                    MpvSetPropertyString("keepaspect", "yes");
                    MpvSetPropertyString("video-unscaled", "no");
                    return;
                }

                // 2. For image slideshow (相册模式):
                // 当未开启虚化填充时，保持宿主满格铺满，由 Ken Burns 运镜平滑缩放与对角线平移
                if (_isCurrentImage && !_smartFillEnabled)
                {
                    if (_lastVfString != "")
                    {
                        _lastVfString = "";
                        MpvSetPropertyString("vf", "");
                    }
                    videoHost.Width = double.NaN;
                    videoHost.Height = double.NaN;
                    videoHost.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
                    videoHost.VerticalAlignment = System.Windows.VerticalAlignment.Stretch;
                    return;
                }

                // For pure audio files without visual display (no embedded album art), bypass vf
                if (PlaylistManager.IsAudioFile(_currentPlayingFilePath) && !GetSourceVideoDimensions(out _, out _))
                {
                    if (_lastVfString != "")
                    {
                        _lastVfString = "";
                        MpvSetPropertyString("vf", "");
                    }
                    MpvSetPropertyString("file-local-options/video-crop", "");
                    return;
                }

                if (!GetSourceVideoDimensions(out double rawW, out double rawH))
                {
                    ScheduleSmartFillRetry();
                    return;
                }

                double pw = videoGrid.ActualWidth > 0 ? videoGrid.ActualWidth : (_videoPanel.ClientSize.Width > 0 ? _videoPanel.ClientSize.Width : 960);
                double ph = videoGrid.ActualHeight > 0 ? videoGrid.ActualHeight : (_videoPanel.ClientSize.Height > 0 ? _videoPanel.ClientSize.Height : 540);

                if (pw <= 0 || ph <= 0)
                {
                    ScheduleSmartFillRetry();
                    return;
                }

                bool isAudio = PlaylistManager.IsAudioFile(_currentPlayingFilePath);
                bool effectiveSmartFill = _smartFillEnabled || isAudio;

                // Short-circuit cache check: If container dimensions and video mode haven't changed, skip redundant P/Invoke and vf recomputation
                if (pw == _cachedSmartFillGridW && ph == _cachedSmartFillGridH &&
                    rawW == _cachedSmartFillRawW && rawH == _cachedSmartFillRawH &&
                    effectiveSmartFill == _cachedSmartFillEnabled &&
                    _smartFillMode == _cachedSmartFillMode &&
                    _isFullscreen == _cachedSmartFillFullscreen &&
                    _autoCropMode == _cachedSmartFillAutoCrop)
                {
                    return;
                }

                _cachedSmartFillGridW = pw;
                _cachedSmartFillGridH = ph;
                _cachedSmartFillRawW = rawW;
                _cachedSmartFillRawH = rawH;
                _cachedSmartFillEnabled = effectiveSmartFill;
                _cachedSmartFillMode = _smartFillMode;
                _cachedSmartFillFullscreen = _isFullscreen;
                _cachedSmartFillAutoCrop = _autoCropMode;

                double vw = rawW;
                double vh = rawH;
                string cropFilter = "";
                string nativeCrop = "";

                if (_autoCropMode != "none")
                {
                    string cropRect = MpvGet("user-data/anni-crop-rect");
                    if (!string.IsNullOrEmpty(cropRect))
                    {
                        cropRect = cropRect.Trim('"');
                        var parts = cropRect.Split(':');
                        if (parts.Length == 4 && 
                            int.TryParse(parts[0], out int cw) && 
                            int.TryParse(parts[1], out int ch) &&
                            int.TryParse(parts[2], out int cx) &&
                            int.TryParse(parts[3], out int cy))
                        {
                            if (cw > 0 && ch > 0)
                            {
                                vw = cw;
                                vh = ch;
                                cropFilter = $"crop={cw}:{ch}:{cx}:{cy},";
                                nativeCrop = $"{cw}x{ch}+{cx}+{cy}";
                            }
                        }
                    }
                }

                if (!effectiveSmartFill)
                {
                    // Only clear vf if it was previously set
                    if (_lastVfString != "")
                    {
                        _lastVfString = "";
                        MpvSetPropertyString("vf", "");
                    }
                    if (_autoCropMode != "none" && !string.IsNullOrEmpty(nativeCrop))
                    {
                        MpvSetPropertyString("file-local-options/video-crop", nativeCrop);
                    }
                    else if (_autoCropMode == "none")
                    {
                        MpvSetPropertyString("file-local-options/video-crop", "");
                    }

                    // 🎨 动态适配视频视口：当未开启虚化填充且处于自定义皮肤模式时，在【窗口模式】下自动缩放视频容器 HWND 尺寸，让未被视频覆盖的两侧/上下空白区域直接通透呈现出皮肤专属母版大理石/曜石/蓝宝材质底图！
                    // 🎬 在【全屏模式】下，画面外部严格恢复为默认纯黑底色，确保沉浸式无干扰观影习惯。
                    if (!_isFullscreen && ThemeService.Instance.IsSkinActive && _hasMedia && !_isSkinIdleVideoPlaying && !_isCurrentImage && !isAudio)
                    {
                        if (pw > 0 && ph > 0 && vw > 0 && vh > 0)
                        {
                            double vr = vw / vh;
                            double gr = pw / ph;
                            if (vr < gr - 0.01)
                            {
                                // 竖屏 / 细长比例视频（如 9:16 在 16:9 窗口中）：满高居中，两侧露出大理石材质底图
                                double fittedW = Math.Round(ph * vr);
                                videoHost.Width = fittedW;
                                videoHost.Height = double.NaN;
                                videoHost.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
                                videoHost.VerticalAlignment = System.Windows.VerticalAlignment.Stretch;
                                return;
                            }
                            else if (vr > gr + 0.01)
                            {
                                // 超宽屏比例视频（如 21:9 在 4:3 窗口中）：满宽居中，上下露出大理石材质底图
                                double fittedH = Math.Round(pw / vr);
                                videoHost.Width = double.NaN;
                                videoHost.Height = fittedH;
                                videoHost.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
                                videoHost.VerticalAlignment = System.Windows.VerticalAlignment.Center;
                                return;
                            }
                        }
                    }

                    // 全屏模式或无缩放需求 -> 视频容器满格铺满，未覆盖部分显示 MPV 原生纯黑底色
                    videoHost.Width = double.NaN;
                    videoHost.Height = double.NaN;
                    videoHost.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
                    videoHost.VerticalAlignment = System.Windows.VerticalAlignment.Stretch;
                    return;
                }
                else
                {
                    // 开启了虚化填充 -> 视频容器满格铺满，由 MPV 渲染动态虚化填充背景（包括相册图片与视频）
                    videoHost.Width = double.NaN;
                    videoHost.Height = double.NaN;
                    videoHost.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
                    videoHost.VerticalAlignment = System.Windows.VerticalAlignment.Stretch;
                }

                // SmartFill is enabled. Clear native video-crop to prevent double-cropping!
                MpvSetPropertyString("file-local-options/video-crop", "");

                double vrFill = vw / vh;
                double pr = pw / ph;

                if (Math.Abs(vrFill - pr) > 0.05)
                {
                    double canvasW = (vrFill > pr) ? vw : vh * pr;
                    double canvasH = (vrFill > pr) ? (vw / pr) : vh;
                    
                    int targetW = ((int)Math.Round(canvasW) / 2) * 2;
                    int targetH = ((int)Math.Round(canvasH) / 2) * 2;
                    int vidW = ((int)Math.Round(vw) / 2) * 2;
                    int vidH = ((int)Math.Round(vh) / 2) * 2;

                    // Resolution-Invariant Downsampling: Lock maximum bound to 256px (O(1) constant ultra-low compute even on 4K/8K)
                    const int MAX_BLUR_BOUND = 256;
                    int blurW, blurH;
                    if (targetW >= targetH)
                    {
                        blurW = MAX_BLUR_BOUND;
                        blurH = Math.Max(16, ((int)Math.Round((double)MAX_BLUR_BOUND * targetH / targetW) / 2) * 2);
                    }
                    else
                    {
                        blurH = MAX_BLUR_BOUND;
                        blurW = Math.Max(16, ((int)Math.Round((double)MAX_BLUR_BOUND * targetW / targetH) / 2) * 2);
                    }

                    string lavfi;
                    if (_smartFillMode == "feather")
                    {
                        // 🚀 100% 全色域真彩·方向感知动态羽化流水线 (Full-Chroma Directional Feather Pipeline):
                        // 1. 100% 真彩保真：背景高斯模糊与原画流严格保持 100% 原生色彩饱和度（RGB 数值与常规填充 100% 精确一致）；
                        // 2. 方向感知双向羽化：仅在有背景填充的边界生成柔和过渡（4:3 仅羽化左右两侧，上下贴合屏幕边界保持 100% 实色实体；21:9 仅羽化上下两端）；
                        // 3. 256px 降采样流水线保证 O(1) 极低开销，60/120 FPS 满速流畅渲染。
                        int maskW, maskH;
                        if (vidW >= vidH)
                        {
                            maskW = MAX_BLUR_BOUND;
                            maskH = Math.Max(16, ((int)Math.Round((double)MAX_BLUR_BOUND * vidH / vidW) / 2) * 2);
                        }
                        else
                        {
                            maskH = MAX_BLUR_BOUND;
                            maskW = Math.Max(16, ((int)Math.Round((double)MAX_BLUR_BOUND * vidW / vidH) / 2) * 2);
                        }

                        const int constInset = 2; // 256 缩略图上收敛 2px（原画上等效 4~6px 微羽化）
                        if (vrFill < pr)
                        {
                            // 柱状黑边（Pillarbox）：仅左右两侧有背景填充 -> 上下满高无羽化，左右双向微羽化
                            lavfi = $"lavfi=\"[vid1]{cropFilter}scale={vidW}:{vidH},setsar=1[vid_sq];[vid_sq]split=2[blur][orig];[blur]scale={blurW}:{blurH}:flags=fast_bilinear,boxblur=3:2,scale={targetW}:{targetH}:flags=bilinear[bg];[orig]split=2[o1][o2];[o1]format=yuva420p[orig_a];[o2]scale={maskW}:{maskH}:flags=fast_bilinear,drawbox=x=0:y=0:w={maskW}:h={maskH}:color=black@1:t=fill,drawbox=x={constInset}:y=0:w={maskW - 2 * constInset}:h={maskH}:color=white@1:t=fill,boxblur=1:1,scale={vidW}:{vidH}:flags=bilinear[mask];[orig_a][mask]alphamerge[feathered];[bg][feathered]overlay=x='trunc((W-w)/4)*2':y='trunc((H-h)/4)*2',setsar=1[vo]\"";
                        }
                        else
                        {
                            // 信箱黑边（Letterbox）：仅上下两端有背景填充 -> 左右满宽无羽化，上下双向微羽化
                            lavfi = $"lavfi=\"[vid1]{cropFilter}scale={vidW}:{vidH},setsar=1[vid_sq];[vid_sq]split=2[blur][orig];[blur]scale={blurW}:{blurH}:flags=fast_bilinear,boxblur=3:2,scale={targetW}:{targetH}:flags=bilinear[bg];[orig]split=2[o1][o2];[o1]format=yuva420p[orig_a];[o2]scale={maskW}:{maskH}:flags=fast_bilinear,drawbox=x=0:y=0:w={maskW}:h={maskH}:color=black@1:t=fill,drawbox=x=0:y={constInset}:w={maskW}:h={maskH - 2 * constInset}:color=white@1:t=fill,boxblur=1:1,scale={vidW}:{vidH}:flags=bilinear[mask];[orig_a][mask]alphamerge[feathered];[bg][feathered]overlay=x='trunc((W-w)/4)*2':y='trunc((H-h)/4)*2',setsar=1[vo]\"";
                        }
                    }
                    else
                    {
                        lavfi = $"lavfi=\"[vid1]{cropFilter}scale={vidW}:{vidH},setsar=1[vid_sq];[vid_sq]split=2[blur][orig];[blur]scale={blurW}:{blurH}:flags=fast_bilinear,boxblur=3:2,scale={targetW}:{targetH}:flags=bilinear[bg];[bg][orig]overlay=x='trunc((W-w)/4)*2':y='trunc((H-h)/4)*2',setsar=1[vo]\"";
                    }
                    // Only rebuild the MPV filter pipeline if the filter string actually changed.
                    if (lavfi != _lastVfString)
                    {
                        _lastVfString = lavfi;
                        MpvSetPropertyString("vf", lavfi);
                    }
                    return;
                }

                // No fill needed — clear vf only if it was previously set
                if (_lastVfString != "")
                {
                    _lastVfString = "";
                    MpvSetPropertyString("vf", "");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateSmartFill exception: {ex.Message}");
            }
            finally
            {
                _isUpdatingSmartFill = false;
            }
        }

        private void VideoGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_isTogglingFullscreen || _isDraggingSize) return;

            if (_smartFillDebounceTimer == null)
            {
                _smartFillDebounceTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(50)
                };
                _smartFillDebounceTimer.Tick += (s, ev) =>
                {
                    _smartFillDebounceTimer?.Stop();
                    if (!_isTogglingFullscreen) UpdateSmartFill();
                };
            }
            _smartFillDebounceTimer.Stop();
            _smartFillDebounceTimer.Start();
        }

        private void VideoGrid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ToggleFullscreen();
                e.Handled = true;
                return;
            }
            if (e.LeftButton == MouseButtonState.Pressed && !_isFullscreen)
            {
                DragMove();
            }
        }

        private void VideoGrid_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (this.Resources["VideoContextMenu"] is System.Windows.Controls.ContextMenu menu)
            {
                menu.PlacementTarget = videoGrid;
                menu.IsOpen = true;
                e.Handled = true;
            }
        }

        private void VideoGrid_DragOver(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
                e.Effects = System.Windows.DragDropEffects.Copy;
            else
                e.Effects = System.Windows.DragDropEffects.None;
            e.Handled = true;
        }

        private void VideoGrid_Drop(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                var files = e.Data.GetData(System.Windows.DataFormats.FileDrop) as string[];
                if (files != null && files.Length > 0)
                {
                    PlayFile(files[0]);
                }
            }
            e.Handled = true;
        }

        private void ApplyImageEnhancement(int b)
        {
            if (_mpv == IntPtr.Zero) return;

            int brightOffset = 0;
            int contrastOffset = 0;
            int saturationOffset = 0;
            int gammaOffset = 0;

            if (b > 0)
            {
                // ── 高亮度增强模式 (High Brightness Boost) ──
                // 进一步提升色彩饱和度与对比度，彻底消除白雾发灰现象，画面饱满明亮
                brightOffset = (int)Math.Round(b * 0.45);
                contrastOffset = (int)Math.Round(b * 0.55);     // 提升对比度比重 (0.35 -> 0.55)
                saturationOffset = (int)Math.Round(b * 0.45);   // 提升色彩度比重 (0.25 -> 0.45)
                gammaOffset = (int)Math.Round(b * 0.40);
            }
            else if (b < 0)
            {
                // ── 低亮度/暗光夜间模式 (Low Brightness Night Mode) ──
                // 适当提升对比度与色彩饱和度，防止低亮度下画面灰暗发糊、细节发灰失真
                int absB = Math.Abs(b);
                brightOffset = (int)Math.Round(b * 0.50);
                contrastOffset = (int)Math.Round(absB * 0.20);   // 低亮度下微调增加对比度 (+2~+20)，防止发灰发糊
                saturationOffset = (int)Math.Round(absB * 0.15); // 低亮度下适当保色 (+1~+15)，保持色彩丰富度
                gammaOffset = (int)Math.Round(b * 0.30);         // 平滑伽马降阶
            }
            else
            {
                // ── 普通/标准亮度 (Normal Brightness b == 0) ──
                // 保持 100% 原始画质，不做任何改动
                brightOffset = 0;
                contrastOffset = 0;
                saturationOffset = 0;
                gammaOffset = 0;
            }

            MpvSetPropertyString("brightness", brightOffset.ToString());
            MpvSetPropertyString("contrast", contrastOffset.ToString());
            MpvSetPropertyString("saturation", saturationOffset.ToString());
            MpvSetPropertyString("gamma", gammaOffset.ToString());
        }

        public void ApplyBaseBrightness()
        {
            if (_mpv != IntPtr.Zero)
            {
                ApplyImageEnhancement(SettingsService.Instance.Config.BaseBrightness);
                // Restore SmartFill filter if it was enabled
                if (_smartFillEnabled)
                {
                    UpdateSmartFill();
                }
            }
        }

        public void ApplyDimmedBrightness()
        {
            if (_mpv != IntPtr.Zero)
            {
                int dimmed = Math.Max(-100, SettingsService.Instance.Config.BaseBrightness - 30);
                ApplyImageEnhancement(dimmed);
                // Temporarily disable filter to prevent bright blurred edges when dimming
                if (_smartFillEnabled)
                {
                    MpvSetPropertyString("vf", "");
                }
            }
        }

        private void ScheduleSmartFillRetry()
        {
            if (_smartFillRetryTimer == null)
            {
                _smartFillRetryTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
                _smartFillRetryTimer.Tick += (s, e) =>
                {
                    _smartFillRetryTimer.Stop();
                    UpdateSmartFill();
                };
            }
            _smartFillRetryTimer.Stop();
            _smartFillRetryTimer.Start();
        }

        private void BtnFullscreen_Click(object s, RoutedEventArgs e)
            => ToggleFullscreen();

        protected override void OnPreviewMouseDoubleClick(MouseButtonEventArgs e)
        {
            if (_isPipMode)
            {
                e.Handled = true;
                return;
            }
            base.OnPreviewMouseDoubleClick(e);
        }

        private double _preFullscreenW;
        private double _preFullscreenH;
        private double _preFullscreenLeft;
        private double _preFullscreenTop;
        private WindowState _preFullscreenState = WindowState.Normal;

        private void ToggleFullscreen()
        {
            if (_isPipMode || _isTogglingFullscreen) return;
            _isTogglingFullscreen = true;
            try
            {
                _isFullscreen = !_isFullscreen;

                // 1. WindowChrome: Permanently resident, only update ResizeBorderThickness
                var chrome = System.Windows.Shell.WindowChrome.GetWindowChrome(this);
                if (chrome != null)
                {
                    chrome.ResizeBorderThickness = _isFullscreen ? new Thickness(0) : new Thickness(5);
                }

                if (_isFullscreen)
                {
                    _preFullscreenState = this.WindowState;
                    if (this.WindowState == WindowState.Normal)
                    {
                        _preFullscreenW = this.Width;
                        _preFullscreenH = this.Height;
                        _preFullscreenLeft = this.Left;
                        _preFullscreenTop = this.Top;
                    }

                    // 2. Set exclusive borderless window parameters
                    WindowStyle = WindowStyle.None;
                    ResizeMode = ResizeMode.NoResize;
                    WindowState = WindowState.Normal;
                    Topmost = true;

                    try
                    {
                        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                        int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
                        int DWMWCP_DONOTROUND = 1;
                        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref DWMWCP_DONOTROUND, sizeof(int));

                        // 3. Obtain exact physical monitor dimensions and apply DIP conversion + HWND_TOPMOST
                        IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
                        if (monitor != IntPtr.Zero)
                        {
                            MONITORINFO mi = new MONITORINFO();
                            mi.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(MONITORINFO));
                            if (GetMonitorInfo(monitor, ref mi))
                            {
                                SetWindowPos(hwnd, HWND_TOPMOST,
                                    mi.rcMonitor.Left, mi.rcMonitor.Top,
                                    mi.rcMonitor.Right - mi.rcMonitor.Left,
                                    mi.rcMonitor.Bottom - mi.rcMonitor.Top,
                                    SWP_SHOWWINDOW | SWP_FRAMECHANGED);
                            }
                        }
                    }
                    catch { }

                    // Zero-Relayout: Collapse title & controls without changing Grid.RowSpan
                    ccControls.Visibility = Visibility.Collapsed;
                    ccTitle.Visibility = Visibility.Collapsed;

                    // Sync title & clock to fullscreen top bar (no reparenting!)
                    if (txtTitleFS != null) txtTitleFS.Text = txtTitle.Text;
                    if (txtClockFS != null) txtClockFS.Text = txtClock.Text;

                    // Ensure fullscreen bars stay hidden unless cursor moves into hot zones
                    _keepUiAliveUntil = DateTime.MinValue;
                    SnapTopBar(false);
                    SnapBottomBar(false);
                    sliderVolumeFS.Value = sliderVolume.Value;

                    btnFullscreen.Content = "\uE73F"; // exit fullscreen icon
                }
                else
                {
                    // Exit fullscreen
                    _keepUiAliveUntil = DateTime.MinValue;
                    ShowCursorSafe();
                    Topmost = SettingsService.Instance.Config.AlwaysOnTop;
                    ResizeMode = ResizeMode.CanResize;
                    WindowStyle = WindowStyle.None;

                    var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                    try
                    {
                        int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
                        int DWMWCP_ROUND = 2;
                        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref DWMWCP_ROUND, sizeof(int));

                        if (!Topmost)
                        {
                            SetWindowPos(hwnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED);
                        }
                    }
                    catch { }

                    // Zero-Relayout: Restore title & controls
                    ccControls.Visibility = Visibility.Visible;
                    ccTitle.Visibility = Visibility.Visible;

                    // Hide fullscreen overlay
                    SnapTopBar(false);
                    SnapBottomBar(false);

                    if (_preFullscreenState == WindowState.Maximized)
                    {
                        WindowState = WindowState.Maximized;
                    }
                    else
                    {
                        WindowState = WindowState.Normal;
                        if (_preFullscreenW > 100 && _preFullscreenH > 100)
                        {
                            // WPF owns the restored window bounds. Do not also submit the same
                            // bounds through SetWindowPos: the two paths each raise size/layout
                            // notifications and were causing a visible double resize on exit.
                            this.Left = _preFullscreenLeft;
                            this.Top = _preFullscreenTop;
                            this.Width = _preFullscreenW;
                            this.Height = _preFullscreenH;
                        }
                        else
                        {
                            RestoreDefaultWindowSizeAndCenter();
                        }
                    }

                    btnFullscreen.Content = "\uE740"; // fullscreen icon
                }
                
                ScheduleFullscreenLayoutCompletion();
            }
            catch
            {
                _isTogglingFullscreen = false;
            }
        }

        private void ScheduleFullscreenLayoutCompletion()
        {
            // Coalesce all resize side effects into one pass after WPF has consumed the
            // WindowStyle/WindowState changes. ContextIdle is deliberate: Normal may run
            // before the layout generated by SetWindowPos has settled.
            _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() =>
            {
                if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
                {
                    _isTogglingFullscreen = false;
                    return;
                }

                try
                {
                    UpdateBackgroundColors();
                    UpdateOuterBorder();
                    UpdateResponsiveControlBar(ActualWidth > 0 ? ActualWidth : Width);
                    UpdateAbMarkers();
                    UpdateSmartFill();
                }
                finally
                {
                    _isTogglingFullscreen = false;
                    UpdatePopupPlacements();
                }
            }));
        }

        private void RestoreDefaultWindowSizeAndCenter()
        {
            var workArea = SystemParameters.WorkArea;
            double ratio = GetTargetAspectRatio();
            if (ratio <= 0) ratio = 16.0 / 9.0;

            double dw = MpvGetDouble("dwidth");
            double dh = MpvGetDouble("dheight");
            double targetW = 960;
            if (dw > 0 && dh > 0)
            {
                targetW = Math.Min(dw, workArea.Width * 0.65);
                targetW = Math.Max(760, Math.Min(1152, targetW));
            }
            double nonVideoH = (rowTitle?.ActualHeight ?? 40) + (rowControls?.ActualHeight ?? 105);
            if (nonVideoH <= 0) nonVideoH = 145.0;

            double targetH = Math.Round((targetW / ratio) + nonVideoH);

            this.Width = targetW;
            this.Height = targetH;
            this.Left = workArea.Left + Math.Max(0, (workArea.Width - this.Width) / 2.0);
            this.Top = workArea.Top + Math.Max(0, (workArea.Height - this.Height) / 2.0);
        }

        private void UpdateBackgroundColors()
        {
            UpdateThemeBackgrounds();
        }

        private void UpdateThemeBackgrounds()
        {
            var theme = ThemeService.Instance.CurrentTheme;
            string bgHex = theme.WindowBgHex;

            if (_isFullscreen)
            {
                if (!_hasMedia)
                {
                    var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(bgHex);
                    if (_videoPanel != null && !_videoPanel.IsDisposed)
                    {
                        _videoPanel.BackColor = System.Drawing.Color.FromArgb(c.R, c.G, c.B);
                    }
                }
                else
                {
                    if (_videoPanel != null && !_videoPanel.IsDisposed)
                    {
                        _videoPanel.BackColor = System.Drawing.Color.Black;
                    }
                }
                if (videoGrid != null) videoGrid.Background = System.Windows.Media.Brushes.Black;
                this.Background = System.Windows.Media.Brushes.Black;
                if (_mpv != IntPtr.Zero && _lastMpvBackgroundColor != "#000000")
                {
                    _lastMpvBackgroundColor = "#000000";
                    MpvSetPropertyString("background-color", "#000000");
                }
            }
            else
            {
                if (videoGrid != null) videoGrid.SetResourceReference(System.Windows.Controls.Panel.BackgroundProperty, "ThemeWindowBgBrush");
                this.SetResourceReference(System.Windows.Window.BackgroundProperty, "ThemeWindowBgBrush");

                if (_videoPanel != null && !_videoPanel.IsDisposed)
                {
                    if (!_hasMedia)
                    {
                        var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(bgHex);
                        _videoPanel.BackColor = System.Drawing.Color.FromArgb(c.R, c.G, c.B);
                    }
                    else
                    {
                        _videoPanel.BackColor = System.Drawing.Color.Black;
                    }
                }

                if (_mpv != IntPtr.Zero)
                {
                    string targetBg = _hasMedia ? "#000000" : bgHex;
                    if (_lastMpvBackgroundColor != targetBg)
                    {
                        _lastMpvBackgroundColor = targetBg;
                        MpvSetPropertyString("background-color", targetBg);
                    }
                }
            }

            if (fsBottomBar != null && System.Windows.Application.Current?.Resources["ThemeControlBarBrush"] is System.Windows.Media.Brush ctrlBrush)
            {
                fsBottomBar.Background = ctrlBrush;
            }

            // Skip context-menu traversal during fullscreen toggle (menu is not open, no visual benefit)
            if (!_isTogglingFullscreen)
            {
                if (this.Resources["VideoContextMenu"] is System.Windows.Controls.ContextMenu menu)
                {
                    ApplyThemeToMenu(menu);
                }
                SyncThemeMenuChecked(ThemeService.Instance.CurrentThemeKey);
            }
            UpdateSmartFillUI();
            UpdateAutoCropUI();

            if (!_hasMedia && _videoPanel != null && !_videoPanel.IsDisposed)
            {
                _videoPanel.Invalidate();
            }
        }

        private void VideoContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            if (this.Resources["VideoContextMenu"] is System.Windows.Controls.ContextMenu menu)
            {
                ApplyThemeToMenu(menu);
                UpdateAspectRatioMenuChecks();

                double scale = SettingsService.Instance.Config.ContextMenuScale;
                if (scale < 0.8 || scale > 2.5) scale = 1.0;
                menu.LayoutTransform = new System.Windows.Media.ScaleTransform(scale, scale);

                string loopA = MpvGet("ab-loop-a");
                string loopB = MpvGet("ab-loop-b");
                bool hasAbLoop = (loopA != "no" && !string.IsNullOrEmpty(loopA) && loopB != "no" && !string.IsNullOrEmpty(loopB));
                bool isStream = _hasMedia && !_isSkinIdleVideoPlaying && IsNetworkUrl(_currentPlayingFilePath);

                foreach (var item in menu.Items)
                {
                    if (item is System.Windows.Controls.MenuItem mi)
                    {
                        if (mi.Name == "menuAlwaysOnTop")
                        {
                            mi.IsChecked = this.Topmost;
                        }
                        else if (mi.Name == "menuCtxVideoSharpen")
                        {
                            mi.IsChecked = SettingsService.Instance.Config.VideoSharpening;
                        }
                        else if (mi.Name == "menuCtxSaveStream")
                        {
                            mi.IsEnabled = isStream;
                        }
                        else if (mi.Name == "menuExportClip")
                        {
                            mi.IsEnabled = hasAbLoop;
                            var hk = SettingsService.Instance.Config.Hotkeys;
                            string exportHk = hk?.ExportClip ?? "Ctrl+Shift+S";
                            string baseText = I18nService.Instance["MenuExportClipBase"];
                            if (string.IsNullOrEmpty(baseText) || baseText.StartsWith("[")) baseText = "保存 A-B 视频切片";
                            mi.Header = $"{baseText} ({exportHk})";
                        }
                        else if (mi.Name == "menuExportClipAs")
                        {
                            mi.IsEnabled = hasAbLoop;
                        }
                        else if (mi.Name == "menuCtxBrightness")
                        {
                            if (mi.Items.Count > 0 && mi.Items[0] is System.Windows.Controls.MenuItem subMi && subMi.Header is System.Windows.Controls.StackPanel sp)
                            {
                                foreach (var child in sp.Children)
                                {
                                    if (child is System.Windows.Controls.Slider s)
                                    {
                                        s.Value = SettingsService.Instance.Config.BaseBrightness;
                                        break;
                                    }
                                }
                            }
                        }
                        else if (mi.Tag?.ToString() == "LanguageSubmenu")
                        {
                            PopulateLanguageMenu(mi);
                        }
                    }
                }
                PopulateAudioTracksMenu();
            }
        }

        private void PopulateAudioTracksMenu()
        {
            if (this.Resources["VideoContextMenu"] is not System.Windows.Controls.ContextMenu menu) return;
            System.Windows.Controls.MenuItem? menuAudioTracks = null;
            foreach (var item in menu.Items)
            {
                if (item is System.Windows.Controls.MenuItem mi && mi.Name == "menuAudioTracks")
                {
                    menuAudioTracks = mi;
                    break;
                }
            }

            if (menuAudioTracks == null) return;
            menuAudioTracks.Items.Clear();

            if (_mpv == IntPtr.Zero)
            {
                menuAudioTracks.Visibility = Visibility.Collapsed;
                return;
            }

            int count = (int)MpvGetDouble("track-list/count");
            var audioTracks = new List<(int id, string label, bool selected)>();

            for (int i = 0; i < count; i++)
            {
                string type = MpvGet($"track-list/{i}/type");
                if (type == "audio")
                {
                    int id = (int)MpvGetDouble($"track-list/{i}/id");
                    string title = MpvGet($"track-list/{i}/title");
                    string lang = MpvGet($"track-list/{i}/lang");
                    bool selected = MpvGet($"track-list/{i}/selected") == "yes";

                    string label = !string.IsNullOrEmpty(title) ? title :
                                  (!string.IsNullOrEmpty(lang) ? lang : $"音轨 #{id}");
                    audioTracks.Add((id, label, selected));
                }
            }

            if (audioTracks.Count <= 1)
            {
                menuAudioTracks.Visibility = Visibility.Collapsed;
                return;
            }

            menuAudioTracks.Visibility = Visibility.Visible;
            foreach (var trk in audioTracks)
            {
                var item = new System.Windows.Controls.MenuItem
                {
                    Header = trk.label,
                    Tag = trk.id,
                    IsCheckable = true,
                    IsChecked = trk.selected,
                    Style = (Style)FindResource("SmartFillMenuItemStyle")
                };
                item.Click += (s, e) =>
                {
                    MpvSetPropertyString("aid", trk.id.ToString());
                    ShowOsd($"音轨已切换: {trk.label}");
                };
                menuAudioTracks.Items.Add(item);
            }
        }

        private void MenuAlwaysOnTop_Click(object sender, RoutedEventArgs e)
        {
            var cfg = SettingsService.Instance.Config;
            cfg.AlwaysOnTop = !cfg.AlwaysOnTop;
            SettingsService.Instance.Save();
            RestoreTopmostState();
            ShowOsd(cfg.AlwaysOnTop ? I18nService.Instance["OsdAlwaysOnTopOn"] : I18nService.Instance["OsdAlwaysOnTopOff"]);
        }

        private void SyncThemeMenuChecked(string activeKey)
        {
            if (this.Resources["VideoContextMenu"] is not System.Windows.Controls.ContextMenu menu) return;
            foreach (var topLevel in menu.Items)
            {
                if (topLevel is System.Windows.Controls.MenuItem parentItem && parentItem.Items.Count > 0)
                {
                    foreach (var child in parentItem.Items)
                    {
                        if (child is System.Windows.Controls.MenuItem mi)
                        {
                            string? tag = mi.Tag?.ToString();
                            if (!string.IsNullOrEmpty(tag) && ThemeService.Instance.Themes.ContainsKey(tag))
                            {
                                mi.IsChecked = (tag == activeKey);
                            }
                        }
                    }
                }
            }
        }

        private void ApplyThemeToMenu(System.Windows.Controls.ContextMenu menu)
        {
            var appRes = System.Windows.Application.Current?.Resources;
            if (appRes == null) return;

            var menuBrush = appRes["ThemeMenuBgBrush"] as System.Windows.Media.Brush;
            var textBrush = appRes["ThemeTextBrush"] as System.Windows.Media.Brush;
            var borderBrush = appRes["ThemeBorderBrush"] as System.Windows.Media.Brush;

            menu.Background = System.Windows.Media.Brushes.Transparent;
            if (textBrush != null)
            {
                menu.Foreground = textBrush;
            }
            if (borderBrush != null)
            {
                menu.BorderBrush = borderBrush;
            }

            if (menu.Template?.FindName("menuBorder", menu) is System.Windows.Controls.Border border)
            {
                if (menuBrush != null) border.Background = menuBrush;
                if (borderBrush != null) border.BorderBrush = borderBrush;
            }

            ApplyThemeToMenuItems(menu.Items);
        }

        private void ApplyThemeToMenuItems(System.Windows.Controls.ItemCollection items)
        {
            var appRes = System.Windows.Application.Current?.Resources;
            var textBrush = appRes?["ThemeTextBrush"] as System.Windows.Media.Brush;
            var sepBrush = appRes?["ThemeMenuSeparatorBrush"] as System.Windows.Media.Brush;

            foreach (var item in items)
            {
                if (item is System.Windows.Controls.MenuItem mi)
                {
                    if (textBrush != null) mi.Foreground = textBrush;

                    if (mi.HasItems)
                    {
                        mi.SubmenuOpened -= MenuItem_SubmenuOpened;
                        mi.SubmenuOpened += MenuItem_SubmenuOpened;
                        ApplyThemeToMenuItems(mi.Items);
                    }
                }
                else if (item is System.Windows.Controls.Separator sep)
                {
                    if (sepBrush != null) sep.Background = sepBrush;
                }
            }
        }

        private void MenuItem_SubmenuOpened(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.MenuItem mi)
            {
                if (mi.Template?.FindName("PART_Popup", mi) is System.Windows.Controls.Primitives.Popup popup)
                {
                    System.Windows.Controls.Border? border = popup.Child as System.Windows.Controls.Border
                        ?? (popup.Child as System.Windows.Controls.Grid)?.Children.OfType<System.Windows.Controls.Border>().FirstOrDefault();

                    if (border != null)
                    {
                        var menuBrush = System.Windows.Application.Current?.Resources["ThemeMenuBgBrush"] as System.Windows.Media.Brush;
                        var borderBrush = System.Windows.Application.Current?.Resources["ThemeBorderBrush"] as System.Windows.Media.Brush;
                        if (menuBrush != null)
                        {
                            border.Background = menuBrush;
                        }
                        if (borderBrush != null)
                        {
                            border.BorderBrush = borderBrush;
                        }
                    }
                }
            }
        }

        private static string GetConfigPath()
        {
            string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AniPlayer");
            Directory.CreateDirectory(dir);
            return System.IO.Path.Combine(dir, "config.json");
        }

        private void LoadSettingsFromConfig()
        {
            _autoCropMode = "none";
            _smartFillMode = "none";

            // Restore Context Menu Checkmarks dynamically
            var videoMenu = this.Resources["VideoContextMenu"] as System.Windows.Controls.ContextMenu;
            if (videoMenu != null)
            {
                string lang = I18nService.Instance.CurrentLanguage;
                string themeKey = ThemeService.Instance.CurrentThemeKey;

                foreach (var item in videoMenu.Items)
                {
                    if (item is System.Windows.Controls.MenuItem mi && mi.HasItems)
                    {
                        foreach (var sub in mi.Items)
                        {
                            if (sub is System.Windows.Controls.MenuItem subMi)
                            {
                                string? tag = subMi.Tag?.ToString();
                                if (tag == "zh-CN" || tag == "en-US")
                                {
                                    subMi.IsChecked = (tag == lang);
                                }
                                else if (!string.IsNullOrEmpty(tag) && ThemeService.Instance.Themes.ContainsKey(tag))
                                {
                                    subMi.IsChecked = (tag == themeKey);
                                }
                            }
                        }
                    }
                }
            }

            // Restore UI indicators
            UpdateSmartFillUI();
            UpdateAutoCropUI();
        }

        private void SaveSettingsToConfig()
        {
            try
            {
                var cfg = SettingsService.Instance.Config;
                cfg.Language = I18nService.Instance.CurrentLanguage;
                cfg.Theme = ThemeService.Instance.CurrentThemeKey;
                cfg.ActiveSkin = ThemeService.Instance.ActiveSkinKey;
                SettingsService.Instance.Save();
            }
            catch { }
        }

        private bool IsMediaSeekable()
        {
            if (_mpv == IntPtr.Zero || string.IsNullOrEmpty(_currentPlayingFilePath)) return false;

            string seekable = MpvGet("seekable");
            if (seekable == "no") return false;

            if (IsNetworkUrl(_currentPlayingFilePath))
            {
                double dur = MpvGetDouble("duration");
                if (dur <= 0) return false;

                if (_currentPlayingFilePath.StartsWith("rtmp://", StringComparison.OrdinalIgnoreCase) ||
                    _currentPlayingFilePath.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase))
                {
                    if (dur <= 0 || seekable != "yes") return false;
                }
            }
            return true;
        }

        // ── Timeline Slider ───────────────────────────────────────────────
        private void Timeline_PreviewMouseDown(object s, MouseButtonEventArgs e)
        {
            if (IsMediaSeekable())
            {
                _draggingTimeline = true;
            }
        }

        private void Timeline_PreviewMouseUp(object s, MouseButtonEventArgs e)
        {
            _draggingTimeline = false;
            if (_isCurrentImage)
            {
                _imageElapsedSec = Math.Clamp(sliderTimeline.Value, 0.0, (double)Math.Max(1, SettingsService.Instance.Config.ImageDurationSec));
                return;
            }
            if (_mpv != IntPtr.Zero && IsMediaSeekable())
            {
                string t = sliderTimeline.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                MpvNative.mpv_command_string(_mpv, $"seek {t} absolute");
            }
        }

        private void Timeline_ValueChanged(object s, RoutedPropertyChangedEventArgs<double> e)
        {
            // When user clicks the slider, IsMoveToPointEnabled sets the value instantly.
            // ValueChanged fires immediately. If we are dragging, we do live scrubbing.
            // If we just clicked (Mouse button might not be held if it was a quick click on the track),
            // we should seek anyway if it wasn't triggered by the timer.
            if (!_timerUpdating)
            {
                if (_isCurrentImage)
                {
                    _imageElapsedSec = Math.Clamp(e.NewValue, 0.0, (double)Math.Max(1, SettingsService.Instance.Config.ImageDurationSec));
                    txtTime.Text = $"{Fmt(_imageElapsedSec)} / {Fmt(sliderTimeline.Maximum)}";
                    if (txtTimeFS != null) txtTimeFS.Text = txtTime.Text;
                    return;
                }

                if (!IsMediaSeekable()) return;

                _timerUpdating = true;
                if (s == sliderTimeline)
                {
                    if (sliderTimelineFS != null) sliderTimelineFS.Value = e.NewValue;
                }
                else if (s == sliderTimelineFS)
                {
                    if (sliderTimeline != null) sliderTimeline.Value = e.NewValue;
                }

                txtTime.Text = $"{Fmt(e.NewValue)} / {Fmt(sliderTimeline?.Maximum ?? 0)}";
                if (txtTimeFS != null)
                    txtTimeFS.Text = txtTime.Text;
                _timerUpdating = false;

                if (_mpv != IntPtr.Zero)
                {
                    string t = e.NewValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    MpvNative.mpv_command_string(_mpv, $"seek {t} absolute");
                }
            }
        }

        public void EnqueuePlaylist(string[] files)
        {
            // Omitted for brevity in diffs, same logic
            foreach (var f in files)
            {
                MpvNative.mpv_command_string(_mpv, $"loadfile \"{f}\" append");
            }
        }

        private System.Windows.Window? _transitionWindow;

        public async System.Threading.Tasks.Task CrossfadeAndPlayAsync(Action playAction)
        {
            if (_isTransitioning) return;
            _isTransitioning = true;

            if (_transitionWindow == null)
            {
                _transitionWindow = new System.Windows.Window
                {
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = System.Windows.Media.Brushes.Transparent,
                    ShowInTaskbar = false,
                    Topmost = false,
                    Owner = this,
                    IsHitTestVisible = false,
                    ResizeMode = ResizeMode.NoResize
                };
                var border = new System.Windows.Controls.Border { Background = System.Windows.Media.Brushes.Black, Opacity = 0 };
                _transitionWindow.Content = border;
            }

            try
            {
                var pt = videoGrid.PointToScreen(new System.Windows.Point(0, 0));
                var source = PresentationSource.FromVisual(this);
                double dpiX = 1.0, dpiY = 1.0;
                if (source?.CompositionTarget != null)
                {
                    dpiX = source.CompositionTarget.TransformToDevice.M11;
                    dpiY = source.CompositionTarget.TransformToDevice.M22;
                }

                _transitionWindow.Left = pt.X / dpiX;
                _transitionWindow.Top = pt.Y / dpiY;
                _transitionWindow.Width = videoGrid.ActualWidth;
                _transitionWindow.Height = videoGrid.ActualHeight;

                _transitionWindow.Show();

                var borderFade = (System.Windows.Controls.Border)_transitionWindow.Content;

                var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0, 1.0, TimeSpan.FromMilliseconds(600))
                {
                    EasingFunction = new System.Windows.Media.Animation.SineEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
                };
                System.Windows.Media.Animation.Timeline.SetDesiredFrameRate(fadeIn, 60);

                var tcsIn = new System.Threading.Tasks.TaskCompletionSource<bool>();
                fadeIn.Completed += (s, ev) => tcsIn.SetResult(true);
                borderFade.BeginAnimation(OpacityProperty, fadeIn);
                await tcsIn.Task;

                playAction();

                await System.Threading.Tasks.Task.Delay(100);

                var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(0.8, 0, TimeSpan.FromMilliseconds(600))
                {
                    EasingFunction = new System.Windows.Media.Animation.SineEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
                };
                System.Windows.Media.Animation.Timeline.SetDesiredFrameRate(fadeOut, 60);

                var tcsOut = new System.Threading.Tasks.TaskCompletionSource<bool>();
                fadeOut.Completed += (s, ev) => tcsOut.SetResult(true);
                borderFade.BeginAnimation(OpacityProperty, fadeOut);
                await tcsOut.Task;

                _transitionWindow.Hide();
            }
            catch { }
            finally
            {
                _isTransitioning = false;
            }
        }

        public async void PlayFileWithTransition(string file)
        {
            if (_useTransition)
            {
                await CrossfadeAndPlayAsync(() => PlayFile(file));
            }
            else
            {
                PlayFile(file);
            }
        }

        // ── Single Instance Named Pipe ─────────────────────────────────────────────────
        private void Volume_ValueChanged(object s, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_mpv == IntPtr.Zero) return;
            
            if (s == sliderVolume && sliderVolumeFS != null && Math.Abs(sliderVolumeFS.Value - e.NewValue) > 0.01)
                sliderVolumeFS.Value = e.NewValue;
            else if (s == sliderVolumeFS && sliderVolume != null && Math.Abs(sliderVolume.Value - e.NewValue) > 0.01)
                sliderVolume.Value = e.NewValue;
                
            string v = e.NewValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
            MpvNative.mpv_command_string(_mpv, $"set volume {v}");
            if (_mpvBgm != IntPtr.Zero && !string.IsNullOrEmpty(_currentBgmFile))
            {
                double bgmVol = CalculateSkinBgmVolume(e.NewValue);
                MpvNative.mpv_set_property_string(_mpvBgm, "volume", bgmVol.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            if (!_timerUpdating && (s == sliderVolume || s == sliderVolumeFS))
            {
                SettingsService.Instance.Config.LastVolume = (int)Math.Round(e.NewValue);
                ShowOsd(string.Format(I18nService.Instance["OsdVolume"], (int)e.NewValue));
            }
        }

        private bool _isNetworkBuffering = false;
        private DateTime _networkLoadStartTime = DateTime.MinValue;
        private bool _networkLoadFailedHandled = false;
        private DateTime _mediaLoadStartTime = DateTime.MinValue;
        private bool _mediaLoadFailedHandled = false;
        private bool _currentMediaLoadedSuccessfully = false;
        private System.Windows.Threading.DispatcherTimer? _corruptAutoNextTimer;
        private int _corruptAutoNextCountdown = 0;
        private DateTime _lastBufferingRefreshTime = DateTime.MinValue;
        private string _cachedBufferingText = "";
        private double _lastDemuxerTotalBytes = -1;
        private DateTime _lastDemuxerBytesTime = DateTime.MinValue;

        public static string FormatNetworkSpeed(double bytesPerSec)
        {
            if (bytesPerSec <= 0) return "0 KB/s";
            if (bytesPerSec < 1024 * 1024)
            {
                double kb = bytesPerSec / 1024.0;
                return kb < 10 ? $"{kb:F1} KB/s" : $"{(int)Math.Round(kb)} KB/s";
            }
            else
            {
                double mb = bytesPerSec / (1024.0 * 1024.0);
                return $"{mb:F1} MB/s";
            }
        }

        private string BuildBufferingMessage(string percentText, string speedText)
        {
            bool hasPercent = !string.IsNullOrWhiteSpace(percentText) && percentText != "0%";
            bool hasSpeed = !string.IsNullOrWhiteSpace(speedText) && speedText != "0 KB/s";

            if (hasPercent && hasSpeed)
            {
                string format = I18nService.Instance["OsdBufferingWithSpeed"];
                if (string.IsNullOrEmpty(format) || format == "OsdBufferingWithSpeed")
                {
                    format = (I18nService.Instance.CurrentLanguage == "en-US")
                        ? "⏳ Buffering: {0} ({1})"
                        : "⏳ 正在缓冲: {0} ({1})";
                }
                return string.Format(format, percentText, speedText);
            }
            else if (hasPercent)
            {
                string format = I18nService.Instance["OsdBuffering"];
                if (string.IsNullOrEmpty(format) || format == "OsdBuffering")
                {
                    format = (I18nService.Instance.CurrentLanguage == "en-US")
                        ? "⏳ Buffering: {0}"
                        : "⏳ 正在缓冲: {0}";
                }
                return string.Format(format, percentText);
            }
            else if (hasSpeed)
            {
                string format = I18nService.Instance["OsdBufferingEmptyWithSpeed"];
                if (string.IsNullOrEmpty(format) || format == "OsdBufferingEmptyWithSpeed")
                {
                    format = (I18nService.Instance.CurrentLanguage == "en-US")
                        ? "⏳ Buffering... ({0})"
                        : "⏳ 正在缓冲... ({0})";
                }
                return string.Format(format, speedText);
            }
            else
            {
                string msg = I18nService.Instance["OsdBufferingEmpty"];
                if (string.IsNullOrEmpty(msg) || msg == "OsdBufferingEmpty")
                {
                    msg = (I18nService.Instance.CurrentLanguage == "en-US") ? "⏳ Connecting to stream..." : "⏳ 正在连接网络流...";
                }
                return msg;
            }
        }

        private void ShowBufferingOsd(string text)
        {
            if (txtOsd == null || popupOsd == null) return;
            if (this.WindowState == WindowState.Minimized || !_isWindowActive)
            {
                popupOsd.IsOpen = false;
                return;
            }
            txtOsd.Text = text;
            popupOsd.IsOpen = true;
            EnsurePopupZOrder(popupOsd);
            _osdTimer?.Stop(); // Keep buffering bubble continuously visible without auto-timer dismissal
        }

        private void ShowBuffering(string percentText = "")
        {
            DateTime now = DateTime.UtcNow;
            if ((now - _lastBufferingRefreshTime).TotalMilliseconds >= 500 || string.IsNullOrEmpty(_cachedBufferingText))
            {
                _lastBufferingRefreshTime = now;

                double rawSpeed = MpvGetDouble("demuxer-cache-state/raw-input-rate");
                if (rawSpeed <= 0) rawSpeed = MpvGetDouble("cache-speed");

                double totalBytes = MpvGetDouble("demuxer-cache-state/total-bytes");
                if (totalBytes <= 0) totalBytes = MpvGetDouble("demuxer-cache-state/fw-bytes");

                double speedBytesPerSec = 0;
                if (rawSpeed > 0)
                {
                    speedBytesPerSec = rawSpeed;
                }
                else if (totalBytes > 0 && _lastDemuxerTotalBytes >= 0 && _lastDemuxerBytesTime > DateTime.MinValue)
                {
                    double dt = (now - _lastDemuxerBytesTime).TotalSeconds;
                    if (dt > 0.05)
                    {
                        double deltaBytes = Math.Max(0, totalBytes - _lastDemuxerTotalBytes);
                        speedBytesPerSec = deltaBytes / dt;
                    }
                }

                if (totalBytes > 0)
                {
                    _lastDemuxerTotalBytes = totalBytes;
                    _lastDemuxerBytesTime = now;
                }

                string sText = (speedBytesPerSec > 0) ? FormatNetworkSpeed(speedBytesPerSec) : "";
                _cachedBufferingText = BuildBufferingMessage(percentText, sText);
            }

            ShowBufferingOsd(_cachedBufferingText);
        }

        private void HideBuffering()
        {
            _cachedBufferingText = "";
            _lastBufferingRefreshTime = DateTime.MinValue;
            _lastDemuxerTotalBytes = -1;
            _lastDemuxerBytesTime = DateTime.MinValue;

            if (popupOsd != null && popupOsd.IsOpen && txtOsd != null && !string.IsNullOrEmpty(txtOsd.Text))
            {
                if (txtOsd.Text.Contains("缓冲") || txtOsd.Text.Contains("Buffer") || txtOsd.Text.Contains("⏳") || txtOsd.Text.Contains("连接") || txtOsd.Text.Contains("Connecting"))
                {
                    popupOsd.IsOpen = false;
                }
            }
        }

        private bool _hasAutoSelectedAudioLanguage = false;

        private void EnsureOptimalAudioTrackSelected()
        {
            if (_mpv == IntPtr.Zero || _isMuted || _isCurrentImage) return;

            // Fast exit: if already auto-selected and audio is actively playing, avoid redundant track-list queries
            if (_hasAutoSelectedAudioLanguage)
            {
                string aid = MpvGet("aid");
                if (!string.IsNullOrEmpty(aid) && aid != "no" && aid != "0")
                {
                    return;
                }
            }

            try
            {
                int count = (int)MpvGetDouble("track-list/count");
                if (count <= 0) return;

                var audioTracks = new List<(int id, string lang, string title, string codec, bool selected, bool isDefault)>();
                for (int i = 0; i < count; i++)
                {
                    if (MpvGet($"track-list/{i}/type") == "audio")
                    {
                        int id = (int)MpvGetDouble($"track-list/{i}/id");
                        string lang = MpvGet($"track-list/{i}/lang") ?? "";
                        string title = MpvGet($"track-list/{i}/title") ?? "";
                        string codec = MpvGet($"track-list/{i}/codec") ?? "";
                        bool selected = MpvGet($"track-list/{i}/selected") == "yes";
                        bool isDef = MpvGet($"track-list/{i}/default") == "yes";
                        if (id > 0)
                        {
                            audioTracks.Add((id, lang, title, codec, selected, isDef));
                        }
                    }
                }

                if (audioTracks.Count == 0) return;

                bool hasSelected = audioTracks.Any(t => t.selected);
                string currentAid = MpvGet("aid");
                bool aidIsNone = string.IsNullOrEmpty(currentAid) || currentAid == "no" || currentAid == "0";

                // If no audio is currently playing/selected, or first auto-select pass
                if (aidIsNone || !hasSelected || !_hasAutoSelectedAudioLanguage)
                {
                    _hasAutoSelectedAudioLanguage = true;

                    // 1. 获取用户当前界面设置的语言（CurrentLanguage），提取多维度泛匹配关键字
                    string currentLangCode = I18nService.Instance.CurrentLanguage ?? "en-US";
                    var preferredKeywords = I18nService.GetLanguageMatchingKeywords(currentLangCode);
                    var englishFallbackKeywords = I18nService.GetLanguageMatchingKeywords("en-US");

                    // 第一优先级：泛匹配当前用户的首选语言 (CurrentLanguage)
                    var match = audioTracks.FirstOrDefault(t =>
                        preferredKeywords.Any(p => t.lang.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                   t.title.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0));

                    // 第二优先级：若当前语言非英语且未命中，则优雅回退到国际通用英语 (English)
                    if (match.id <= 0 && !currentLangCode.StartsWith("en", StringComparison.OrdinalIgnoreCase))
                    {
                        match = audioTracks.FirstOrDefault(t =>
                            englishFallbackKeywords.Any(f => t.lang.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                             t.title.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0));
                    }

                    // 第三优先级：若英语亦未命中，回退到流媒体标记的默认轨 (isDefault)，或列表第一项 (Track 0)
                    if (match.id <= 0)
                    {
                        match = audioTracks.FirstOrDefault(t => t.isDefault);
                        if (match.id <= 0) match = audioTracks[0];
                    }

                    if (match.id > 0)
                    {
                        MpvNative.mpv_command_string(_mpv, $"set aid {match.id}");
                    }
                }
            }
            catch { }
        }

        private bool _autoQualityAdaptiveEnabled = true;
        private readonly List<DateTime> _recentBufferStalls = new();
        private DateTime _lastQualityAutoDowngradeTime = DateTime.MinValue;
        private int _autoDowngradedFromTrackId = -1;

        private void CheckAdaptiveBitrateStall()
        {
            if (!_autoQualityAdaptiveEnabled || _mpv == IntPtr.Zero) return;
            DateTime nowUtc = DateTime.UtcNow;
            _recentBufferStalls.Add(nowUtc);
            _recentBufferStalls.RemoveAll(t => (nowUtc - t).TotalSeconds > 30);

            if (_recentBufferStalls.Count >= 2 && (nowUtc - _lastQualityAutoDowngradeTime).TotalSeconds > 25)
            {
                TryAutoDowngradeStreamQuality();
            }
        }

        private void TryAutoDowngradeStreamQuality()
        {
            if (_mpv == IntPtr.Zero) return;
            try
            {
                int count = (int)MpvGetDouble("track-list/count");
                var videoTracks = new List<(int id, int w, int h, long bitrate, string title, bool selected)>();

                for (int i = 0; i < count; i++)
                {
                    if (MpvGet($"track-list/{i}/type") == "video")
                    {
                        int id = (int)MpvGetDouble($"track-list/{i}/id");
                        int w = (int)MpvGetDouble($"track-list/{i}/demux-w");
                        int h = (int)MpvGetDouble($"track-list/{i}/demux-h");
                        long br = (long)MpvGetDouble($"track-list/{i}/demux-bitrate");
                        string title = MpvGet($"track-list/{i}/title") ?? "";
                        bool sel = MpvGet($"track-list/{i}/selected") == "yes";
                        if (id > 0)
                        {
                            videoTracks.Add((id, w, h, br, title, sel));
                        }
                    }
                }

                if (videoTracks.Count <= 1) return;

                // Sort descending by resolution (height * width) and bitrate
                videoTracks.Sort((a, b) =>
                {
                    long resA = (long)a.w * a.h;
                    long resB = (long)b.w * b.h;
                    if (resA != resB) return resB.CompareTo(resA);
                    return b.bitrate.CompareTo(a.bitrate);
                });

                var current = videoTracks.FirstOrDefault(t => t.selected);
                int currentIndex = current.id > 0 ? videoTracks.FindIndex(t => t.id == current.id) : 0;

                // If a lower quality track exists
                if (currentIndex >= 0 && currentIndex + 1 < videoTracks.Count)
                {
                    var nextLower = videoTracks[currentIndex + 1];
                    if (_autoDowngradedFromTrackId < 0)
                    {
                        _autoDowngradedFromTrackId = current.id;
                    }
                    _lastQualityAutoDowngradeTime = DateTime.UtcNow;
                    _recentBufferStalls.Clear();

                    MpvNative.mpv_command_string(_mpv, $"set vid {nextLower.id}");

                    string qualityLabel = nextLower.h > 0 ? $"{nextLower.h}P" : (!string.IsNullOrEmpty(nextLower.title) ? nextLower.title : $"#{nextLower.id}");
                    string format = I18nService.Instance["OsdAutoQualityDowngraded"];
                    if (string.IsNullOrEmpty(format) || format == "OsdAutoQualityDowngraded")
                    {
                        format = (I18nService.Instance.CurrentLanguage == "en-US")
                            ? "📶 Network fluctuation detected: Auto-switched to {0} for smooth playback"
                            : "📶 检测到网络波动，已自动优化清晰度至 {0} 保持流畅播放";
                    }
                    ShowOsd(string.Format(format, qualityLabel), 4000);
                }
            }
            catch { }
        }

        private async void HandleNetworkStreamLoadFailed(string failedUrl, string? reason = null)
        {
            HideBuffering();
            _isNetworkBuffering = false;
            _hasMedia = false;

            string displayReason = reason ?? "";
            if (string.IsNullOrWhiteSpace(displayReason))
            {
                try
                {
                    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3.5) };
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                    var req = new HttpRequestMessage(HttpMethod.Get, failedUrl);
                    var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
                    if (!resp.IsSuccessStatusCode)
                    {
                        int code = (int)resp.StatusCode;
                        if (code == 403)
                            displayReason = I18nService.Instance["OsdStreamError403"];
                        else if (code == 404)
                            displayReason = I18nService.Instance["OsdStreamError404"];
                        else if (code == 401)
                            displayReason = I18nService.Instance["OsdStreamError401"];
                        else
                            displayReason = $"HTTP {code} ({resp.ReasonPhrase})";
                    }
                }
                catch (TaskCanceledException)
                {
                    displayReason = I18nService.Instance["OsdStreamErrorTimeout"];
                }
                catch (HttpRequestException ex)
                {
                    if (ex.InnerException is System.Net.Sockets.SocketException)
                    {
                        displayReason = I18nService.Instance["OsdStreamErrorHost"];
                    }
                    else
                    {
                        displayReason = ex.Message;
                    }
                }
                catch { }
            }

            if (string.IsNullOrWhiteSpace(displayReason))
            {
                displayReason = I18nService.Instance["OsdStreamErrorUnsupported"];
            }

            string format = I18nService.Instance["OsdStreamLoadFailedFormat"];
            if (string.IsNullOrEmpty(format) || format == "OsdStreamLoadFailedFormat")
            {
                format = (I18nService.Instance.CurrentLanguage == "en-US")
                    ? "⚠️ Network stream load failed: {0}"
                    : "⚠️ 网络流加载失败: {0}";
            }

            string finalMsg = string.Format(format, displayReason);
            ShowOsd(finalMsg, 5000);
        }

        private void HandleUnplayableMedia(string path)
        {
            CancelCorruptAutoNext();
            _hasMedia = false;
            _videoSizeSet = false;
            _currentMediaHasVideoTrack = false;

            // Reset transport controls & time labels
            txtTime.Text = "--:-- / --:--";
            if (txtTimeFS != null) txtTimeFS.Text = "--:-- / --:--";
            btnPlay.Content = "\uE102";
            if (btnPlayFS != null) btnPlayFS.Content = "\uE102";

            // Check if playlist has other playable items WITHOUT mutating CurrentIndex
            var peekNext = PlaylistManager.Instance.PeekNext();
            bool hasOtherItem = (peekNext != null && !string.Equals(peekNext.FilePath, path, StringComparison.OrdinalIgnoreCase));

            if (hasOtherItem)
            {
                _corruptAutoNextCountdown = 5;
                UpdateCorruptCountdownOsd();

                _corruptAutoNextTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1)
                };
                _corruptAutoNextTimer.Tick += (s, e) =>
                {
                    _corruptAutoNextCountdown--;
                    if (_corruptAutoNextCountdown <= 0)
                    {
                        CancelCorruptAutoNext();
                        var next = PlaylistManager.Instance.GetNext();
                        if (next != null)
                        {
                            PlayFileWithTransition(next.FilePath);
                        }
                    }
                    else
                    {
                        UpdateCorruptCountdownOsd();
                    }
                };
                _corruptAutoNextTimer.Start();
            }
            else
            {
                string msg = I18nService.Instance["OsdCorruptFileNoNext"];
                ShowOsd(msg, 5000);
            }
        }

        private void UpdateCorruptCountdownOsd()
        {
            string fmt = I18nService.Instance["OsdCorruptFileAutoNextFormat"];
            string msg = string.Format(fmt, _corruptAutoNextCountdown);
            ShowOsd(msg, 1500);
        }

        private void CancelCorruptAutoNext()
        {
            if (_corruptAutoNextTimer != null)
            {
                _corruptAutoNextTimer.Stop();
                _corruptAutoNextTimer = null;
            }
            _corruptAutoNextCountdown = 0;
        }

        private void ShowOsd(string text, int durationMs = 1500)
        {
            if (txtOsd == null || popupOsd == null || _osdTimer == null) return;
            if (this.WindowState == WindowState.Minimized || !_isWindowActive)
            {
                popupOsd.IsOpen = false;
                return;
            }
            txtOsd.Text = text;
            popupOsd.IsOpen = true;
            EnsurePopupZOrder(popupOsd);
            _osdTimer.Stop();
            _osdTimer.Interval = TimeSpan.FromMilliseconds(durationMs);
            _osdTimer.Start();
        }

        private static double CalculateSteppedValue(double current, double step, bool isIncrease, double min, double max)
        {
            if (step <= 0) return Math.Clamp(current, min, max);

            double next;
            if (isIncrease)
            {
                double remainder = current % step;
                if (remainder < 0) remainder += step;

                if (remainder < 0.001 || (step - remainder) < 0.001)
                {
                    next = current + step;
                }
                else
                {
                    next = Math.Floor(current / step) * step + step;
                }
            }
            else
            {
                double remainder = current % step;
                if (remainder < 0) remainder += step;

                if (remainder < 0.001 || (step - remainder) < 0.001)
                {
                    next = current - step;
                }
                else
                {
                    next = Math.Ceiling(current / step) * step - step;
                }
            }

            return Math.Clamp(Math.Round(next, 2), min, max);
        }

        private void AdjustVolume(bool isIncrease, double step = 5.0)
        {
            if (sliderVolume == null) return;
            double current = sliderVolume.Value;
            double newVol = CalculateSteppedValue(current, step, isIncrease, 0, 200);
            if (Math.Abs(sliderVolume.Value - newVol) < 0.001)
            {
                ShowOsd(string.Format(I18nService.Instance["OsdVolume"], (int)Math.Round(newVol)));
            }
            else
            {
                sliderVolume.Value = newVol;
            }
        }

        private void AdjustBrightness(bool isIncrease, double step = 10.0)
        {
            if (sliderBrightness == null) return;
            double current = sliderBrightness.Value;
            double newBrightness = CalculateSteppedValue(current, step, isIncrease, -50, 100);
            if (Math.Abs(sliderBrightness.Value - newBrightness) < 0.001)
            {
                int displayPct = 100 + (int)Math.Round(newBrightness);
                string fmtStr = I18nService.Instance["OsdBrightness"];
                if (string.IsNullOrEmpty(fmtStr) || fmtStr.StartsWith("[")) fmtStr = "亮度: {0}%";
                ShowOsd(string.Format(fmtStr, displayPct));
            }
            else
            {
                sliderBrightness.Value = newBrightness;
            }
        }

        private void Volume_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            AdjustVolume(e.Delta > 0);
            e.Handled = true;
        }

        private void Brightness_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            AdjustBrightness(e.Delta > 0);
            e.Handled = true;
        }

        private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_mpv == IntPtr.Zero) return;
            // Only handle wheel on the video area (not when hovering over sliders / buttons / popups)
            var hit = Mouse.DirectlyOver;
            bool onVideoArea = hit == null || hit is System.Windows.Forms.Integration.WindowsFormsHost
                || (hit is System.Windows.FrameworkElement fe && (fe.Name == "videoGrid" || fe.Name == "videoHost" || fe.Name == "mainGrid"));
            if (!onVideoArea) return;

            AdjustVolume(e.Delta > 0);
            e.Handled = true;
        }

        // ── Keyboard shortcuts ────────────────────────────────────────────
        private static bool IsHotkeyMatch(string combo, string primary, string secondary)
        {
            if (string.IsNullOrEmpty(combo)) return false;
            if (!string.IsNullOrEmpty(primary) && combo == primary) return true;
            if (!string.IsNullOrEmpty(secondary) && combo == secondary) return true;
            return false;
        }

        private void Window_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!popupSettings.IsOpen && !popupLibrary.IsOpen && !(popupOpenUrl != null && popupOpenUrl.IsOpen))
            {
                EnsureMainCanvasFocusAndDisableIme();
            }
        }

        private void Window_PreviewKeyDown(object sender, WpfKeyEventArgs e)
        {
            if (_mpv == IntPtr.Zero || !this.IsActive) return;

            // Block shortcuts when OpenUrl overlay is open
            if (popupOpenUrl != null && popupOpenUrl.IsOpen)
            {
                if (e.Key == Key.Escape)
                {
                    CloseOpenUrlOverlay();
                    e.Handled = true;
                }
                return;
            }

            // Block ALL background player shortcuts when Settings overlay is open!
            if (popupSettings.IsOpen)
            {
                if (e.Key == Key.Escape || e.Key == Key.F2)
                {
                    if (!Views.SettingsOverlay.IsRecordingKey)
                    {
                        CloseSettingsOverlay();
                        e.Handled = true;
                    }
                    return;
                }

                if (!Views.SettingsOverlay.IsRecordingKey)
                {
                    e.Handled = true;
                }
                return;
            }

            // Block ALL background player shortcuts when Media Library overlay is open (allowing normal text typing)!
            if (popupLibrary.IsOpen)
            {
                var focusedElem = Keyboard.FocusedElement;
                if (focusedElem is System.Windows.Controls.Primitives.TextBoxBase || focusedElem is System.Windows.Controls.TextBox)
                {
                    // Allow normal text typing (e.g. typing 'L' in playlist rename box) without closing overlay
                    return;
                }

                if (overlayLibrary.IsEditingOrMerging)
                {
                    // Let PlaylistOverlay handle keys (e.g. Escape to return to selection) when editing/merging
                    return;
                }

                if (e.Key == Key.Escape || e.Key == Key.L)
                {
                    CloseLibraryOverlay();
                    e.Handled = true;
                }
                return;
            }

            // Block shortcuts if focus is inside any TextBox or ComboBox (e.g. searching or editing playlist)
            var focused = Keyboard.FocusedElement;
            if (focused is System.Windows.Controls.TextBox || focused is System.Windows.Controls.ComboBox)
            {
                if (e.Key == Key.Escape)
                {
                    EnsureMainCanvasFocusAndDisableIme();
                    e.Handled = true;
                }
                return;
            }

            Key realKey = e.Key;
            if (realKey == Key.System) realKey = e.SystemKey;
            if (realKey == Key.ImeProcessed) realKey = e.ImeProcessedKey;
            if (ExecutePlayerHotkey(realKey, Keyboard.Modifiers))
            {
                e.Handled = true;
            }
        }

        private bool ExecutePlayerHotkey(Key realKey, ModifierKeys modifiers)
        {
            string currentCombo = Views.SettingsOverlay.FormatKeyCombo(realKey, modifiers);
            var hk = SettingsService.Instance.Config.Hotkeys ??= new HotkeyConfig();

            if (IsHotkeyMatch(currentCombo, hk.PlayPause, hk.SecPlayPause))
            {
                TogglePlayPause();
                ShowOsd(MpvGet("pause") == "yes" ? I18nService.Instance["OsdPaused"] : I18nService.Instance["OsdPlaying"]);
                return true;
            }
            if (IsHotkeyMatch(currentCombo, hk.SeekForward, hk.SecSeekForward))
            {
                PerformSeek(5);
                return true;
            }
            if (IsHotkeyMatch(currentCombo, hk.SeekBackward, hk.SecSeekBackward))
            {
                PerformSeek(-5);
                return true;
            }
            if (IsHotkeyMatch(currentCombo, hk.SeekForward30, hk.SecSeekForward30))
            {
                PerformSeek(30);
                return true;
            }
            if (IsHotkeyMatch(currentCombo, hk.SeekBackward30, hk.SecSeekBackward30))
            {
                PerformSeek(-30);
                return true;
            }
            if (IsHotkeyMatch(currentCombo, hk.SpeedUp, hk.SecSpeedUp))
            {
                StepSpeed(true);
                return true;
            }
            if (IsHotkeyMatch(currentCombo, hk.SpeedDown, hk.SecSpeedDown))
            {
                StepSpeed(false);
                return true;
            }
            if (IsHotkeyMatch(currentCombo, hk.VolumeUp, hk.SecVolumeUp))
            {
                AdjustVolume(true);
                return true;
            }
            if (IsHotkeyMatch(currentCombo, hk.VolumeDown, hk.SecVolumeDown))
            {
                AdjustVolume(false);
                return true;
            }
            if (IsHotkeyMatch(currentCombo, hk.BrightnessUp, hk.SecBrightnessUp))
            {
                AdjustBrightness(true, 10.0);
                return true;
            }
            if (IsHotkeyMatch(currentCombo, hk.BrightnessDown, hk.SecBrightnessDown))
            {
                AdjustBrightness(false, 10.0);
                return true;
            }
            if (IsHotkeyMatch(currentCombo, hk.BrightnessReset, hk.SecBrightnessReset))
            {
                BrightnessReset_Click(null, new RoutedEventArgs());
                return true;
            }
            if (IsHotkeyMatch(currentCombo, hk.VideoSharpening, hk.SecVideoSharpening))
            {
                MenuVideoSharpen_Click(null, new RoutedEventArgs());
                return true;
            }
            if (IsHotkeyMatch(currentCombo, hk.ResetAspectRatio, hk.SecResetAspectRatio))
            {
                SetAspectRatio("default");
                return true;
            }
            if (IsHotkeyMatch(currentCombo, hk.ToggleMute, hk.SecToggleMute))
            {
                BtnMute_Click(null, new RoutedEventArgs());
                return true;
            }
            if (IsHotkeyMatch(currentCombo, hk.ToggleFullscreen, hk.SecToggleFullscreen))
            {
                if (!_isPipMode) ToggleFullscreen();
                return true;
            }
            if (IsHotkeyMatch(currentCombo, hk.PrevMedia, hk.SecPrevMedia))
            {
                BtnPrev_Click(null, new RoutedEventArgs());
                return true;
            }
            if (IsHotkeyMatch(currentCombo, hk.NextMedia, hk.SecNextMedia))
            {
                BtnNext_Click(null, new RoutedEventArgs());
                return true;
            }
            if (IsHotkeyMatch(currentCombo, hk.Screenshot, hk.SecScreenshot))
            {
                BtnScreenshot_Click(null, new RoutedEventArgs());
                return true;
            }
            if (IsHotkeyMatch(currentCombo, hk.AbLoop, hk.SecAbLoop))
            {
                ToggleAbLoop();
                return true;
            }
            if (IsHotkeyMatch(currentCombo, hk.ExportClip, hk.SecExportClip))
            {
                ExportAbLoopClip();
                return true;
            }
            if (IsHotkeyMatch(currentCombo, hk.TogglePip, hk.SecTogglePip))
            {
                TogglePipMode();
                return true;
            }
            if (IsHotkeyMatch(currentCombo, hk.AlwaysOnTop, hk.SecAlwaysOnTop))
            {
                ToggleTopmost();
                return true;
            }
            if (IsHotkeyMatch(currentCombo, hk.OpenFile, hk.SecOpenFile))
            {
                MenuOpenFile_Click(null, new RoutedEventArgs());
                return true;
            }
            if (IsHotkeyMatch(currentCombo, hk.OpenFolder, hk.SecOpenFolder))
            {
                MenuOpenFolder_Click(null, new RoutedEventArgs());
                return true;
            }
            if (IsHotkeyMatch(currentCombo, hk.OpenUrl, hk.SecOpenUrl))
            {
                MenuOpenUrl_Click(null, new RoutedEventArgs());
                return true;
            }
            if (currentCombo == "Ctrl+V")
            {
                // Ignore direct clipboard paste when media is loaded (playing or paused)
                if (!string.IsNullOrEmpty(_currentPlayingFilePath) || _hasMedia || (_mpv != IntPtr.Zero && MpvGet("idle-active") != "yes"))
                {
                    return false;
                }

                Dispatcher.Invoke(() => _clickTimer?.Stop());
                try
                {
                    if (System.Windows.Clipboard.ContainsFileDropList())
                    {
                        var files = System.Windows.Clipboard.GetFileDropList();
                        if (files != null && files.Count > 0)
                        {
                            var validPaths = new List<string>();
                            foreach (string? f in files)
                            {
                                if (!string.IsNullOrEmpty(f) && (File.Exists(f) || Directory.Exists(f)))
                                {
                                    validPaths.Add(f);
                                }
                            }
                            if (validPaths.Count > 0)
                            {
                                HandleDropPaths(validPaths.ToArray(), fromDrop: false);
                                return true;
                            }
                        }
                    }

                    string clipText = System.Windows.Clipboard.GetText()?.Trim() ?? "";
                    if (!string.IsNullOrEmpty(clipText))
                    {
                        if (clipText.StartsWith("\"") && clipText.EndsWith("\"") && clipText.Length > 2)
                        {
                            clipText = clipText.Substring(1, clipText.Length - 2);
                        }

                        if (IsNetworkUrl(clipText) || File.Exists(clipText) || Directory.Exists(clipText))
                        {
                            HandleDropPaths(new[] { clipText }, fromDrop: false);
                            return true;
                        }
                    }
                }
                catch { }
            }
            if (IsHotkeyMatch(currentCombo, hk.TogglePlaylist, hk.SecTogglePlaylist))
            {
                BtnPlaylist_Click(null, new RoutedEventArgs());
                return true;
            }
            if (IsHotkeyMatch(currentCombo, hk.ToggleLibrary, hk.SecToggleLibrary))
            {
                BtnLibrary_Click(null, new RoutedEventArgs());
                return true;
            }
            if (IsHotkeyMatch(currentCombo, hk.SmartFill, hk.SecSmartFill))
            {
                BtnSmartFill_Click(null, new RoutedEventArgs());
                return true;
            }
            if (IsHotkeyMatch(currentCombo, hk.AutoCrop, hk.SecAutoCrop))
            {
                BtnAutoCrop_Click(null, new RoutedEventArgs());
                return true;
            }

            switch (realKey)
            {
                case Key.OemBackslash:
                case Key.Back:
                    SetPlaybackSpeed(1.0);
                    return true;
                case Key.F2:
                    MenuSettings_Click(null, new RoutedEventArgs());
                    return true;
                case Key.Escape:
                    if (popupSettings != null && popupSettings.IsOpen)
                    {
                        CloseSettingsOverlay();
                        return true;
                    }
                    else if (popupLibrary != null && popupLibrary.IsOpen)
                    {
                        CloseLibraryOverlay();
                        return true;
                    }
                    else if (popupSideDrawer != null && popupSideDrawer.IsOpen)
                    {
                        popupSideDrawer.IsOpen = false;
                        _isDrawerOpen = false;
                        return true;
                    }
                    else if (_isPipMode)
                    {
                        TogglePipMode();
                        return true;
                    }
                    else if (_isFullscreen)
                    {
                        ToggleFullscreen();
                        return true;
                    }
                    else if (SettingsService.Instance.Config.DoubleEscToExit)
                    {
                        DateTime now = DateTime.Now;
                        if ((now - _lastEscPressTime).TotalMilliseconds < 500)
                        {
                            Close();
                        }
                        else
                        {
                            _lastEscPressTime = now;
                            ShowOsd(I18nService.Instance["OsdPressEscAgainToExit"]);
                        }
                        return true;
                    }
                    break;
            }

            return false;
        }
        // ── Cleanup ───────────────────────────────────────────────────────
        private void Window_Closed(object? sender, EventArgs e)
        {
            _windowLifetimeCts.Cancel();
            _timer?.Stop();
            _mousePollTimer?.Stop();
            _clickTimer?.Stop();
            _cmdQueueTimer?.Stop();
            StopVinylDiscAnimation();
            if (_mpvBgm != IntPtr.Zero)
            {
                MpvNative.mpv_command_string(_mpvBgm, "quit");
                MpvNative.mpv_terminate_destroy(_mpvBgm);
                _mpvBgm = IntPtr.Zero;
            }
            if (_mpv != IntPtr.Zero)
            {
                MpvNative.mpv_command_string(_mpv, "quit");
                MpvNative.mpv_terminate_destroy(_mpv);
                _mpv = IntPtr.Zero;
            }
        }

        // ── Named-Pipe Server & File Queue Listener (single instance) ───
        private System.Windows.Threading.DispatcherTimer? _cmdQueueTimer;

        private void StartPipeServer()
        {
            var cancellationToken = _windowLifetimeCts.Token;
            // A. Named Pipe Server
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        using var pipe = new NamedPipeServerStream(
                            "AniPlayerPipe", PipeDirection.In,
                            10, PipeTransmissionMode.Byte,
                            PipeOptions.Asynchronous);
                        await pipe.WaitForConnectionAsync(cancellationToken);
                        using var sr = new StreamReader(pipe);
                        string? rawPayload = await sr.ReadLineAsync();
                        if (!string.IsNullOrEmpty(rawPayload) && !cancellationToken.IsCancellationRequested)
                        {
                            // Never synchronously wait for the UI thread from this listener.
                            // The application can be closing while a second instance connects.
                            _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Normal, new Action(() =>
                            {
                                if (!Dispatcher.HasShutdownStarted) HandleIpcPayload(rawPayload);
                            }));
                        }
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch
                    {
                        try { await System.Threading.Tasks.Task.Delay(300, cancellationToken); }
                        catch (OperationCanceledException) { break; }
                    }
                }
            });

            // B. Cmd Queue File Timer Fallback
            _cmdQueueTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            _cmdQueueTimer.Tick += (s, ev) =>
            {
                try
                {
                    string queueFile = Path.Combine(App.UserDir, "cmd_queue.txt");
                    if (File.Exists(queueFile))
                    {
                        var lines = File.ReadAllLines(queueFile);
                        File.Delete(queueFile);
                        foreach (var line in lines)
                        {
                            string rawPayload = line.Trim();
                            if (!string.IsNullOrEmpty(rawPayload))
                            {
                                HandleIpcPayload(rawPayload);
                            }
                        }
                    }
                }
                catch { }
            };
            _cmdQueueTimer.Start();
        }

        private void HandleIpcPayload(string rawPayload)
        {
            Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            ForceForeground();
            Activate();
            RestoreTopmostState();
            if (rawPayload == "__SHOW_WINDOW__") return;

            bool forceFolder = false;
            string path = rawPayload;
            if (path.StartsWith("__SHIFT_OPEN__|", StringComparison.Ordinal))
            {
                forceFolder = true;
                path = path.Substring("__SHIFT_OPEN__|".Length);
            }
            RequestPlayFile(path, forceFolder || IsShiftKeyDown());
        }

        // ── Self-Test ─────────────────────────────────────────────────────
        private async void RunSelfTestAsync()
        {
            Console.WriteLine("[INFO] Self-test started.");
            if (!File.Exists(_selfTestMedia))
            {
                Console.WriteLine($"[ERROR] Self-test media not found: {_selfTestMedia}");
                Environment.Exit(1); return;
            }

            PlayFile(_selfTestMedia);
            await System.Threading.Tasks.Task.Delay(2500);

            string pos = MpvGet("time-pos");
            if (string.IsNullOrEmpty(pos) || pos == "0")
            {
                Console.WriteLine("[ERROR] Render Failed: Video not playing.");
                Environment.Exit(1); return;
            }

            // Test fullscreen
            ToggleFullscreen();
            await System.Threading.Tasks.Task.Delay(500);
            if (!_isFullscreen)
            {
                Console.WriteLine("[ERROR] Interaction Failed: Fullscreen not achieved.");
                Environment.Exit(2); return;
            }

            Console.WriteLine("[SUCCESS] Self-test passed.");
            Environment.Exit(0);
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int dwProcessId);

        // ── Performance Debug & Subsystem Profiler ────────────────────────
        private async void RunPerfDebugAsync()
        {
            try { File.WriteAllText(@"E:\Winnie-history\Anni player\perf_debug_running.txt", "RUNNING"); } catch {}
            AttachConsole(-1);
            Console.WriteLine("================================================================================");
            Console.WriteLine("          ANIPLAYER PERFORMANCE DEBUG & COMPONENT PROFILER REPORT               ");
            Console.WriteLine("================================================================================");
            Console.WriteLine($"[INFO] Test Started at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"[INFO] OS: {Environment.OSVersion}, 64-bit: {Environment.Is64BitOperatingSystem}, Logical Cores: {Environment.ProcessorCount}");

            string testMedia = _perfDebugMedia;
            if (string.IsNullOrEmpty(testMedia) || !File.Exists(testMedia))
            {
                string[] candidates = new[]
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "test.mp4"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "Assets", "test.mp4"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Assets", "test.mp4"),
                    @"E:\Winnie-history\Anni player\Assets\test.mp4"
                };
                foreach (var c in candidates)
                {
                    if (File.Exists(c)) { testMedia = Path.GetFullPath(c); break; }
                }
            }
            Console.WriteLine($"[INFO] Benchmark Test Media: {(File.Exists(testMedia) ? testMedia : "None (will test UI only)")}");

            var report = new System.Text.StringBuilder();
            report.AppendLine("# AniPlayer 性能诊断与组件耗时分析报告 (Performance Debug Report)");
            report.AppendLine();
            report.AppendLine($"- **测试时间**: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            report.AppendLine($"- **操作系统**: {Environment.OSVersion} ({ (Environment.Is64BitProcess ? "64-bit" : "32-bit") })");
            report.AppendLine($"- **CPU 逻辑核心数**: {Environment.ProcessorCount}");
            report.AppendLine($"- **初始进程内存占用**: {System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 / 1024 / 1024} MB");
            report.AppendLine();

            var results = new List<(string Name, double AvgMs, double MinMs, double MaxMs, string Category, string Impact)>();

            // Helper to measure fullscreen toggle performance
            async Task<(double Avg, double Min, double Max)> MeasureFullscreenCycleAsync(int count = 2)
            {
                async Task WaitForFullscreenTransitionAsync()
                {
                    DateTime deadline = DateTime.UtcNow.AddSeconds(2);
                    while (_isTogglingFullscreen && DateTime.UtcNow < deadline)
                    {
                        await Task.Delay(10);
                    }
                }

                var times = new List<double>();
                for (int i = 0; i < count; i++)
                {
                    await Task.Delay(50);
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    ToggleFullscreen();
                    await WaitForFullscreenTransitionAsync();
                    sw.Stop();
                    times.Add(sw.Elapsed.TotalMilliseconds);

                    await Task.Delay(50);
                    ToggleFullscreen();
                    await WaitForFullscreenTransitionAsync();
                    await Task.Delay(50);
                }
                return (times.Average(), times.Min(), times.Max());
            }

            // 1. Benchmark: Pure Win32 SetWindowPos Resize (Baseline)
            Console.WriteLine("[PROFILING] 1/7 Testing pure Win32 Window Resizing baseline...");
            var timesW32 = new List<double>();
            for (int i = 0; i < 3; i++)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 1920, 1080, SWP_NOACTIVATE);
                await Task.Delay(50);
                SetWindowPos(hwnd, IntPtr.Zero, 100, 100, 1280, 720, SWP_NOACTIVATE);
                sw.Stop();
                timesW32.Add(sw.Elapsed.TotalMilliseconds);
            }
            results.Add(("Win32 SetWindowPos 原生窗口缩放基准", timesW32.Average(), timesW32.Min(), timesW32.Max(), "OS / DWM", "极低 (操作系统级)"));

            // 2. Benchmark: Built-in Solid Theme Fullscreen In & Out
            Console.WriteLine("[PROFILING] 2/7 Testing Built-in Theme Fullscreen In & Out...");
            ThemeService.Instance.ActiveSkinKey = "";
            ThemeService.Instance.ApplyActiveSkinOrTheme();
            var solidThemeMetrics = await MeasureFullscreenCycleAsync(2);
            results.Add(("系统默认纯色主题全屏切换 (无视频)", solidThemeMetrics.Avg, solidThemeMetrics.Min, solidThemeMetrics.Max, "WPF 视觉树", "极低 (原生矢量渲染)"));

            // 3. Benchmark: Custom Skin High-Res Texture Fullscreen In & Out
            Console.WriteLine("[PROFILING] 3/7 Testing Custom Skin Master Texture Fullscreen In & Out...");
            if (ThemeService.Instance.Skins.Count > 0)
            {
                var firstSkinKey = ThemeService.Instance.Skins.Keys.First();
                ThemeService.Instance.ActiveSkinKey = firstSkinKey;
                ThemeService.Instance.ApplyActiveSkinOrTheme();
                var skinThemeMetrics = await MeasureFullscreenCycleAsync(2);
                results.Add(("自定义皮肤高清母版大理石底图全屏切换 (无视频)", skinThemeMetrics.Avg, skinThemeMetrics.Min, skinThemeMetrics.Max, "XAML 材质图层", "低 (GPU 纹理采样本底)"));
            }

            // 4. Benchmark: Fullscreen Overlay Popups Opening & Closing
            Console.WriteLine("[PROFILING] 4/7 Testing Fullscreen Top/Bottom Overlay Popups Animation...");
            var timesPopup = new List<double>();
            for (int i = 0; i < 3; i++)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                SnapTopBar(true);
                SnapBottomBar(true);
                await Task.Delay(100);
                SnapTopBar(false);
                SnapBottomBar(false);
                sw.Stop();
                timesPopup.Add(sw.Elapsed.TotalMilliseconds);
            }
            results.Add(("全屏浮层抽屉 (popupTop / popupBottom) 弹出与收起", timesPopup.Average(), timesPopup.Min(), timesPopup.Max(), "WPF 动画与命中测试", "极低"));

            // 5. Benchmark: Video Playback with Zero-Copy Direct3D 11 (auto-safe)
            if (File.Exists(testMedia))
            {
                Console.WriteLine("[PROFILING] 5/7 Testing Direct3D 11 Zero-Copy (auto-safe) Fullscreen In & Out...");
                PlayFile(testMedia);
                await Task.Delay(2000);
                _smartFillEnabled = false;
                _autoCropMode = "none";
                UpdateHardwareDecodingMode();
                await Task.Delay(300);

                var d3d11SafeMetrics = await MeasureFullscreenCycleAsync(2);
                results.Add(("D3D11 显存零拷贝硬解 (auto-safe) 全屏切换", d3d11SafeMetrics.Avg, d3d11SafeMetrics.Min, d3d11SafeMetrics.Max, "DirectX 11 交换链", "中 (与 GPU 算力强相关)"));

                // 6. Benchmark: Video Playback with PCIe Memory Copy (auto-copy)
                Console.WriteLine("[PROFILING] 6/7 Testing PCIe Memory Copy (auto-copy) Fullscreen In & Out...");
                MpvSetPropertyString("hwdec", "auto-copy");
                await Task.Delay(300);
                var autoCopyMetrics = await MeasureFullscreenCycleAsync(2);
                results.Add(("显存到内存跨总线搬运 (auto-copy) 全屏切换", autoCopyMetrics.Avg, autoCopyMetrics.Min, autoCopyMetrics.Max, "PCIe 总线 & 显存拷贝", "极高 (高负载下主卡顿源)"));

                // 7. Benchmark: Video Playback with SmartFill (lavfi boxblur CPU Filter)
                Console.WriteLine("[PROFILING] 7/7 Testing SmartFill lavfi CPU Blur Filter Fullscreen In & Out...");
                _smartFillEnabled = true;
                UpdateHardwareDecodingMode();
                UpdateSmartFill();
                await Task.Delay(300);
                var smartFillMetrics = await MeasureFullscreenCycleAsync(2);
                results.Add(("虚化填充 (SmartFill CPU lavfi 滤镜) 全屏切换", smartFillMetrics.Avg, smartFillMetrics.Min, smartFillMetrics.Max, "CPU 滤镜与像素着色", "高 (CPU/GPU 双重开销)"));
            }

            // Restore clean defaults
            _smartFillEnabled = false;
            _autoCropMode = "none";
            UpdateHardwareDecodingMode();
            UpdateSmartFill();

            // Print Final Report Table
            report.AppendLine("### 📊 各功能与组件耗时测量结果表 (以平均毫秒降序排列)");
            report.AppendLine();
            report.AppendLine("| 测试项目 / 组件功能 | 平均耗时 (Avg ms) | 最短耗时 (Min ms) | 最长耗时 (Max ms) | 所属子系统 | 资源消耗评级 |");
            report.AppendLine("| :--- | :--- | :--- | :--- | :--- | :--- |");

            foreach (var r in results.OrderByDescending(x => x.AvgMs))
            {
                report.AppendLine($"| **{r.Name}** | {r.AvgMs:F1} ms | {r.MinMs:F1} ms | {r.MaxMs:F1} ms | {r.Category} | {r.Impact} |");
            }

            report.AppendLine();
            report.AppendLine("### 🔍 性能瓶颈分析与诊断结论");
            report.AppendLine();
            report.AppendLine("1. **UI 控件与皮肤材质分析**：");
            report.AppendLine("   - WPF 视觉树面板、标题栏、控制栏、全屏抽屉浮层以及皮肤大理石底图的重绘开销均在极小范围内；");
            report.AppendLine("   - 皮肤材质纹理已经过 GPU 静态缓存，**UI 与皮肤不是导致全屏卡顿的根本瓶颈**。");
            report.AppendLine("2. **图形与显存管线分析**：");
            report.AppendLine("   - 耗时最显著的阶段发生在 **DirectX 显存交换链分辨率改变（Swapchain Resize）** 与 **显存跨总线拷贝（auto-copy）**；");
            report.AppendLine("   - 当后台显卡被 AI 满载占用时，`auto-copy` 的显存数据复制会产生指数级排队；而 `auto-safe` 零拷贝直通则大幅削减了这一开销。");

            string reportContent = report.ToString();
            Console.WriteLine();
            Console.WriteLine(reportContent);

            try
            {
                string reportPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "perf_debug_report.md");
                File.WriteAllText(reportPath, reportContent, System.Text.Encoding.UTF8);
                Console.WriteLine($"[SUCCESS] Performance debug report saved to: {reportPath}");
            }
            catch { }

            Console.WriteLine("[INFO] Performance debug completed successfully.");
            Environment.Exit(0);
        }

        private void ScrollPlaylistToCurrentItem()
        {
            try
            {
                if (lbPlaylist == null || lbPlaylist.Items.Count == 0) return;

                int curIdx = PlaylistManager.Instance.CurrentIndex;
                var curItem = PlaylistManager.Instance.GetCurrent();

                if (curItem != null && (curIdx < 0 || curIdx >= lbPlaylist.Items.Count || lbPlaylist.Items[curIdx] != curItem))
                {
                    curIdx = PlaylistManager.Instance.Items.IndexOf(curItem);
                }

                if (curIdx < 0 || curIdx >= lbPlaylist.Items.Count)
                {
                    // Fallback to match by _currentPlayingFilePath
                    if (!string.IsNullOrEmpty(_currentPlayingFilePath))
                    {
                        for (int i = 0; i < PlaylistManager.Instance.Items.Count; i++)
                        {
                            if (string.Equals(PlaylistManager.Instance.Items[i].FilePath, _currentPlayingFilePath, StringComparison.OrdinalIgnoreCase))
                            {
                                curIdx = i;
                                curItem = PlaylistManager.Instance.Items[i];
                                break;
                            }
                        }
                    }
                }

                if (curIdx >= 0 && curIdx < lbPlaylist.Items.Count)
                {
                    if (curItem == null) curItem = (PlaylistItem)lbPlaylist.Items[curIdx];
                    lbPlaylist.SelectedItem = curItem;

                    // Locate ScrollViewer in lbPlaylist to center the target item with comfortable buffer above and below
                    var sv = FindVisualChild<ScrollViewer>(lbPlaylist);
                    if (sv != null)
                    {
                        double viewport = sv.ViewportHeight;
                        double targetOffset;
                        if (viewport > 1)
                        {
                            // Center the item in viewport: curIdx - (viewport / 2) + 0.5
                            targetOffset = Math.Max(0, curIdx - (viewport / 2.0) + 0.5);
                        }
                        else
                        {
                            // Fallback: reserve ~4 items buffer above
                            targetOffset = Math.Max(0, curIdx - 4);
                        }

                        double maxOffset = Math.Max(0, lbPlaylist.Items.Count - (viewport > 0 ? viewport : 1));
                        if (targetOffset > maxOffset) targetOffset = maxOffset;

                        sv.ScrollToVerticalOffset(targetOffset);
                    }
                    else
                    {
                        // Direct fallback using WPF ListBox ScrollIntoView with buffer
                        int bufferIdx = Math.Max(0, curIdx - 4);
                        if (bufferIdx < lbPlaylist.Items.Count)
                        {
                            lbPlaylist.ScrollIntoView(lbPlaylist.Items[bufferIdx]);
                        }
                        lbPlaylist.ScrollIntoView(curItem);
                    }
                }
            }
            catch { }
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;
            int childCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild)
                    return typedChild;
                var found = FindVisualChild<T>(child);
                if (found != null)
                    return found;
            }
            return null;
        }

        private async void ToggleSideDrawer(bool open)
        {
            if (_isPipMode)
            {
                if (popupSideDrawer != null) popupSideDrawer.IsOpen = false;
                _isDrawerOpen = false;
                _isDrawerPinned = false;
                return;
            }
            if (_isDrawerOpen == open) return;
            _isDrawerOpen = open;
            _drawerAnimating = true;
            
            if (open && popupSideDrawer != null)
            {
                popupSideDrawer.IsOpen = true;
                UpdatePopupPlacements();
                EnsureSideDrawerOnTop();
                EnsureAudioBannerBelowSideDrawer();
                UpdateShuffleButtonUI();
                UpdateRepeatButtonUI();
                _ = Dispatcher.BeginInvoke(new Action(() => ScrollPlaylistToCurrentItem()), System.Windows.Threading.DispatcherPriority.Loaded);
            }

            double drawerW = sideDrawer.ActualWidth > 0 ? sideDrawer.ActualWidth : 420;
            var ta = new ThicknessAnimation
            {
                To = open ? new Thickness(0, 40, 20, 120) : new Thickness(0, 40, -drawerW, 120),
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
            };

            sideDrawer.BeginAnimation(MarginProperty, ta);

            await System.Threading.Tasks.Task.Delay(350);
            _drawerAnimating = false;
            if (!_isDrawerOpen && popupSideDrawer != null)
            {
                popupSideDrawer.IsOpen = false;
                EnsureMainCanvasFocusAndDisableIme();
            }
        }

        private void BtnPlaylist_Click(object? sender, RoutedEventArgs e)
        {
            if (_isPipMode) return;
            _isDrawerPinned = !_isDrawerPinned;
            if (_isDrawerPinned)
            {
                ToggleSideDrawer(true);
            }
            else
            {
                // Unpinned, if mouse is not over it, close it
                var globalMouse = System.Windows.Forms.Cursor.Position;
                try
                {
                    var pt = this.PointFromScreen(new System.Windows.Point(globalMouse.X, globalMouse.Y));
                    double dw = sideDrawer.ActualWidth > 0 ? sideDrawer.ActualWidth : 420;
                    if (pt.X < this.ActualWidth - dw)
                    {
                        ToggleSideDrawer(false);
                    }
                }
                catch { ToggleSideDrawer(false); }
            }
        }

        private void CloseLibraryOverlay()
        {
            if (popupLibrary != null && popupLibrary.IsOpen)
            {
                popupLibrary.IsOpen = false;
                ApplyBaseBrightness();
                if (_isFullscreen)
                {
                    SnapTopBar(true);
                    SnapBottomBar(true);
                }
                EnsureMainCanvasFocusAndDisableIme();
            }
        }

        public void ShowLibraryOverlay()
        {
            if (_isPipMode) return;
            if (_isFullscreen)
            {
                SnapTopBar(false);
                SnapBottomBar(false);
            }
            UpdateLibraryPopupSize();
            overlayLibrary?.ResetAndRefresh();
            if (popupIdleHint != null && popupIdleHint.IsOpen)
            {
                popupIdleHint.IsOpen = false;
            }
            popupLibrary.IsOpen = true;
            ApplyDimmedBrightness();

            // Auto pause playback when modal overlay is open
            PausePlaybackForModal();

            overlayLibrary?.Focus();
        }

        private void BtnLibrary_Click(object? sender, RoutedEventArgs e)
        {
            if (_isPipMode) return;
            if (popupLibrary.IsOpen)
            {
                CloseLibraryOverlay();
            }
            else
            {
                ShowLibraryOverlay();
            }
        }

        private void UpdateLibraryPopupSize()
        {
            if (popupLibrary != null)
            {
                popupLibrary.Placement = System.Windows.Controls.Primitives.PlacementMode.Center;
                popupLibrary.PlacementTarget = mainGrid;
                popupLibrary.HorizontalOffset = 0;
                popupLibrary.VerticalOffset = 0;
                
                double w = Math.Max(this.ActualWidth, 100);
                double h = Math.Max(this.ActualHeight, 100);
                
                popupLibrary.Width = w;
                popupLibrary.Height = h;
                
                if (gridLibraryHost != null)
                {
                    gridLibraryHost.Width = w;
                    gridLibraryHost.Height = h;
                }
                
                if (overlayLibrary != null)
                {
                    overlayLibrary.Margin = new Thickness(0);
                }
            }
        }

        private void UpdateSettingsPopupSize()
        {
            if (popupSettings != null)
            {
                popupSettings.Placement = System.Windows.Controls.Primitives.PlacementMode.Center;
                popupSettings.PlacementTarget = mainGrid;
                popupSettings.HorizontalOffset = 0;
                popupSettings.VerticalOffset = 0;
                
                double w = Math.Max(this.ActualWidth, 100);
                double h = Math.Max(this.ActualHeight, 100);
                
                popupSettings.Width = w;
                popupSettings.Height = h;
                
                if (gridSettingsHost != null)
                {
                    gridSettingsHost.Width = w;
                    gridSettingsHost.Height = h;
                }
                
                // Center overlay evenly within the window bounds
                overlaySettings.Margin = new Thickness(0);
            }
        }

        private void UpdateOpenUrlPopupSize()
        {
            if (popupOpenUrl != null)
            {
                popupOpenUrl.Placement = System.Windows.Controls.Primitives.PlacementMode.Center;
                popupOpenUrl.PlacementTarget = mainGrid;
                popupOpenUrl.HorizontalOffset = 0;
                popupOpenUrl.VerticalOffset = 0;

                double w = Math.Max(this.ActualWidth, 100);
                double h = Math.Max(this.ActualHeight, 100);

                popupOpenUrl.Width = w;
                popupOpenUrl.Height = h;

                if (gridOpenUrlHost != null)
                {
                    gridOpenUrlHost.Width = w;
                    gridOpenUrlHost.Height = h;
                }

                if (overlayOpenUrl != null)
                {
                    overlayOpenUrl.Margin = _isFullscreen ? new Thickness(0) : new Thickness(0, 40, 0, 0);
                }
            }
        }

        private void CloseOpenUrlOverlay()
        {
            if (popupOpenUrl != null)
            {
                popupOpenUrl.IsOpen = false;
                ApplyBaseBrightness();
                if (_isFullscreen)
                {
                    SnapTopBar(true);
                    SnapBottomBar(true);
                }
                EnsureMainCanvasFocusAndDisableIme();
            }
        }

        private void UpdateSponsorPopupSize()
        {
            if (popupSponsor != null)
            {
                popupSponsor.Placement = System.Windows.Controls.Primitives.PlacementMode.Center;
                popupSponsor.PlacementTarget = mainGrid;
                popupSponsor.HorizontalOffset = 0;
                popupSponsor.VerticalOffset = 0;

                double w = Math.Max(this.ActualWidth, 100);
                double h = Math.Max(this.ActualHeight, 100);

                popupSponsor.Width = w;
                popupSponsor.Height = h;

                if (gridSponsorHost != null)
                {
                    gridSponsorHost.Width = w;
                    gridSponsorHost.Height = h;
                }

                if (overlaySponsor != null)
                {
                    overlaySponsor.Margin = _isFullscreen ? new Thickness(0) : new Thickness(0, 40, 0, 0);
                }
            }
        }

        private bool IsAnyModalOverlayOpen()
        {
            return (popupSettings != null && popupSettings.IsOpen) ||
                   (popupLibrary != null && popupLibrary.IsOpen) ||
                   (popupOpenUrl != null && popupOpenUrl.IsOpen) ||
                   (popupSponsor != null && popupSponsor.IsOpen) ||
                   (popupTracks != null && popupTracks.IsOpen);
        }

        private void PausePlaybackForModal()
        {
            if (_mpv != IntPtr.Zero)
            {
                MpvSetPropertyString("pause", "yes");
                MpvNative.mpv_command_string(_mpv, "set pause yes");
                if (_mpvBgm != IntPtr.Zero && !string.IsNullOrEmpty(_currentBgmFile))
                {
                    MpvNative.mpv_set_property_string(_mpvBgm, "pause", "yes");
                }
                ShowOsd(I18nService.Instance["OsdPaused"]);
            }
        }

        public void ShowSponsorOverlay()
        {
            if (_isPipMode) return;
            if (_isFullscreen)
            {
                SnapTopBar(false);
                SnapBottomBar(false);
            }
            UpdateSponsorPopupSize();
            if (popupSponsor != null && overlaySponsor != null)
            {
                overlaySponsor.Visibility = Visibility.Visible;
                popupSponsor.IsOpen = true;
                ApplyDimmedBrightness();
                PausePlaybackForModal();
            }
        }

        private void CloseSponsorOverlay()
        {
            if (popupSponsor != null)
            {
                popupSponsor.IsOpen = false;
                ApplyBaseBrightness();
                if (_isFullscreen)
                {
                    SnapTopBar(true);
                    SnapBottomBar(true);
                }
                EnsureMainCanvasFocusAndDisableIme();
            }
        }

        public void ShowSettingsOverlay(bool selectAbout = false)
        {
            if (_isPipMode) return;
            if (_isFullscreen)
            {
                SnapTopBar(false);
                SnapBottomBar(false);
            }
            overlaySettings.LoadFromService();
            if (selectAbout)
            {
                overlaySettings.SelectAboutTab();
            }
            UpdateSettingsPopupSize();
            if (popupIdleHint != null && popupIdleHint.IsOpen)
            {
                popupIdleHint.IsOpen = false;
            }
            popupSettings.IsOpen = true;
            ApplyDimmedBrightness();

            // Auto pause playback when modal overlay is open
            PausePlaybackForModal();
        }

        private void MenuSettings_Click(object? sender, RoutedEventArgs e)
        {
            if (_isPipMode) return;
            if (popupSettings.IsOpen)
            {
                CloseSettingsOverlay();
            }
            else
            {
                ShowSettingsOverlay(selectAbout: false);
            }
        }

        public void UpdateControlBarTooltips()
        {
            var hk = SettingsService.Instance.Config.Hotkeys ??= new HotkeyConfig();
            var i18n = I18nService.Instance;

            SetButtonTooltip(btnOpen, i18n["TooltipOpen"], hk.OpenFile);
            SetButtonTooltip(btnLibrary, i18n["TooltipLibrary"], hk.ToggleLibrary);
            SetButtonTooltip(btnPrev, i18n["TooltipPrev"], hk.PrevMedia);
            SetButtonTooltip(btnPlay, i18n["TooltipPlay"], hk.PlayPause);
            SetButtonTooltip(btnNext, i18n["TooltipNext"], hk.NextMedia);
            SetButtonTooltip(btnPip, i18n["TooltipPip"], hk.TogglePip);
            SetButtonTooltip(btnScreenshot, i18n["TooltipScreenshot"], hk.Screenshot);
            SetButtonTooltip(btnSpeed, i18n["TooltipSpeed"], $"{hk.SpeedUp}/{hk.SpeedDown}");
            SetButtonTooltip(btnSmartFill, i18n["TooltipSmartFill"], hk.SmartFill);
            SetButtonTooltip(btnAutoCrop, i18n["TooltipAutoCrop"], hk.AutoCrop);
            SetButtonTooltip(btnMute, i18n["TooltipMute"], hk.ToggleMute);
            SetButtonTooltip(btnFullscreen, i18n["TooltipFullscreen"], hk.ToggleFullscreen);

            // Fullscreen controls
            SetButtonTooltip(btnLibraryFS, i18n["TooltipLibrary"], hk.ToggleLibrary);
            SetButtonTooltip(btnPrevFS, i18n["TooltipPrev"], hk.PrevMedia);
            SetButtonTooltip(btnPlayFS, i18n["TooltipPlay"], hk.PlayPause);
            SetButtonTooltip(btnNextFS, i18n["TooltipNext"], hk.NextMedia);
            SetButtonTooltip(btnPipFS, i18n["TooltipPip"], hk.TogglePip);
            SetButtonTooltip(btnScreenshotFS, i18n["TooltipScreenshot"], hk.Screenshot);
            SetButtonTooltip(btnSpeedFS, i18n["TooltipSpeed"], $"{hk.SpeedUp}/{hk.SpeedDown}");
            SetButtonTooltip(btnSmartFillFS, i18n["TooltipSmartFill"], hk.SmartFill);
            SetButtonTooltip(btnAutoCropFS, i18n["TooltipAutoCrop"], hk.AutoCrop);
            SetButtonTooltip(btnMuteFS, i18n["TooltipMute"], hk.ToggleMute);
            SetButtonTooltip(btnFullscreenFS, i18n["TooltipFullscreen"], hk.ToggleFullscreen);

            // Sub-menu items for Open buttons
            SetMenuItemHeader(menuBtnOpenFile, i18n["MenuOpenFile"], hk.OpenFile);
            SetMenuItemHeader(menuBtnOpenFolder, i18n["MenuOpenFolder"], hk.OpenFolder);
            SetMenuItemHeader(menuBtnOpenUrl, i18n["MenuOpenUrl"], hk.OpenUrl);

            SetMenuItemHeader(menuBtnOpenFileFS, i18n["MenuOpenFile"], hk.OpenFile);
            SetMenuItemHeader(menuBtnOpenFolderFS, i18n["MenuOpenFolder"], hk.OpenFolder);
            SetMenuItemHeader(menuBtnOpenUrlFS, i18n["MenuOpenUrl"], hk.OpenUrl);

            // Context Menu Headers
            var ctxMenu = this.Resources["VideoContextMenu"] as System.Windows.Controls.ContextMenu;
            if (ctxMenu != null)
            {
                SetMenuItemHeader(FindMenuItem(ctxMenu, "menuCtxPlay"), i18n["MenuPlayPause"], hk.PlayPause);
                SetMenuItemHeader(FindMenuItem(ctxMenu, "menuCtxPrev"), i18n["MenuPrev"], hk.PrevMedia);
                SetMenuItemHeader(FindMenuItem(ctxMenu, "menuCtxNext"), i18n["MenuNext"], hk.NextMedia);
                SetMenuItemHeader(FindMenuItem(ctxMenu, "menuCtxSmartFill"), i18n["MenuSmartFill"], hk.SmartFill);
                SetMenuItemHeader(FindMenuItem(ctxMenu, "menuCtxAutoCrop"), i18n["MenuAutoCrop"], hk.AutoCrop);
                SetMenuItemHeader(FindMenuItem(ctxMenu, "menuAlwaysOnTop"), i18n["MenuAlwaysOnTop"], hk.AlwaysOnTop);
                SetMenuItemHeader(FindMenuItem(ctxMenu, "menuAbLoop"), i18n["MenuAbLoop"], hk.AbLoop);
                SetMenuItemHeader(FindMenuItem(ctxMenu, "menuCtxScreenshot"), i18n["MenuScreenshot"], hk.Screenshot);
                SetMenuItemHeader(FindMenuItem(ctxMenu, "menuCtxPip"), i18n["MenuPip"], hk.TogglePip);
                SetMenuItemHeader(FindMenuItem(ctxMenu, "menuCtxOpenFile"), i18n["MenuOpenFile"], hk.OpenFile);
                SetMenuItemHeader(FindMenuItem(ctxMenu, "menuCtxOpenFolder"), i18n["MenuOpenFolder"], hk.OpenFolder);
                SetMenuItemHeader(FindMenuItem(ctxMenu, "menuCtxOpenUrl"), i18n["MenuOpenUrl"], hk.OpenUrl);
                SetMenuItemHeader(FindMenuItem(ctxMenu, "menuCtxLibrary"), i18n["MenuLibrary"], hk.ToggleLibrary);
                SetMenuItemHeader(FindMenuItem(ctxMenu, "menuCtxPlaylist"), i18n["MenuPlaylist"], hk.TogglePlaylist);
                SetMenuItemHeader(FindMenuItem(ctxMenu, "menuCtxVideoSharpen"), i18n["MenuVideoSharpen"], hk.VideoSharpening);

                var menuAspect = FindMenuItem(ctxMenu, "menuAspectRatio");
                if (menuAspect != null)
                {
                    foreach (var item in menuAspect.Items)
                    {
                        if (item is System.Windows.Controls.MenuItem subMi && subMi.Name == "menuAspectDefault")
                        {
                            SetMenuItemHeader(subMi, i18n["AspectDefault"], hk.ResetAspectRatio);
                        }
                    }
                }
            }
        }

        private System.Windows.Controls.MenuItem? FindMenuItem(System.Windows.Controls.ContextMenu menu, string name)
        {
            foreach (var item in menu.Items)
            {
                if (item is System.Windows.Controls.MenuItem mi && mi.Name == name)
                    return mi;
            }
            return null;
        }

        private void SetButtonTooltip(System.Windows.Controls.Button? btn, string baseTitle, string hotkey)
        {
            if (btn == null) return;
            if (string.IsNullOrEmpty(hotkey))
                btn.ToolTip = baseTitle;
            else
                btn.ToolTip = $"{baseTitle} ({hotkey})";
        }

        private void SetMenuItemHeader(System.Windows.Controls.MenuItem? item, string baseTitle, string hotkey)
        {
            if (item == null) return;
            if (string.IsNullOrEmpty(hotkey))
                item.Header = baseTitle;
            else
                item.Header = $"{baseTitle} ({hotkey})";
        }

        private void CloseSettingsOverlay()
        {
            popupSettings.IsOpen = false;
            ApplyBaseBrightness();
            if (_isFullscreen)
            {
                SnapTopBar(true);
                SnapBottomBar(true);
            }
            EnsureMainCanvasFocusAndDisableIme();
            UpdateIdleHintOverlay();
        }

        // ── Subtitle & Audio Track Control ──────────────────────────────────────
        private void BtnTracks_Click(object sender, RoutedEventArgs e)
        {
            RefreshTracksOverlayUI();
            if (sender is FrameworkElement elem)
            {
                popupTracks.PlacementTarget = elem;
            }
            popupTracks.IsOpen = true;
            PausePlaybackForModal();
        }

        private void RefreshTracksOverlayUI()
        {
            if (_mpv == IntPtr.Zero || overlayTracks == null) return;

            var subTracks = GetTracksFromMpv("sub");
            double subDelay = MpvGetDouble("sub-delay");
            overlayTracks.PopulateSubTracks(subTracks, subDelay);

            var audioTracks = GetTracksFromMpv("audio");
            double audioDelay = MpvGetDouble("audio-delay");
            overlayTracks.PopulateAudioTracks(audioTracks, audioDelay);
            overlayTracks.SetNightMode(SettingsService.Instance.Config.AudioNightMode);
        }

        private List<Views.TrackItemInfo> GetTracksFromMpv(string targetType)
        {
            var result = new List<Views.TrackItemInfo>();

            // When in slideshow/image mode, background music runs independently on _mpvBgm
            if (targetType == "audio" && _isCurrentImage)
            {
                if (!string.IsNullOrEmpty(_currentBgmFile) && File.Exists(_currentBgmFile))
                {
                    string fileName = Path.GetFileName(_currentBgmFile);
                    bool isMuted = _isMuted || (MpvGetBgm("mute") == "yes");
                    result.Add(new Views.TrackItemInfo
                    {
                        Id = 1,
                        Type = "audio",
                        Title = fileName,
                        IsSelected = !isMuted,
                        IsExternal = true,
                        ExternalFilename = _currentBgmFile
                    });
                }
                return result;
            }

            if (_mpv == IntPtr.Zero) return result;

            try
            {
                int count = (int)MpvGetDouble("track-list/count");
                var seenExternalFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                for (int i = 0; i < count; i++)
                {
                    string type = MpvGet($"track-list/{i}/type");
                    if (type != targetType) continue;

                    long.TryParse(MpvGet($"track-list/{i}/id"), out long id);
                    string title = MpvGet($"track-list/{i}/title");
                    string lang = MpvGet($"track-list/{i}/lang");
                    string codec = MpvGet($"track-list/{i}/codec");
                    string selectedStr = MpvGet($"track-list/{i}/selected");
                    bool selected = (selectedStr == "yes" || selectedStr == "true");
                    string externalStr = MpvGet($"track-list/{i}/external");
                    bool external = (externalStr == "yes" || externalStr == "true");
                    string externalFilename = MpvGet($"track-list/{i}/external-filename");

                    // Filter duplicate external files if mpv added same file multiple times
                    if (external && !string.IsNullOrEmpty(externalFilename))
                    {
                        try
                        {
                            string normPath = Path.GetFullPath(externalFilename.Replace("/", "\\"));
                            if (seenExternalFiles.Contains(normPath)) continue;
                            seenExternalFiles.Add(normPath);
                        }
                        catch { }
                    }

                    result.Add(new Views.TrackItemInfo
                    {
                        Id = (int)id,
                        Type = type,
                        Title = title,
                        Language = lang,
                        Codec = codec,
                        IsSelected = selected,
                        IsExternal = external,
                        ExternalFilename = externalFilename
                    });
                }
            }
            catch { }
            return result;
        }

        private void SelectSubTrack(int trackId)
        {
            if (_mpv == IntPtr.Zero) return;
            if (trackId == 0)
            {
                MpvNative.mpv_command_string(_mpv, "set sid no");
                ShowOsd(I18nService.Instance["SubTrackNone"]);
            }
            else
            {
                MpvNative.mpv_command_string(_mpv, $"set sid {trackId}");
                ShowOsd($"💬 字幕轨: [{trackId}]");
            }
            RefreshTracksOverlayUI();
        }

        private void SelectAudioTrack(int trackId)
        {
            if (_isCurrentImage)
            {
                if (trackId == 0)
                {
                    StopBgmAudio();
                    ShowOsd(I18nService.Instance["AudioTrackNone"]);
                }
                else
                {
                    if (!string.IsNullOrEmpty(_currentBgmFile))
                    {
                        PlayBgmAudio(_currentBgmFile);
                        ShowOsd($"🎵 背景音轨: {Path.GetFileName(_currentBgmFile)}");
                    }
                }
                RefreshTracksOverlayUI();
                return;
            }

            if (_mpv == IntPtr.Zero) return;
            if (trackId == 0)
            {
                MpvNative.mpv_command_string(_mpv, "set aid no");
                ShowOsd(I18nService.Instance["AudioTrackNone"]);
            }
            else
            {
                MpvNative.mpv_command_string(_mpv, $"set aid {trackId}");
                ShowOsd($"🎵 音轨: [{trackId}]");
            }
            RefreshTracksOverlayUI();
        }

        private void SetSubDelay(double delay)
        {
            if (_mpv == IntPtr.Zero) return;
            string val = delay.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
            MpvNative.mpv_command_string(_mpv, $"set sub-delay {val}");
            ShowOsd(string.Format(I18nService.Instance["OsdSubDelay"], delay.ToString("F1")));
        }

        private void SetAudioDelay(double delay)
        {
            if (_mpv == IntPtr.Zero) return;
            string val = delay.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
            MpvNative.mpv_command_string(_mpv, $"set audio-delay {val}");
            ShowOsd(string.Format(I18nService.Instance["OsdAudioDelay"], delay.ToString("F1")));
        }

        private void SetSubPos(int posOffset)
        {
            if (_mpv == IntPtr.Zero) return;
            if (posOffset == 100)
            {
                MpvNative.mpv_command_string(_mpv, "set sub-pos 100");
            }
            else
            {
                MpvNative.mpv_command_string(_mpv, $"add sub-pos {posOffset}");
            }
            string posStr = MpvGet("sub-pos");
            int posVal = 100;
            if (double.TryParse(posStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsed))
            {
                posVal = (int)Math.Round(parsed);
            }
            ShowOsd(string.Format(I18nService.Instance["OsdSubPos"], posVal));
        }

        private DateTime _lastSubDialogTime = DateTime.MinValue;
        private void OpenExternalSubDialog()
        {
            if ((DateTime.UtcNow - _lastSubDialogTime).TotalMilliseconds < 1000) return;
            _lastSubDialogTime = DateTime.UtcNow;

            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
            {
                try
                {
                    if (popupTracks != null) popupTracks.IsOpen = false;

                    string initialDir = "";
                    if (!string.IsNullOrEmpty(_currentPlayingFilePath) && File.Exists(_currentPlayingFilePath))
                    {
                        initialDir = Path.GetDirectoryName(_currentPlayingFilePath) ?? "";
                    }
                    else
                    {
                        var curItem = PlaylistManager.Instance.GetCurrent();
                        if (curItem != null && !string.IsNullOrEmpty(curItem.FilePath) && File.Exists(curItem.FilePath))
                        {
                            initialDir = Path.GetDirectoryName(curItem.FilePath) ?? "";
                        }
                    }

                    var ofd = new Microsoft.Win32.OpenFileDialog
                    {
                        Filter = "字幕文件 (*.srt;*.ass;*.vtt;*.sub;*.sup)|*.srt;*.ass;*.vtt;*.sub;*.sup|所有文件 (*.*)|*.*",
                        Title = I18nService.Instance["BtnLoadExternalSub"]
                    };

                    if (!string.IsNullOrEmpty(initialDir) && Directory.Exists(initialDir))
                    {
                        ofd.InitialDirectory = initialDir;
                    }

                    if (ofd.ShowDialog() == true)
                    {
                        LoadExternalSubFile(ofd.FileName);
                    }
                }
                catch { }
            }));
        }

        private DateTime _lastAudioDialogTime = DateTime.MinValue;
        private void OpenExternalAudioDialog()
        {
            if ((DateTime.UtcNow - _lastAudioDialogTime).TotalMilliseconds < 1000) return;
            _lastAudioDialogTime = DateTime.UtcNow;

            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
            {
                try
                {
                    if (popupTracks != null) popupTracks.IsOpen = false;

                    string initialDir = "";
                    if (!string.IsNullOrEmpty(_currentPlayingFilePath) && File.Exists(_currentPlayingFilePath))
                    {
                        initialDir = Path.GetDirectoryName(_currentPlayingFilePath) ?? "";
                    }
                    else
                    {
                        var curItem = PlaylistManager.Instance.GetCurrent();
                        if (curItem != null && !string.IsNullOrEmpty(curItem.FilePath) && File.Exists(curItem.FilePath))
                        {
                            initialDir = Path.GetDirectoryName(curItem.FilePath) ?? "";
                        }
                    }

                    var ofd = new Microsoft.Win32.OpenFileDialog
                    {
                        Filter = "音视频文件 (*.mp3;*.flac;*.wav;*.m4a;*.aac;*.ogg;*.opus;*.ac3;*.dts;*.mp4;*.mkv;*.avi;*.mov;*.flv;*.webm;*.ts;*.m4v;*.wmv)|*.mp3;*.flac;*.wav;*.m4a;*.aac;*.ogg;*.opus;*.ac3;*.dts;*.mp4;*.mkv;*.avi;*.mov;*.flv;*.webm;*.ts;*.m4v;*.wmv|音频文件 (*.mp3;*.flac;*.wav;*.m4a;*.aac;*.ogg;*.opus;*.ac3;*.dts)|*.mp3;*.flac;*.wav;*.m4a;*.aac;*.ogg;*.opus;*.ac3;*.dts|视频提取音轨 (*.mp4;*.mkv;*.avi;*.mov;*.flv;*.webm;*.ts;*.m4v;*.wmv)|*.mp4;*.mkv;*.avi;*.mov;*.flv;*.webm;*.ts;*.m4v;*.wmv|所有文件 (*.*)|*.*",
                        Title = I18nService.Instance["BtnLoadExternalAudio"]
                    };

                    if (!string.IsNullOrEmpty(initialDir) && Directory.Exists(initialDir))
                    {
                        ofd.InitialDirectory = initialDir;
                    }

                    if (ofd.ShowDialog() == true)
                    {
                        LoadExternalAudioFile(ofd.FileName);
                    }
                }
                catch { }
            }));
        }

        private void LoadExternalSubFile(string filePath)
        {
            if (_mpv == IntPtr.Zero || !File.Exists(filePath)) return;
            string posix = filePath.Replace("\\", "/");
            MpvNative.mpv_command_string(_mpv, $"sub-add \"{posix}\" select");
            ShowOsd(string.Format(I18nService.Instance["OsdSubLoaded"], Path.GetFileName(filePath)));
            RefreshTracksOverlayUI();
        }

        private void LoadExternalAudioFile(string filePath)
        {
            if (!File.Exists(filePath)) return;
            if (_isCurrentImage)
            {
                PlayBgmAudio(filePath);
                ShowOsd(string.Format(I18nService.Instance["OsdAudioLoaded"], Path.GetFileName(filePath)));
                RefreshTracksOverlayUI();
                return;
            }
            if (_mpv == IntPtr.Zero) return;
            string posix = filePath.Replace("\\", "/");
            MpvNative.mpv_command_string(_mpv, $"audio-add \"{posix}\" select");
            ShowOsd(string.Format(I18nService.Instance["OsdAudioLoaded"], Path.GetFileName(filePath)));
            RefreshTracksOverlayUI();
        }

        private string _currentPlayingFilePath = "";

        private void AutoMatchLocalSubtitles(string videoPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(videoPath) || _isCurrentImage) return;
                string dir = Path.GetDirectoryName(videoPath) ?? "";
                if (!Directory.Exists(dir)) return;

                string videoNameWithoutExt = Path.GetFileNameWithoutExtension(videoPath);
                string[] subExts = new[] { ".srt", ".ass", ".vtt", ".sub", ".ssa" };
                List<string> candidateDirs = new List<string> { dir };

                // 查找位置 1 及其子目录
                string[] subFolderNames = new[] { "subs", "subtitles", "字幕", "Subs", "Subtitles" };
                foreach (var subDirName in subFolderNames)
                {
                    string subDir = Path.Combine(dir, subDirName);
                    if (Directory.Exists(subDir)) candidateDirs.Add(subDir);
                }

                // 查找位置 2：%APPDATA%\AniPlayer\subtitles\
                string appDataSubDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AniPlayer", "subtitles");
                if (Directory.Exists(appDataSubDir)) candidateDirs.Add(appDataSubDir);

                // 获取 MPV 中已经存在的外部字幕列表，防止重复 sub-add
                var existingTracks = GetTracksFromMpv("sub");
                var existingFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var t in existingTracks)
                {
                    if (!string.IsNullOrEmpty(t.ExternalFilename))
                    {
                        try { existingFiles.Add(Path.GetFullPath(t.ExternalFilename.Replace("/", "\\"))); } catch { }
                    }
                }

                foreach (var searchDir in candidateDirs)
                {
                    foreach (var ext in subExts)
                    {
                        var matches = Directory.GetFiles(searchDir, $"{videoNameWithoutExt}*{ext}", SearchOption.TopDirectoryOnly);
                        foreach (var match in matches)
                        {
                            try
                            {
                                string fullMatch = Path.GetFullPath(match);
                                if (!existingFiles.Contains(fullMatch))
                                {
                                    string posixSub = match.Replace("\\", "/");
                                    MpvNative.mpv_command_string(_mpv, $"sub-add \"{posixSub}\" auto");
                                    existingFiles.Add(fullMatch);
                                }
                            }
                            catch { }
                        }
                    }
                }
            }
            catch { }
        }

        public void RestoreTopmostState()
        {
            try
            {
                bool shouldBeTopmost = _isFullscreen || _isPipMode || SettingsService.Instance.Config.AlwaysOnTop;
                if (_isFullscreen && !SettingsService.Instance.Config.AlwaysOnTop && !IsCurrentAppActive())
                {
                    shouldBeTopmost = false;
                }

                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    long exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
                    if (shouldBeTopmost)
                    {
                        if ((exStyle & WS_EX_TOPMOST) == 0 || this.Topmost != true)
                        {
                            SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(exStyle | WS_EX_TOPMOST));
                            this.Topmost = true;
                        }
                        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
                    }
                    else
                    {
                        if ((exStyle & WS_EX_TOPMOST) != 0 || this.Topmost != false)
                        {
                            SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(exStyle & ~WS_EX_TOPMOST));
                            this.Topmost = false;
                        }
                        SetWindowPos(hwnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
                    }
                }
                else
                {
                    this.Topmost = shouldBeTopmost;
                }

                SyncActivePopupsTopmost(shouldBeTopmost);
            }
            catch { }
        }

        private void SyncActivePopupsTopmost(bool shouldBeTopmost)
        {
            try
            {
                var popups = new System.Windows.Controls.Primitives.Popup?[]
                {
                    popupSideDrawer, popupLibrary, popupSettings, popupOpenUrl,
                    popupTracks, popupSponsor, popupVolume, popupBrightness,
                    popupVolumeFS, popupBrightnessFS, popupAudioBanner, popupOsd, popupPipClose
                };

                IntPtr mainHwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                foreach (var p in popups)
                {
                    if (p != null && p.IsOpen && p.Child != null)
                    {
                        var src = System.Windows.PresentationSource.FromVisual(p.Child) as System.Windows.Interop.HwndSource;
                        if (src != null && src.Handle != IntPtr.Zero)
                        {
                            IntPtr pHwnd = src.Handle;
                            if (mainHwnd != IntPtr.Zero)
                            {
                                SetWindowLongPtr(pHwnd, GWLP_HWNDPARENT, mainHwnd);
                            }
                            long exStyle = GetWindowLongPtr(pHwnd, GWL_EXSTYLE).ToInt64();
                            if (shouldBeTopmost)
                            {
                                if ((exStyle & WS_EX_TOPMOST) == 0)
                                {
                                    SetWindowLongPtr(pHwnd, GWL_EXSTYLE, new IntPtr(exStyle | WS_EX_TOPMOST));
                                }
                                SetWindowPos(pHwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
                            }
                            else
                            {
                                if ((exStyle & WS_EX_TOPMOST) != 0)
                                {
                                    SetWindowLongPtr(pHwnd, GWL_EXSTYLE, new IntPtr(exStyle & ~WS_EX_TOPMOST));
                                }
                                SetWindowPos(pHwnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private bool IsCurrentAppActive()
        {
            try
            {
                var fgHwnd = GetForegroundWindow();
                if (fgHwnd == IntPtr.Zero) return false;
                GetWindowThreadProcessId(fgHwnd, out uint fgPid);
                uint myPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
                return fgPid == myPid;
            }
            catch
            {
                return true;
            }
        }

        private void EnsureMainCanvasFocusAndDisableIme()
        {
            try
            {
                System.Windows.Input.Keyboard.ClearFocus();
                System.Windows.Input.FocusManager.SetFocusedElement(this, this);
                this.Focus();

                RestoreTopmostState();

                System.Windows.Input.InputMethod.SetIsInputMethodEnabled(this, false);

                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    ImmAssociateContext(hwnd, IntPtr.Zero);
                }

                if (_videoPanel != null && _videoPanel.IsHandleCreated && _videoPanel.Handle != IntPtr.Zero)
                {
                    ImmAssociateContext(_videoPanel.Handle, IntPtr.Zero);
                }
            }
            catch { }
        }

        private void BtnClearPlaylist_Click(object sender, RoutedEventArgs e)
        {
            PlaylistManager.Instance.Clear();
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern short GetKeyState(int vKey);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        private const int SW_RESTORE = 9;
        private const int SW_SHOW = 5;

        private const int VK_SHIFT = 0x10;
        private const int VK_LSHIFT = 0xA0;
        private const int VK_RSHIFT = 0xA1;

        private static bool IsShiftKeyDown()
        {
            try
            {
                if ((GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0 || (GetAsyncKeyState(VK_SHIFT) & 0x0001) != 0) return true;
                if ((GetAsyncKeyState(VK_LSHIFT) & 0x8000) != 0 || (GetAsyncKeyState(VK_RSHIFT) & 0x8000) != 0) return true;
                if ((GetKeyState(VK_SHIFT) & 0x8000) != 0) return true;
                if ((GetKeyState(VK_LSHIFT) & 0x8000) != 0 || (GetKeyState(VK_RSHIFT) & 0x8000) != 0) return true;
                if (System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.LeftShift) ||
                    System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.RightShift))
                    return true;
            }
            catch { }
            return false;
        }

        public void ForceForeground()
        {
            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return;

                IntPtr fgHwnd = GetForegroundWindow();
                uint fgThread = fgHwnd != IntPtr.Zero ? GetWindowThreadProcessId(fgHwnd, out _) : 0;
                uint curThread = GetCurrentThreadId();

                if (fgThread != 0 && fgThread != curThread)
                {
                    AttachThreadInput(curThread, fgThread, true);
                    ShowWindow(hwnd, SW_RESTORE);
                    BringWindowToTop(hwnd);
                    SetForegroundWindow(hwnd);
                    AttachThreadInput(curThread, fgThread, false);
                }
                else
                {
                    ShowWindow(hwnd, SW_RESTORE);
                    BringWindowToTop(hwnd);
                    SetForegroundWindow(hwnd);
                }
            }
            catch { }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT Point);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [System.Runtime.InteropServices.DllImport("imm32.dll")]
        private static extern IntPtr ImmAssociateContext(IntPtr hWnd, IntPtr hIMC);

        [System.Runtime.InteropServices.DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HTCAPTION = 0x2;

        private const int GWL_EXSTYLE = -20;
        private const int GWLP_HWNDPARENT = -8;
        private const int WS_EX_TOPMOST = 0x00000008;

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
        private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

        public static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
        {
            if (IntPtr.Size == 8)
                return GetWindowLongPtr64(hWnd, nIndex);
            else
                return new IntPtr(GetWindowLong32(hWnd, nIndex));
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
        private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

        public static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        {
            if (IntPtr.Size == 8)
                return SetWindowLongPtr64(hWnd, nIndex, dwNewLong);
            else
                return new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        private static readonly IntPtr HWND_TOP = new IntPtr(0);
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const uint SWP_FRAMECHANGED = 0x0020;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
        private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
            public MEMORYSTATUSEX()
            {
                dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            }
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([System.Runtime.InteropServices.In, System.Runtime.InteropServices.Out] MEMORYSTATUSEX lpBuffer);

        public static ulong GetAvailablePhysicalMemoryBytes()
        {
            try
            {
                var memStatus = new MEMORYSTATUSEX();
                if (GlobalMemoryStatusEx(memStatus))
                {
                    return memStatus.ullAvailPhys;
                }
            }
            catch { }
            return 4UL * 1024 * 1024 * 1024;
        }

        private void PerformSeek(double relativeSeconds)
        {
            if (_mpv == IntPtr.Zero || !IsMediaSeekable())
            {
                ShowOsd(I18nService.Instance["OsdLiveStreamUnseekable"]);
                return;
            }

            double pos = MpvGetDouble("time-pos");
            double dur = MpvGetDouble("duration");
            bool isNet = IsNetworkUrl(_currentPlayingFilePath);

            if (!isNet && dur > 0)
            {
                // Local video with fixed duration:
                if (relativeSeconds > 0 && pos >= dur - 0.5)
                {
                    ShowOsd(I18nService.Instance["OsdReachedMediaEnd"]);
                    return;
                }

                double target = Math.Clamp(pos + relativeSeconds, 0, dur);
                MpvNative.mpv_command_string(_mpv, $"seek {relativeSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)} relative exact");
            }
            else
            {
                // Online network stream: duration can be dynamic/growing/chunked
                // Always issue seek command so mpv demuxer requests subsequent segments from the server
                MpvNative.mpv_command_string(_mpv, $"seek {relativeSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)} relative");
            }

            // Ensure playback is unpaused when user seeks
            if (MpvGet("pause") == "yes" && MpvGet("eof-reached") == "yes")
            {
                MpvSetPropertyString("pause", "no");
            }

            int sec = (int)Math.Abs(relativeSeconds);
            if (relativeSeconds >= 0)
            {
                ShowOsd(string.Format(I18nService.Instance["OsdFastForward"], sec));
            }
            else
            {
                ShowOsd(string.Format(I18nService.Instance["OsdRewind"], sec));
            }
        }

        private void ApplyAdaptiveNetworkCache(double width = 0, double height = 0)
        {
            if (_mpv == IntPtr.Zero) return;
            string mode = SettingsService.Instance.Config.NetworkStreamCacheMode ?? "auto";

            long forwardBytes;
            long backBytes;

            if (mode != "auto" && long.TryParse(mode, out long fixedMb))
            {
                forwardBytes = fixedMb * 1024 * 1024;
                backBytes = Math.Max(50 * 1024 * 1024, forwardBytes / 4);
            }
            else
            {
                // Dynamic Adaptive Algorithm:
                // 1. Get real-time available physical memory in GB
                ulong availBytes = GetAvailablePhysicalMemoryBytes();
                double availGb = availBytes / (1024.0 * 1024.0 * 1024.0);

                // 2. Base target forward buffer for ~180s (3 minutes) of playback based on video resolution
                long targetForwardMb;
                if (height >= 2160 || width >= 3840) // 4K UHD / 8K
                {
                    targetForwardMb = 800; // ~3 mins @ 35 Mbps
                }
                else if (height >= 1440 || width >= 2560) // 2K / 1440P
                {
                    targetForwardMb = 400; // ~3 mins @ 18 Mbps
                }
                else if (height >= 1080 || width >= 1920) // 1080P FHD
                {
                    targetForwardMb = 200; // ~3 mins @ 9 Mbps
                }
                else if (height > 0 || width > 0) // 720P & SD
                {
                    targetForwardMb = 100; // ~3 mins @ 4.5 Mbps
                }
                else // Unprobed or Audio
                {
                    targetForwardMb = 150;
                }

                // 3. Safety ceiling based on available physical RAM
                long maxSafetyMb;
                if (availGb < 2.0) maxSafetyMb = 150;
                else if (availGb < 4.0) maxSafetyMb = 350;
                else if (availGb < 8.0) maxSafetyMb = 800;
                else maxSafetyMb = 1500;

                long finalForwardMb = Math.Clamp(targetForwardMb, 80, maxSafetyMb);
                long finalBackMb = Math.Clamp(finalForwardMb / 4, 20, 384);

                forwardBytes = finalForwardMb * 1024 * 1024;
                backBytes = finalBackMb * 1024 * 1024;
            }

            MpvSetPropertyString("cache", "yes");
            MpvSetPropertyString("cache-on-disk", "no"); // strictly RAM
            MpvSetPropertyString("demuxer-max-bytes", forwardBytes.ToString());
            MpvSetPropertyString("demuxer-max-back-bytes", backBytes.ToString());
            MpvSetPropertyString("demuxer-readahead-secs", "180");
            MpvSetPropertyString("cache-secs", "180");
            MpvSetPropertyString("demuxer-seekable-cache", "yes");
            MpvSetPropertyString("demuxer-hysteresis-secs", "15");
            MpvSetPropertyString("stream-buffer-size", "4096k");
        }

        private void ApplyLocalFileCache()
        {
            if (_mpv == IntPtr.Zero) return;
            // 本地 NVMe/SSD/HDD 文件具备微秒级近零延迟与 GB/s 级极速吞吐，无需在内存中囤积数十分钟的高清数据包
            // 采用 15秒 / 48MiB 精简动态缓存：拖拽快进零延迟毫秒响应，同时内存占用从 600MB 骤降至 150MB 左右
            MpvSetPropertyString("cache", "yes");
            MpvSetPropertyString("cache-on-disk", "no");
            MpvSetPropertyString("demuxer-max-bytes", "50331648"); // 48 MiB max RAM
            MpvSetPropertyString("demuxer-max-back-bytes", "25165824"); // 24 MiB
            MpvSetPropertyString("demuxer-readahead-secs", "15");
            MpvSetPropertyString("cache-secs", "15");
            MpvSetPropertyString("demuxer-seekable-cache", "yes");
            MpvSetPropertyString("demuxer-hysteresis-secs", "5");
            MpvSetPropertyString("stream-buffer-size", "2048k");
        }

        private void Popup_Opened(object sender, EventArgs e)
        {
            if (sender is System.Windows.Controls.Primitives.Popup popup)
            {
                // When any modal/content popup opens, hide the floating idle hint
                if (popup != popupIdleHint && popupIdleHint != null && popupIdleHint.IsOpen)
                {
                    popupIdleHint.IsOpen = false;
                }

                // When any MODAL dialog/overlay popup opens (not the playlist side drawer), hide the floating audio banner so it doesn't overlap the dialog
                if (popup != popupAudioBanner && popup != popupSideDrawer && popupAudioBanner != null && popupAudioBanner.IsOpen)
                {
                    popupAudioBanner.IsOpen = false;
                }

                EnsurePopupZOrder(popup);
                EnsureSideDrawerOnTop();
            }
        }

        private void BtnSortPlaylist_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.ContextMenu != null)
            {
                btn.ContextMenu.PlacementTarget = btn;
                btn.ContextMenu.IsOpen = true;
            }
        }

        private void SortMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.MenuItem clicked) return;

            // Uncheck all sort menu items
            menuSortNameAsc.IsChecked  = false;
            menuSortNameDesc.IsChecked = false;
            menuSortDateDesc.IsChecked = false;
            menuSortDateAsc.IsChecked  = false;
            clicked.IsChecked = true;

            if (Enum.TryParse<PlaylistSortOption>(clicked.Tag?.ToString(), out var option))
            {
                PlaylistManager.Instance.Sort(option);
                // Scroll to current item in playlist
                var cur = PlaylistManager.Instance.GetCurrent();
                if (cur != null)
                {
                    ScrollPlaylistToCurrentItem();
                }
                ShowOsd(string.Format(I18nService.Instance["OsdSorted"], System.Text.RegularExpressions.Regex.Replace(clicked.Header?.ToString() ?? "", @"^[\p{So}\p{Sm}\s]+", "")));
            }
        }

        private void BtnShufflePlaylist_Click(object sender, RoutedEventArgs e)
        {
            var mgr = PlaylistManager.Instance;
            bool nowShuffle = mgr.Mode == PlaybackMode.Sequential;
            mgr.Mode = nowShuffle ? PlaybackMode.Shuffle : PlaybackMode.Sequential;

            UpdateShuffleButtonUI(showOsd: true);
        }

        private void UpdateShuffleButtonUI(bool showOsd = false)
        {
            if (btnShufflePlaylist == null) return;
            var mgr = PlaylistManager.Instance;
            bool isShuffle = mgr.Mode == PlaybackMode.Shuffle;
            var accent = (System.Windows.Media.Brush)FindResource("ThemeAccentBrush");
            var normalText = (System.Windows.Media.Brush)FindResource("ThemeTextBrush");

            btnShufflePlaylist.Content = isShuffle ? "\uE8B1" : "\uE72A";
            btnShufflePlaylist.Foreground = isShuffle ? accent : normalText;
            btnShufflePlaylist.ToolTip = isShuffle 
                ? I18nService.Instance["OsdShuffleOn"] 
                : I18nService.Instance["OsdShuffleOff"];
            if (showOsd) ShowOsd(isShuffle ? I18nService.Instance["OsdShuffleOn"] : I18nService.Instance["OsdShuffleOff"]);
        }

        private void BtnRepeatMode_Click(object sender, RoutedEventArgs e)
        {
            var mgr = PlaylistManager.Instance;
            mgr.Repeat = mgr.Repeat switch
            {
                RepeatMode.RepeatAll => RepeatMode.RepeatSingle,
                RepeatMode.RepeatSingle => RepeatMode.None,
                _ => RepeatMode.RepeatAll
            };

            UpdateRepeatButtonUI(showOsd: true);
        }

        private void UpdateRepeatButtonUI(bool showOsd = false)
        {
            if (btnRepeatMode == null) return;
            var mgr = PlaylistManager.Instance;
            var accent = (System.Windows.Media.Brush)FindResource("ThemeAccentBrush");
            var normalText = (System.Windows.Media.Brush)FindResource("ThemeTextBrush");

            switch (mgr.Repeat)
            {
                case RepeatMode.RepeatAll:
                    btnRepeatMode.Content = "\uE8EE";
                    btnRepeatMode.Foreground = accent;
                    btnRepeatMode.ToolTip = I18nService.Instance["ToolTipRepeatAll"];
                    if (showOsd) ShowOsd(I18nService.Instance["OsdRepeatAll"]);
                    break;
                case RepeatMode.RepeatSingle:
                    btnRepeatMode.Content = "\uE8ED";
                    btnRepeatMode.Foreground = accent;
                    btnRepeatMode.ToolTip = I18nService.Instance["ToolTipRepeatSingle"];
                    if (showOsd) ShowOsd(I18nService.Instance["OsdRepeatSingle"]);
                    break;
                case RepeatMode.None:
                    btnRepeatMode.Content = "\uE8E4";
                    btnRepeatMode.Foreground = normalText;
                    btnRepeatMode.ToolTip = I18nService.Instance["ToolTipRepeatNone"];
                    if (showOsd) ShowOsd(I18nService.Instance["OsdRepeatNone"]);
                    break;
            }
        }

        private void BtnDeletePlaylistItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is PlaylistItem item)
            {
                PlaylistManager.Instance.Remove(item);
            }
        }

        private void LbPlaylist_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
        }

        private void LbPlaylist_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (lbPlaylist.SelectedItem is PlaylistItem item)
            {
                if (IsShiftKeyDown() && File.Exists(item.FilePath))
                {
                    HandleDropPaths(new[] { item.FilePath });
                }
                else
                {
                    PlayFileWithTransition(item.FilePath);
                }
            }
        }

        private void BtnAddFolderPlaylist_Click(object sender, RoutedEventArgs e)
        {
            var folderDlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = I18nService.Instance["DialogAddFolderToPlaylist"],
                Multiselect = true
            };
            if (folderDlg.ShowDialog() == true && folderDlg.FolderNames.Length > 0)
            {
                _ = PlaylistManager.Instance.AppendDirectoryAsync(folderDlg.FolderNames, targetOpenFile: null, onPlayTarget: null);
            }
        }

        private void BtnAddFilePlaylist_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new WpfOpenFileDialog
            {
                Title = I18nService.Instance["DialogAddFilesToPlaylist"],
                Multiselect = true,
                Filter = "媒体文件|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.flv;*.rmvb;*.ts;*.m2ts;*.webm;*.iso;*.mp3;*.flac;*.aac;*.wav;*.m4a;*.ogg;*.opus;*.jpg;*.jpeg;*.png;*.webp;*.bmp;*.gif|所有文件|*.*"
            };
            if (dlg.ShowDialog() == true && dlg.FileNames.Length > 0)
            {
                PlaylistManager.Instance.AddFilesBatch(dlg.FileNames, playImmediatelyFirst: false);
            }
        }

        private System.Windows.Point _playlistDragStartPoint;
        private bool _isDraggingPlaylistItem = false;
        private PlaylistItem? _draggedPlaylistItem = null;

        private void LbPlaylist_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (FindVisualParent<System.Windows.Controls.Button>((DependencyObject)e.OriginalSource) != null) return;

            _playlistDragStartPoint = e.GetPosition(null);
            var item = FindVisualParent<System.Windows.Controls.ListBoxItem>((DependencyObject)e.OriginalSource);
            if (item != null && item.DataContext is PlaylistItem pItem)
            {
                _draggedPlaylistItem = pItem;
            }
            else
            {
                _draggedPlaylistItem = null;
            }
        }

        private void LbPlaylist_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed && _draggedPlaylistItem != null && !_isDraggingPlaylistItem)
            {
                System.Windows.Point currentPos = e.GetPosition(null);
                System.Windows.Vector diff = _playlistDragStartPoint - currentPos;
                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    _isDraggingPlaylistItem = true;
                    try
                    {
                        var data = new System.Windows.DataObject("AniPlaylistItem", _draggedPlaylistItem);
                        System.Windows.DragDrop.DoDragDrop(lbPlaylist, data, System.Windows.DragDropEffects.Move);
                    }
                    finally
                    {
                        _isDraggingPlaylistItem = false;
                        _draggedPlaylistItem = null;
                    }
                }
            }
        }

        private void LbPlaylist_DragOver(object sender, WpfDragEventArgs e)
        {
            if (e.Data.GetDataPresent("AniPlaylistItem"))
            {
                e.Effects = System.Windows.DragDropEffects.Move;
                e.Handled = true;
            }
            else if (e.Data.GetDataPresent(WpfDataFormats.FileDrop))
            {
                e.Effects = System.Windows.DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        private void PlaylistDrawer_Drop(object sender, WpfDragEventArgs e)
        {
            if (e.Data.GetDataPresent("AniPlaylistItem"))
            {
                if (e.Data.GetData("AniPlaylistItem") is PlaylistItem dragged)
                {
                    int oldIndex = PlaylistManager.Instance.Items.IndexOf(dragged);
                    if (oldIndex >= 0)
                    {
                        var targetItem = FindVisualParent<System.Windows.Controls.ListBoxItem>((DependencyObject)e.OriginalSource);
                        int newIndex;
                        if (targetItem != null && targetItem.DataContext is PlaylistItem target)
                        {
                            newIndex = PlaylistManager.Instance.Items.IndexOf(target);
                            if (newIndex < 0) newIndex = PlaylistManager.Instance.Items.Count - 1;
                        }
                        else
                        {
                            newIndex = PlaylistManager.Instance.Items.Count - 1;
                        }

                        if (oldIndex != newIndex && newIndex >= 0 && newIndex < PlaylistManager.Instance.Items.Count)
                        {
                            PlaylistManager.Instance.MoveItem(oldIndex, newIndex);
                            lbPlaylist.SelectedIndex = newIndex;
                        }
                    }
                }
                e.Handled = true;
                return;
            }

            if (e.Data.GetDataPresent(WpfDataFormats.FileDrop))
            {
                if (e.Data.GetData(WpfDataFormats.FileDrop) is string[] paths && paths.Length > 0)
                {
                    PlaylistManager.Instance.AddPaths(paths, playFirst: false);
                    e.Handled = true;
                }
            }
        }

        private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
        {
            while (child != null)
            {
                if (child is T parent) return parent;
                child = System.Windows.Media.VisualTreeHelper.GetParent(child);
            }
            return null;
        }

        private void LbPlaylist_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            if (sender is System.Windows.Controls.ListBox lb)
            {
                var scrollViewer = GetScrollViewer(lb);
                if (scrollViewer != null)
                {
                    if (e.Delta < 0)
                        scrollViewer.LineDown();
                    else
                        scrollViewer.LineUp();
                    e.Handled = true;
                }
            }
        }

        private System.Windows.Controls.ScrollViewer? GetScrollViewer(System.Windows.DependencyObject depObj)
        {
            if (depObj is System.Windows.Controls.ScrollViewer sv) return sv;
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(depObj, i);
                var result = GetScrollViewer(child);
                if (result != null) return result;
            }
            return null;
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                // 1. Save last volume setting before exiting
                if (sliderVolume != null)
                {
                    SettingsService.Instance.Config.LastVolume = (int)Math.Round(sliderVolume.Value);
                }

                // 2. Save playback progress of the last played video if applicable
                if (_mpv != IntPtr.Zero && _hasMedia && !string.IsNullOrEmpty(_currentPlayingFilePath) && !_isCurrentImage)
                {
                    double pos = MpvGetDouble("time-pos");
                    double dur = MpvGetDouble("duration");
                    string eof = MpvGet("eof-reached");

                    // If video played past 3 seconds and is not within the last 5 seconds / not EOF, record it
                    if (pos > 3.0 && dur > 6.0 && pos < dur - 5.0 && eof != "yes")
                    {
                        SettingsService.Instance.Config.LastPlayedFilePath = _currentPlayingFilePath;
                        SettingsService.Instance.Config.LastPlayedPosition = pos;
                        SettingsService.Instance.Config.LastPlayedDuration = dur;
                    }
                    else
                    {
                        // Finished playing or just started: clear so it doesn't resume from the very end
                        SettingsService.Instance.Config.LastPlayedFilePath = "";
                        SettingsService.Instance.Config.LastPlayedPosition = 0.0;
                        SettingsService.Instance.Config.LastPlayedDuration = 0.0;
                    }
                }

                // 3. Clear current playlist on exit if option is enabled (saved library playlists in library.json are untouched)
                if (SettingsService.Instance.Config.ClearPlaylistOnExit)
                {
                    PlaylistManager.Instance.Clear();
                    SettingsService.Instance.Config.LastPlayedFilePath = "";
                    SettingsService.Instance.Config.LastPlayedPosition = 0.0;
                    SettingsService.Instance.Config.LastPlayedDuration = 0.0;
                }

                SettingsService.Instance.Save();
            }
            catch { }
            base.OnClosing(e);
        }
    }

    public class NaturalStringComparer : IComparer<string>
    {
        [System.Runtime.InteropServices.DllImport("shlwapi.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int StrCmpLogicalW(string psz1, string psz2);

        public int Compare(string? x, string? y)
        {
            return StrCmpLogicalW(x ?? "", y ?? "");
        }
    }
    internal class DoubleBufferedPanel : System.Windows.Forms.Panel
    {
        public DoubleBufferedPanel()
        {
            SetStyle(
                System.Windows.Forms.ControlStyles.AllPaintingInWmPaint |
                System.Windows.Forms.ControlStyles.UserPaint |
                System.Windows.Forms.ControlStyles.OptimizedDoubleBuffer |
                System.Windows.Forms.ControlStyles.ResizeRedraw,
                true);
            UpdateStyles();
        }
    }
}
