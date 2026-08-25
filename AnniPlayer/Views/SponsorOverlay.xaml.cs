using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using AnniPlayer.Models;
using AnniPlayer.Services;

using WpfUserControl = System.Windows.Controls.UserControl;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfRadioButton = System.Windows.Controls.RadioButton;
using WpfTextBlock = System.Windows.Controls.TextBlock;

namespace AnniPlayer.Views
{
    public partial class SponsorOverlay : WpfUserControl
    {
        public event EventHandler? CloseRequested;

        private DonationConfig _config;
        private DispatcherTimer? _toastTimer;
        private int _selectedNetworkIndex = 0;

        public SponsorOverlay()
        {
            InitializeComponent();
            _config = DonationService.Instance.Config;
            this.KeyDown += SponsorOverlay_KeyDown;
        }

        private async void UserControl_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if ((bool)e.NewValue)
            {
                // Instantly load current/default
                _config = DonationService.Instance.Config;
                UpdateLanguageAndContent();
                RenderNetworkTabs();
                UpdateSelectedNetwork();
                this.Focus();
                Keyboard.Focus(this);

                // Asynchronously fetch latest remote donation.json from web in background
                try
                {
                    var updatedConfig = await DonationService.Instance.FetchConfigAsync();
                    if (updatedConfig != null)
                    {
                        _config = updatedConfig;
                        UpdateLanguageAndContent();
                        RenderNetworkTabs();
                        UpdateSelectedNetwork();
                    }
                }
                catch { }
            }
            else
            {
                if (txtCopyToast != null) txtCopyToast.Visibility = Visibility.Collapsed;
            }
        }

        private void SponsorOverlay_KeyDown(object sender, WpfKeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        }

        private void Backdrop_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource == sender)
            {
                Close();
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        public void Close()
        {
            this.Visibility = Visibility.Collapsed;
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        private void UpdateLanguageAndContent()
        {
            bool isEn = I18nService.Instance.CurrentLanguage == "en-US";
            txtTitle.Text = isEn ? _config.TitleEn : _config.TitleZh;
            txtDescription.Text = isEn ? _config.DescriptionEn : _config.DescriptionZh;
        }

        private void RenderNetworkTabs()
        {
            if (panelNetworkTabs == null) return;
            panelNetworkTabs.Children.Clear();

            if (_config.Networks == null || _config.Networks.Count == 0) return;

            if (_selectedNetworkIndex >= _config.Networks.Count)
            {
                _selectedNetworkIndex = 0;
            }

            for (int i = 0; i < _config.Networks.Count; i++)
            {
                var net = _config.Networks[i];
                int index = i;

                var rb = new WpfRadioButton
                {
                    GroupName = "DonationNetworkGroup",
                    IsChecked = (i == _selectedNetworkIndex),
                    Margin = new Thickness(0, 0, 10, 0),
                    Padding = new Thickness(16, 0, 16, 0),
                    Style = (System.Windows.Style)this.FindResource("ThemeTabButtonStyle")
                };

                var tb = new WpfTextBlock
                {
                    Text = net.Name,
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center
                };

                rb.Content = tb;
                rb.Checked += (s, e) =>
                {
                    _selectedNetworkIndex = index;
                    UpdateSelectedNetwork();
                };

                panelNetworkTabs.Children.Add(rb);
            }
        }

        private void UpdateSelectedNetwork()
        {
            if (txtAddress == null || imgQrCode == null) return;
            if (_config.Networks == null || _config.Networks.Count == 0) return;

            if (_selectedNetworkIndex < 0 || _selectedNetworkIndex >= _config.Networks.Count)
            {
                _selectedNetworkIndex = 0;
            }

            bool isEn = I18nService.Instance.CurrentLanguage == "en-US";
            var net = _config.Networks[_selectedNetworkIndex];

            txtAddress.Text = net.Address;
            txtNetworkNote.Text = isEn ? net.NoteEn : net.NoteZh;

            try
            {
                var qrBitmap = QrCodeService.GenerateQrBitmap(net.Address, pixelSize: 280, quietZone: 3);
                imgQrCode.Source = qrBitmap;
            }
            catch { }

            if (txtCopyToast != null) txtCopyToast.Visibility = Visibility.Collapsed;
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string address = txtAddress.Text;
                if (!string.IsNullOrWhiteSpace(address))
                {
                    System.Windows.Clipboard.SetText(address);

                    txtCopyToast.Text = I18nService.Instance.CurrentLanguage == "en-US"
                        ? "✅ Wallet address copied to clipboard!"
                        : "✅ 钱包地址已成功复制到剪贴板！";
                    txtCopyToast.Visibility = Visibility.Visible;

                    _toastTimer?.Stop();
                    _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
                    _toastTimer.Tick += (s, args) =>
                    {
                        txtCopyToast.Visibility = Visibility.Collapsed;
                        _toastTimer?.Stop();
                    };
                    _toastTimer.Start();
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"复制失败：{ex.Message}", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnWebsite_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string url = !string.IsNullOrEmpty(_config.WebsiteUrl) ? _config.WebsiteUrl : "https://aniplayer.ai.studio/";
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }
}
