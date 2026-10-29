using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using LmimaShopV2.Desktop.Config;
using LmimaShopV2.Desktop.Core.Interfaces;
using LmimaShopV2.Desktop.Core.Services;
using LmimaShopV2.Desktop.Core.Sync;
using LmimaShopV2.Desktop.Infrastructure.Data;
using LmimaShopV2.Desktop.Infrastructure.Server;
using LmimaShopV2.Desktop.Presentation.Services;
using LmimaShopV2.Desktop.Presentation.ViewModels;
using LmimaShopV2.Desktop.Presentation.Views;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace LmimaShopV2.Desktop;

/// <summary>
/// Application entry point. Owns the DI container, the embedded HTTP server and the
/// login / main-window loop. Shutdown is explicit so the loop can swap windows.
/// </summary>
public partial class App : Application
{
    private static IServiceProvider? _services;
    private IHost? _host;
    private CancellationTokenSource? _shutdown;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        ConfigureCulture();

        try
        {
            _host = BuildHost();
            await _host.StartAsync();
            _services = _host.Services;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "تعذّر تشغيل التطبيق:\n" + ex.Message,
                "LmimaShop V2",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        _shutdown = new CancellationTokenSource();
        _ = RunAsync(_shutdown.Token);
    }

    private static IHost BuildHost()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddJsonFile(
            Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true, reloadOnChange: false);

        var logPath = ResolveLogPath(builder.Configuration);
        builder.Services.AddSerilog(
            (Action<LoggerConfiguration>)(configuration => configuration
                .MinimumLevel.Information()
                .Enrich.FromLogContext()
                .WriteTo.File(
                    logPath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    shared: true)),
            preserveStaticLogger: false,
            writeToProviders: false);

        RegisterServices(builder.Services);
        return builder.Build();
    }

    private static void RegisterServices(IServiceCollection services)
    {
        // Configuration + infrastructure.
        services.AddSingleton<AppConfigService>();
        services.AddSingleton<Db>();
        services.AddSingleton<DbInitializer>();
        services.AddSingleton<ISessionService, SessionService>();

        // Core business services (single instances shared by the UI and the HTTP API).
        services.AddSingleton<IAuthService, AuthService>();
        services.AddSingleton<IProductService, ProductService>();
        services.AddSingleton<ICustomerService, CustomerService>();
        services.AddSingleton<ISupplierService, SupplierService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IAuditLogger, AuditLogger>();
        services.AddSingleton<SseHub>();
        services.AddSingleton<ISseHub>(sp => sp.GetRequiredService<SseHub>());
        services.AddSingleton<IShiftService, ShiftService>();
        services.AddSingleton<IReportService, ReportService>();
        services.AddSingleton<ICheckoutService, CheckoutService>();
        services.AddSingleton<IReturnsService, ReturnsService>();
        services.AddSingleton<ISalesService, SalesQueryService>();
        services.AddSingleton<ISyncService, SyncService>();
        services.AddSingleton<BackupService>();

        // Web server + static assets.
        services.AddSingleton<PairingService>();
        services.AddSingleton<StaticFileServer>();
        services.AddSingleton<ApiHandlers>();
        services.AddSingleton<EmbeddedServer>();

        // Presentation services.
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IWindowService, WindowService>();
        services.AddSingleton<ReceiptService>();

        // Windows + view models.
        services.AddSingleton<LoginViewModel>();
        services.AddSingleton<LoginWindow>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
        services.AddTransient<DbSetupViewModel>();
        services.AddTransient<DbSetupWindow>();
        services.AddTransient<ProductsViewModel>();
        services.AddTransient<ProductsWindow>();
        services.AddTransient<CustomersViewModel>();
        services.AddTransient<CustomersWindow>();
        services.AddTransient<SuppliersViewModel>();
        services.AddTransient<SuppliersWindow>();
        services.AddTransient<ReportsViewModel>();
        services.AddTransient<ReportsWindow>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<SettingsWindow>();
        services.AddTransient<UsersViewModel>();
        services.AddTransient<UsersWindow>();
        services.AddTransient<ShiftViewModel>();
        services.AddTransient<ShiftWindow>();
        services.AddTransient<ReturnsViewModel>();
        services.AddTransient<ReturnsWindow>();
    }

    /// <summary>Database setup, schema bootstrap, server start, then the login / main loop until exit.</summary>
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var services = _host!.Services;
        var initializer = services.GetRequiredService<DbInitializer>();
        var server = services.GetRequiredService<EmbeddedServer>();
        var session = services.GetRequiredService<ISessionService>();
        var dialogs = services.GetRequiredService<IDialogService>();
        var logger = services.GetRequiredService<ILogger<App>>();

        var haveDatabase = await EnsureDatabaseAsync(initializer, dialogs);
        if (haveDatabase)
        {
            try
            {
                await server.StartAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unable to start the embedded HTTP server");
            }
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            var loginResult = await ShowDialogAsync(() => services.GetRequiredService<LoginWindow>());
            if (!loginResult)
            {
                if (haveDatabase) continue;
                break;
            }

            var mainResult = await ShowDialogAsync(() => services.GetRequiredService<MainWindow>());
            session.SignOut();
            if (mainResult) break;
        }

        await StopServerAsync(server);
        Shutdown();
    }

    private static async Task<bool> EnsureDatabaseAsync(DbInitializer initializer, IDialogService dialogs)
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var configured = await TryInitialiseAsync(initializer, dialogs);
            if (configured) return true;
            var setup = await ShowDialogAsync(() => HostServices.GetRequiredService<DbSetupWindow>());
            if (!setup) return false;
        }

        return false;
    }

    private static async Task<bool> TryInitialiseAsync(DbInitializer initializer, IDialogService dialogs)
    {
        var config = HostServices.GetRequiredService<AppConfigService>();
        if (!config.IsConfigured) return false;

        try
        {
            await initializer.EnsureAsync();
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Database initialisation failed");
            dialogs.ShowError(
                "تعذّر الاتصال بقاعدة البيانات:\n" + ex.Message + "\n\nافتح إعدادات قاعدة البيانات للتصحيح.");
            return false;
        }
    }

    private static bool? ShowDialog(Func<Window> factory) =>
        Current.Dispatcher.Invoke(() => factory().ShowDialog());

    private static async Task<bool> ShowDialogAsync(Func<Window> factory)
    {
        var result = await Current.Dispatcher.InvokeAsync(() => factory().ShowDialog());
        return result == true;
    }

    private static async Task StopServerAsync(EmbeddedServer server)
    {
        try
        {
            await server.StopAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error stopping the embedded server");
        }
    }

    private static void ConfigureCulture()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage("ar-MA")));
    }

    private static string ResolveLogPath(IConfiguration configuration)
    {
        var folder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var directory = Path.Combine(folder, "LmimaShopV2", "logs");
        Directory.CreateDirectory(directory);
        var configured = configuration["Serilog:LogPath"];
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(directory, "lmimashop-.log")
            : configured.Replace("%APPDATA%", folder, StringComparison.OrdinalIgnoreCase);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            if (_host is not null)
            {
                _host.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                _host.Dispose();
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error disposing the host");
        }

        _shutdown?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unhandled dispatcher exception");
        MessageBox.Show(
            "حدث خطأ غير متوقع:\n" + e.Exception.Message,
            "LmimaShop V2",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception) Log.Fatal(exception, "Unhandled application exception");
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unobserved task exception");
        e.SetObserved();
    }

    private static IServiceProvider HostServices => _services
        ?? throw new InvalidOperationException("Application host is not ready.");
}
