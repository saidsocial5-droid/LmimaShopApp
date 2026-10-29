using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LmimaShopV2.Desktop.Core.Interfaces;
using LmimaShopV2.Desktop.Core.Models;
using LmimaShopV2.Desktop.Presentation.Services;

namespace LmimaShopV2.Desktop.Presentation.ViewModels;

public sealed partial class SuppliersViewModel : ObservableObject
{
    private readonly ISupplierService _suppliers;
    private readonly ISessionService _session;
    private readonly IDialogService _dialogs;

    public SuppliersViewModel(ISupplierService suppliers, ISessionService session, IDialogService dialogs)
    {
        _suppliers = suppliers;
        _session = session;
        _dialogs = dialogs;
    }

    public ObservableCollection<Supplier> Suppliers { get; } = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private Supplier? _selectedSupplier;

    [ObservableProperty]
    private string _editingUuid = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _phone = string.Empty;

    [ObservableProperty]
    private string _address = string.Empty;

    [ObservableProperty]
    private string _notes = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isError;

    [ObservableProperty]
    private bool _isBusy;

    public string FormTitle => string.IsNullOrWhiteSpace(EditingUuid) ? "مورد جديد" : "تعديل مورد";

    public Task InitializeAsync() => SearchAsync();

    [RelayCommand]
    private async Task SearchAsync()
    {
        try
        {
            IsBusy = true;
            var list = await _suppliers.SearchAsync(SearchText);
            Suppliers.Clear();
            foreach (var supplier in list) Suppliers.Add(supplier);
            StatusMessage = $"عدد الموردين: {list.Count}";
            IsError = false;
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
    private void NewSupplier()
    {
        EditingUuid = string.Empty;
        Name = string.Empty;
        Phone = string.Empty;
        Address = string.Empty;
        Notes = string.Empty;
        SelectedSupplier = null;
        OnPropertyChanged(nameof(FormTitle));
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            IsBusy = true;
            var supplier = new Supplier
            {
                Uuid = EditingUuid,
                Name = Name,
                Phone = Phone,
                Address = Address,
                Notes = Notes
            };
            var id = await _suppliers.SaveAsync(supplier, _session.Actor);
            await SearchAsync();
            SelectedSupplier = Suppliers.FirstOrDefault(s => s.Id == id);
            SetStatus("تم حفظ المورد.");
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
        if (SelectedSupplier is null) return;
        if (!_dialogs.Confirm($"حذف المورد \"{SelectedSupplier.Name}\"؟")) return;

        try
        {
            IsBusy = true;
            await _suppliers.DeleteAsync(SelectedSupplier.Uuid, _session.Actor);
            NewSupplier();
            await SearchAsync();
            SetStatus("تم حذف المورد.");
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

    partial void OnSelectedSupplierChanged(Supplier? value)
    {
        if (value is null) return;
        EditingUuid = value.Uuid;
        Name = value.Name;
        Phone = value.Phone ?? string.Empty;
        Address = value.Address ?? string.Empty;
        Notes = value.Notes ?? string.Empty;
        OnPropertyChanged(nameof(FormTitle));
    }

    private void SetStatus(string message, bool error = false)
    {
        StatusMessage = message;
        IsError = error;
    }
}
