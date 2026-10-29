using System.Windows;
using LmimaShopV2.Desktop.Presentation.ViewModels;

namespace LmimaShopV2.Desktop.Presentation.Views;

public partial class ReportsWindow : Window
{
    private readonly ReportsViewModel _viewModel;

    public ReportsWindow(ReportsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += async (_, _) => await _viewModel.InitializeAsync();
    }

    private void OnReloadAllClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.LoadDailyCommand.CanExecute(null)) _viewModel.LoadDailyCommand.Execute(null);
        if (_viewModel.LoadShiftsCommand.CanExecute(null)) _viewModel.LoadShiftsCommand.Execute(null);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
