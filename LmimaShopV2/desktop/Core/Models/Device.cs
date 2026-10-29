namespace LmimaShopV2.Desktop.Core.Models;

public sealed class Device
{
    public long Id { get; set; }
    public string Uuid { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public DateTime PairedAt { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}
