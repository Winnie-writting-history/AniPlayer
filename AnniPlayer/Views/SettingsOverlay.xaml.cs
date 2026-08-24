using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AnniPlayer.Services;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;

using WpfUserControl = System.Windows.Controls.UserControl;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfComboBoxItem = System.Windows.Controls.ComboBoxItem;

namespace AnniPlayer.Views
{
    public partial class SettingsOverlay : WpfUserControl
    {
        public event EventHandler? Closed;
        public event EventHandler? SettingsSaved;
        public event EventHandler? OpenLibraryRequested;
        public event EventHandler? SponsorRequested;

        private bool _isLoading = false;

        public SettingsOverlay()
        {
            InitializeComponent();
            RefreshLanguageItems();
            RefreshImageDurationItems();
            LoadFromService();
        }

        private void RefreshImageDurationItems()
        {
            if (ComboImageDuration == null) return;
            string secLabel = I18nService.Instance["SettingsSeconds"];
            if (string.IsNullOrEmpty(secLabel) || secLabel.StartsWith("[")) secLabel = "秒";

            string currentTag = (ComboImageDuration.SelectedItem as WpfComboBoxItem)?.Tag?.ToString() ?? "5";

            ComboImageDuration.Items.Clear();
            int[] durations = new[] { 3, 5, 10, 15, 30 };
            foreach (var sec in durations)
            {
                var item = new WpfComboBoxItem
                {
                    Content = $"{sec} {secLabel}",
                    Tag = sec.ToString()
                };
                ComboImageDuration.Items.Add(item);
                if (sec.ToString() == currentTag)
                {
                    ComboImageDuration.SelectedItem = item;
                }
            }
            if (ComboImageDuration.SelectedItem == null && ComboImageDuration.Items.Count > 1)
            {
                ComboImageDuration.SelectedIndex = 1;
            }
        }

        private void RefreshLanguageItems()
        {
            if (ComboLanguage == null) return;
            string currentTag = (ComboLanguage.SelectedItem as WpfComboBoxItem)?.Tag?.ToString() 
                                ?? SettingsService.Instance.Config.Language;

            ComboLanguage.Items.Clear();
            var availableLangs = I18nService.Instance.GetAvailableLanguages();

            foreach (var langInfo in availableLangs)
            {
                var item = new WpfComboBoxItem
                {
                    Content = langInfo.DisplayName,
                    Tag = langInfo.Code
                };
                ComboLanguage.Items.Add(item);
                if (langInfo.Code.Equals(currentTag, StringComparison.OrdinalIgnoreCase))
                {
                    ComboLanguage.SelectedItem = item;
                }
            }

            if (ComboLanguage.SelectedItem == null && ComboLanguage.Items.Count > 0)
            {
                ComboLanguage.SelectedIndex = 0;
            }
        }

        private void RefreshSkinItems()
        {
            if (ComboSkin == null) return;
            ThemeService.Instance.ScanAndLoadCustomSkins();

            string currentSkin = !string.IsNullOrEmpty(SettingsService.Instance.Config.ActiveSkin) 
                ? SettingsService.Instance.Config.ActiveSkin 
                : ThemeService.Instance.ActiveSkinKey;
            bool isEn = I18nService.Instance.CurrentLanguage == "en-US";
            ComboSkin.Items.Clear();

            // None / Built-in Theme Item
            string noneTitle = isEn ? "🈚 None (Use Built-in Theme Below)" : "🈚 无 (使用下方内置主题)";
            var itemNone = new WpfComboBoxItem
            {
                Content = noneTitle,
                Tag = "none"
            };
            ComboSkin.Items.Add(itemNone);
            if (string.Equals(currentSkin, "none", StringComparison.OrdinalIgnoreCase))
            {
                ComboSkin.SelectedItem = itemNone;
            }

            foreach (var kvp in ThemeService.Instance.Skins)
            {
                var skin = kvp.Value;
                string displayName = isEn ? skin.NameEn : skin.NameZh;
                var item = new WpfComboBoxItem
                {
                    Content = displayName,
                    Tag = skin.Key
                };
                ComboSkin.Items.Add(item);
                if (skin.Key.Equals(currentSkin, StringComparison.OrdinalIgnoreCase))
                {
                    ComboSkin.SelectedItem = item;
                }
            }

            if (ComboSkin.SelectedItem == null && ComboSkin.Items.Count > 0)
            {
                ComboSkin.SelectedIndex = 0;
            }

            UpdateSkinStatusUI();
        }

