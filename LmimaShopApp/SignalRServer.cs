using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Cors.Infrastructure;

namespace LmimaShopApp
{
    public class BarcodeHub : Hub
    {
        // Mobile app will send scanned barcode to WPF
        public async Task SendBarcode(string barcode)
        {
            if (string.IsNullOrWhiteSpace(barcode)) return;
            if (barcode.Equals("SYNC_REFRESH", StringComparison.OrdinalIgnoreCase) ||
                barcode.Equals("REFRESH", StringComparison.OrdinalIgnoreCase) ||
                barcode.Equals("PING", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            SignalRServer.NotifyBarcodeReceived(barcode);
            await Clients.All.SendAsync("ReceiveBarcode", barcode);
        }

        // Broadcast sync to all connected devices
        public async Task RequestSync()
        {
            await Clients.All.SendAsync("SyncNow");
        }
    }

    public static class SignalRServer
    {
        private static WebApplication? _app;
        public static event Action<string>? OnBarcodeReceived;

        internal static void NotifyBarcodeReceived(string barcode)
        {
            OnBarcodeReceived?.Invoke(barcode);
        }

        public static async Task BroadcastSyncToClientsAsync()
        {
            try
            {
                if (_app != null)
                {
                    var hubContext = _app.Services.GetService<IHubContext<BarcodeHub>>();
                    if (hubContext != null)
                    {
                        await hubContext.Clients.All.SendAsync("SyncNow");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SignalRServer] BroadcastSync error: {ex.Message}");
            }
        }

        public static async Task StartAsync(int port = 8081)
        {
            if (_app != null) return;

            var builder = WebApplication.CreateBuilder();
            
            builder.Services.AddSignalR();
            builder.Services.AddCors(options =>
            {
                options.AddDefaultPolicy(policy =>
                {
                    policy.AllowAnyHeader()
                          .AllowAnyMethod()
                          .SetIsOriginAllowed(_ => true)
                          .AllowCredentials();
                });
            });

            builder.WebHost.UseUrls($"http://*:{port}");

            _app = builder.Build();

            _app.UseCors();
            _app.MapHub<BarcodeHub>("/barcodeHub");

            await _app.StartAsync();
            System.Diagnostics.Debug.WriteLine($"[SignalRServer] Started on port {port}");
        }

        public static async Task StopAsync()
        {
            if (_app != null)
            {
                await _app.StopAsync();
                await _app.DisposeAsync();
                _app = null;
            }
        }
    }
}
