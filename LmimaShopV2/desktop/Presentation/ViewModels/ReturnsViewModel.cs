using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LmimaShopV2.Desktop.Core.Interfaces;
using LmimaShopV2.Desktop.Core.Models;
using LmimaShopV2.Desktop.Helpers;
using LmimaShopV2.Desktop.Presentation.Services;

namespace LmimaShopV2.Desktop.Presentation.ViewModels;

public sealed partial class ReturnLineViewModel : ObservableObject
{
    public ReturnLineViewModel(ReturnableLine line)
    {
        Source = line;
        _quantity = 0m;
    }

    public ReturnableLine Source { get; }
    public string ProductName => Source.ProductName;
    public decimal PurchasedQty => Source.Qty;
    public decimal ReturnedQty => Source.ReturnedQty;
    public decimal ReturnableQty => Source.ReturnableQty;
    public decimal UnitPrice => Source.UnitPrice;

    [ObservableProperty]
    private decimal _quantity;

    public decimal LineRefund => MoneyHelper.Round(Quantity * UnitPrice);
    public string MaxText => $"{ReturnableQty:0.###}";

    partial void OnQuantityChanged(decimal value) => OnPropertyChanged(nameof(LineRefund));
}

public sealed partial class ReturnsViewModel : ObservableObject
{
    private readonly ISalesService _sales;
    private readonly IReturnsService _returns;
    private readonly ISessionService _session;
    private readonly IDialogService _dialogs;

    public ReturnsViewModel(
        ISalesService sales,
        IReturnsService returns,
        ISessionService session,
        IDialogService dialogs)
    {
        _sales = sales;
        _returns = returns;
        _session = session;
        _dialogs = dialogs;
    }

    public ObservableCollection<SaleDetail> Sales { get; } = [];
    public ObservableCollection<ReturnLineViewModel> Lines { get; } = [];
    public ObservableCollection<ReturnListItem> RecentReturns { get; } = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private SaleDetail? _selectedSale;

    [ObservableProperty]
    private string _reason = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "ابحث برقم الفاتورة أو اختر عملية بيع حديثة.";

    [ObservableProperty]
    private bool _isError;

    [ObservableProperty]
    private bool _isBusy;

    public decimal RefundPreview => MoneyHelper.Round(Lines.Sum(l => l.LineRefund));
    public string RefundPreviewText => MoneyHelper.FormatMad(RefundPreview);
    public int SelectedCount => Lines.Count(l => l.Quantity > 0m);

    public async Task InitializeAsync()
    {
        await Task.WhenAll(LoadRecentSalesAsync(), LoadRecentReturnsAsync());
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        try
        {
            IsBusy = true;
            var term = SearchText.Trim();
            var list = term.Length == 0
                ? await _sales.GetRecentAsync()
                : await _sales.SearchAsync(term);

            Sales.Clear();
            foreach (var sale in list) Sales.Add(sale);
            SelectedSale = null;
            SetStatus(list.Count == 0 ? "لا توجد نتائج." : $"تم إيجاد {list.Count} عملية بيع.");
        }
        catch (Exception ex) when (ex is DomainException or InvalidOperationException)
        {
            SetStatus(ex.Message, true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ReloadAsync()
    {
        await Task.WhenAll(LoadRecentSalesAsync(), LoadRecentReturnsAsync());
    }

    [RelayCommand]
    private async Task SubmitReturnAsync()
    {
        if (SelectedSale is null)
        {
            SetStatus("اختر عملية بيع أولًا.", true);
            return;
        }

        var items = Lines
            .Where(l => l.Quantity > 0m)
            .Select(l => new ReturnLineRequest { SaleItemUuid = l.Source.SaleItemUuid, Qty = l.Quantity })
            .ToList();
        if (items.Count == 0)
        {
            SetStatus("حدد الكميات المراد إرجاعها.", true);
            return;
        }

        if (!_dialogs.Confirm($"تأكيد الإرجاع بقيمة تقريبية {MoneyHelper.FormatMad(RefundPreview)}؟")) return;

        try
        {
            IsBusy = true;
            var result = await _returns.ReturnAsync(
                new ReturnRequest
                {
                    OpId = Guid.NewGuid().ToString(),
                    SaleUuid = SelectedSale.Uuid,
                    Reason = Reason,
                    Items = items,
                    Cashier = _session.CurrentUser?.FullName
                },
                new ReturnContext(_session.CurrentUser?.FullName ?? "Caisse", _session.CurrentUser?.Id));

            _dialogs.ShowInfo(
                $"تم تسجيل الإرجاع.\n\nرقم العملية: {result.ReturnUuid[..8]}\n" +
                $"قيمة الإرجاع: {MoneyHelper.FormatMad(result.RefundTotal)}\n" +
                $"استرداد نقدي: {MoneyHelper.FormatMad(result.CashRefund)}\n" +
                $"خصم من الدين: {MoneyHelper.FormatMad(result.DueReduction)}");

            Reason = string.Empty;
            await ReloadAsync();
            if (SelectedSale is not null)
                await LoadDetailAsync(SelectedSale.Uuid);
            SetStatus("تم تسجيل الإرجاع وإرجاع الكمية إلى المخزون.");
        }
        catch (Exception ex) when (ex is DomainException or InvalidOperationException)
        {
            SetStatus(ex.Message, true);
            _dialogs.ShowError(ex.Message);
        }
        catch (Exception ex)
        {
            SetStatus("تعذّر تسجيل الإرجاع: " + ex.Message, true);
            _dialogs.ShowError("تعذّر تسجيل الإرجاع.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSelectedSaleChanged(SaleDetail? value)
    {
        foreach (var line in Lines) line.PropertyChanged -= OnLineChanged;
        Lines.Clear();
        if (value is null)
        {
            NotifyPreview();
            return;
        }

        foreach (var line in value.Items)
        {
            var viewModel = new ReturnLineViewModel(line);
            viewModel.PropertyChanged += OnLineChanged;
            Lines.Add(viewModel);
        }

        NotifyPreview();
    }

    private void OnLineChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReturnLineViewModel.Quantity))
            NotifyPreview();
    }

    private async Task LoadRecentSalesAsync()
    {
        var list = await _sales.GetRecentAsync();
        Sales.Clear();
        foreach (var sale in list) Sales.Add(sale);
    }

    private async Task LoadRecentReturnsAsync()
    {
        var list = await _sales.GetRecentReturnsAsync();
        RecentReturns.Clear();
        foreach (var item in list) RecentReturns.Add(item);
    }

    private async Task LoadDetailAsync(string saleUuid)
    {
        var detail = await _sales.GetDetailAsync(saleUuid);
        if (detail is null) return;
        var index = Sales.IndexOf(Sales.FirstOrDefault(s => s.Uuid == saleUuid) ?? new SaleDetail());
        if (index >= 0)
        {
            Sales[index] = detail;
            SelectedSale = detail;
        }
    }

    private void NotifyPreview()
    {
        OnPropertyChanged(nameof(RefundPreview));
        OnPropertyChanged(nameof(RefundPreviewText));
        OnPropertyChanged(nameof(SelectedCount));
    }

    private void SetStatus(string message, bool error = false)
    {
        StatusMessage = message;
        IsError = error;
    }
}