        private void UpdateSkinStatusUI()
        {
            bool isEn = I18nService.Instance.CurrentLanguage == "en-US";
            bool isSkinActive = ThemeService.Instance.IsSkinActive;

            if (TxtSkinStatus != null)
            {
                TxtSkinStatus.Text = isSkinActive
                    ? (isEn ? "🟢 Custom skin active (Highest priority, overrides theme colors)" : "🟢 外部皮肤正在生效（优先级最高，覆盖内置主题设置）")
                    : (isEn ? "⚪ No custom skin loaded (Using built-in theme below)" : "⚪ 未加载外部皮肤（当前使用下方内置主题配色）");
            }

            if (ComboTheme != null)
            {
                ComboTheme.Opacity = isSkinActive ? 0.45 : 1.0;
            }
        }

        private void RefreshThemeItems()
        {
            if (ComboTheme == null) return;

            string currentTag = (ComboTheme.SelectedItem as WpfComboBoxItem)?.Tag?.ToString() 
                                ?? ThemeService.Instance.CurrentThemeKey;

            bool isEn = I18nService.Instance.CurrentLanguage == "en-US";
            ComboTheme.Items.Clear();

            foreach (var kvp in ThemeService.Instance.Themes)
            {
                var themeItem = kvp.Value;
                string displayName = isEn ? themeItem.NameEn : themeItem.NameZh;

                var item = new WpfComboBoxItem
                {
                    Content = displayName,
                    Tag = themeItem.Key
                };
                ComboTheme.Items.Add(item);
                if (themeItem.Key == currentTag)
                {
                    ComboTheme.SelectedItem = item;
                }
            }
            if (ComboTheme.SelectedItem == null && ComboTheme.Items.Count > 0)
            {
                ComboTheme.SelectedIndex = 0;
            }

            UpdateSkinStatusUI();
        }

        public void LoadFromService()
        {
            _isLoading = true;
            try
            {
                var config = SettingsService.Instance.Config;

                // General
                CbAutoResume.IsChecked = config.AutoResume;
                CbAlwaysOnTop.IsChecked = config.AlwaysOnTop;
                CbRightEdgeHoverPlaylist.IsChecked = config.RightEdgeHoverPlaylist;
                CbDoubleEscToExit.IsChecked = config.DoubleEscToExit;
                CbClearPlaylistOnExit.IsChecked = config.ClearPlaylistOnExit;
                
                string path = config.ScreenshotPath;
                if (string.IsNullOrWhiteSpace(path) || path.Contains("AnniPlayer", StringComparison.OrdinalIgnoreCase))
                {
                    path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "AniPlayer");
                    config.ScreenshotPath = path;
                }
                TxtScreenshotPath.Text = path;
                ChkSaveScreenshotToFile.IsChecked = config.SaveScreenshotToFile;
                ChkCopyScreenshotToClipboard.IsChecked = config.CopyScreenshotToClipboard;
                ChkSaveScreenshotToMediaDir.IsChecked = config.SaveScreenshotToMediaDir;
                UpdateScreenshotPathEnableState();

                SelectComboTag(ComboContextMenuScale, config.ContextMenuScale.ToString(System.Globalization.CultureInfo.InvariantCulture));

                // Playback
                if (config.HwDec == "no") RbHwNo.IsChecked = true;
                else RbHwAuto.IsChecked = true;
                CbClickToActivateOnly.IsChecked = config.ClickToActivateOnly;
                CbVideoSharpening.IsChecked = config.VideoSharpening;
                CbAudioNightMode.IsChecked = config.AudioNightMode;
                SelectComboTag(ComboStreamCache, config.NetworkStreamCacheMode);

                // Slideshow & BGM
                RefreshImageDurationItems();
                SelectComboTag(ComboImageDuration, config.ImageDurationSec.ToString());
                if (config.BgmMode == 1) RbBgmManual.IsChecked = true;
                else if (config.BgmMode == 2) RbBgmDisabled.IsChecked = true;
                else RbBgmAuto.IsChecked = true;
                TxtManualBgmPath.Text = config.ManualBgmPath;
                ChkBgmSyncPlayPause.IsChecked = config.BgmSyncPlayPause;

                // Crop & Fill
                if (config.DefaultCropMode == 1) RbCropPreserve.IsChecked = true;
                else if (config.DefaultCropMode == 2) RbCropAll.IsChecked = true;
                else RbCropOff.IsChecked = true;
                CbDefaultSmartFill.IsChecked = config.DefaultSmartFill;

                // Language & Skin / Theme
                RefreshLanguageItems();
                SelectComboTag(ComboLanguage, config.Language);
                RefreshSkinItems();
                SelectComboTag(ComboSkin, config.ActiveSkin);
                RefreshThemeItems();
                SelectComboTag(ComboTheme, config.Theme);
                UpdateSkinStatusUI();

                // File Associations & Shortcuts
                CbAssociateVideos.IsChecked = config.AssociateVideos;
                CbAssociateAudios.IsChecked = config.AssociateAudios;
                CbAssociateFolderContextMenu.IsChecked = FileAssociationService.Instance.IsFolderContextMenuRegistered();
                CbDesktopShortcut.IsChecked = FileAssociationService.Instance.IsDesktopShortcutCreated();
                CbStartMenuShortcut.IsChecked = FileAssociationService.Instance.IsStartMenuShortcutCreated();

                // Hotkeys
                LoadHotkeysUI();
            }
            finally
            {
                _isLoading = false;
            }
        }

