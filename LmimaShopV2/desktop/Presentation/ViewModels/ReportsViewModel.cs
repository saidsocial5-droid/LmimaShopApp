using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LmimaShopV2.Desktop.Core.Interfaces;
using LmimaShopV2.Desktop.Core.Models;
using LmimaShopV2.Desktop.Presentation.Services;
using Serilog;

namespace LmimaShopV2.Desktop.Presentation.ViewModels;

public sealed partial class ReportsViewModel : ObservableObject
{
    private readonly IReportService _reports;
    private readonly IShiftService _shifts;
    private readonly ReceiptService _receipt;
    private readonly IDialogService _dialogs;

    public ReportsViewModel(
        IReportService reports,
        IShiftService shifts,
        ReceiptService receipt,
        IDialogService dialogs)
    {
        _reports = reports;
        _shifts = shifts;
        _receipt = receipt;
        _dialogs = dialogs;
        _date = DateTime.Today;
    }

    public ObservableCollection<Shift> Shifts { get; } = [];

    [ObservableProperty]
    private DateTime? _date;

    [ObservableProperty]
    private DailyReport? _dailyReport;

    [ObservableProperty]
    private Shift? _selectedShift;

    [ObservableProperty]
    private ShiftReport? _zReport;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isError;

    [ObservableProperty]
    private bool _isBusy;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(LoadDailyAsync(), LoadShiftsAsync());
    }

    [RelayCommand]
    private async Task LoadDailyAsync()
    {
        try
        {
            IsBusy = true;
            DailyReport = await _reports.GetDailyReportAsync(Date ?? DateTime.Today);
            SetStatus("تم تحميل تقرير اليوم.");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Unable to load daily report");
            SetStatus(ex.Message, true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task LoadShiftsAsync()
    {
        try
        {
            var list = await _shifts.GetRecentAsync(60);
            Shifts.Clear();
            foreach (var shift in list) Shifts.Add(shift);
            if (SelectedShift is null && Shifts.Count > 0) SelectedShift = Shifts[0];
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Unable to load shift history");
            SetStatus(ex.Message, true);
        }
    }

    [RelayCommand]
    private async Task LoadZReportAsync()
    {
        if (SelectedShift is null)
        {
            SetStatus("اختر وردية لعرض تقرير Z.", true);
            return;
        }

        try
        {
            IsBusy = true;
            ZReport = await _reports.GetZReportAsync(SelectedShift.Id);
            SetStatus("تم تحميل تقرير Z.");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Unable to load Z report");
            SetStatus(ex.Message, true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task PrintZReportAsync()
    {
        if (ZReport is null)
        {
            SetStatus("حمّل تقرير Z أولًا.", true);
            return;
        }

        try
        {
            var printed = await _receipt.PrintZReportAsync(ZReport);
            if (!printed)
            {
                SetStatus("لم يتم إعداد طابعة.", true);
                _dialogs.ShowError("لم يتم إعداد طابعة في الإعدادات.");
                return;
            }

            SetStatus("تم إرسال تقرير Z إلى الطابعة.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Z report printing failed");
            SetStatus("تعذّرت طباعة تقرير Z.", true);
        }
    }

    partial void OnSelectedShiftChanged(Shift? value)
    {
        if (value is not null) LoadZReportCommand.Execute(null);
    }

    private void SetStatus(string message, bool error = false)
    {
        StatusMessage = message;
        IsError = error;
    }
}
