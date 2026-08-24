using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AnniPlayer.Services;

using WpfUserControl = System.Windows.Controls.UserControl;
using WpfRadioButton = System.Windows.Controls.RadioButton;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfApplication = System.Windows.Application;
using WpfBrush = System.Windows.Media.Brush;

namespace AnniPlayer.Views
{
    public class TrackItemInfo
    {
        public int Id { get; set; }
        public string Type { get; set; } = "sub"; // "sub" or "audio"
        public string Title { get; set; } = "";
        public string Language { get; set; } = "";
        public string Codec { get; set; } = "";
        public bool IsSelected { get; set; }
        public bool IsExternal { get; set; }
        public string ExternalFilename { get; set; } = "";

        public string FullDisplayName
        {
            get
            {
                if (Id == 0)
                {
                    return Type == "sub" ? I18nService.Instance["SubTrackNone"] : I18nService.Instance["AudioTrackNone"];
                }
                if (IsExternal && !string.IsNullOrEmpty(ExternalFilename))
                {
                    string fileName = System.IO.Path.GetFileName(ExternalFilename);
                    return $"[{Id}] {fileName}";
                }
                string titleStr = !string.IsNullOrEmpty(Title) ? $" {Title}" : "";
                string lang = !string.IsNullOrEmpty(Language) ? $" ({Language})" : "";
                string codecStr = !string.IsNullOrEmpty(Codec) ? $" [{Codec}]" : "";
                string extTag = I18nService.Instance["TrackTagExternal"];
                if (string.IsNullOrEmpty(extTag) || extTag == "TrackTagExternal")
                {
                    extTag = (I18nService.Instance.CurrentLanguage == "en-US") ? " [External]" : " [外部]";
                }
                string tag = IsExternal ? extTag : "";
                return $"[{Id}]{titleStr}{lang}{codecStr}{tag}".Trim();
            }
        }

        public string DisplayName => FullDisplayName;
    }

    public partial class TracksOverlay : WpfUserControl
    {
        public event EventHandler? Closed;
        public event EventHandler<int>? SubTrackSelected;
        public event EventHandler<int>? AudioTrackSelected;
        public event EventHandler<double>? SubDelayChanged;
        public event EventHandler<double>? AudioDelayChanged;
        public event EventHandler<int>? SubPosChanged;
        public event EventHandler? LoadExternalSubRequested;
        public event EventHandler? LoadExternalAudioRequested;
        public event EventHandler<bool>? NightModeToggled;

        private double _subDelay = 0.0;
        private double _audioDelay = 0.0;

        public void SetNightMode(bool enabled)
        {
            if (ChkNightMode != null)
            {
                ChkNightMode.IsChecked = enabled;
            }
        }

        private void ChkNightMode_Click(object sender, RoutedEventArgs e)
        {
            NightModeToggled?.Invoke(this, ChkNightMode.IsChecked == true);
        }

        public TracksOverlay()
        {
            InitializeComponent();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Closed?.Invoke(this, EventArgs.Empty);
        }

        private void TabSubtitles_Checked(object sender, RoutedEventArgs e)
        {
            if (PanelSubtitles != null && PanelAudio != null)
            {
                PanelSubtitles.Visibility = Visibility.Visible;
                PanelAudio.Visibility = Visibility.Collapsed;
            }
        }

        private void TabAudio_Checked(object sender, RoutedEventArgs e)
        {
            if (PanelSubtitles != null && PanelAudio != null)
            {
                PanelSubtitles.Visibility = Visibility.Collapsed;
                PanelAudio.Visibility = Visibility.Visible;
            }
        }

        private void RadioSubNone_Click(object sender, RoutedEventArgs e)
        {
            SubTrackSelected?.Invoke(this, 0);
        }

        private void RadioAudioNone_Click(object sender, RoutedEventArgs e)
        {
            AudioTrackSelected?.Invoke(this, 0);
        }

        public void PopulateSubTracks(List<TrackItemInfo> tracks, double currentDelay)
        {
            _subDelay = Math.Truncate(currentDelay * 10.0) / 10.0;
            TxtSubDelay.Text = $"{_subDelay:F1}s";
            StackSubTracks.Children.Clear();

            bool hasSelection = false;
            foreach (var t in tracks)
            {
                if (t.IsSelected) { hasSelection = true; break; }
            }

            if (RadioSubNone != null)
            {
                RadioSubNone.IsChecked = !hasSelection;
            }

            foreach (var t in tracks)
            {
                AddTrackRadioButton(StackSubTracks, t, (id) => SubTrackSelected?.Invoke(this, id));
            }
        }

        public void PopulateAudioTracks(List<TrackItemInfo> tracks, double currentDelay)
        {
            _audioDelay = Math.Truncate(currentDelay * 10.0) / 10.0;
            TxtAudioDelay.Text = $"{_audioDelay:F1}s";
            StackAudioTracks.Children.Clear();

            bool hasSelection = false;
            foreach (var t in tracks)
            {
                if (t.IsSelected) { hasSelection = true; break; }
            }

            if (RadioAudioNone != null)
            {
                RadioAudioNone.IsChecked = !hasSelection;
            }

            foreach (var t in tracks)
            {
                AddTrackRadioButton(StackAudioTracks, t, (id) => AudioTrackSelected?.Invoke(this, id));
            }
        }

