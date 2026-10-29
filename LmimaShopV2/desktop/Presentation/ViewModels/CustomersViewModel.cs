using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LmimaShopV2.Desktop.Core.Interfaces;
using LmimaShopV2.Desktop.Core.Models;
using LmimaShopV2.Desktop.Helpers;
using LmimaShopV2.Desktop.Presentation.Services;
using Serilog;

namespace LmimaShopV2.Desktop.Presentation.ViewModels;

public sealed record PaymentMethodOption(string Value, string Label);

public sealed partial class CustomersViewModel : ObservableObject
{
    private readonly ICustomerService _customers;
    private readonly ISessionService _session;
    private readonly IDialogService _dialogs;

    public CustomersViewModel(ICustomerService customers, ISessionService session, IDialogService dialogs)
    {
        _customers = customers;
        _session = session;
        _dialogs = dialogs;
        PaymentMethods = [new("cash", "نقدًا"), new("card", "بطاقة")];
        _selectedPaymentMethod = PaymentMethods[0];
    }

    public ObservableCollection<Customer> Customers { get; } = [];
    public ObservableCollection<StatementEntry> StatementEntries { get; } = [];
    public IReadOnlyList<PaymentMethodOption> PaymentMethods { get; }

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private Customer? _selectedCustomer;

    [ObservableProperty]
    private string _editingUuid = string.Empty;

    [ObservableProperty]
    private string _fullName = string.Empty;

    [ObservableProperty]
    private string _phone = string.Empty;

    [ObservableProperty]
    private string _address = string.Empty;

    [ObservableProperty]
    private decimal _creditLimit;

    [ObservableProperty]
    private string _notes = string.Empty;

    [ObservableProperty]
    private decimal _paymentAmount;

    [ObservableProperty]
    private PaymentMethodOption _selectedPaymentMethod;

    [ObservableProperty]
    private string _paymentNotes = string.Empty;

    [ObservableProperty]
    private bool _isStatementVisible;

    [ObservableProperty]
    private decimal _openingBalance;

    [ObservableProperty]
    private string _statementTitle = "كشف الحساب";

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isError;

    [ObservableProperty]
    private bool _isBusy;

    public string FormTitle => string.IsNullOrWhiteSpace(EditingUuid) ? "عميل جديد" : "تعديل عميل";
    public decimal SelectedDue => SelectedCustomer?.DueBalance ?? 0m;
    public int SelectedLoyalty => SelectedCustomer?.LoyaltyPoints ?? 0;
    public string SelectedDueText => MoneyHelper.FormatMad(SelectedDue);

    public async Task InitializeAsync()
    {
        await SearchAsync();
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        try
        {
            IsBusy = true;
            var list = await _customers.SearchAsync(SearchText);
            Customers.Clear();
            foreach (var customer in list) Customers.Add(customer);
            SetStatus($"عدد العملاء: {list.Count}");
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
    private void NewCustomer()
    {
        EditingUuid = string.Empty;
        FullName = string.Empty;
        Phone = string.Empty;
        Address = string.Empty;
        CreditLimit = 0m;
        Notes = string.Empty;
        PaymentAmount = 0m;
        SelectedCustomer = null;
        IsStatementVisible = false;
        OnPropertyChanged(nameof(FormTitle));
    }

    [RelayCommand]
    private async Task SaveCustomerAsync()
    {
        try
        {
            IsBusy = true;
            var customer = new Customer
            {
                Uuid = EditingUuid,
                FullName = FullName,
                Phone = Phone,
                Address = Address,
                CreditLimit = CreditLimit,
                Notes = Notes
            };
            var id = await _customers.SaveAsync(customer, _session.Actor);
            await SearchAsync();
            SelectedCustomer = Customers.FirstOrDefault(c => c.Id == id);
            if (SelectedCustomer is not null) EditingUuid = SelectedCustomer.Uuid;
            SetStatus("تم حفظ بيانات العميل.");
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
    private async Task DeleteCustomerAsync()
    {
        if (SelectedCustomer is null) return;
        if (!_dialogs.Confirm($"حذف العميل \"{SelectedCustomer.FullName}\"؟")) return;

        try
        {
            IsBusy = true;
            await _customers.DeleteAsync(SelectedCustomer.Uuid, _session.Actor);
            NewCustomer();
            await SearchAsync();
            SetStatus("تم حذف العميل.");
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
    private async Task AddPaymentAsync()
    {
        if (SelectedCustomer is null)
        {
            SetStatus("اختر عميلًا أولًا.", true);
            return;
        }

        if (PaymentAmount <= 0)
        {
            SetStatus("أدخل مبلغًا أكبر من صفر.", true);
            return;
        }

        try
        {
            IsBusy = true;
            await _customers.AddPaymentAsync(
                SelectedCustomer.Uuid,
                PaymentAmount,
                SelectedPaymentMethod.Value,
                PaymentNotes,
                _session.CurrentUser?.Id,
                _session.Actor);
            PaymentAmount = 0m;
            PaymentNotes = string.Empty;
            await SearchAsync();
            SelectedCustomer = Customers.FirstOrDefault(c => c.Uuid == EditingUuid);
            SetStatus("تم تسجيل الدفعة.");
            if (IsStatementVisible) await LoadStatementAsync();
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
    private async Task ShowStatementAsync()
    {
        if (SelectedCustomer is null)
        {
            SetStatus("اختر عميلًا لعرض كشف الحساب.", true);
            return;
        }

        IsStatementVisible = true;
        await LoadStatementAsync();
    }

    private async Task LoadStatementAsync()
    {
        if (SelectedCustomer is null) return;
        try
        {
            IsBusy = true;
            var statement = await _customers.GetStatementAsync(SelectedCustomer.Uuid);
            StatementTitle = $"كشف حساب: {statement.Customer.FullName}";
            OpeningBalance = statement.OpeningBalance;
            StatementEntries.Clear();
            foreach (var entry in statement.Entries) StatementEntries.Add(entry);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Unable to load customer statement");
            SetStatus(ex.Message, true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSelectedCustomerChanged(Customer? value)
    {
        if (value is null) return;
        EditingUuid = value.Uuid;
        FullName = value.FullName;
        Phone = value.Phone ?? string.Empty;
        Address = value.Address ?? string.Empty;
        CreditLimit = value.CreditLimit;
        Notes = value.Notes ?? string.Empty;
        OnPropertyChanged(nameof(FormTitle));
        OnPropertyChanged(nameof(SelectedDue));
        OnPropertyChanged(nameof(SelectedLoyalty));
        OnPropertyChanged(nameof(SelectedDueText));
    }

    private void SetStatus(string message, bool error = false)
    {
        StatusMessage = message;
        IsError = error;
    }
}
