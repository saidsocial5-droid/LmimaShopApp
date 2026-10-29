using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LmimaShopV2.Desktop.Config;
using LmimaShopV2.Desktop.Core.Interfaces;
using LmimaShopV2.Desktop.Core.Models;
using LmimaShopV2.Desktop.Helpers;
using LmimaShopV2.Desktop.Infrastructure.Server;
using LmimaShopV2.Desktop.Presentation.Services;
using Serilog;

namespace LmimaShopV2.Desktop.Presentation.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IProductService _products;
    private readonly ICustomerService _customers;
    private readonly ICheckoutService _checkout;
    private readonly IShiftService _shifts;
    private readonly ISettingsService _settings;
    private readonly IWindowService _windows;
    private readonly ISessionService _session;
    private readonly IDialogService _dialogs;
    private readonly ReceiptService _receipt;
    private readonly PairingService _pairing;
    private readonly EmbeddedServer _server;
    private readonly AppConfigService _config;
    private readonly IThemeService _theme;

    private bool _autoPrint;
    private bool _pricesIncludeTva = true;
    private decimal _tvaRate = 20m;
    private DateTime _lastRefresh = DateTime.MinValue;

    public MainViewModel(
        IProductService products,
        ICustomerService customers,
        ICheckoutService checkout,
        IShiftService shifts,
        ISettingsService settings,
        IWindowService windows,
        ISessionService session,
        IDialogService dialogs,
        ReceiptService receipt,
        PairingService pairing,
        EmbeddedServer server,
        AppConfigService config,
        IThemeService theme)
    {
        _products = products;
        _customers = customers;
        _checkout = checkout;
        _shifts = shifts;
        _settings = settings;
        _windows = windows;
        _session = session;
        _dialogs = dialogs;
        _receipt = receipt;
        _pairing = pairing;
        _server = server;
        _config = config;
        _theme = theme;

        Cart.CollectionChanged += OnCartChanged;
        _isDark = theme.IsDark;
    }

    public ObservableCollection<Product> Products { get; } = [];
    public ObservableCollection<Category> Categories { get; } = [];
    public ObservableCollection<Customer> Customers { get; } = [];
    public ObservableCollection<CartLineViewModel> Cart { get; } = [];

    public string UserName => _session.CurrentUser?.FullName ?? string.Empty;
    public string UserRole => _session.CurrentUser?.Role ?? string.Empty;
    public bool IsAdmin => _session.CurrentUser?.IsAdmin == true;
    public bool IsManager => _session.CurrentUser?.IsManager == true;

    [ObservableProperty]
    private string _shopName = "LmimaShop";

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private Category? _selectedCategory;

    [ObservableProperty]
    private Product? _selectedProduct;

    [ObservableProperty]
    private Customer? _selectedCustomer;

    [ObservableProperty]
    private CartLineViewModel? _selectedCartLine;

    [ObservableProperty]
    private decimal _overallDiscountPct;

    [ObservableProperty]
    private string _statusMessage = "جاهز";

    [ObservableProperty]
    private bool _isError;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _hasOpenShift;

    [ObservableProperty]
    private string _shiftStatus = "جارٍ التحقق من الوردية...";

    [ObservableProperty]
    private string _serverStatus = "الخادم متوقف";

    [ObservableProperty]
    private bool _isDark;

    [ObservableProperty]
    private string _pairingCode = string.Empty;

    [ObservableProperty]
    private string _pairingUrl = string.Empty;

    [ObservableProperty]
    private string _pairingExpires = string.Empty;

    [ObservableProperty]
    private BitmapSource? _pairingQr;

    public decimal Subtotal => MoneyHelper.Round(Cart.Sum(l => l.LineTotal));
    public decimal DiscountAmount => MoneyHelper.Round(Subtotal * OverallDiscountPct / 100m);
    public decimal Total => MoneyHelper.Round(Subtotal - DiscountAmount);
    public decimal TvaAmount { get; private set; }
    public int CartCount => Cart.Count;
    public string Currency => "DH";

    partial void OnOverallDiscountPctChanged(decimal value) => Recalculate();

    public async Task InitializeAsync()
    {
        try
        {
            IsBusy = true;
            var store = await _settings.GetStoreSettingsAsync();
            ShopName = store.Shop.Name;
            _tvaRate = store.TvaRate;
            _pricesIncludeTva = store.PricesIncludeTva;
            _autoPrint = string.Equals(
                await _settings.GetValueAsync("receipt_print_auto"), "true", StringComparison.OrdinalIgnoreCase);

            await LoadCategoriesAsync();
            await LoadProductsAsync();
            await LoadCustomersAsync();
            await RefreshShiftAsync();
            RefreshServerStatus();

            var noticeShown = await _settings.IsAdminNoticeShownAsync();
            if (!noticeShown)
            {
                _dialogs.ShowInfo(
                    "تم إنشاء حساب المدير الافتراضي.\n\nاسم المستخدم: admin\nكلمة المرور: admin123\n\n" +
                    "يُنصح بتغيير كلمة المرور من نافذة المستخدمين.");
                await _settings.MarkAdminNoticeShownAsync();
            }

            SetStatus("جاهز");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Main window initialisation failed");
            SetStatus(ex.Message, true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Reloads reference data after a modal window closes, without disturbing the cart.</summary>
    public async Task RefreshOnActivateAsync()
    {
        if (IsBusy || DateTime.UtcNow - _lastRefresh < TimeSpan.FromSeconds(3)) return;
        try
        {
            var store = await _settings.GetStoreSettingsAsync();
            ShopName = store.Shop.Name;
            _tvaRate = store.TvaRate;
            _pricesIncludeTva = store.PricesIncludeTva;
            _autoPrint = string.Equals(
                await _settings.GetValueAsync("receipt_print_auto"), "true", StringComparison.OrdinalIgnoreCase);

            await LoadCategoriesAsync();
            await LoadProductsAsync();
            await LoadCustomersAsync();
            await RefreshShiftAsync();
            RefreshServerStatus();
            Recalculate();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Refresh on activation failed");
        }
    }

    [RelayCommand]
    private async Task SearchProductsAsync()
    {
        await LoadProductsAsync();
    }

    [RelayCommand]
    private async Task ClearFiltersAsync()
    {
        SearchText = string.Empty;
        SelectedCategory = null;
        await LoadProductsAsync();
    }

    [RelayCommand]
    private async Task ScanBarcodeAsync()
    {
        var code = SearchText.Trim();
        if (code.Length == 0)
        {
            SetStatus("أدخل أو امسح رمزًا شريطيًا.", true);
            return;
        }

        try
        {
            var product = await _products.GetByBarcodeAsync(code);
            if (product is null)
            {
                await LoadProductsAsync();
                if (Products.Count == 1) AddProduct(Products[0]);
                else SetStatus("لا يوجد منتج مطابق لهذا الرمز.", true);
                return;
            }

            AddProduct(product);
            SearchText = string.Empty;
        }
        catch (Exception ex) when (ex is DomainException or InvalidOperationException)
        {
            SetStatus(ex.Message, true);
        }
    }

    [RelayCommand]
    private void AddProduct(Product? product)
    {
        if (product is null) return;
        var line = Cart.FirstOrDefault(l => l.ProductUuid == product.Uuid);
        if (line is not null)
        {
            line.Qty += 1m;
        }
        else
        {
            Cart.Add(new CartLineViewModel(product));
        }

        Recalculate();
        SetStatus($"تمت إضافة {product.Name}");
    }

    [RelayCommand]
    private void IncreaseQuantity(CartLineViewModel? line)
    {
        if (line is null) return;
        line.Qty += 1m;
    }

    [RelayCommand]
    private void DecreaseQuantity(CartLineViewModel? line)
    {
        if (line is null) return;
        if (line.Qty <= 1m) Cart.Remove(line);
        else line.Qty -= 1m;
    }

    [RelayCommand]
    private void RemoveLine(CartLineViewModel? line)
    {
        if (line is null) return;
        Cart.Remove(line);
    }

    [RelayCommand]
    private void ClearCart()
    {
        if (Cart.Count == 0) return;
        if (!_dialogs.Confirm("هل تريد إفراغ سلة البيع؟")) return;
        Cart.Clear();
        OverallDiscountPct = 0m;
        SetStatus("تم إفراغ السلة.");
    }

    [RelayCommand]
    private void ClearCustomer()
    {
        SelectedCustomer = null;
        SetStatus("تم اختيار بيع بدون عميل.");
    }

    [RelayCommand]
    private async Task CheckoutAsync()
    {
        if (IsBusy) return;
        if (Cart.Count == 0)
        {
            SetStatus("السلة فارغة.", true);
            return;
        }

        if (!HasOpenShift)
        {
            SetStatus("يجب فتح الوردية قبل البيع.", true);
            _windows.ShowShift();
            return;
        }

        Recalculate();
        if (Total <= 0)
        {
            SetStatus("المجموع يجب أن يكون أكبر من صفر.", true);
            return;
        }

        var context = new PaymentDialogContext(Total, SelectedCustomer?.FullName, SelectedCustomer?.AvailableCredit ?? 0m);
        var selection = _windows.ShowSplitPayment(context);
        if (selection is null) return;

        var request = new CheckoutRequest
        {
            OpId = Guid.NewGuid().ToString(),
            CustomerUuid = SelectedCustomer?.Uuid,
            DiscountPct = OverallDiscountPct,
            Cashier = _session.CurrentUser?.FullName,
            Items = Cart.Select(line => new CheckoutItem
            {
                ProductUuid = line.ProductUuid,
                Qty = line.Qty,
                UnitPrice = line.UnitPrice,
                DiscountPct = line.DiscountPct
            }).ToList(),
            Payments = BuildPayments(selection)
        };

        try
        {
            IsBusy = true;
            var result = await _checkout.CheckoutAsync(
                request, new CheckoutContext(_session.CurrentUser?.FullName ?? "Caisse", _session.CurrentUser?.Id));

            Cart.Clear();
            SelectedCustomer = null;
            OverallDiscountPct = 0m;
            await LoadProductsAsync();

            if (_autoPrint)
            {
                try
                {
                    var printed = await _receipt.PrintSaleAsync(result, result.Lines);
                    if (!printed) SetStatus($"تمت العملية {result.InvoiceNo} لكن الطابعة غير مهيأة.", true);
                }
                catch (Exception printError)
                {
                    Log.Error(printError, "Receipt printing failed for sale {Invoice}", result.InvoiceNo);
                    SetStatus($"تمت العملية {result.InvoiceNo} لكن فشل الطباعة.", true);
                }
            }

            _dialogs.ShowInfo(
                $"تم إتمام البيع بنجاح.\n\nرقم الفاتورة: {result.InvoiceNo}\n" +
                $"المجموع: {MoneyHelper.FormatMad(result.Total)}\n" +
                $"المدفوع: {MoneyHelper.FormatMad(result.Paid)}\n" +
                $"الباقي: {MoneyHelper.FormatMad(result.Change)}\n" +
                $"الآجل: {MoneyHelper.FormatMad(result.Due)}");
            SetStatus($"تم حفظ الفاتورة {result.InvoiceNo}.");
        }
        catch (Exception ex) when (ex is DomainException or InvalidOperationException)
        {
            Log.Warning(ex, "Checkout failed");
            SetStatus(ex.Message, true);
            _dialogs.ShowError(ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unexpected checkout error");
            SetStatus("تعذّر إتمام البيع. راجع السجل.", true);
            _dialogs.ShowError("تعذّر إتمام البيع: " + ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static List<CheckoutPayment> BuildPayments(PaymentSelection selection)
    {
        var payments = new List<CheckoutPayment>();
        if (selection.Cash > 0) payments.Add(new CheckoutPayment { Method = "cash", Amount = selection.Cash });
        if (selection.Card > 0) payments.Add(new CheckoutPayment { Method = "card", Amount = selection.Card });
        if (selection.Credit > 0) payments.Add(new CheckoutPayment { Method = "credit", Amount = selection.Credit });
        return payments;
    }

    [RelayCommand]
    private async Task GeneratePairingAsync()
    {
        try
        {
            var info = await _pairing.GenerateAsync();
            var url = BuildPairingUrl(info.Code);
            PairingCode = info.Code;
            PairingUrl = url;
            PairingExpires = info.ExpiresAt.ToString("HH:mm:ss");
            PairingQr = QrCodeHelper.CreateBitmap(url);
            SetStatus("تم إنشاء رمز اقتران صالح لمدة دقيقتين.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unable to generate pairing code");
            SetStatus("تعذّر إنشاء رمز الاقتران.", true);
        }
    }

    [RelayCommand]
    private void CopyPairingLink()
    {
        if (string.IsNullOrWhiteSpace(PairingUrl))
        {
            SetStatus("أنشئ رمز اقتران أولًا.", true);
            return;
        }

        try
        {
            Clipboard.SetText(PairingUrl);
            SetStatus("تم نسخ رابط الاقتران.");
        }
        catch (ExternalException ex)
        {
            Log.Warning(ex, "Clipboard unavailable");
            SetStatus("تعذّر النسخ إلى الحافظة.", true);
        }
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        _theme.Apply(_theme.IsDark ? "light" : "dark");
        IsDark = _theme.IsDark;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        _lastRefresh = DateTime.MinValue;
        await RefreshOnActivateAsync();
        await LoadProductsAsync();
        SetStatus("تم تحديث البيانات.");
    }

    [RelayCommand] private void OpenProducts() => _windows.ShowProducts();
    [RelayCommand] private void OpenCustomers() => _windows.ShowCustomers();
    [RelayCommand] private void OpenSuppliers() => _windows.ShowSuppliers();
    [RelayCommand] private void OpenReports() => _windows.ShowReports();
    [RelayCommand] private void OpenReturns() => _windows.ShowReturns();
    [RelayCommand] private void OpenSettings() => _windows.ShowSettings();
    [RelayCommand] private void OpenUsers() => _windows.ShowUsers();
    [RelayCommand] private void OpenShift() => _windows.ShowShift();

    [RelayCommand]
    private void Logout()
    {
        if (!_dialogs.Confirm("هل تريد تسجيل الخروج؟ البيع الحالي لن يُحفظ.")) return;
        _session.SignOut();
        Application.Current.MainWindow?.Close();
    }

    private async Task LoadCategoriesAsync()
    {
        var selectedUuid = SelectedCategory?.Uuid;
        var categories = await _products.GetCategoriesAsync();
        Categories.Clear();
        foreach (var category in categories) Categories.Add(category);
        if (selectedUuid is not null)
            SelectedCategory = Categories.FirstOrDefault(c => c.Uuid == selectedUuid);
    }

    private async Task LoadProductsAsync()
    {
        try
        {
            var selectedUuid = SelectedCategory?.Uuid;
            var categoryId = Categories.FirstOrDefault(c => c.Uuid == selectedUuid)?.Id;
            var list = await _products.GetAllAsync(SearchText, categoryId);
            Products.Clear();
            foreach (var product in list) Products.Add(product);
            _lastRefresh = DateTime.UtcNow;
        }
        catch (Exception ex) when (ex is DomainException or InvalidOperationException)
        {
            SetStatus(ex.Message, true);
        }
    }

    private async Task LoadCustomersAsync()
    {
        var selectedUuid = SelectedCustomer?.Uuid;
        var list = await _customers.SearchAsync();
        Customers.Clear();
        foreach (var customer in list) Customers.Add(customer);
        if (selectedUuid is not null)
            SelectedCustomer = Customers.FirstOrDefault(c => c.Uuid == selectedUuid);
    }

    private async Task RefreshShiftAsync()
    {
        var shift = await _shifts.GetOpenShiftAsync();
        HasOpenShift = shift is not null;
        ShiftStatus = shift is null
            ? "لا توجد وردية مفتوحة"
            : $"الوردية مفتوحة · {shift.CashierName} · منذ {shift.OpenedAt:HH:mm}";
    }

    private void RefreshServerStatus()
    {
        ServerStatus = _server.StatusText;
    }

    private void Recalculate()
    {
        TvaAmount = ComputeTva(Total);
        foreach (var name in new[]
                 {
                     nameof(Subtotal), nameof(DiscountAmount), nameof(Total), nameof(TvaAmount),
                     nameof(CartCount)
                 })
            OnPropertyChanged(name);
    }

    private decimal ComputeTva(decimal total)
    {
        if (total <= 0 || _tvaRate <= 0) return 0m;
        return _pricesIncludeTva
            ? MoneyHelper.Round(total - total / (1m + _tvaRate / 100m))
            : MoneyHelper.Round(total * _tvaRate / 100m);
    }

    private string BuildPairingUrl(string code)
    {
        var address = GetLocalAddress();
        return $"http://{address}:{_config.ServerPort}/#pair={code}";
    }

    private static string GetLocalAddress()
    {
        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            var address = host.AddressList.FirstOrDefault(a =>
                a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a));
            return address?.ToString() ?? "127.0.0.1";
        }
        catch (SocketException)
        {
            return "127.0.0.1";
        }
    }

    private void OnCartChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (CartLineViewModel line in e.OldItems)
                line.PropertyChanged -= OnLinePropertyChanged;
        }

        if (e.NewItems is not null)
        {
            foreach (CartLineViewModel line in e.NewItems)
                line.PropertyChanged += OnLinePropertyChanged;
        }

        Recalculate();
    }

    private void OnLinePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CartLineViewModel.LineTotal))
            Recalculate();
    }

    private void SetStatus(string message, bool isError = false)
    {
        StatusMessage = message;
        IsError = isError;
    }
}
