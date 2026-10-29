using System.Windows;
using System.Windows.Controls;
using LmimaShopV2.Desktop.Presentation.ViewModels;

namespace LmimaShopV2.Desktop.Presentation.Views;

public partial class DbSetupWindow : Window
{
    private readonly DbSetupViewModel _viewModel;

    public DbSetupWindow(DbSetupViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.CloseRequested += OnCloseRequested;
    }

    private void OnCloseRequested(object? sender, bool result)
    {
        DialogResult = result;
        Close();
    }

    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox box) _viewModel.Password = box.Password;
    }
}
