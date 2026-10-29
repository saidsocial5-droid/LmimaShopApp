using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dapper;
using LmimaShopApp.Core.Domain.Models;
using LmimaShopApp.Core.Domain.Repositories;
using MySqlConnector;

namespace LmimaShopApp.Infrastructure.Repositories
{
    public class SaleRepository : ISaleRepository
    {
        private readonly string _connectionString;

        public SaleRepository()
        {
            _connectionString = SecurityHelper.GetConnectionString();
        }

        public async Task<int> CreateSaleTransactionAsync(Sale sale, IEnumerable<SaleDetail> details, int? customerId, decimal debtAmount)
        {
            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            using var transaction = await connection.BeginTransactionAsync();

            try
            {
                // 1. Insert Sale
                string insertSaleSql = @"INSERT INTO Sales (TotalAmount, CashierName, CustomerName, Date, Status) 
                                         VALUES (@TotalAmount, @CashierName, @CustomerName, NOW(), @Status);
                                         SELECT LAST_INSERT_ID();";
                int saleId = await connection.ExecuteScalarAsync<int>(insertSaleSql, sale, transaction);

                // 2. Process Details
                string insertDetailSql = @"INSERT INTO SalesDetails (SaleId, ProductId, ProductName, Quantity, UnitPrice, SubTotal) 
                                           VALUES (@SaleId, @ProductId, @ProductName, @Quantity, @UnitPrice, @SubTotal)";
                
                string updateStockSql = @"UPDATE Products SET StockQuantity = StockQuantity - @Quantity WHERE Id = @ProductId AND StockQuantity >= @Quantity";
                
                string insertMovementSql = @"INSERT INTO StockMovements (ProductId, MovementType, Quantity, ReferenceId, Notes, UserId) 
                                             VALUES (@ProductId, 'Sale', @NegativeQty, @ReferenceId, 'فاتورة مبيعات', @UserId)";

                foreach (var detail in details)
                {
                    detail.SaleId = saleId;
                    await connection.ExecuteAsync(insertDetailSql, detail, transaction);

                    int rowsAffected = await connection.ExecuteAsync(updateStockSql, new { detail.Quantity, detail.ProductId }, transaction);
                    if (rowsAffected == 0)
                    {
                        throw new Exception($"الكمية غير متوفرة لـ {detail.ProductName}.");
                    }

                    await connection.ExecuteAsync(insertMovementSql, new 
                    { 
                        detail.ProductId, 
                        NegativeQty = -detail.Quantity, 
                        ReferenceId = saleId.ToString(), 
                        UserId = AppSession.CurrentUserId 
                    }, transaction);
                }

                // 3. Update Customer Debt
                if (debtAmount > 0 && customerId.HasValue && customerId.Value > 0)
                {
                    string updateDebtSql = "UPDATE Customers SET TotalDebt = TotalDebt + @DebtAmount WHERE Id = @Id";
                    await connection.ExecuteAsync(updateDebtSql, new { DebtAmount = debtAmount, Id = customerId.Value }, transaction);
                }

                await transaction.CommitAsync();
                return saleId;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
    }
}