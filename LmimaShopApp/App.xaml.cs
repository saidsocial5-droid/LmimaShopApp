using System.Configuration;
using System.Data;
using System.Windows;
using Serilog;

namespace LmimaShopApp
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public App()
        {
            // Configure Serilog
            Serilog.Log.Logger = new Serilog.LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "lmimashop-.log"),
                              rollingInterval: Serilog.RollingInterval.Day,
                              retainedFileCountLimit: 30)
                .CreateLogger();

            Serilog.Log.Information("--- بدء تشغيل تطبيق LmimaShop ---");

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                var ex = args.ExceptionObject as Exception;
                Serilog.Log.Fatal(ex, "AppDomain UnhandledException");
                MessageBox.Show($"خطأ غير متوقع في النظام:\n\n{ex?.Message}", "خطأ فادح", MessageBoxButton.OK, MessageBoxImage.Error);
            };
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            // إعداد مفتاح قراءة التحديثات لمرة واحدة: LmimaShopApp.exe --set-update-token <TOKEN>
            int tokenArg = Array.IndexOf(e.Args, "--set-update-token");
            if (tokenArg >= 0 && tokenArg + 1 < e.Args.Length)
            {
                UpdateService.SaveToken(e.Args[tokenArg + 1]);
                MessageBox.Show("تم حفظ مفتاح التحديث التلقائي بشكل مشفر على هذا الحاسوب.", "LMIMA SHOP", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            Serilog.Log.Information("LmimaShop version {Version}", AppVersion.Current);
            AppHost.Initialize();
            
            this.DispatcherUnhandledException += (s, args) =>
            {
                Serilog.Log.Fatal(args.Exception, "DispatcherUnhandledException");
                MessageBox.Show($"خطأ غير متوقع في الواجهة:\n\n{args.Exception.Message}", "خطأ فادح", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };

            TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                Serilog.Log.Error(args.Exception, "UnobservedTaskException");
                args.SetObserved();
            };

            LoadAndApplyFont();
            try
            {
                MobileSyncHttpServer.Start(8080);
                _ = SignalRServer.StartAsync(8081);
                CloudSyncService.StartGoogleDriveWatcher();
                UpdateService.StartBackgroundChecks();
                Task.Run(async () =>
                {
                    try
                    {
                        CloudSyncService.ImportPendingImagesFromGoogleDrive();
                        await CloudSyncService.SyncMobileDataAsync(force: true);
                    }
                    catch (Exception ex) { Serilog.Log.Error(ex, "Initial cloud sync failed"); }
                });
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Failed starting background services (mobile server / sync / updates)");
            }
            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (AppHost.Host == null)
            {
                // البرنامج لم يبدأ خدماته (مثلاً عند حفظ مفتاح التحديث فقط)
                base.OnExit(e);
                return;
            }

            try
            {
                MobileSyncHttpServer.Stop();
                _ = SignalRServer.StopAsync();
                BackupService.PerformAutoBackupIfConfigured();
                CustomerGoogleSheetService.SyncCustomersSheet(true, false, out _);
                CloudSyncService.SyncMobileData(force: true);
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Error during shutdown tasks");
            }
            Serilog.Log.CloseAndFlush();
            base.OnExit(e);
        }

        private void LoadAndApplyFont()
        {
            try
            {
                string settingsFilePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
                if (System.IO.File.Exists(settingsFilePath))
                {
                    string json = System.IO.File.ReadAllText(settingsFilePath);
                    using (var doc = System.Text.Json.JsonDocument.Parse(json))
                    {
                        var root = doc.RootElement;
                        if (root.TryGetProperty("SelectedFont", out var fontProp))
                        {
                            string fontName = fontProp.GetString() ?? "Cairo";
                            FontHelper.ApplyFont(fontName);
                            return;
                        }
                    }
                }
            }
            catch
            {
                // Fallback silently
            }
            FontHelper.ApplyFont("Cairo"); // Default fallback
        }
    }

}




