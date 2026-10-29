namespace LmimaShopV2.Desktop.Core.Models;

public sealed class SettingsRow
{
    public string SettingKey { get; set; } = string.Empty;
    public string? SettingValue { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class ShopInfo
{
    public string Name { get; set; } = "LmimaShop";
    public string Ice { get; set; } = string.Empty;
    public string Rc { get; set; } = string.Empty;
    public string FiscalIf { get; set; } = string.Empty;
    public string Patente { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Footer { get; set; } = string.Empty;
}

public sealed class StoreSettings
{
    public decimal TvaRate { get; set; } = 20m;
    public bool PricesIncludeTva { get; set; } = true;
    public decimal LoyaltyMadPerPoint { get; set; } = 10m;
    public ShopInfo Shop { get; set; } = new();
}

public sealed class PairingCodeInfo
{
    public string Code { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public string Url { get; set; } = string.Empty;
}
