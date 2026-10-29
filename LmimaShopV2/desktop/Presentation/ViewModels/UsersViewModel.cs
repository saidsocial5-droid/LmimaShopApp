using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LmimaShopV2.Desktop.Core.Interfaces;
using LmimaShopV2.Desktop.Core.Models;
using LmimaShopV2.Desktop.Presentation.Services;

namespace LmimaShopV2.Desktop.Presentation.ViewModels;

public sealed record RoleOption(string Value, string Label);

public sealed partial class UsersViewModel : ObservableObject
{
    private readonly IAuthService _auth;
    private readonly ISessionService _session;
    private readonly IDialogService _dialogs;

    public UsersViewModel(IAuthService auth, ISessionService session, IDialogService dialogs)
    {
        _auth = auth;
        _session = session;
        _dialogs = dialogs;
        Roles =
        [
            new(UserRoles.Cashier, "أمين صندوق"),
            new(UserRoles.Manager, "مدير"),
            new(UserRoles.Admin, "مسؤول النظام")
        ];
        _selectedRole = Roles[0];
    }

    public ObservableCollection<User> Users { get; } = [];
    public IReadOnlyList<RoleOption> Roles { get; }

    [ObservableProperty]
    private User? _selectedUser;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _fullName = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private RoleOption _selectedRole;

    [ObservableProperty]
    private string _newPassword = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isError;

    [ObservableProperty]
    private bool _isBusy;

    public Task InitializeAsync() => LoadAsync();

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            IsBusy = true;
            var list = await _auth.GetUsersAsync();
            Users.Clear();
            foreach (var user in list) Users.Add(user);
            StatusMessage = $"عدد المستخدمين: {list.Count}";
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
    private async Task CreateAsync()
    {
        try
        {
            IsBusy = true;
            await _auth.CreateUserAsync(Username, FullName, Password, SelectedRole.Value, _session.Actor);
            Username = string.Empty;
            FullName = string.Empty;
            Password = string.Empty;
            await LoadAsync();
            SetStatus("تم إنشاء المستخدم.");
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
    private async Task ResetPasswordAsync()
    {
        if (SelectedUser is null)
        {
            SetStatus("اختر مستخدمًا أولًا.", true);
            return;
        }

        if (string.IsNullOrWhiteSpace(NewPassword))
        {
            SetStatus("أدخل كلمة المرور الجديدة.", true);
            return;
        }

        try
        {
            IsBusy = true;
            await _auth.ResetPasswordAsync(SelectedUser.Id, NewPassword, _session.Actor);
            NewPassword = string.Empty;
            SetStatus("تم تغيير كلمة المرور.");
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
    private async Task ToggleActiveAsync()
    {
        if (SelectedUser is null)
        {
            SetStatus("اختر مستخدمًا أولًا.", true);
            return;
        }

        var target = !SelectedUser.IsActive;
        var action = target ? "تفعيل" : "تعطيل";
        if (!_dialogs.Confirm($"{action} المستخدم \"{SelectedUser.Username}\"؟")) return;

        try
        {
            IsBusy = true;
            await _auth.SetActiveAsync(SelectedUser.Id, target, _session.Actor);
            await LoadAsync();
            SelectedUser = Users.FirstOrDefault(u => u.Username == SelectedUser.Username);
            SetStatus($"تم {action} المستخدم.");
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

    partial void OnSelectedUserChanged(User? value)
    {
        if (value is null) return;
        Username = value.Username;
        FullName = value.FullName;
        SelectedRole = Roles.FirstOrDefault(r => r.Value == value.Role) ?? Roles[0];
    }

    private void SetStatus(string message, bool error = false)
    {
        StatusMessage = message;
        IsError = error;
    }
}
