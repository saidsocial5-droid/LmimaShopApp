using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LmimaShopV2.Desktop.Config;
using Serilog;

namespace LmimaShopV2.Desktop.Presentation.ViewModels;

public sealed partial class DbSetupViewModel : ObservableObject
{
    private readonly AppConfigService _config;

    public DbSetupViewModel(AppConfigService config)
    {
        _config = config;
        _host = config.DefaultDbHost;
        _port = config.DefaultDbPort;
        _database = config.DefaultDbName;
        _user = "root";
        _statusMessage = config.IsConfigured
            ? "قاعدة البيانات مهيأة. يمكنك تعديل الإعدادات ثم اختبار الاتصال."
            : "لم يتم إعداد قاعدة البيانات بعد. أدخل بيانات الاتصال.";
    }

    public event EventHandler<bool>? CloseRequested;

    [ObservableProperty]
    private string _host;

    [ObservableProperty]
    private int _port;

    [ObservableProperty]
    private string _user;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _database;

    [ObservableProperty]
    private string _statusMessage;

    [ObservableProperty]
    private bool _isError;

    [ObservableProperty]
    private bool _isBusy;

    [RelayCommand]
    private async Task TestAsync()
    {
        try
        {
            IsBusy = true;
            SetStatus("جارٍ اختبار الاتصال...");
            var connectionString = AppConfigService.BuildConnectionString(Host, Port, User, Password, string.Empty);
            await AppConfigService.TestConnectionAsync(connectionString);
            SetStatus("الاتصال ناجح. يمكنك حفظ الإعدادات.");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "MySQL connection test failed");
            SetStatus("فشل الاتصال: " + ex.Message, true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            SetStatus("جارٍ التحقق من الخادم...");
            var serverConnection = AppConfigService.BuildConnectionString(Host, Port, User, Password, string.Empty);
            await AppConfigService.TestConnectionAsync(serverConnection);

            _config.SaveDatabaseConnection(Host, Port, User, Password, Database);
            SetStatus("تم حفظ الإعدادات.");
            CloseRequested?.Invoke(this, true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unable to save database settings");
            SetStatus("تعذّر حفظ الإعدادات: " + ex.Message, true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, false);

    private void SetStatus(string message, bool error = false)
    {
        StatusMessage = message;
        IsError = error;
    }
}
