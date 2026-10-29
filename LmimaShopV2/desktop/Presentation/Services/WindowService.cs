using LmimaShopV2.Desktop.Core.Interfaces;
using LmimaShopV2.Desktop.Core.Models;

namespace LmimaShopV2.Desktop.Presentation.Services;

public sealed record PaymentDialogContext(decimal Total, string? CustomerName, decimal AvailableCredit);

public interface IWindowService
{
    void ShowProducts();
    void ShowCustomers();
    void ShowSuppliers();
    void ShowReports();
    void ShowReturns();
    void ShowSettings();
    void ShowUsers();
    void ShowShift();
    PaymentSelection? ShowSplitPayment(PaymentDialogContext context);
}

public sealed class WindowService : IWindowService
{
    private readonly IServiceProvider _provider;
    private readonly ISessionService _session;
    private readonly IDialogService _dialogs;

    public WindowService(IServiceProvider provider, ISessionService session, IDialogService dialogs)
    {
        _provider = provider;
        _session = session;
        _dialogs = dialogs;
    }

    public void ShowProducts() => ShowDialog<Views.ProductsWindow>();
    public void ShowCustomers() => ShowDialog<Views.CustomersWindow>();
    public void ShowSuppliers() => ShowDialog<Views.SuppliersWindow>();
    public void ShowShift() => ShowDialog<Views.ShiftWindow>();
    public void ShowReturns() => ShowDialog<Views.ReturnsWindow>();

    public void ShowReports()
    {
        if (_session.CurrentUser?.CanViewReports != true)
        {
            _dialogs.ShowError("Acces reserve aux managers et administrateurs.");
            return;
        }

        ShowDialog<Views.ReportsWindow>();
    }

    public void ShowUsers()
    {
        if (_session.CurrentUser?.IsAdmin != true)
        {
            _dialogs.ShowError("Acces reserve aux administrateurs.");
            return;
        }

        ShowDialog<Views.UsersWindow>();
    }

    public void ShowSettings()
    {
        if (_session.CurrentUser?.IsManager != true)
        {
            _dialogs.ShowError("Acces reserve aux managers et administrateurs.");
            return;
        }

        ShowDialog<Views.SettingsWindow>();
    }

    public PaymentSelection? ShowSplitPayment(PaymentDialogContext context)
    {
        var viewModel = ActivatorUtilities.CreateInstance<ViewModels.SplitPaymentViewModel>(_provider, context);
        var window = new Views.SplitPaymentWindow(viewModel)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        return window.ShowDialog() == true ? viewModel.Selection : null;
    }

    private void ShowDialog<TWindow>() where TWindow : System.Windows.Window
    {
        var window = _provider.GetRequiredService<TWindow>();
        window.Owner = System.Windows.Application.Current?.MainWindow;
        window.ShowDialog();
    }
}