        public static bool IsRecordingKey { get; set; } = false;

        private static string FormatDisplayHotkey(string val)
        {
            if (string.IsNullOrEmpty(val))
            {
                string unbound = I18nService.Instance["HotkeyUnbound"];
                if (string.IsNullOrEmpty(unbound) || unbound.StartsWith("["))
                    unbound = I18nService.Instance.CurrentLanguage == "en-US" ? "Not Bound" : "未绑定";
                return unbound;
            }
            return val;
        }

        private void LoadHotkeysUI()
        {
            var hk = SettingsService.Instance.Config.Hotkeys ??= new HotkeyConfig();
            foreach (var p in typeof(HotkeyConfig).GetProperties())
            {
                if (p.PropertyType == typeof(string))
                {
                    string val = p.GetValue(hk)?.ToString() ?? "";
                    if (FindName("BtnKey_" + p.Name) is System.Windows.Controls.Button btn)
                    {
                        btn.Content = FormatDisplayHotkey(val);
                    }
                }
            }
        }

        private string GetHotkeyStringFromConfig(string propName)
        {
            var hk = SettingsService.Instance.Config.Hotkeys ??= new HotkeyConfig();
            var prop = typeof(HotkeyConfig).GetProperty(propName);
            return prop?.GetValue(hk)?.ToString() ?? "";
        }

        private void SetHotkeyConfigValue(string propName, string val)
        {
            var hk = SettingsService.Instance.Config.Hotkeys ??= new HotkeyConfig();

            // Auto-fallback: If primary hotkey is empty, set secondary hotkey into primary slot
            if (propName.StartsWith("Sec"))
            {
                string primaryPropName = propName.Substring(3);
                string currentPrimary = GetHotkeyStringFromConfig(primaryPropName);
                if (string.IsNullOrEmpty(currentPrimary))
                {
                    propName = primaryPropName;
                }
            }

            // Clear conflict on any other feature currently using the exact same hotkey
            if (!string.IsNullOrEmpty(val))
            {
                foreach (var p in typeof(HotkeyConfig).GetProperties())
                {
                    if (p.PropertyType == typeof(string) && p.Name != propName)
                    {
                        if (p.GetValue(hk)?.ToString() == val)
                        {
                            p.SetValue(hk, "");
                        }
                    }
                }
            }

            var targetProp = typeof(HotkeyConfig).GetProperty(propName);
            targetProp?.SetValue(hk, val ?? "");

            LoadHotkeysUI();
        }

        private void SelectComboTag(WpfComboBox combo, string tagValue)
        {
            if (combo == null) return;
            foreach (var obj in combo.Items)
            {
                if (obj is FrameworkElement el && el.Tag?.ToString() == tagValue)
                {
                    combo.SelectedItem = obj;
                    break;
                }
            }
        }

        private void Tab_Checked(object sender, RoutedEventArgs e)
        {
            if (PanelGeneral == null) return;

            PanelGeneral.Visibility = Visibility.Collapsed;
            if (PanelHotkeys != null) PanelHotkeys.Visibility = Visibility.Collapsed;
            PanelPlayback.Visibility = Visibility.Collapsed;
            PanelSlideshow.Visibility = Visibility.Collapsed;
            PanelCrop.Visibility = Visibility.Collapsed;
            PanelAppearance.Visibility = Visibility.Collapsed;
            PanelAssociations.Visibility = Visibility.Collapsed;
            if (PanelAbout != null) PanelAbout.Visibility = Visibility.Collapsed;

            if (sender == TabGeneral) PanelGeneral.Visibility = Visibility.Visible;
            else if (sender == TabHotkeys && PanelHotkeys != null) PanelHotkeys.Visibility = Visibility.Visible;
            else if (sender == TabPlayback) PanelPlayback.Visibility = Visibility.Visible;
            else if (sender == TabSlideshow) PanelSlideshow.Visibility = Visibility.Visible;
            else if (sender == TabCrop) PanelCrop.Visibility = Visibility.Visible;
            else if (sender == TabAppearance) PanelAppearance.Visibility = Visibility.Visible;
            else if (sender == TabAssociations) PanelAssociations.Visibility = Visibility.Visible;
            else if (sender == TabAbout && PanelAbout != null) PanelAbout.Visibility = Visibility.Visible;
        }

