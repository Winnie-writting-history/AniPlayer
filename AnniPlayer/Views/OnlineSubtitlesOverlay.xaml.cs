using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AnniPlayer.Services;

using WpfUserControl = System.Windows.Controls.UserControl;
using WpfButton = System.Windows.Controls.Button;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using Key = System.Windows.Input.Key;

namespace AnniPlayer.Views
{
    public partial class OnlineSubtitlesOverlay : WpfUserControl
    {
        public event EventHandler? Closed;
        public event EventHandler<string>? SubtitleDownloadedAndSelected;

        private string _currentVideoPath = "";
        private bool _isBusy = false;

        public OnlineSubtitlesOverlay()
        {
            InitializeComponent();
            Loaded += OnlineSubtitlesOverlay_Loaded;
            PreviewKeyDown += OnlineSubtitlesOverlay_PreviewKeyDown;
        }

        private void OnlineSubtitlesOverlay_Loaded(object sender, RoutedEventArgs e)
        {
            Focus();
            TxtSearchKeyword.Focus();
        }

        private void OnlineSubtitlesOverlay_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Closed?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            }
        }

        private void MaskGrid_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource == sender)
            {
                Closed?.Invoke(this, EventArgs.Empty);
            }
        }

        public void InitializeForVideo(string videoPath)
        {
            _currentVideoPath = videoPath;
            TxtVideoName.Text = !string.IsNullOrEmpty(videoPath) ? Path.GetFileName(videoPath) : "";
            
            string cleanKeyword = OnlineSubtitleService.Instance.ExtractCleanKeyword(videoPath);
            TxtSearchKeyword.Text = cleanKeyword;

            // 自动加载智能搜索
            _ = PerformSearchAsync(cleanKeyword);
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Closed?.Invoke(this, EventArgs.Empty);
        }

        private void TxtSearchKeyword_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                _ = PerformSearchAsync(TxtSearchKeyword.Text.Trim());
            }
        }

        private void BtnSearch_Click(object sender, RoutedEventArgs e)
        {
            _ = PerformSearchAsync(TxtSearchKeyword.Text.Trim());
        }

        private void BtnSmartMatch_Click(object sender, RoutedEventArgs e)
        {
            string clean = OnlineSubtitleService.Instance.ExtractCleanKeyword(_currentVideoPath);
            TxtSearchKeyword.Text = clean;
            _ = PerformSearchAsync(clean);
        }

        private async System.Threading.Tasks.Task PerformSearchAsync(string keyword)
        {
            if (_isBusy) return;
            _isBusy = true;
            ProgressSearching.Visibility = Visibility.Visible;
            TxtStatus.Text = I18nService.Instance["TxtSearching"];
            TxtEmptyHint.Visibility = Visibility.Collapsed;
            LstSubtitles.ItemsSource = null;

            try
            {
                var list = await OnlineSubtitleService.Instance.SearchSubtitlesAsync(_currentVideoPath, keyword);
                LstSubtitles.ItemsSource = list;
                if (list.Count == 0)
                {
                    TxtEmptyHint.Visibility = Visibility.Visible;
                    TxtStatus.Text = I18nService.Instance["TxtNoResults"];
                }
                else
                {
                    TxtStatus.Text = $"找到 {list.Count} 条符合条件的在线字幕，双击列表项即可自动下载载入。";
                }
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"搜索失败: {ex.Message}";
            }
            finally
            {
                ProgressSearching.Visibility = Visibility.Collapsed;
                _isBusy = false;
            }
        }

        private void LstSubtitles_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (LstSubtitles.SelectedItem is SubtitleSearchResult selected)
            {
                _ = PerformDownloadAsync(selected);
            }
        }

        private void BtnItemDownload_Click(object sender, RoutedEventArgs e)
        {
            if (sender is WpfButton btn && btn.DataContext is SubtitleSearchResult item)
            {
                _ = PerformDownloadAsync(item);
            }
        }

        private async System.Threading.Tasks.Task PerformDownloadAsync(SubtitleSearchResult item)
        {
            if (_isBusy || string.IsNullOrEmpty(_currentVideoPath)) return;
            _isBusy = true;
            ProgressSearching.Visibility = Visibility.Visible;
            TxtStatus.Text = I18nService.Instance["TxtDownloading"];

            try
            {
                string? savedPath = await OnlineSubtitleService.Instance.DownloadSubtitleFileAsync(item, _currentVideoPath);
                if (!string.IsNullOrEmpty(savedPath) && File.Exists(savedPath))
                {
                    SubtitleDownloadedAndSelected?.Invoke(this, savedPath);
                    Closed?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    TxtStatus.Text = "字幕文件下载保存失败，请检查网络或路径权限。";
                }
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"下载出错: {ex.Message}";
            }
            finally
            {
                ProgressSearching.Visibility = Visibility.Collapsed;
                _isBusy = false;
            }
        }
    }
}
