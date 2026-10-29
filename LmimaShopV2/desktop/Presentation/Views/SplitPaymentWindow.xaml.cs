using System.Windows;
using LmimaShopV2.Desktop.Presentation.ViewModels;

namespace LmimaShopV2.Desktop.Presentation.Views;

public partial class SplitPaymentWindow : Window
{
    public SplitPaymentWindow(SplitPaymentViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseRequested += OnCloseRequested;
    }

    private void OnCloseRequested(object? sender, bool result)
    {
        DialogResult = result;
        Close();
    }
}
