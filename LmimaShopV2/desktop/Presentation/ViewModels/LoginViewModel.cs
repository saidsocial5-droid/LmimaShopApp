using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LmimaShopV2.Desktop.Core.Interfaces;
using LmimaShopV2.Desktop.Core.Models;
using LmimaShopV2.Desktop.Infrastructure.Data;
using LmimaShopV2.Desktop.Presentation.Services;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace LmimaShopV2.Desktop.Presentation.ViewModels;

public sealed partial class LoginViewModel : ObservableObject
{
    private readonly IAuthService _auth;
    private readonly ISessionService _session;
    private readonly IServiceProvider _provider;
    private readonly DbInitializer _initializer;
    private readonly IDialogService _dialogs;

    public LoginViewModel(
        IAuthService auth,
        ISessionService session,
        IServiceProvider provider,
        DbInitializer initializer,
        IDialogService dialogs)
    {
        _auth = auth;
        _session = session;
        _provider = provider;
        _initializer = initializer;
        _dialogs = dialogs;
    }

    public event EventHandler<bool>? CloseRequested;

    [ObservableProperty]
    private string _username = "admin";

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (IsBusy) return;
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrEmpty(Password))
        {
            StatusMessage = "أدخل اسم المستخدم وكلمة المرور.";
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = "جارٍ التحقق...";
            var user = await _auth.LoginAsync(Username.Trim(), Password);
            _session.SignIn(user);
            Password = string.Empty;
            StatusMessage = string.Empty;
            CloseRequested?.Invoke(this, true);
        }
        catch (Exception ex) when (ex is DomainException or InvalidOperationException)
        {
            StatusMessage = ex.Message;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Login failed");
            StatusMessage = "تعذّر الاتصال بالخادم. تحقق من إعدادات قاعدة البيانات.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ConfigureDatabaseAsync()
    {
        var window = _provider.GetRequiredService<Views.DbSetupWindow>();
        window.Owner = System.Windows.Application.Current?.Windows
            .OfType<System.Windows.Window>()
            .FirstOrDefault(w => w.IsActive);
        if (window.ShowDialog() != true) return;

        try
        {
            IsBusy = true;
            await _initializer.EnsureAsync();
            StatusMessage = "تم الاتصال بقاعدة البيانات.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Database initialisation failed from login screen");
            StatusMessage = "فشل تهيئة قاعدة البيانات: " + ex.Message;
            _dialogs.ShowError("فشل تهيئة قاعدة البيانات:\n" + ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, false);
}
