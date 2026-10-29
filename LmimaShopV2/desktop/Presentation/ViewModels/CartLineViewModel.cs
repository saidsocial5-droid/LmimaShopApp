using CommunityToolkit.Mvvm.ComponentModel;
using LmimaShopV2.Desktop.Core.Models;
using LmimaShopV2.Desktop.Helpers;

namespace LmimaShopV2.Desktop.Presentation.ViewModels;

public sealed partial class CartLineViewModel : ObservableObject
{
    public CartLineViewModel(Product product, decimal quantity = 1m)
    {
        Product = product;
        _qty = quantity;
        _unitPrice = product.SellPrice;
    }

    public Product Product { get; }
    public string ProductUuid => Product.Uuid;
    public string Name => Product.Name;
    public string? Barcode => Product.Barcode;
    public string Unit => Product.IsWeighed ? "كغ" : "قطعة";

    [ObservableProperty]
    private decimal _qty;

    [ObservableProperty]
    private decimal _unitPrice;

    [ObservableProperty]
    private decimal _discountPct;

    public decimal LineTotal => MoneyHelper.Round(Qty * UnitPrice * (1m - DiscountPct / 100m));

    partial void OnQtyChanged(decimal value) => Recalculate();
    partial void OnUnitPriceChanged(decimal value) => Recalculate();
    partial void OnDiscountPctChanged(decimal value) => Recalculate();

    private void Recalculate()
    {
        OnPropertyChanged(nameof(LineTotal));
        OnPropertyChanged(nameof(DisplayTotal));
    }

    public string DisplayTotal => MoneyHelper.FormatMad(LineTotal);
}
