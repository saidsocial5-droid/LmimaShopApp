using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LmimaShopV2.Desktop.Core.Interfaces;
using LmimaShopV2.Desktop.Core.Models;
using LmimaShopV2.Desktop.Presentation.Services;
using Serilog;

namespace LmimaShopV2.Desktop.Presentation.ViewModels;

public sealed partial class ProductsViewModel : ObservableObject
{
    private readonly IProductService _products;
    private readonly ISessionService _session;
    private readonly IDialogService _dialogs;

    public ProductsViewModel(IProductService products, ISessionService session, IDialogService dialogs)
    {
        _products = products;
        _session = session;
        _dialogs = dialogs;
    }

    public ObservableCollection<Product> Products { get; } = [];
    public ObservableCollection<Category> Categories { get; } = [];
    public bool CanManage => _session.CurrentUser?.IsManager == true;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private Category? _filterCategory;

    [ObservableProperty]
    private Product? _selectedProduct;

    [ObservableProperty]
    private string _editingUuid = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _barcode = string.Empty;

    [ObservableProperty]
    private Category? _editCategory;

    [ObservableProperty]
    private decimal _costPrice;

    [ObservableProperty]
    private decimal _sellPrice;

    [ObservableProperty]
    private decimal _stockQuantity;

    [ObservableProperty]
    private decimal _tvaRate = 20m;

    [ObservableProperty]
    private bool _isWeighed;

    [ObservableProperty]
    private string _imagePath = string.Empty;

    [ObservableProperty]
    private string _newCategoryName = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isError;

    [ObservableProperty]
    private bool _isBusy;

    public string FormTitle => string.IsNullOrWhiteSpace(EditingUuid) ? "منتج جديد" : "تعديل منتج";
    public bool HasSelection => !string.IsNullOrWhiteSpace(EditingUuid);

    public async Task InitializeAsync()
    {
        await LoadCategoriesAsync();
        await SearchAsync();
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        try
        {
            IsBusy = true;
            var list = await _products.GetAllAsync(SearchText, FilterCategory?.Id);
            Products.Clear();
            foreach (var product in list) Products.Add(product);
            SetStatus($"عدد المنتجات: {list.Count}");
        }
        catch (Exception ex) when (ex is DomainException or InvalidOperationException)
        {
            SetStatus(ex.Message, true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void NewProduct()
    {
        EditingUuid = string.Empty;
        Name = string.Empty;
        Barcode = string.Empty;
        EditCategory = FilterCategory;
        CostPrice = 0m;
        SellPrice = 0m;
        StockQuantity = 0m;
        TvaRate = 20m;
        IsWeighed = false;
        ImagePath = string.Empty;
        SelectedProduct = null;
        OnPropertyChanged(nameof(FormTitle));
        OnPropertyChanged(nameof(HasSelection));
    }

    [RelayCommand]
    private void EditSelected()
    {
        if (SelectedProduct is null) return;
        EditingUuid = SelectedProduct.Uuid;
        Name = SelectedProduct.Name;
        Barcode = SelectedProduct.Barcode ?? string.Empty;
        EditCategory = Categories.FirstOrDefault(c => c.Id == SelectedProduct.CategoryId);
        CostPrice = SelectedProduct.CostPrice;
        SellPrice = SelectedProduct.SellPrice;
        StockQuantity = SelectedProduct.StockQuantity;
        TvaRate = SelectedProduct.TvaRate;
        IsWeighed = SelectedProduct.IsWeighed;
        ImagePath = SelectedProduct.ImagePath ?? string.Empty;
        OnPropertyChanged(nameof(FormTitle));
        OnPropertyChanged(nameof(HasSelection));
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!CanManage)
        {
            SetStatus("تعديل المنتجات متاح للمدير فقط.", true);
            return;
        }

        try
        {
            IsBusy = true;
            var product = new Product
            {
                Uuid = EditingUuid,
                Name = Name,
                Barcode = Barcode,
                CategoryId = EditCategory?.Id,
                CostPrice = CostPrice,
                SellPrice = SellPrice,
                StockQuantity = StockQuantity,
                TvaRate = TvaRate,
                IsWeighed = IsWeighed,
                ImagePath = ImagePath
            };
            await _products.SaveAsync(product, _session.Actor);
            await LoadCategoriesAsync();
            await SearchAsync();
            EditingUuid = string.Empty;
            NewProduct();
            SetStatus("تم حفظ المنتج.");
        }
        catch (Exception ex) when (ex is DomainException or InvalidOperationException)
        {
            SetStatus(ex.Message, true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (!CanManage || SelectedProduct is null) return;
        if (!_dialogs.Confirm($"حذف المنتج \"{SelectedProduct.Name}\"؟")) return;

        try
        {
            IsBusy = true;
            await _products.DeleteAsync(SelectedProduct.Uuid, _session.Actor);
            NewProduct();
            await SearchAsync();
            SetStatus("تم حذف المنتج.");
        }
        catch (Exception ex) when (ex is DomainException or InvalidOperationException)
        {
            SetStatus(ex.Message, true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task AddCategoryAsync()
    {
        if (!CanManage) return;
        var name = NewCategoryName.Trim();
        if (name.Length == 0)
        {
            SetStatus("أدخل اسم الصنف.", true);
            return;
        }

        try
        {
            IsBusy = true;
            await _products.EnsureCategoryAsync(name, _session.Actor);
            NewCategoryName = string.Empty;
            await LoadCategoriesAsync();
            SetStatus("تمت إضافة الصنف.");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Unable to add category");
            SetStatus(ex.Message, true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadCategoriesAsync()
    {
        var currentId = EditCategory?.Id;
        var filterId = FilterCategory?.Id;
        var list = await _products.GetCategoriesAsync();
        Categories.Clear();
        foreach (var category in list) Categories.Add(category);
        if (currentId is not null) EditCategory = Categories.FirstOrDefault(c => c.Id == currentId);
        if (filterId is not null) FilterCategory = Categories.FirstOrDefault(c => c.Id == filterId);
    }

    private void SetStatus(string message, bool error = false)
    {
        StatusMessage = message;
        IsError = error;
    }
}
