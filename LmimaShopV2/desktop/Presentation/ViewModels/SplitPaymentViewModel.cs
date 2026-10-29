using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LmimaShopV2.Desktop.Core.Models;
using LmimaShopV2.Desktop.Helpers;
using LmimaShopV2.Desktop.Presentation.Services;

namespace LmimaShopV2.Desktop.Presentation.ViewModels;

public sealed partial class SplitPaymentViewModel : ObservableObject
{
    public SplitPaymentViewModel(PaymentDialogContext context)
    {
        Total = context.Total;
        CustomerName = context.CustomerName;
        AvailableCredit = context.AvailableCredit;
        _cash = context.Total;
        _credit = 0m;
    }

    public event EventHandler<bool>? CloseRequested;

    public decimal Total { get; }
    public string? CustomerName { get; }
    public decimal AvailableCredit { get; }
    public bool HasCustomer => !string.IsNullOrWhiteSpace(CustomerName);
    public string TotalText => MoneyHelper.FormatMad(Total);
    public string CustomerText => HasCustomer ? CustomerName! : "بيع بدون عميل";
    public string CreditText => !HasCustomer
        ? "البيع الآجل يتطلب اختيار عميل من الشاشة الرئيسية."
        : AvailableCredit >= decimal.MaxValue / 2
            ? "حد الائتمان: غير محدود"
            : $"الائتمان المتاح: {MoneyHelper.FormatMad(AvailableCredit)}";

    [ObservableProperty]
    private decimal _cash;

    [ObservableProperty]
    private decimal _card;

    [ObservableProperty]
    private decimal _credit;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    public decimal Remaining => MoneyHelper.Round(Math.Max(0m, Total - Cash - Card - Credit));
    public decimal Change => MoneyHelper.Round(Math.Max(0m, Cash + Card + Credit - Total));
    public string RemainingText => MoneyHelper.FormatMad(Remaining);
    public string ChangeText => MoneyHelper.FormatMad(Change);

    public PaymentSelection Selection { get; private set; } = new();

    partial void OnCashChanged(decimal value) => Recalculate();
    partial void OnCardChanged(decimal value) => Recalculate();
    partial void OnCreditChanged(decimal value) => Recalculate();

    [RelayCommand]
    private void PayRemainingOnCredit()
    {
        if (!HasCustomer)
        {
            ErrorMessage = CreditText;
            return;
        }

        var remaining = MoneyHelper.Round(Math.Max(0m, Total - Cash - Card));
        if (remaining <= 0)
        {
            ErrorMessage = "لا يوجد مبلغ متبقٍ للبيع الآجل.";
            return;
        }

        if (AvailableCredit < remaining - 0.009m)
        {
            ErrorMessage = $"المبلغ المتبقي يتجاوز الائتمان المتاح ({MoneyHelper.FormatMad(AvailableCredit)}).";
            return;
        }

        Credit = remaining;
        ErrorMessage = string.Empty;
    }

    [RelayCommand]
    private void Confirm()
    {
        if (Cash < 0 || Card < 0 || Credit < 0)
        {
            ErrorMessage = "المبالغ لا يمكن أن تكون سالبة.";
            return;
        }

        if (Card > Total + 0.009m)
        {
            ErrorMessage = "الدفع بالبطاقة لا يمكن أن يتجاوز المجموع.";
            return;
        }

        var dueAfterCashAndCard = MoneyHelper.Round(Math.Max(0m, Total - Cash - Card));
        if (Credit > dueAfterCashAndCard + 0.009m)
        {
            ErrorMessage = "المبلغ الآجل يتجاوز المتبقي بعد الدفع النقدي والبطاقة.";
            return;
        }

        if (Credit < dueAfterCashAndCard - 0.009m)
        {
            ErrorMessage = "المبلغ المتبقي يجب دفعه نقدًا أو بالبطاقة أو تسجيله آجلًا.";
            return;
        }

        if (HasCustomer && AvailableCredit < Credit - 0.009m)
        {
            ErrorMessage = $"الائتمان المتاح غير كافٍ ({MoneyHelper.FormatMad(AvailableCredit)}).";
            return;
        }

        Selection = new PaymentSelection
        {
            Cash = MoneyHelper.Round(Cash),
            Card = MoneyHelper.Round(Card),
            Credit = MoneyHelper.Round(Credit)
        };
        CloseRequested?.Invoke(this, true);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, false);

    private void Recalculate()
    {
        OnPropertyChanged(nameof(Remaining));
        OnPropertyChanged(nameof(Change));
        OnPropertyChanged(nameof(RemainingText));
        OnPropertyChanged(nameof(ChangeText));
    }
}
