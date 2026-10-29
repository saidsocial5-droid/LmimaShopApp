namespace LmimaShopV2.Desktop.Core.Models;

public sealed class Product
{
    public long Id { get; set; }
    public string Uuid { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public string Name { get; set; } = string.Empty;
    public long? CategoryId { get; set; }
    public string? Category { get; set; }
    public decimal CostPrice { get; set; }
    public decimal SellPrice { get; set; }
    public decimal StockQuantity { get; set; }
    public decimal TvaRate { get; set; } = 20m;
    public bool IsWeighed { get; set; }
    public string? ImagePath { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int Version { get; set; }
}

public sealed class Category
{
    public long Id { get; set; }
    public string Uuid { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int Version { get; set; }
}

public sealed class CartItem
{
    public CartItem(Product product, decimal qty = 1m)
    {
        Product = product;
        Qty = qty;
        UnitPrice = product.SellPrice;
    }

    public Product Product { get; }
    public decimal Qty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountPct { get; set; }

    public string ProductUuid => Product.Uuid;
    public string Name => Product.Name;
    public string? Barcode => Product.Barcode;

    public decimal LineTotal => Math.Round(
        Qty * UnitPrice * (1m - (DiscountPct / 100m)), 2, MidpointRounding.AwayFromZero);
}