        public void SelectAboutTab()
        {
            if (TabAbout != null)
            {
                TabAbout.IsChecked = true;
            }
        }

        private void BtnOpenHomepage_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://aniplayer.ai.studio/") { UseShellExecute = true });
            }
            catch { }
        }

        private void BtnOpenGithub_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/Winnie-writting-history/AniPlayer") { UseShellExecute = true });
            }
            catch { }
        }

        private void BtnOpenYoutube_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://www.youtube.com/channel/UCpq209kHbKajSEbbXjMVJZw/") { UseShellExecute = true });
            }
            catch { }
        }

        private void BtnBrowseBgm_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new WpfOpenFileDialog
            {
                Title = "选择背景音频文件",
                Filter = "音频文件|*.mp3;*.flac;*.aac;*.wav;*.m4a;*.ogg;*.opus|所有文件|*.*"
            };
            if (dlg.ShowDialog() == true)
            {
                TxtManualBgmPath.Text = dlg.FileName;
                RbBgmManual.IsChecked = true;
            }
        }

        private void BtnOpenConfigDir_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AniPlayer");
                if (!System.IO.Directory.Exists(path)) System.IO.Directory.CreateDirectory(path);
                System.Diagnostics.Process.Start("explorer.exe", path);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"打开失败: {ex.Message}");
            }
        }

        private void BtnOpenScreenshotDir_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string path = SettingsService.Instance.Config.ScreenshotPath;
                if (string.IsNullOrWhiteSpace(path) || path.Contains("AnniPlayer", StringComparison.OrdinalIgnoreCase) || !System.IO.Directory.Exists(path))
                {
                    path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "AniPlayer");
                }
                if (!System.IO.Directory.Exists(path)) System.IO.Directory.CreateDirectory(path);
                System.Diagnostics.Process.Start("explorer.exe", path);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"打开失败: {ex.Message}");
            }
        }

        private void BtnOpenLibrary_Click(object sender, RoutedEventArgs e)
        {
            OpenLibraryRequested?.Invoke(this, EventArgs.Empty);
        }

        private void ChkSaveScreenshotToFile_Click(object sender, RoutedEventArgs e)
        {
            if (ChkSaveScreenshotToFile.IsChecked == true)
            {
                ChkSaveScreenshotToMediaDir.IsChecked = false;
            }
            UpdateScreenshotPathEnableState();
        }

        private void ChkSaveScreenshotToMediaDir_Click(object sender, RoutedEventArgs e)
        {
            if (ChkSaveScreenshotToMediaDir.IsChecked == true)
            {
                ChkSaveScreenshotToFile.IsChecked = false;
            }
            UpdateScreenshotPathEnableState();
        }

        private void UpdateScreenshotPathEnableState()
        {
            bool enabled = ChkSaveScreenshotToFile.IsChecked == true;
            if (TxtScreenshotPath != null) TxtScreenshotPath.IsEnabled = enabled;
            if (BtnBrowseScreenshot != null) BtnBrowseScreenshot.IsEnabled = enabled;
        }



        private void BtnRestoreDefaultHotkeys_Click(object sender, RoutedEventArgs e)
        {
            SettingsService.Instance.Config.Hotkeys = new HotkeyConfig();
            SettingsService.Instance.Save();
            LoadHotkeysUI();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            var config = SettingsService.Instance.Config;

            // General
            config.AutoResume = CbAutoResume.IsChecked == true;
            config.AlwaysOnTop = CbAlwaysOnTop.IsChecked == true;
            config.RightEdgeHoverPlaylist = CbRightEdgeHoverPlaylist.IsChecked == true;
            config.DoubleEscToExit = CbDoubleEscToExit.IsChecked == true;
            config.ClearPlaylistOnExit = CbClearPlaylistOnExit.IsChecked == true;
            config.ScreenshotPath = TxtScreenshotPath.Text?.Trim() ?? "";
            config.SaveScreenshotToFile = ChkSaveScreenshotToFile.IsChecked == true;
            config.CopyScreenshotToClipboard = ChkCopyScreenshotToClipboard.IsChecked == true;
            config.SaveScreenshotToMediaDir = ChkSaveScreenshotToMediaDir.IsChecked == true;

            if (ComboContextMenuScale.SelectedItem is FrameworkElement scaleItem && double.TryParse(scaleItem.Tag?.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double scVal))
            {
                config.ContextMenuScale = scVal;
            }

            // Playback
            config.HwDec = RbHwNo.IsChecked == true ? "no" : "auto";
            config.ClickToActivateOnly = CbClickToActivateOnly.IsChecked == true;
            config.VideoSharpening = CbVideoSharpening.IsChecked == true;
            config.AudioNightMode = CbAudioNightMode.IsChecked == true;
            if (ComboStreamCache.SelectedItem is FrameworkElement cacheItem && cacheItem.Tag is string cMode)
            {
                config.NetworkStreamCacheMode = cMode;
            }

            // Slideshow & BGM
            if (ComboImageDuration.SelectedItem is WpfComboBoxItem durItem && int.TryParse(durItem.Tag?.ToString(), out int sec))
            {
                config.ImageDurationSec = sec;
            }
            if (RbBgmManual.IsChecked == true) config.BgmMode = 1;
            else if (RbBgmDisabled.IsChecked == true) config.BgmMode = 2;
            else config.BgmMode = 0;
            config.ManualBgmPath = TxtManualBgmPath.Text.Trim();
            config.BgmSyncPlayPause = ChkBgmSyncPlayPause.IsChecked == true;

            // Crop
            if (RbCropPreserve.IsChecked == true) config.DefaultCropMode = 1;
            else if (RbCropAll.IsChecked == true) config.DefaultCropMode = 2;
            else config.DefaultCropMode = 0;

            config.DefaultSmartFill = CbDefaultSmartFill.IsChecked == true;

            // Language & Skin & Theme
            if (ComboLanguage.SelectedItem is WpfComboBoxItem langItem && langItem.Tag is string lang)
            {
                config.Language = lang;
            }
            if (ComboSkin.SelectedItem is WpfComboBoxItem skinItem && skinItem.Tag is string skinKey)
            {
                config.ActiveSkin = skinKey;
                ThemeService.Instance.ActiveSkinKey = skinKey;
            }
            if (ComboTheme.SelectedItem is WpfComboBoxItem themeItem && themeItem.Tag is string theme)
            {
                config.Theme = theme;
                ThemeService.Instance.CurrentThemeKey = theme;
            }

            // File Associations & Shortcuts
            config.AssociateVideos = CbAssociateVideos.IsChecked == true;
            config.AssociateAudios = CbAssociateAudios.IsChecked == true;
            config.AssociateFolder = CbAssociateFolderContextMenu.IsChecked == true;

            FileAssociationService.Instance.SetVideoAssociated(config.AssociateVideos);
            FileAssociationService.Instance.SetAudioAssociated(config.AssociateAudios);
            FileAssociationService.Instance.SetFolderContextMenuRegistered(config.AssociateFolder);
            FileAssociationService.Instance.SetDesktopShortcut(CbDesktopShortcut.IsChecked == true);
            FileAssociationService.Instance.SetStartMenuShortcut(CbStartMenuShortcut.IsChecked == true);

            SettingsService.Instance.Save();
            SettingsSaved?.Invoke(this, EventArgs.Empty);
            Closed?.Invoke(this, EventArgs.Empty);
        }

        private System.Windows.Controls.Button? _recordingButton = null;

        private void HotkeyButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn)
            {
                if (_recordingButton != null && _recordingButton != btn)
                {
                    ResetRecordingButton(_recordingButton);
                }

                _recordingButton = btn;
                IsRecordingKey = true;
                btn.Content = I18nService.Instance.CurrentLanguage == "en-US" ? "Press key..." : "请按键盘按键...";
                btn.Background = (System.Windows.Media.Brush)FindResource("ThemeAccentBrush");
                btn.Foreground = System.Windows.Media.Brushes.White;
            }
        }

        private void ResetRecordingButton(System.Windows.Controls.Button btn)
        {
            btn.ClearValue(System.Windows.Controls.Button.BackgroundProperty);
            btn.ClearValue(System.Windows.Controls.Button.ForegroundProperty);
            IsRecordingKey = false;
            if (btn.Tag is string propName && !string.IsNullOrEmpty(propName))
            {
                btn.Content = FormatDisplayHotkey(GetHotkeyStringFromConfig(propName));
            }
        }

        protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            if (_recordingButton != null)
            {
                e.Handled = true;

                System.Windows.Input.Key key = (e.Key == System.Windows.Input.Key.System) ? e.SystemKey : e.Key;
                if (key == System.Windows.Input.Key.LeftCtrl || key == System.Windows.Input.Key.RightCtrl ||
                    key == System.Windows.Input.Key.LeftShift || key == System.Windows.Input.Key.RightShift ||
                    key == System.Windows.Input.Key.LeftAlt || key == System.Windows.Input.Key.RightAlt ||
                    key == System.Windows.Input.Key.LWin || key == System.Windows.Input.Key.RWin)
                {
                    return;
                }

                if (key == System.Windows.Input.Key.Escape)
                {
                    ResetRecordingButton(_recordingButton);
                    _recordingButton = null;
                    return;
                }

                string formattedCombo = FormatKeyCombo(key, System.Windows.Input.Keyboard.Modifiers);
                _recordingButton.Content = formattedCombo;

                string propName = _recordingButton.Tag?.ToString() ?? "";
                SetHotkeyConfigValue(propName, formattedCombo);

                _recordingButton.ClearValue(System.Windows.Controls.Button.BackgroundProperty);
                _recordingButton.ClearValue(System.Windows.Controls.Button.ForegroundProperty);
                _recordingButton = null;
                return;
            }

            base.OnPreviewKeyDown(e);
        }

        public static string FormatKeyCombo(System.Windows.Input.Key key, System.Windows.Input.ModifierKeys modifiers)
        {
            string keyStr = key switch
            {
                System.Windows.Input.Key.Left => "←",
                System.Windows.Input.Key.Right => "→",
                System.Windows.Input.Key.Up => "↑",
                System.Windows.Input.Key.Down => "↓",
                System.Windows.Input.Key.OemMinus or System.Windows.Input.Key.Subtract => "-",
                System.Windows.Input.Key.OemPlus or System.Windows.Input.Key.Add => "=",
                System.Windows.Input.Key.Space => "Space",
                System.Windows.Input.Key.Return => "Enter",
                System.Windows.Input.Key.Tab => "Tab",
                System.Windows.Input.Key.Back => "Backspace",
                System.Windows.Input.Key.Delete => "Delete",
                System.Windows.Input.Key.Home => "Home",
                System.Windows.Input.Key.End => "End",
                System.Windows.Input.Key.PageUp => "PageUp",
                System.Windows.Input.Key.PageDown => "PageDown",
                System.Windows.Input.Key.Insert => "Insert",
                // OEM keys - map to actual keyboard characters
                System.Windows.Input.Key.OemOpenBrackets => "[",
                System.Windows.Input.Key.Oem6 => "]",
                System.Windows.Input.Key.OemSemicolon => ";",
                System.Windows.Input.Key.OemQuotes => "'",
                System.Windows.Input.Key.OemComma => ",",
                System.Windows.Input.Key.OemPeriod => ".",
                System.Windows.Input.Key.OemQuestion => "/",
                System.Windows.Input.Key.Oem3 => "`",
                System.Windows.Input.Key.OemBackslash => "\\",
                System.Windows.Input.Key.OemPipe => "|",
                System.Windows.Input.Key.Multiply => "*",
                System.Windows.Input.Key.Divide => "/",
                _ => key.ToString()
            };

            string prefix = "";
            if (modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control)) prefix += "Ctrl+";
            if (modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift)) prefix += "Shift+";
            if (modifiers.HasFlag(System.Windows.Input.ModifierKeys.Alt)) prefix += "Alt+";

            return prefix + keyStr;
        }

        private void BtnResetHotkeys_Click(object sender, RoutedEventArgs e)
        {
            SettingsService.Instance.Config.Hotkeys = new HotkeyConfig();
            LoadHotkeysUI();
        }

        private void BtnBrowseScreenshotPath_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFolderDialog();
                dialog.Title = "选择画面截图保存文件夹";
                if (!string.IsNullOrWhiteSpace(TxtScreenshotPath.Text) && System.IO.Directory.Exists(TxtScreenshotPath.Text))
                {
                    dialog.InitialDirectory = TxtScreenshotPath.Text;
                }
                if (dialog.ShowDialog() == true)
                {
                    TxtScreenshotPath.Text = dialog.FolderName;
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"无法打开文件夹选择对话框: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Closed?.Invoke(this, EventArgs.Empty);
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Closed?.Invoke(this, EventArgs.Empty);
        }

        private void ComboLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            if (ComboLanguage.SelectedItem is WpfComboBoxItem item && item.Tag is string lang)
            {
                _isLoading = true;
                try
                {
                    I18nService.Instance.ChangeLanguage(lang);
                    RefreshImageDurationItems();
                    RefreshSkinItems();
                    RefreshThemeItems();
                }
                finally
                {
                    _isLoading = false;
                }
            }
        }

        private void ComboSkin_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            if (ComboSkin.SelectedItem is WpfComboBoxItem item && item.Tag is string skinKey)
            {
                ThemeService.Instance.ActiveSkinKey = skinKey;
                UpdateSkinStatusUI();
            }
        }

        private void ComboTheme_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            if (ComboTheme.SelectedItem is WpfComboBoxItem item && item.Tag is string theme)
            {
                ThemeService.Instance.CurrentThemeKey = theme;
                UpdateSkinStatusUI();
            }
        }

        private void BtnExportSkinXaml_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var sfd = new Microsoft.Win32.SaveFileDialog
                {
                    Title = I18nService.Instance["SettingsExportSkinTitle"] ?? "导出 XAML 皮肤样式表 (Export Skin Stylesheet)",
                    Filter = "XAML 样式表 (*.xaml)|*.xaml|所有文件 (*.*)|*.*",
                    FileName = "skin.xaml",
                    DefaultExt = ".xaml"
                };

                if (sfd.ShowDialog() == true)
                {
                    string activeSkin = ThemeService.Instance.ActiveSkinKey;
                    if (ThemeService.Instance.ExportSkinXaml(sfd.FileName, activeSkin))
                    {
                        RefreshSkinItems();
                        string folder = System.IO.Path.GetDirectoryName(sfd.FileName) ?? "";
                        string msg = $"已成功导出完整的皮肤样式包 (skin.xaml & skin.json) 到:\n{folder}";

                        var result = System.Windows.MessageBox.Show($"{msg}\n\n是否立即在文件管理器中定位该文件夹？", "导出成功", MessageBoxButton.YesNo, MessageBoxImage.Information);
                        if (result == MessageBoxResult.Yes)
                        {
                            try
                            {
                                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{sfd.FileName}\"");
                            }
                            catch { }
                        }
                    }
                    else
                    {
                        System.Windows.MessageBox.Show("导出皮肤文件失败，请检查文件写入权限。", "导出失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"导出异常: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnImportSkin_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe)
            {
                var appRes = System.Windows.Application.Current?.Resources;
                var menuBg = appRes?["ThemeMenuBgBrush"] as System.Windows.Media.Brush ?? appRes?["ThemeWindowBgBrush"] as System.Windows.Media.Brush;
                var textBrush = appRes?["ThemeTextBrush"] as System.Windows.Media.Brush;
                var borderBrush = appRes?["ThemeBorderBrush"] as System.Windows.Media.Brush;
                var accentBrush = appRes?["ThemeAccentBrush"] as System.Windows.Media.Brush;
                var sheenBrush = appRes?["ThemeMenuSheenBrush"] as System.Windows.Media.Brush;
                var cornerRadius = appRes?["ThemePanelCornerRadius"] is CornerRadius cr ? cr : new CornerRadius(8);
                var borderThickness = appRes?["ThemeBorderThickness"] is Thickness bt ? bt : new Thickness(1);

                var menu = new ContextMenu
                {
                    PlacementTarget = fe,
                    Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
                    Background = System.Windows.Media.Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    BorderBrush = System.Windows.Media.Brushes.Transparent,
                    Padding = new Thickness(2),
                    HasDropShadow = false
                };

                // ── Custom ControlTemplate matching SmartFillContextMenuStyle ──
                var menuTemplate = new ControlTemplate(typeof(ContextMenu));
                var borderFactory = new FrameworkElementFactory(typeof(Border), "menuBorder");
                borderFactory.SetValue(Border.BackgroundProperty, menuBg);
                borderFactory.SetValue(Border.BorderBrushProperty, borderBrush);
                borderFactory.SetValue(Border.BorderThicknessProperty, borderThickness);
                borderFactory.SetValue(Border.CornerRadiusProperty, cornerRadius);
                borderFactory.SetValue(Border.PaddingProperty, new Thickness(2));

                var gridFactory = new FrameworkElementFactory(typeof(Grid));

                // Top specular sheen highlight
                if (sheenBrush != null && sheenBrush != System.Windows.Media.Brushes.Transparent)
                {
                    var sheenFactory = new FrameworkElementFactory(typeof(Border));
                    sheenFactory.SetValue(Border.VerticalAlignmentProperty, VerticalAlignment.Top);
                    sheenFactory.SetValue(Border.HeightProperty, 22.0);
                    sheenFactory.SetValue(Border.CornerRadiusProperty, cornerRadius);
                    sheenFactory.SetValue(Border.BackgroundProperty, sheenBrush);
                    sheenFactory.SetValue(Border.IsHitTestVisibleProperty, false);
                    gridFactory.AppendChild(sheenFactory);
                }

                var stackFactory = new FrameworkElementFactory(typeof(StackPanel));
                stackFactory.SetValue(StackPanel.IsItemsHostProperty, true);
                gridFactory.AppendChild(stackFactory);

                borderFactory.AppendChild(gridFactory);
                menuTemplate.VisualTree = borderFactory;
                menu.Template = menuTemplate;

                // ── Theme-styled MenuItems ──
                var hoverBg = accentBrush != null
                    ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x30,
                        ((System.Windows.Media.SolidColorBrush)accentBrush).Color.R,
                        ((System.Windows.Media.SolidColorBrush)accentBrush).Color.G,
                        ((System.Windows.Media.SolidColorBrush)accentBrush).Color.B))
                    : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x30, 255, 255, 255));
                if (hoverBg.CanFreeze) hoverBg.Freeze();

                var itemFolder = CreateThemedMenuItem("📁 选择皮肤文件夹 (含 skin.xaml / skin.json)", textBrush, hoverBg, cornerRadius);
                itemFolder.Click += (s, ev) => ImportSkinFromFolderDialog();

                var itemFile = CreateThemedMenuItem("📦 选择 .pkg / .zip 皮肤包文件导入", textBrush, hoverBg, cornerRadius);
                itemFile.Click += (s, ev) => ImportSkinFromFileDialog();

                menu.Items.Add(itemFolder);
                menu.Items.Add(itemFile);
                menu.IsOpen = true;
            }
        }

        /// <summary>
        /// Creates a themed MenuItem with custom ControlTemplate matching the app's SmartFillMenuItemStyle.
        /// </summary>
        private MenuItem CreateThemedMenuItem(string header, System.Windows.Media.Brush? textBrush,
            System.Windows.Media.Brush hoverBg, CornerRadius cornerRadius)
        {
            var mi = new MenuItem { Header = header, FontSize = 13.5 };

            var miTemplate = new ControlTemplate(typeof(MenuItem));
            var miBorder = new FrameworkElementFactory(typeof(Border), "Border");
            miBorder.SetValue(Border.BackgroundProperty, System.Windows.Media.Brushes.Transparent);
            miBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            miBorder.SetValue(Border.PaddingProperty, new Thickness(10, 7, 10, 7));
            miBorder.SetValue(Border.MarginProperty, new Thickness(2, 1, 2, 1));

            var miContent = new FrameworkElementFactory(typeof(ContentPresenter), "HeaderText");
            miContent.SetValue(ContentPresenter.ContentSourceProperty, "Header");
            miContent.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            miBorder.AppendChild(miContent);
            miTemplate.VisualTree = miBorder;

            // Triggers
            var mouseOverTrigger = new Trigger { Property = MenuItem.IsMouseOverProperty, Value = true };
            mouseOverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, hoverBg, "Border"));
            miTemplate.Triggers.Add(mouseOverTrigger);

            mi.Template = miTemplate;
            if (textBrush != null) mi.Foreground = textBrush;

            return mi;
        }

        private void ImportSkinFromFolderDialog()
        {
            try
            {
                var dlg = new Microsoft.Win32.OpenFolderDialog
                {
                    Title = "选择包含 skin.xaml 或 skin.json 的皮肤文件夹"
                };

                if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.FolderName))
                {
                    if (ThemeService.Instance.ImportSkinPackageFromFolder(dlg.FolderName, out string skinKey, out string errorMsg))
                    {
                        RefreshSkinItems();
                        RefreshThemeItems();
                        System.Windows.MessageBox.Show(
                            string.Format(I18nService.Instance["SettingsSkinImportSuccess"], skinKey),
                            "皮肤导入成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        System.Windows.MessageBox.Show(
                            string.Format(I18nService.Instance["SettingsSkinImportFailed"], errorMsg),
                            "导入失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"导入异常: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ImportSkinFromFileDialog()
        {
            try
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "选择皮肤包文件 (.pkg 或 .zip)",
                    Filter = "皮肤包 (*.pkg;*.zip)|*.pkg;*.zip|所有文件 (*.*)|*.*",
                    Multiselect = false
                };

                if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.FileName))
                {
                    if (ThemeService.Instance.ImportSkinPackageFromFile(dlg.FileName, out string skinKey, out string errorMsg))
                    {
                        RefreshSkinItems();
                        RefreshThemeItems();
                        System.Windows.MessageBox.Show(
                            string.Format(I18nService.Instance["SettingsSkinImportSuccess"], skinKey),
                            "皮肤导入成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        System.Windows.MessageBox.Show(
                            string.Format(I18nService.Instance["SettingsSkinImportFailed"], errorMsg),
                            "导入失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"导入异常: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnOpenSkinsFolder_Click(object sender, RoutedEventArgs e)
        {
            ThemeService.Instance.OpenSkinsFolder();
        }

        private void BtnRefreshSkins_Click(object sender, RoutedEventArgs e)
        {
            ThemeService.Instance.ScanAndLoadCustomSkins();
            RefreshSkinItems();
            RefreshThemeItems();
        }

        private void BtnOpenSysDefaults_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "ms-settings:defaultapps",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"无法打开系统设置：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnOpenSponsor_Click(object sender, RoutedEventArgs e)
        {
            SponsorRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
