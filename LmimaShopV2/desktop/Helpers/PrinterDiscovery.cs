using System.Printing;
using Serilog;

namespace LmimaShopV2.Desktop.Helpers;

public static class PrinterDiscovery
{
    public static IReadOnlyList<string> GetInstalledPrinters()
    {
        try
        {
            using var server = new LocalPrintServer();
            return server.GetPrintQueues()
                .Select(q => q.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Unable to enumerate installed printers");
            return [];
        }
    }
}
