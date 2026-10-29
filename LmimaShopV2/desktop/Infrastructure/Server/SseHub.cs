using System.Collections.Concurrent;
using System.Threading.Channels;
using LmimaShopV2.Desktop.Core.Interfaces;

namespace LmimaShopV2.Desktop.Infrastructure.Server;

public sealed record SseSubscription(Guid Id, ChannelReader<string> Reader);

/// <summary>Fan-out hub for Server-Sent Events. One bounded queue per connected client.</summary>
public sealed class SseHub : ISseHub
{
    private readonly ConcurrentDictionary<Guid, Channel<string>> _clients = new();

    public int ClientCount => _clients.Count;

    public SseSubscription Subscribe()
    {
        var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
        var id = Guid.NewGuid();
        _clients[id] = channel;
        return new SseSubscription(id, channel.Reader);
    }

    public void Unsubscribe(Guid id)
    {
        if (_clients.TryRemove(id, out var channel))
            channel.Writer.TryComplete();
    }

    public void BroadcastDataChanged()
    {
        var payload = $"data: {{\"type\":\"data_changed\",\"at\":\"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}\"}}\n\n";
        foreach (var pair in _clients)
        {
            if (!pair.Value.Writer.TryWrite(payload))
            {
                _clients.TryRemove(pair.Key, out _);
                pair.Value.Writer.TryComplete();
            }
        }
    }
}
