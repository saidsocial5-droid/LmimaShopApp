using System.Collections.Generic;
using System.Threading.Tasks;
using LmimaShopApp.Core.Domain.Models;

namespace LmimaShopApp.Core.Domain.Repositories
{
    public interface ISaleRepository
    {
        // Add domain model for Sale and SalesDetail eventually, but for now we focus on operations
        Task<int> CreateSaleTransactionAsync(Sale sale, IEnumerable<SaleDetail> details, int? customerId, decimal debtAmount);
    }
}