        private void AddTrackRadioButton(StackPanel container, TrackItemInfo info, Action<int> onSelected)
        {
            var textBlock = new TextBlock
            {
                Text = info.DisplayName,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left
            };

            var btn = new WpfRadioButton
            {
                Content = textBlock,
                ToolTip = info.FullDisplayName,
                IsChecked = info.IsSelected,
                GroupName = info.Type == "sub" ? "GroupSubTracks" : "GroupAudioTracks",
                Height = 34,
                Margin = new Thickness(0, 2, 0, 2),
                FontSize = 13,
                VerticalContentAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left,
                Foreground = info.IsSelected ? (WpfBrush)WpfApplication.Current.Resources["ThemeAccentBrush"] : (WpfBrush)WpfApplication.Current.Resources["ThemeTextBrush"],
                Style = (Style)WpfApplication.Current.Resources["ThemeTabButtonStyle"]
            };

            btn.Click += (s, e) => onSelected(info.Id);
            container.Children.Add(btn);
        }

        private void DelayInput_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is WpfTextBox tb)
            {
                tb.Text = "";
            }
        }

        private void DelayInput_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is WpfTextBox tb && !tb.IsFocused)
            {
                tb.Focus();
                tb.Text = "";
                e.Handled = true;
            }
        }

        // Subtitle Delay
        private void BtnSubDelayMinus_Click(object sender, RoutedEventArgs e)
        {
            _subDelay = Math.Truncate((_subDelay - 0.5) * 10.0) / 10.0;
            TxtSubDelay.Text = $"{_subDelay:F1}s";
            SubDelayChanged?.Invoke(this, _subDelay);
        }

        private void BtnSubDelayPlus_Click(object sender, RoutedEventArgs e)
        {
            _subDelay = Math.Truncate((_subDelay + 0.5) * 10.0) / 10.0;
            TxtSubDelay.Text = $"{_subDelay:F1}s";
            SubDelayChanged?.Invoke(this, _subDelay);
        }

        private void BtnSubDelayReset_Click(object sender, RoutedEventArgs e)
        {
            _subDelay = 0.0;
            TxtSubDelay.Text = "0.0s";
            SubDelayChanged?.Invoke(this, _subDelay);
        }

        private void TxtSubDelay_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                CommitSubDelayInput();
                e.Handled = true;
            }
        }

        private void TxtSubDelay_LostFocus(object sender, RoutedEventArgs e)
        {
            CommitSubDelayInput();
        }

        private void CommitSubDelayInput()
        {
            string raw = TxtSubDelay.Text.Trim().TrimEnd('s', 'S', ' ');
            if (double.TryParse(raw, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsed))
            {
                _subDelay = Math.Truncate(parsed * 10.0) / 10.0;
                TxtSubDelay.Text = $"{_subDelay:F1}s";
                SubDelayChanged?.Invoke(this, _subDelay);
            }
            else
            {
                TxtSubDelay.Text = $"{_subDelay:F1}s";
            }
        }

        // Subtitle Position
        private void BtnSubPosUp_Click(object sender, RoutedEventArgs e)
        {
            SubPosChanged?.Invoke(this, -5);
        }

        private void BtnSubPosDown_Click(object sender, RoutedEventArgs e)
        {
            SubPosChanged?.Invoke(this, 5);
        }

        private void BtnSubPosReset_Click(object sender, RoutedEventArgs e)
        {
            SubPosChanged?.Invoke(this, 100); // 100 is default mpv sub-pos
        }

        // Audio Delay
        private void BtnAudioDelayMinus_Click(object sender, RoutedEventArgs e)
        {
            _audioDelay = Math.Truncate((_audioDelay - 0.1) * 10.0) / 10.0;
            TxtAudioDelay.Text = $"{_audioDelay:F1}s";
            AudioDelayChanged?.Invoke(this, _audioDelay);
        }

        private void BtnAudioDelayPlus_Click(object sender, RoutedEventArgs e)
        {
            _audioDelay = Math.Truncate((_audioDelay + 0.1) * 10.0) / 10.0;
            TxtAudioDelay.Text = $"{_audioDelay:F1}s";
            AudioDelayChanged?.Invoke(this, _audioDelay);
        }

        private void BtnAudioDelayReset_Click(object sender, RoutedEventArgs e)
        {
            _audioDelay = 0.0;
            TxtAudioDelay.Text = "0.0s";
            AudioDelayChanged?.Invoke(this, _audioDelay);
        }

        private void TxtAudioDelay_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                CommitAudioDelayInput();
                e.Handled = true;
            }
        }

        private void TxtAudioDelay_LostFocus(object sender, RoutedEventArgs e)
        {
            CommitAudioDelayInput();
        }

        private void CommitAudioDelayInput()
        {
            string raw = TxtAudioDelay.Text.Trim().TrimEnd('s', 'S', ' ');
            if (double.TryParse(raw, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsed))
            {
                _audioDelay = Math.Truncate(parsed * 10.0) / 10.0;
                TxtAudioDelay.Text = $"{_audioDelay:F1}s";
                AudioDelayChanged?.Invoke(this, _audioDelay);
            }
            else
            {
                TxtAudioDelay.Text = $"{_audioDelay:F1}s";
            }
        }

        private void BtnLoadExternalSub_Click(object sender, RoutedEventArgs e)
        {
            LoadExternalSubRequested?.Invoke(this, EventArgs.Empty);
        }

        private void BtnLoadExternalAudio_Click(object sender, RoutedEventArgs e)
        {
            LoadExternalAudioRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
