using LmimaShopV2.Desktop.Core.Models;

namespace LmimaShopV2.Desktop.Core.Interfaces;

public interface ICheckoutService
{
    Task<CheckoutResult> CheckoutAsync(
        CheckoutRequest request,
        CheckoutContext context,
        CancellationToken cancellationToken = default);
}

public interface IReturnsService
{
    Task<ReturnResult> ReturnAsync(
        ReturnRequest request,
        ReturnContext context,
        CancellationToken cancellationToken = default);
}
