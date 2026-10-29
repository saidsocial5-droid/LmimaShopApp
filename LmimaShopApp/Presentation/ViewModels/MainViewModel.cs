using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LmimaShopApp.Core.Domain.Repositories;

namespace LmimaShopApp.Presentation.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly IProductRepository _productRepository;

        [ObservableProperty]
        private ObservableCollection<Product> _products = new();

        [ObservableProperty]
        private ObservableCollection<CartItem> _cart = new();

        [ObservableProperty]
        private string _searchQuery = string.Empty;

        [ObservableProperty]
        private decimal _totalAmount;

        public MainViewModel(IProductRepository productRepository)
        {
            _productRepository = productRepository;
        }

        [RelayCommand]
        public async Task LoadProductsAsync()
        {
            var loadedProducts = await _productRepository.GetAllAsync();
            Products.Clear();
            foreach (var p in loadedProducts)
            {
                Products.Add(p);
            }
        }

        [RelayCommand]
        public async Task SearchProductsAsync()
        {
            if (string.IsNullOrWhiteSpace(SearchQuery))
            {
                await LoadProductsAsync();
                return;
            }

            var results = await _productRepository.SearchAsync(SearchQuery);
            Products.Clear();
            foreach (var p in results)
            {
                Products.Add(p);
            }
        }

        [RelayCommand]
        public void AddToCart(Product product)
        {
            if (product == null) return;
            
            // Logic to add to cart or increment quantity
            // This isolates the cart logic from the UI (MainWindow.xaml.cs)
            // Example simplified implementation:
            var existing = Cart.FirstOrDefault(c => c.ProductId == product.Id);
            if (existing != null)
            {
                existing.Quantity++;
            }
            else
            {
                Cart.Add(new CartItem 
                { 
                    ProductId = product.Id, 
                    ProductName = product.ProductName, 
                    Barcode = product.Barcode,
                    SellPrice = product.SellPrice, 
                    Quantity = 1 
                });
            }
            UpdateTotal();
        }
        
        private void UpdateTotal()
        {
            decimal total = 0;
            foreach(var item in Cart) total += item.Total;
            TotalAmount = total;
        }
    }
}