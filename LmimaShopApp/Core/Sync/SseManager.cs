using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace LmimaShopApp.Core.Sync
{
    public static class SseManager
    {
        private static readonly ConcurrentDictionary<Guid, SseClient> _clients = new();
        private static CancellationTokenSource? _heartbeatCts;

        public static void StartHeartbeat()
        {
            if (_heartbeatCts != null) return;
            _heartbeatCts = new CancellationTokenSource();
            
            Task.Run(async () =>
            {
                while (!_heartbeatCts.Token.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(20), _heartbeatCts.Token);
                    await BroadcastRawAsync(": heartbeat\n\n");
                }
            });
        }

        public static void StopHeartbeat()
        {
            _heartbeatCts?.Cancel();
            _heartbeatCts = null;
        }

        public static async Task AddClientAsync(Guid clientId, Stream stream, CancellationToken clientDisconnectToken)
        {
            var client = new SseClient { Stream = stream };
            _clients.TryAdd(clientId, client);
            Log.Information($"SSE Client connected: {clientId}");

            try
            {
                // Send immediate connection success event
                await SendEventAsync(client, "connected", "true");
                
                // Wait until client disconnects or server stops
                await Task.Delay(-1, clientDisconnectToken);
            }
            catch (TaskCanceledException)
            {
                // Disconnected naturally
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"SSE Client error: {clientId}");
            }
            finally
            {
                _clients.TryRemove(clientId, out _);
                Log.Information($"SSE Client disconnected: {clientId}");
            }
        }

        public static async Task BroadcastEventAsync(string eventType, string cursorData)
        {
            string payload = $"{{\"type\":\"{eventType}\", \"cursor\":\"{cursorData}\"}}";
            string sseMessage = $"event: {eventType}\ndata: {payload}\n\n";
            await BroadcastRawAsync(sseMessage);
        }

        private static async Task BroadcastRawAsync(string message)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(message);
            foreach (var kvp in _clients)
            {
                try
                {
                    if (kvp.Value.Stream.CanWrite)
                    {
                        await kvp.Value.Stream.WriteAsync(buffer, 0, buffer.Length);
                        await kvp.Value.Stream.FlushAsync();
                    }
                    else
                    {
                        _clients.TryRemove(kvp.Key, out _);
                    }
                }
                catch
                {
                    _clients.TryRemove(kvp.Key, out _);
                }
            }
        }

        private static async Task SendEventAsync(SseClient client, string eventType, string data)
        {
            if (!client.Stream.CanWrite) return;
            string sseMessage = $"event: {eventType}\ndata: {data}\n\n";
            byte[] buffer = Encoding.UTF8.GetBytes(sseMessage);
            await client.Stream.WriteAsync(buffer, 0, buffer.Length);
            await client.Stream.FlushAsync();
        }

        private class SseClient
        {
            public Stream Stream { get; set; }
        }
    }
}
