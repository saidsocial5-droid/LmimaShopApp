using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using LmimaShopV2.Desktop.Presentation.ViewModels;

namespace LmimaShopV2.Desktop.Presentation.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        Loaded += OnLoaded;
        Activated += OnActivated;
        Closing += OnClosing;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
    }

    private async void OnActivated(object? sender, EventArgs e)
    {
        await _viewModel.RefreshOnActivateAsync();
    }

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        if (_viewModel.ScanBarcodeCommand.CanExecute(null)) _viewModel.ScanBarcodeCommand.Execute(null);
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (System.Windows.Application.Current?.Windows.OfType<Window>().Count(w => w.IsVisible) > 1)
        {
            e.Cancel = true;
            return;
        }

        var result = MessageBox.Show(
            this,
            "هل تريد إغلاق التطبيق؟",
            "LmimaShop V2",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
        {
            e.Cancel = true;
            return;
        }

        DialogResult = true;
    }

    private void OnExitClick(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            this,
            "هل تريد إغلاق التطبيق؟",
            "LmimaShop V2",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result == MessageBoxResult.Yes) DialogResult = true;
    }
}
