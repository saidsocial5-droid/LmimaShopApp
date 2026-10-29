using LmimaShopV2.Desktop.Config;
using LmimaShopV2.Desktop.Core.Interfaces;
using LmimaShopV2.Desktop.Core.Models;
using LmimaShopV2.Desktop.Helpers;
using Serilog;

namespace LmimaShopV2.Desktop.Presentation.Services;

public sealed class ReceiptService
{
    private readonly ISettingsService _settings;
    private readonly AppConfigService _config;
    private readonly ISessionService _session;

    public ReceiptService(ISettingsService settings, AppConfigService config, ISessionService session)
    {
        _settings = settings;
        _config = config;
        _session = session;
    }

    public async Task<bool> PrintSaleAsync(
        CheckoutResult sale,
        IReadOnlyList<CheckoutLineResult> lines,
        CancellationToken cancellationToken = default)
    {
        var store = await _settings.GetStoreSettingsAsync(cancellationToken);
        var printer = await _settings.GetValueAsync("printer_name", cancellationToken);
        var codePage = (int)await _settings.GetDecimalAsync("receipt_code_page", 864, cancellationToken);
        var showRial = await _settings.GetValueAsync("show_rial", cancellationToken) != "false";
        if (string.IsNullOrWhiteSpace(printer)) printer = _config.PrinterName;

        if (string.IsNullOrWhiteSpace(printer))
        {
            Log.Warning("Receipt printing skipped: no printer configured");
            return false;
        }

        var text = ReceiptPrinter.BuildReceipt(sale, lines, store, _session.CurrentUser?.FullName ?? "Caisse", showRial);
        return await Task.Run(() => ReceiptPrinter.Print(printer, text, codePage), cancellationToken);
    }

    public async Task<bool> PrintTestAsync(CancellationToken cancellationToken = default)
    {
        var store = await _settings.GetStoreSettingsAsync(cancellationToken);
        var printer = await _settings.GetValueAsync("printer_name", cancellationToken);
        var codePage = (int)await _settings.GetDecimalAsync("receipt_code_page", 864, cancellationToken);
        if (string.IsNullOrWhiteSpace(printer)) printer = _config.PrinterName;
        if (string.IsNullOrWhiteSpace(printer)) return false;
        var text = ReceiptPrinter.BuildTestReceipt(store);
        return await Task.Run(() => ReceiptPrinter.Print(printer, text, codePage), cancellationToken);
    }

    public async Task<bool> PrintZReportAsync(ShiftReport report, CancellationToken cancellationToken = default)
    {
        var store = await _settings.GetStoreSettingsAsync(cancellationToken);
        var printer = await _settings.GetValueAsync("printer_name", cancellationToken);
        var codePage = (int)await _settings.GetDecimalAsync("receipt_code_page", 864, cancellationToken);
        if (string.IsNullOrWhiteSpace(printer)) printer = _config.PrinterName;
        if (string.IsNullOrWhiteSpace(printer)) return false;
        var text = ReceiptPrinter.BuildZReport(report, store);
        return await Task.Run(() => ReceiptPrinter.Print(printer, text, codePage), cancellationToken);
    }
}
