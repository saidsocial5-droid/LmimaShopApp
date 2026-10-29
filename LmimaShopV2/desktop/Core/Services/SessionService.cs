using LmimaShopV2.Desktop.Core.Interfaces;
using LmimaShopV2.Desktop.Core.Models;

namespace LmimaShopV2.Desktop.Core.Services;

public sealed class SessionService : ISessionService
{
    public AuthenticatedUser? CurrentUser { get; private set; }
    public bool IsAuthenticated => CurrentUser is not null;
    public string Actor => CurrentUser?.Username ?? "system";
    public event EventHandler? LoggedOut;

    public void SignIn(AuthenticatedUser user) => CurrentUser = user;

    public void SignOut()
    {
        CurrentUser = null;
        LoggedOut?.Invoke(this, EventArgs.Empty);
    }
}
