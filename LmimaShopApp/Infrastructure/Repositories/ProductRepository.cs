using System.Collections.Generic;
using System.Threading.Tasks;
using Dapper;
using LmimaShopApp.Core.Domain.Repositories;
using MySqlConnector;

namespace LmimaShopApp.Infrastructure.Repositories
{
    public class ProductRepository : IProductRepository
    {
        private readonly string _connectionString;

        public ProductRepository()
        {
            _connectionString = SecurityHelper.GetConnectionString();
        }

        public async Task<IEnumerable<Product>> GetAllAsync()
        {
            using var connection = new MySqlConnection(_connectionString);
            return await connection.QueryAsync<Product>("SELECT * FROM Products WHERE deleted_at IS NULL");
        }

        public async Task<Product?> GetByIdAsync(int id)
        {
            using var connection = new MySqlConnection(_connectionString);
            return await connection.QueryFirstOrDefaultAsync<Product>("SELECT * FROM Products WHERE Id = @Id AND deleted_at IS NULL", new { Id = id });
        }

        public async Task<IEnumerable<Product>> SearchAsync(string searchTerm)
        {
            using var connection = new MySqlConnection(_connectionString);
            string sql = "SELECT * FROM Products WHERE (ProductName LIKE @Term OR Barcode LIKE @Term) AND deleted_at IS NULL";
            return await connection.QueryAsync<Product>(sql, new { Term = $"%{searchTerm}%" });
        }

        public async Task UpdateStockAsync(int productId, decimal quantityChange)
        {
            using var connection = new MySqlConnection(_connectionString);
            string sql = "UPDATE Products SET StockQuantity = StockQuantity + @Change WHERE Id = @Id";
            await connection.ExecuteAsync(sql, new { Change = quantityChange, Id = productId });
        }
    }
}