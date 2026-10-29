using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Drawing;
using System.IO;
using Dapper;
using MySqlConnector;
using QRCoder;
using LmimaShopApp.Core.Sync;
using System.Linq;

namespace LmimaShopApp
{
    public partial class DeviceManagerWindow : Window
    {
        private readonly string _connectionString = SecurityHelper.GetConnectionString();

        public DeviceManagerWindow()
        {
            InitializeComponent();
            LoadDevices();
        }

        private async void LoadDevices()
        {
            try
            {
                using var conn = new MySqlConnection(_connectionString);
                var devices = await conn.QueryAsync<DeviceDto>(
                    "SELECT device_id AS DeviceId, name AS Name, last_seen AS LastSeen, revoked AS Revoked FROM devices");
                
                DevicesDataGrid.ItemsSource = devices.Select(d => new DeviceDisplay {
                    DeviceId = d.DeviceId,
                    Name = d.Name,
                    LastSeen = d.LastSeen?.ToString("yyyy-MM-dd HH:mm") ?? "لم يتصل بعد",
                    Status = d.Revoked == 1 ? "ملغى" : "نشط"
                }).ToList();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"خطأ في تحميل الأجهزة: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void RevokeDevice_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string deviceId)
            {
                var result = MessageBox.Show("هل أنت متأكد من إلغاء صلاحية هذا الجهاز؟ لن يتمكن من المزامنة بعد الآن.", "تأكيد", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.Yes)
                {
                    using var conn = new MySqlConnection(_connectionString);
                    await conn.ExecuteAsync("UPDATE devices SET revoked = 1 WHERE device_id = @Id", new { Id = deviceId });
                    MobileAuth.ClearTokenCache();
                    LoadDevices();
                }
            }
        }

        private void RefreshList_Click(object sender, RoutedEventArgs e)
        {
            LoadDevices();
        }

        private void ShowPairingQR_Click(object sender, RoutedEventArgs e)
        {
            string pairingCode = MobileAuth.RegeneratePairingCode();

            // Create payload for QR
            // Using a generic local IP, later we can add Tailscale IP here if active
            string localIp = MobileSyncHttpServer.LocalIp;
            int port = MobileSyncHttpServer.Port;

            var qrData = new {
                ips = new[] { $"http://{localIp}:{port}" },
                pairing_code = pairingCode
            };

            string json = System.Text.Json.JsonSerializer.Serialize(qrData);

            using (QRCodeGenerator qrGenerator = new QRCodeGenerator())
            using (QRCodeData qrCodeData = qrGenerator.CreateQrCode(json, QRCodeGenerator.ECCLevel.M))
            using (QRCode qrCode = new QRCode(qrCodeData))
            {
                Bitmap qrCodeImage = qrCode.GetGraphic(5);
                var window = new Window {
                    Title = "امسح الكود للاقتران",
                    Width = 400, Height = 450,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen
                };
                var stack = new StackPanel { Margin = new Thickness(20), HorizontalAlignment = HorizontalAlignment.Center };
                stack.Children.Add(new TextBlock { Text = $"امسح هذا الرمز باستخدام تطبيق الهاتف (صالح لـ {MobileAuth.PairingCodeLifetime.TotalMinutes:0} دقيقة)", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,10) });
                
                var img = new System.Windows.Controls.Image { Source = BitmapToImageSource(qrCodeImage), Width = 300, Height = 300 };
                stack.Children.Add(img);
                
                stack.Children.Add(new TextBlock { Text = $"رمز الاقتران: {pairingCode}", FontWeight = FontWeights.Bold, FontSize = 20, Margin = new Thickness(0,10,0,0), HorizontalAlignment = HorizontalAlignment.Center });
                window.Content = stack;
                window.ShowDialog();
            }
            LoadDevices(); // reload just in case
        }

        private BitmapImage BitmapToImageSource(Bitmap bitmap)
        {
            using (MemoryStream memory = new MemoryStream())
            {
                bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Png);
                memory.Position = 0;
                BitmapImage bitmapimage = new BitmapImage();
                bitmapimage.BeginInit();
                bitmapimage.StreamSource = memory;
                bitmapimage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapimage.EndInit();
                return bitmapimage;
            }
        }
    }

    public class DeviceDto
    {
        public string DeviceId { get; set; }
        public string Name { get; set; }
        public DateTime? LastSeen { get; set; }
        public int Revoked { get; set; }
    }

    public class DeviceDisplay
    {
        public string DeviceId { get; set; }
        public string Name { get; set; }
        public string LastSeen { get; set; }
        public string Status { get; set; }
    }
}

