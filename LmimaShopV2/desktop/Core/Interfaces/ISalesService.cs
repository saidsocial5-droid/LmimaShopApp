using LmimaShopV2.Desktop.Core.Models;

namespace LmimaShopV2.Desktop.Core.Interfaces;

public interface ISalesService
{
    Task<List<SaleDetail>> GetRecentAsync(int limit = 50, CancellationToken cancellationToken = default);
    Task<List<SaleDetail>> SearchAsync(string term, CancellationToken cancellationToken = default);
    Task<SaleDetail?> GetDetailAsync(string saleUuid, CancellationToken cancellationToken = default);
    Task<List<ReturnListItem>> GetRecentReturnsAsync(int limit = 50, CancellationToken cancellationToken = default);
}
