using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LmimaShopApp.Core.Domain.Models;
using LmimaShopApp.Core.Domain.Repositories;

namespace LmimaShopApp.Presentation.ViewModels
{
    public partial class CustomersViewModel : ObservableObject
    {
        private readonly ICustomerRepository _customerRepository;

        [ObservableProperty]
        private ObservableCollection<Customer> _customers = new();

        [ObservableProperty]
        private string _searchQuery = string.Empty;

        [ObservableProperty]
        private decimal _totalDebts;

        public CustomersViewModel(ICustomerRepository customerRepository)
        {
            _customerRepository = customerRepository;
        }

        [RelayCommand]
        public async Task LoadCustomersAsync()
        {
            var loaded = await _customerRepository.GetAllAsync();
            Customers.Clear();
            decimal total = 0;
            foreach (var c in loaded)
            {
                Customers.Add(c);
                total += c.DueBalance;
            }
            TotalDebts = total;
        }
        
        [RelayCommand]
        public async Task SearchCustomersAsync()
        {
            if (string.IsNullOrWhiteSpace(SearchQuery))
            {
                await LoadCustomersAsync();
                return;
            }

            var loaded = await _customerRepository.GetAllAsync(); // Ideal: Add Search to Repo
            var filtered = loaded.Where(c => c.FullName.Contains(SearchQuery, System.StringComparison.OrdinalIgnoreCase) || c.Phone.Contains(SearchQuery)).ToList();
            
            Customers.Clear();
            foreach (var c in filtered)
            {
                Customers.Add(c);
            }
        }
    }
}