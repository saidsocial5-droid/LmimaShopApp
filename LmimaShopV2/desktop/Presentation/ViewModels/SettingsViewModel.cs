using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LmimaShopV2.Desktop.Config;
using LmimaShopV2.Desktop.Core.Interfaces;
using LmimaShopV2.Desktop.Helpers;
using LmimaShopV2.Desktop.Infrastructure.Data;
using LmimaShopV2.Desktop.Presentation.Services;
using Serilog;

namespace LmimaShopV2.Desktop.Presentation.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly AppConfigService _config;
    private readonly IThemeService _theme;
    private readonly ReceiptService _receipt;
    private readonly BackupService _backup;
    private readonly ISessionService _session;
    private readonly IDialogService _dialogs;

    public SettingsViewModel(
        ISettingsService settings,
        AppConfigService config,
        IThemeService theme,
        ReceiptService receipt,
        BackupService backup,
        ISessionService session,
        IDialogService dialogs)
    {
        _settings = settings;
        _config = config;
        _theme = theme;
        _receipt = receipt;
        _backup = backup;
        _session = session;
        _dialogs = dialogs;
        _isDark = theme.IsDark;
    }

    public ObservableCollection<string> Printers { get; } = [];

    [ObservableProperty]
    private string _shopName = "LmimaShop";

    [ObservableProperty]
    private string _shopIce = string.Empty;

    [ObservableProperty]
    private string _shopRc = string.Empty;

    [ObservableProperty]
    private string _shopIf = string.Empty;

    [ObservableProperty]
    private string _shopPatente = string.Empty;

    [ObservableProperty]
    private string _shopAddress = string.Empty;

    [ObservableProperty]
    private string _shopPhone = string.Empty;

    [ObservableProperty]
    private string _receiptFooter = string.Empty;

    [ObservableProperty]
    private decimal _tvaRate = 20m;

    [ObservableProperty]
    private bool _pricesIncludeTva = true;

    [ObservableProperty]
    private decimal _loyaltyMadPerPoint = 10m;

    [ObservableProperty]
    private string _printerName = string.Empty;

    [ObservableProperty]
    private int _receiptCodePage = 864;

    [ObservableProperty]
    private bool _showRial = true;

    [ObservableProperty]
    private bool _autoPrintReceipt;

    [ObservableProperty]
    private bool _isDark;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isError;

    [ObservableProperty]
    private bool _isBusy;

    public async Task InitializeAsync()
    {
        try
        {
            IsBusy = true;
            Printers.Clear();
            foreach (var printer in PrinterDiscovery.GetInstalledPrinters()) Printers.Add(printer);

            var values = await _settings.GetAllAsync();
            ShopName = Get(values, "shop_name", "LmimaShop");
            ShopIce = Get(values, "shop_ice", string.Empty);
            ShopRc = Get(values, "shop_rc", string.Empty);
            ShopIf = Get(values, "shop_if", string.Empty);
            ShopPatente = Get(values, "shop_patente", string.Empty);
            ShopAddress = Get(values, "shop_address", string.Empty);
            ShopPhone = Get(values, "shop_phone", string.Empty);
            ReceiptFooter = Get(values, "receipt_footer", string.Empty);
            TvaRate = ParseDecimal(values, "tva_rate", 20m);
            PricesIncludeTva = ParseBool(values, "prices_include_tva", true);
            LoyaltyMadPerPoint = ParseDecimal(values, "loyalty_mad_per_point", 10m);
            PrinterName = Get(values, "printer_name", _config.PrinterName);
            ReceiptCodePage = (int)ParseDecimal(values, "receipt_code_page", 864m);
            ShowRial = ParseBool(values, "show_rial", true);
            AutoPrintReceipt = ParseBool(values, "receipt_print_auto", false);
            IsDark = string.Equals(Get(values, "theme", _config.Theme), "dark", StringComparison.OrdinalIgnoreCase);
            StatusMessage = "قم بتعديل الإعدادات ثم اضغط حفظ.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unable to load settings");
            SetStatus("تعذّر تحميل الإعدادات: " + ex.Message, true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (TvaRate is < 0 or > 100)
        {
            SetStatus("نسبة TVA يجب أن تكون بين 0 و100.", true);
            return;
        }

        if (LoyaltyMadPerPoint < 1m)
        {
            SetStatus("قيمة نقطة الولاء يجب أن تكون 1 على الأقل.", true);
            return;
        }

        if (ReceiptCodePage is < 1 or > 65001)
        {
            SetStatus("ترميز الطابعة غير صالح.", true);
            return;
        }

        try
        {
            IsBusy = true;
            var theme = IsDark ? "dark" : "light";
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["shop_name"] = ShopName.Trim(),
                ["shop_ice"] = ShopIce.Trim(),
                ["shop_rc"] = ShopRc.Trim(),
                ["shop_if"] = ShopIf.Trim(),
                ["shop_patente"] = ShopPatente.Trim(),
                ["shop_address"] = ShopAddress.Trim(),
                ["shop_phone"] = ShopPhone.Trim(),
                ["receipt_footer"] = ReceiptFooter.Trim(),
                ["tva_rate"] = TvaRate.ToString(CultureInfo.InvariantCulture),
                ["prices_include_tva"] = PricesIncludeTva ? "true" : "false",
                ["loyalty_mad_per_point"] = LoyaltyMadPerPoint.ToString(CultureInfo.InvariantCulture),
                ["printer_name"] = PrinterName.Trim(),
                ["receipt_code_page"] = ReceiptCodePage.ToString(CultureInfo.InvariantCulture),
                ["show_rial"] = ShowRial ? "true" : "false",
                ["receipt_print_auto"] = AutoPrintReceipt ? "true" : "false",
                ["theme"] = theme
            };

            await _settings.SetManyAsync(values, _session.Actor);
            _config.PrinterName = PrinterName.Trim();
            _theme.Apply(theme);
            SetStatus("تم حفظ الإعدادات.");
            _dialogs.ShowInfo("تم حفظ الإعدادات بنجاح.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unable to save settings");
            SetStatus("تعذّر حفظ الإعدادات: " + ex.Message, true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task PrintTestAsync()
    {
        try
        {
            IsBusy = true;
            if (!string.IsNullOrWhiteSpace(PrinterName))
            {
                await _settings.SetValueAsync("printer_name", PrinterName.Trim(), audit: false);
                await _settings.SetValueAsync("receipt_code_page",
                    ReceiptCodePage.ToString(CultureInfo.InvariantCulture), audit: false);
            }

            var printed = await _receipt.PrintTestAsync();
            if (printed) SetStatus("تم إرسال صفحة اختبار إلى الطابعة.");
            else
            {
                SetStatus("اختر طابعة أولًا.", true);
                _dialogs.ShowError("لم يتم اختيار طابعة.");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Test print failed");
            SetStatus("تعذّرت الطباعة التجريبية.", true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task BackupAsync()
    {
        try
        {
            IsBusy = true;
            var path = await _backup.CreateBackupAsync();
            SetStatus("تم إنشاء النسخة الاحتياطية: " + path);
            _dialogs.ShowInfo("تم إنشاء النسخة الاحتياطية:\n" + path);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Backup failed");
            SetStatus("تعذّر إنشاء النسخة الاحتياطية: " + ex.Message, true);
            _dialogs.ShowError("تعذّر إنشاء النسخة الاحتياطية.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenBackupFolder()
    {
        try
        {
            Directory.CreateDirectory(_config.BackupFolder);
            Process.Start(new ProcessStartInfo
            {
                FileName = _config.BackupFolder,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Unable to open backup folder");
            SetStatus("تعذّر فتح مجلد النسخ الاحتياطي.", true);
        }
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        IsDark = !IsDark;
        _theme.Apply(IsDark ? "dark" : "light");
    }

    [RelayCommand]
    private void Close() => System.Windows.Application.Current?.Windows
        .OfType<System.Windows.Window>()
        .FirstOrDefault(w => w.DataContext == this)?.Close();

    private static string Get(Dictionary<string, string> values, string key, string fallback) =>
        values.TryGetValue(key, out var value) && value.Length > 0 ? value : fallback;

    private static decimal ParseDecimal(Dictionary<string, string> values, string key, decimal fallback) =>
        values.TryGetValue(key, out var raw) &&
        decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    private static bool ParseBool(Dictionary<string, string> values, string key, bool fallback) =>
        values.TryGetValue(key, out var raw) && bool.TryParse(raw, out var value) ? value : fallback;

    private void SetStatus(string message, bool error = false)
    {
        StatusMessage = message;
        IsError = error;
    }
}
