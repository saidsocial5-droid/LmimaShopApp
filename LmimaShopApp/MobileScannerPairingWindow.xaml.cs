using System;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using QRCoder;
using LmimaShopApp.Core.Sync;

namespace LmimaShopApp
{
    public partial class MobileScannerPairingWindow : Window
    {
        public MobileScannerPairingWindow()
        {
            InitializeComponent();
            Loaded += MobileScannerPairingWindow_Loaded;
        }

        private void MobileScannerPairingWindow_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshDisplay();
        }

        private void RefreshDisplay()
        {
            string ip = MobileSyncHttpServer.LocalIp;
            int port = MobileSyncHttpServer.Port;

            txtServerUrl.Text = $"http://{ip}:{port}";
            txtPairingCode.Text = MobileAuth.EnsureFreshPairingCode();

            bool running = MobileSyncHttpServer.IsRunning;
            lblServerStatus.Text = running ? "🟢 الخادم يعمل بنشاط" : "🔴 الخادم متوقف";
            borderServerBadge.Background = running ? 
                new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(222, 247, 236)) : 
                new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(254, 226, 226));
            lblServerStatus.Foreground = running ? 
                new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(3, 84, 63)) : 
                new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(153, 27, 27));

            var ips = MobileSyncHttpServer.GetAllLocalIps();
            cmbAlternativeIps.Items.Clear();
            foreach (var itemIp in ips)
            {
                cmbAlternativeIps.Items.Add(new ComboBoxItem { Content = $"كارت الشبكة: {itemIp}:{port}", Tag = itemIp });
            }
            if (cmbAlternativeIps.Items.Count > 0) cmbAlternativeIps.SelectedIndex = 0;

            GenerateQrCode(ip, port, txtPairingCode.Text.Trim());
        }

        private void GenerateQrCode(string ip, int port, string pairingCode)
        {
            try
            {
                var qrPayload = new
                {
                    server = $"{ip}:{port}",
                    url = $"http://{ip}:{port}",
                    pairing_code = pairingCode,
                    ips = new[] { $"http://{ip}:{port}" }
                };

                string json = JsonSerializer.Serialize(qrPayload);

                using (var qrGenerator = new QRCodeGenerator())
                using (var qrCodeData = qrGenerator.CreateQrCode(json, QRCodeGenerator.ECCLevel.M))
                using (var qrCode = new QRCode(qrCodeData))
                {
                    using (Bitmap bmp = qrCode.GetGraphic(5))
                    {
                        imgQrCode.Source = BitmapToImageSource(bmp);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error generating QR: {ex.Message}");
            }
        }

        private BitmapImage BitmapToImageSource(Bitmap bitmap)
        {
            using (MemoryStream memory = new MemoryStream())
            {
                bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Bmp);
                memory.Position = 0;
                BitmapImage bitmapimage = new BitmapImage();
                bitmapimage.BeginInit();
                bitmapimage.StreamSource = memory;
                bitmapimage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapimage.EndInit();
                return bitmapimage;
            }
        }

        private void CmbAlternativeIps_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbAlternativeIps.SelectedItem is ComboBoxItem item && item.Tag is string selectedIp)
            {
                int port = MobileSyncHttpServer.Port;
                txtServerUrl.Text = $"http://{selectedIp}:{port}";
                GenerateQrCode(selectedIp, port, txtPairingCode.Text.Trim());
            }
        }

        private void BtnCopyUrl_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(txtServerUrl.Text);
            MessageBox.Show($"تم نسخ العنوان بنجاح:\n{txtServerUrl.Text}", "تم النسخ", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnNewPairingCode_Click(object sender, RoutedEventArgs e)
        {
            string newCode = MobileAuth.RegeneratePairingCode();
            txtPairingCode.Text = newCode;

            string ip = MobileSyncHttpServer.LocalIp;
            if (cmbAlternativeIps.SelectedItem is ComboBoxItem item && item.Tag is string selectedIp) ip = selectedIp;
            GenerateQrCode(ip, MobileSyncHttpServer.Port, newCode);
        }

        private void BtnSavePairingCode_Click(object sender, RoutedEventArgs e)
        {
            string code = txtPairingCode.Text.Trim();
            try
            {
                MobileAuth.SetPairingCode(code);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string ip = MobileSyncHttpServer.LocalIp;
            if (cmbAlternativeIps.SelectedItem is ComboBoxItem item && item.Tag is string selectedIp) ip = selectedIp;
            GenerateQrCode(ip, MobileSyncHttpServer.Port, code);

            MessageBox.Show($"تم حفظ رمز الاقتران الجديد بنجاح:\n{code}", "تم الحفظ", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnBroadcastSync_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _ = SseManager.BroadcastEventAsync("changed", DateTime.UtcNow.ToString("O"));
                _ = SignalRServer.BroadcastSyncToClientsAsync();
                CloudSyncService.SyncMobileData(force: true);

                MessageBox.Show("تم بث إشارة تحيين وتحديث فوري لجميع الهواتف المتصلة بالمتجر بنجاح! ⚡", "تحيين فوري", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"حدث خطأ أثناء بث التحيين: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnRestartServer_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                MobileSyncHttpServer.Restart();
                _ = SignalRServer.StartAsync(8081);
                RefreshDisplay();
                MessageBox.Show("تمت إعادة تشغيل خادم الاتصال بالهاتف بنجاح!", "إعادة التشغيل", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"حدث خطأ أثناء إعادة تشغيل الخادم: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnOpenFirewall_Click(object sender, RoutedEventArgs e)
        {
            bool ok = MobileSyncHttpServer.OpenWindowsFirewall();
            if (ok)
            {
                MessageBox.Show("تم فتح جدار حماية ويندوز (Windows Firewall) للمنافذ 8080 و 8081 بنجاح!\nيمكن لهاتفك الآن الاتصال دون أي قيود أمنية من الويندوز.", "جدار الحماية", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("تعذر فتح جدار الحماية تلقائياً. يرجى تشغيل برنامج Install_Service_And_Firewall.bat كمسؤول (Run as administrator).", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnManageDevices_Click(object sender, RoutedEventArgs e)
        {
            var dm = new DeviceManagerWindow { Owner = this };
            dm.ShowDialog();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
