using System.Collections.Generic;
using System.Threading.Tasks;

namespace LmimaShopApp.Core.Domain.Repositories
{
    public interface IProductRepository
    {
        Task<IEnumerable<Product>> GetAllAsync();
        Task<Product?> GetByIdAsync(int id);
        Task<IEnumerable<Product>> SearchAsync(string searchTerm);
        Task UpdateStockAsync(int productId, decimal quantityChange);
    }
}