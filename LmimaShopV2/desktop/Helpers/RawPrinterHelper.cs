using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Serilog;

namespace LmimaShopV2.Desktop.Helpers;

/// <summary>Minimal RAW (ESC/POS) printing through the Windows spooler.</summary>
public static class RawPrinterHelper
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DocInfo
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string DocName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? OutputFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string DataType;
    }

    [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool OpenPrinter(string printerName, out IntPtr handle, IntPtr defaults);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr handle);

    [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool StartDocPrinter(IntPtr handle, int level, ref DocInfo docInfo);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndDocPrinter(IntPtr handle);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool StartPagePrinter(IntPtr handle);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndPagePrinter(IntPtr handle);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool WritePrinter(IntPtr handle, byte[] bytes, int count, out int written);

    public static bool Send(string printerName, string text, int codePage = 864)
    {
        if (string.IsNullOrWhiteSpace(printerName))
        {
            Log.Warning("Print requested but no printer is configured");
            return false;
        }

        Encoding encoding;
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            encoding = Encoding.GetEncoding(codePage);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            Log.Warning(ex, "Unsupported receipt code page {CodePage}; falling back to UTF-8", codePage);
            encoding = Encoding.UTF8;
        }

        var bytes = encoding.GetBytes(text);
        return SendBytes(printerName, bytes);
    }

    private static bool SendBytes(string printerName, byte[] bytes)
    {
        var handle = IntPtr.Zero;
        try
        {
            if (!OpenPrinter(printerName, out handle, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            var docInfo = new DocInfo
            {
                DocName = "LmimaShop Receipt",
                OutputFile = null,
                DataType = "RAW"
            };
            if (!StartDocPrinter(handle, 1, ref docInfo))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            if (!StartPagePrinter(handle))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            if (!WritePrinter(handle, bytes, bytes.Length, out var written) || written != bytes.Length)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            EndPagePrinter(handle);
            EndDocPrinter(handle);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "RAW printing failed for printer {Printer}", printerName);
            return false;
        }
        finally
        {
            if (handle != IntPtr.Zero) ClosePrinter(handle);
        }
    }
}
