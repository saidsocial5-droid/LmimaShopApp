using System.Windows;
using LmimaShopV2.Desktop.Presentation.ViewModels;

namespace LmimaShopV2.Desktop.Presentation.Views;

public partial class ShiftWindow : Window
{
    private readonly ShiftViewModel _viewModel;

    public ShiftWindow(ShiftViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += async (_, _) => await _viewModel.InitializeAsync();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
