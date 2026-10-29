using System.Windows;
using System.Windows.Input;
using LmimaShopV2.Desktop.Presentation.ViewModels;

namespace LmimaShopV2.Desktop.Presentation.Views;

public partial class ReturnsWindow : Window
{
    private readonly ReturnsViewModel _viewModel;

    public ReturnsWindow(ReturnsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += async (_, _) => await _viewModel.InitializeAsync();
    }

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        if (_viewModel.SearchCommand.CanExecute(null)) _viewModel.SearchCommand.Execute(null);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
