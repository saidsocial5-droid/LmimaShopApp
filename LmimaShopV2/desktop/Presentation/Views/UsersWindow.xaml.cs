using System.Windows;
using System.Windows.Controls;
using LmimaShopV2.Desktop.Presentation.ViewModels;

namespace LmimaShopV2.Desktop.Presentation.Views;

public partial class UsersWindow : Window
{
    private readonly UsersViewModel _viewModel;

    public UsersWindow(UsersViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += async (_, _) => await _viewModel.InitializeAsync();
    }

    private void OnNewUserPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox box) _viewModel.Password = box.Password;
    }

    private void OnResetPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox box) _viewModel.NewPassword = box.Password;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
