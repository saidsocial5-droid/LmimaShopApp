using LmimaShopV2.Desktop.Core.Models;

namespace LmimaShopV2.Desktop.Core.Interfaces;

public interface IAuthService
{
    Task<AuthenticatedUser> LoginAsync(string username, string password, CancellationToken cancellationToken = default);
    Task<List<User>> GetUsersAsync(CancellationToken cancellationToken = default);
    Task<long> CreateUserAsync(
        string username,
        string fullName,
        string password,
        string role,
        string? actor = null,
        CancellationToken cancellationToken = default);
    Task ResetPasswordAsync(long userId, string newPassword, string? actor = null, CancellationToken cancellationToken = default);
    Task SetActiveAsync(long userId, bool isActive, string? actor = null, CancellationToken cancellationToken = default);
}

public interface IShiftService
{
    Task<Shift?> GetOpenShiftAsync(CancellationToken cancellationToken = default);
    Task<Shift?> GetShiftAsync(long shiftId, CancellationToken cancellationToken = default);
    Task<List<Shift>> GetRecentAsync(int limit = 30, CancellationToken cancellationToken = default);
    Task<Shift> OpenAsync(long userId, string cashierName, decimal openingFloat, CancellationToken cancellationToken = default);
    Task<ShiftReport> CloseAsync(
        long shiftId,
        decimal countedCash,
        string? notes,
        long userId,
        string cashierName,
        CancellationToken cancellationToken = default);
    Task<ShiftReport> GetReportAsync(long shiftId, CancellationToken cancellationToken = default);
}

public interface IReportService
{
    Task<ShiftReport> GetZReportAsync(long shiftId, CancellationToken cancellationToken = default);
    Task<DailyReport> GetDailyReportAsync(DateTime date, CancellationToken cancellationToken = default);
}

public interface ISyncService
{
    Task<SyncResponse> GetDeltaAsync(string? since, CancellationToken cancellationToken = default);
    Task<SyncPushResponse> PushAsync(SyncPushRequest request, string? actor = null, CancellationToken cancellationToken = default);
}

public interface ISettingsService
{
    Task<Dictionary<string, string>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<string?> GetValueAsync(string key, CancellationToken cancellationToken = default);
    Task<decimal> GetDecimalAsync(string key, decimal fallback, CancellationToken cancellationToken = default);
    Task SetValueAsync(
        string key,
        string value,
        bool audit = true,
        string? actor = null,
        CancellationToken cancellationToken = default);
    Task SetManyAsync(
        IReadOnlyDictionary<string, string> values,
        string? actor = null,
        CancellationToken cancellationToken = default);
    Task<StoreSettings> GetStoreSettingsAsync(CancellationToken cancellationToken = default);
    Task<bool> IsAdminNoticeShownAsync(CancellationToken cancellationToken = default);
    Task MarkAdminNoticeShownAsync(CancellationToken cancellationToken = default);
}

public interface IAuditLogger
{
    Task LogAsync(
        string action,
        string entityType,
        string? entityUuid = null,
        string? details = null,
        long? userId = null,
        string? username = null,
        CancellationToken cancellationToken = default);
}

public interface ISseHub
{
    void BroadcastDataChanged();
}

public interface ISessionService
{
    AuthenticatedUser? CurrentUser { get; }
    bool IsAuthenticated { get; }
    string Actor { get; }
    event EventHandler? LoggedOut;
    void SignIn(AuthenticatedUser user);
    void SignOut();
}
