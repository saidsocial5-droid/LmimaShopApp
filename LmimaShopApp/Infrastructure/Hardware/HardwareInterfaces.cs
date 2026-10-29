using System;

namespace LmimaShopApp.Infrastructure.Hardware
{
    public interface IReceiptPrinter
    {
        bool IsAvailable();
        void PrintReceipt(object receiptData); // Will replace with concrete model later
    }

    public interface ICashDrawer
    {
        void OpenDrawer();
    }

    public interface IBarcodeScanner
    {
        event EventHandler<string> BarcodeScanned;
        void Enable();
        void Disable();
    }
}