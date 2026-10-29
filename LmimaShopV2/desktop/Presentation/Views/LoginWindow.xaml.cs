using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LmimaShopV2.Desktop.Presentation.ViewModels;

namespace LmimaShopV2.Desktop.Presentation.Views;

public partial class LoginWindow : Window
{
    private readonly LoginViewModel _viewModel;

    public LoginWindow(LoginViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.CloseRequested += OnCloseRequested;
        Loaded += (_, _) => UsernameBox.Focus();
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

    private void OnPasswordKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        if (_viewModel.LoginCommand.CanExecute(null)) _viewModel.LoginCommand.Execute(null);
    }
}
