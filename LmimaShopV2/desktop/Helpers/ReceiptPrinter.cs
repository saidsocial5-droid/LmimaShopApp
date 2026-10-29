using System.Text;
using LmimaShopV2.Desktop.Core.Models;

namespace LmimaShopV2.Desktop.Helpers;

public static class ReceiptPrinter
{
    private const int Width = 42;

    public static string BuildReceipt(
        CheckoutResult sale,
        IReadOnlyList<CheckoutLineResult> lines,
        StoreSettings settings,
        string cashierName,
        bool showRial = true)
    {
        var shop = settings.Shop;
        var sb = new StringBuilder();
        sb.AppendLine(Center(shop.Name.ToUpperInvariant()));
        if (!string.IsNullOrWhiteSpace(shop.Address)) sb.AppendLine(Center(shop.Address));
        if (!string.IsNullOrWhiteSpace(shop.Phone)) sb.AppendLine(Center("Tel: " + shop.Phone));
        if (!string.IsNullOrWhiteSpace(shop.Ice)) sb.AppendLine(Center("ICE: " + shop.Ice));
        if (!string.IsNullOrWhiteSpace(shop.Rc)) sb.AppendLine(Center("RC: " + shop.Rc));
        if (!string.IsNullOrWhiteSpace(shop.FiscalIf)) sb.AppendLine(Center("IF: " + shop.FiscalIf));
        if (!string.IsNullOrWhiteSpace(shop.Patente)) sb.AppendLine(Center("Patente: " + shop.Patente));
        sb.AppendLine(new string('-', Width));
        sb.AppendLine($"Ticket : {sale.InvoiceNo}");
        sb.AppendLine($"Date   : {sale.CreatedAt:dd/MM/yyyy HH:mm}");
        sb.AppendLine($"Caissier: {cashierName}");
        if (!string.IsNullOrWhiteSpace(sale.CustomerName)) sb.AppendLine($"Client : {sale.CustomerName}");
        sb.AppendLine(new string('-', Width));

        foreach (var line in lines)
        {
            var qty = line.Qty.ToString("0.###");
            sb.AppendLine($"{qty} x {line.Name}");
            sb.AppendLine($"    {MoneyHelper.FormatPlain(line.UnitPrice),12}   {MoneyHelper.FormatPlain(line.LineTotal),12}");
        }

        sb.AppendLine(new string('-', Width));
        var subTotal = lines.Sum(l => l.LineTotal);
        AppendAmount(sb, "Sous-total", subTotal);
        if (sale.DiscountAmount > 0) AppendAmount(sb, "Remise", -sale.DiscountAmount);
        AppendAmount(sb, "TOTAL TTC", sale.Total);
        AppendAmount(sb, $"TVA {settings.TvaRate:0.##}% (incluse)", sale.Tva);
        AppendAmount(sb, "Paye", sale.Paid);
        AppendAmount(sb, "Monnaie", sale.Change);
        if (sale.Due > 0) AppendAmount(sb, "Credit / dette", sale.Due);
        if (showRial) AppendAmount(sb, "Equivalent Rial", MoneyHelper.ToRial(sale.Total));
        if (sale.LoyaltyEarned > 0) sb.AppendLine($"Points fidelite gagnes : {sale.LoyaltyEarned}");
        sb.AppendLine(new string('-', Width));
        if (!string.IsNullOrWhiteSpace(shop.Footer)) sb.AppendLine(Center(shop.Footer));
        sb.AppendLine(Center("Merci !"));
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("\x1D\x56\x00"); // ESC/POS partial cut (ignored by text-only printers)
        return sb.ToString();
    }

    public static string BuildTestReceipt(StoreSettings settings) =>
        $"{Center(settings.Shop.Name.ToUpperInvariant())}\n{Center("Test imprimante")}\n{new string('-', Width)}\n" +
        $"TVA: {settings.TvaRate:0.##}%\nFidelite: 1 point / {settings.LoyaltyMadPerPoint:0.##} DH\n\n\n\n";

    public static string BuildZReport(ShiftReport report, StoreSettings settings)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Center(settings.Shop.Name.ToUpperInvariant()));
        sb.AppendLine(Center("RAPPORT Z - CLOTURE DE CAISSE"));
        sb.AppendLine(new string('-', Width));
        sb.AppendLine($"Shift   : {report.ShiftId}");
        sb.AppendLine($"Caissier: {report.CashierName}");
        sb.AppendLine($"Ouvert  : {report.OpenedAt:dd/MM/yyyy HH:mm}");
        if (report.ClosedAt is not null) sb.AppendLine($"Ferme   : {report.ClosedAt:dd/MM/yyyy HH:mm}");
        sb.AppendLine(new string('-', Width));
        AppendAmount(sb, "Fond de caisse", report.OpeningFloat);
        AppendAmount(sb, "Ventes cash", report.CashSales);
        AppendAmount(sb, "Ventes carte", report.CardSales);
        AppendAmount(sb, "Ventes credit", report.CreditSales);
        AppendAmount(sb, "Paiements dettes", report.DebtPaymentsCash);
        AppendAmount(sb, "Retours", -report.ReturnsRefund);
        AppendAmount(sb, "TVA collectee", report.TvaCollected);
        sb.AppendLine(new string('-', Width));
        AppendAmount(sb, "Chiffre d'affaires", report.TotalRevenue);
        AppendAmount(sb, "Cash attendu", report.ExpectedCash);
        if (report.CountedCash is not null) AppendAmount(sb, "Cash compte", report.CountedCash.Value);
        if (report.Difference is not null) AppendAmount(sb, "Ecart", report.Difference.Value);
        sb.AppendLine($"Tickets : {report.SalesCount}");
        sb.AppendLine($"Points fidelite : {report.LoyaltyGranted}");
        if (!string.IsNullOrWhiteSpace(report.Notes)) sb.AppendLine("Note: " + report.Notes);
        sb.AppendLine(new string('-', Width));
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("\x1D\x56\x00");
        return sb.ToString();
    }

    public static bool Print(string printerName, string text, int codePage) =>
        RawPrinterHelper.Send(printerName, text, codePage);

    private static void AppendAmount(StringBuilder sb, string label, decimal amount)
    {
        var left = label;
        var right = MoneyHelper.FormatPlain(amount) + " DH";
        var padding = Math.Max(1, Width - left.Length - right.Length);
        sb.AppendLine(left + new string(' ', padding) + right);
    }

    private static string Center(string text) =>
        text.Length >= Width ? text : new string(' ', (Width - text.Length) / 2) + text;
}
