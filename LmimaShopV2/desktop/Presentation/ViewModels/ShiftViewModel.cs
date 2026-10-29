using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LmimaShopV2.Desktop.Core.Interfaces;
using LmimaShopV2.Desktop.Core.Models;
using LmimaShopV2.Desktop.Helpers;
using LmimaShopV2.Desktop.Presentation.Services;
using Serilog;

namespace LmimaShopV2.Desktop.Presentation.ViewModels;

public sealed partial class ShiftViewModel : ObservableObject
{
    private readonly IShiftService _shifts;
    private readonly IReportService _reports;
    private readonly ISessionService _session;
    private readonly ReceiptService _receipt;
    private readonly IDialogService _dialogs;

    public ShiftViewModel(
        IShiftService shifts,
        IReportService reports,
        ISessionService session,
        ReceiptService receipt,
        IDialogService dialogs)
    {
        _shifts = shifts;
        _reports = reports;
        _session = session;
        _receipt = receipt;
        _dialogs = dialogs;
    }

    public ObservableCollection<Shift> RecentShifts { get; } = [];

    [ObservableProperty]
    private Shift? _openShift;

    [ObservableProperty]
    private Shift? _selectedShift;

    [ObservableProperty]
    private ShiftReport? _zReport;

    [ObservableProperty]
    private decimal _openingFloat;

    [ObservableProperty]
    private decimal _countedCash;

    [ObservableProperty]
    private string _notes = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isError;

    [ObservableProperty]
    private bool _isBusy;

    public bool HasOpenShift => OpenShift is not null;
    public string OpenShiftText => OpenShift is null
        ? "لا توجد وردية مفتوحة"
        : $"مفتوحة بواسطة {OpenShift.CashierName} · {OpenShift.OpenedAt:dd/MM/yyyy HH:mm}";
    public string ExpectedCashText => OpenShift is null ? "-" : MoneyHelper.FormatMad(OpenShift.ExpectedCash);

    public async Task InitializeAsync()
    {
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        try
        {
            IsBusy = true;
            OpenShift = await _shifts.GetOpenShiftAsync();
            var list = await _shifts.GetRecentAsync(40);
            RecentShifts.Clear();
            foreach (var shift in list) RecentShifts.Add(shift);
            if (SelectedShift is null && RecentShifts.Count > 0) SelectedShift = RecentShifts[0];
            CountedCash = OpenShift?.ExpectedCash ?? 0m;
            NotifyShiftState();
            SetStatus(OpenShift is null ? "افتح وردية لبدء البيع." : "الوردية جاهزة للبيع.");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Unable to refresh shifts");
            SetStatus(ex.Message, true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task OpenAsync()
    {
        if (OpenShift is not null)
        {
            SetStatus("هناك وردية مفتوحة بالفعل.", true);
            return;
        }

        if (OpeningFloat < 0)
        {
            SetStatus("الرصيد الافتتاحي لا يمكن أن يكون سالبًا.", true);
            return;
        }

        var user = _session.CurrentUser;
        if (user is null)
        {
            SetStatus("يجب تسجيل الدخول.", true);
            return;
        }

        try
        {
            IsBusy = true;
            OpenShift = await _shifts.OpenAsync(user.Id, user.FullName, OpeningFloat);
            await RefreshAsync();
            _dialogs.ShowInfo("تم فتح الوردية بنجاح.");
        }
        catch (Exception ex) when (ex is DomainException or InvalidOperationException)
        {
            SetStatus(ex.Message, true);
            _dialogs.ShowError(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CloseAsync()
    {
        if (OpenShift is null)
        {
            SetStatus("لا توجد وردية مفتوحة.", true);
            return;
        }

        if (CountedCash < 0)
        {
            SetStatus("المبلغ المعدود لا يمكن أن يكون سالبًا.", true);
            return;
        }

        if (!_dialogs.Confirm($"إغلاق الوردية؟\nالمتوقع: {MoneyHelper.FormatMad(OpenShift.ExpectedCash)}\n" +
                              $"المعدود: {MoneyHelper.FormatMad(CountedCash)}")) return;

        var user = _session.CurrentUser;
        if (user is null) return;

        try
        {
            IsBusy = true;
            var report = await _shifts.CloseAsync(OpenShift.Id, CountedCash, Notes, user.Id, user.FullName);
            OpenShift = null;
            await RefreshAsync();
            ZReport = report;
            _dialogs.ShowInfo(
                $"تم إغلاق الوردية.\n\nالمبيعات: {MoneyHelper.FormatMad(report.TotalRevenue)}\n" +
                $"المتوقع نقدًا: {MoneyHelper.FormatMad(report.ExpectedCash)}\n" +
                $"المعدود: {MoneyHelper.FormatMad(report.CountedCash ?? 0m)}\n" +
                $"الفرق: {MoneyHelper.FormatMad(report.Difference ?? 0m)}");
        }
        catch (Exception ex) when (ex is DomainException or InvalidOperationException)
        {
            SetStatus(ex.Message, true);
            _dialogs.ShowError(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task LoadReportAsync()
    {
        if (SelectedShift is null)
        {
            SetStatus("اختر وردية.", true);
            return;
        }

        try
        {
            IsBusy = true;
            ZReport = await _reports.GetZReportAsync(SelectedShift.Id);
            SetStatus("تم تحميل تقرير الوردية.");
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
    private async Task PrintReportAsync()
    {
        if (ZReport is null)
        {
            SetStatus("حمّل التقرير أولًا.", true);
            return;
        }

        try
        {
            var printed = await _receipt.PrintZReportAsync(ZReport);
            SetStatus(printed ? "تم إرسال التقرير إلى الطابعة." : "لم يتم إعداد طابعة.", !printed);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Shift report printing failed");
            SetStatus("تعذّرت الطباعة.", true);
        }
    }

    partial void OnSelectedShiftChanged(Shift? value)
    {
        if (value is not null) LoadReportCommand.Execute(null);
    }

    private void NotifyShiftState()
    {
        OnPropertyChanged(nameof(HasOpenShift));
        OnPropertyChanged(nameof(OpenShiftText));
        OnPropertyChanged(nameof(ExpectedCashText));
    }

    private void SetStatus(string message, bool error = false)
    {
        StatusMessage = message;
        IsError = error;
    }
}
