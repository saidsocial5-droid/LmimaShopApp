using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using LmimaShopApp.Infrastructure.Hardware;

namespace LmimaShopApp
{
    public static class AppHost
    {
        public static IHost? Host { get; private set; }

        public static void Initialize()
        {
            Host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
                .ConfigureServices((context, services) =>
                {
                    // ViewModels
                    services.AddTransient<LmimaShopApp.Presentation.ViewModels.MainViewModel>();
                    services.AddTransient<LmimaShopApp.Presentation.ViewModels.CustomersViewModel>();

                    // Services

                    // Infrastructure (Repositories & Hardware)
                    services.AddScoped<LmimaShopApp.Core.Domain.Repositories.IProductRepository, LmimaShopApp.Infrastructure.Repositories.ProductRepository>();
                    services.AddScoped<LmimaShopApp.Core.Domain.Repositories.ICustomerRepository, LmimaShopApp.Infrastructure.Repositories.CustomerRepository>();
                    services.AddScoped<LmimaShopApp.Core.Domain.Repositories.ISaleRepository, LmimaShopApp.Infrastructure.Repositories.SaleRepository>();

                    // Windows
                    services.AddTransient<MainWindow>();
                    services.AddTransient<LoginWindow>();
                    services.AddTransient<ProductsWindow>();
                    services.AddTransient<CustomersWindow>();
                    services.AddTransient<ReportsWindow>();
                    services.AddTransient<SettingsWindow>();
                })
                .Build();
        }

        public static T GetService<T>() where T : class
        {
            if (Host == null)
                throw new InvalidOperationException("AppHost is not initialized.");
            
            return Host.Services.GetRequiredService<T>();
        }
    }
}