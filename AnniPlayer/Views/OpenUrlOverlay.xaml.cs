using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using AnniPlayer.Services;
using WpfUserControl = System.Windows.Controls.UserControl;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfMouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using WpfClipboard = System.Windows.Clipboard;

namespace AnniPlayer.Views
{
    public class OpenUrlEventArgs : EventArgs
    {
        public string Url { get; }
        public bool SaveToLocal { get; }
        public string SaveDir { get; }

        public OpenUrlEventArgs(string url, bool saveToLocal, string saveDir)
        {
            Url = url;
            SaveToLocal = saveToLocal;
            SaveDir = saveDir;
        }
    }

    public partial class OpenUrlOverlay : WpfUserControl
    {
        public event EventHandler? Closed;
        public event EventHandler<OpenUrlEventArgs>? PlayRequested;

        public OpenUrlOverlay()
        {
            InitializeComponent();
            Loaded += OpenUrlOverlay_Loaded;
            PreviewKeyDown += OpenUrlOverlay_PreviewKeyDown;
        }

        private void OpenUrlOverlay_Loaded(object sender, RoutedEventArgs e)
        {
            InitializeForOpen();
        }

        private void OpenUrlOverlay_PreviewKeyDown(object sender, WpfKeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Closed?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            }
        }

        private void Backdrop_MouseDown(object sender, WpfMouseButtonEventArgs e)
        {
            if (e.OriginalSource == OverlayBackground)
            {
                Closed?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            }
        }

        private void Card_MouseDown(object sender, WpfMouseButtonEventArgs e)
        {
            // Prevent clicking inside the card from closing the dialog
            e.Handled = true;
        }

        public void InitializeForOpen()
        {
            try
            {
                string clip = WpfClipboard.GetText()?.Trim() ?? "";
                if (IsLikelyUrl(clip))
                {
                    TxtUrl.Text = clip;
                    TxtUrl.SelectAll();
                }
                else if (string.IsNullOrWhiteSpace(TxtUrl.Text))
                {
                    TxtUrl.Text = "";
                }
                else
                {
                    TxtUrl.SelectAll();
                }
            }
            catch
            {
                if (string.IsNullOrWhiteSpace(TxtUrl.Text))
                {
                    TxtUrl.Text = "";
                }
            }

            // Always uncheck save to local on open so user must explicitly check it for this session
            CbSaveToLocal.IsChecked = false;
            GridSaveDir.Visibility = Visibility.Collapsed;

            var config = SettingsService.Instance.Config;
            string streamDir = config.NetworkStreamSaveDir;
            if (string.IsNullOrWhiteSpace(streamDir))
            {
                streamDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "AniPlayer", "Streams");
            }
            TxtSaveDir.Text = streamDir;

            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
            {
                TxtUrl.Focus();
            }));
        }

        public static bool IsLikelyUrl(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            return text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                   text.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                   text.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase) ||
                   text.StartsWith("rtmp://", StringComparison.OrdinalIgnoreCase) ||
                   text.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase) ||
                   text.StartsWith("mms://", StringComparison.OrdinalIgnoreCase) ||
                   text.StartsWith("\\\\", StringComparison.OrdinalIgnoreCase) ||
                   text.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase) ||
                   text.EndsWith(".mpd", StringComparison.OrdinalIgnoreCase);
        }

        private void CbSaveToLocal_Click(object sender, RoutedEventArgs e)
        {
            bool isChecked = CbSaveToLocal.IsChecked == true;
            GridSaveDir.Visibility = isChecked ? Visibility.Visible : Visibility.Collapsed;

            if (isChecked)
            {
                if (string.IsNullOrWhiteSpace(TxtSaveDir.Text) || !Directory.Exists(TxtSaveDir.Text))
                {
                    BtnBrowseSaveDir_Click(sender, e);
                    if (string.IsNullOrWhiteSpace(TxtSaveDir.Text) || !Directory.Exists(TxtSaveDir.Text))
                    {
                        string defaultDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "AniPlayer", "Streams");
                        try { Directory.CreateDirectory(defaultDir); TxtSaveDir.Text = defaultDir; } catch { }
                    }
                }
            }
        }

        private void BtnBrowseSaveDir_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new Microsoft.Win32.OpenFolderDialog
                {
                    Title = "选择网络视频保存目录"
                };
                if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.FolderName))
                {
                    TxtSaveDir.Text = dlg.FolderName;
                }
            }
            catch { }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Closed?.Invoke(this, EventArgs.Empty);
        }

        private void BtnPaste_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string clip = WpfClipboard.GetText()?.Trim() ?? "";
                if (!string.IsNullOrEmpty(clip))
                {
                    TxtUrl.Text = clip;
                    TxtUrl.SelectAll();
                    TxtUrl.Focus();
                }
            }
            catch { }
        }

        private void BtnPlay_Click(object sender, RoutedEventArgs e)
        {
            SubmitUrl();
        }

        private void TxtUrl_KeyDown(object sender, WpfKeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                SubmitUrl();
                e.Handled = true;
            }
        }

        private void SubmitUrl()
        {
            string url = TxtUrl.Text.Trim();
            if (string.IsNullOrWhiteSpace(url)) return;

            bool saveToLocal = CbSaveToLocal.IsChecked == true;
            string saveDir = TxtSaveDir.Text.Trim();

            // Save user directory preference if checked
            if (saveToLocal)
            {
                var config = SettingsService.Instance.Config;
                config.NetworkStreamSaveDir = saveDir;
                SettingsService.Instance.Save();
            }

            PlayRequested?.Invoke(this, new OpenUrlEventArgs(url, saveToLocal, saveDir));
        }
    }
}
