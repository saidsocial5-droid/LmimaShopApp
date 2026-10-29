using System.Collections.Generic;
using System.Threading.Tasks;
using Dapper;
using LmimaShopApp.Core.Domain.Models;
using LmimaShopApp.Core.Domain.Repositories;
using MySqlConnector;

namespace LmimaShopApp.Infrastructure.Repositories
{
    public class CustomerRepository : ICustomerRepository
    {
        private readonly string _connectionString;

        public CustomerRepository()
        {
            _connectionString = SecurityHelper.GetConnectionString();
        }

        public async Task<IEnumerable<Customer>> GetAllAsync()
        {
            using var connection = new MySqlConnection(_connectionString);
            return await connection.QueryAsync<Customer>("SELECT * FROM Customers WHERE deleted_at IS NULL");
        }

        public async Task<Customer?> GetByIdAsync(int id)
        {
            using var connection = new MySqlConnection(_connectionString);
            return await connection.QueryFirstOrDefaultAsync<Customer>("SELECT * FROM Customers WHERE Id = @Id AND deleted_at IS NULL", new { Id = id });
        }

        public async Task<Customer?> GetByNameAsync(string fullName)
        {
            using var connection = new MySqlConnection(_connectionString);
            return await connection.QueryFirstOrDefaultAsync<Customer>("SELECT * FROM Customers WHERE FullName = @Name AND deleted_at IS NULL", new { Name = fullName });
        }

        public async Task AddAsync(Customer customer)
        {
            using var connection = new MySqlConnection(_connectionString);
            string sql = @"INSERT INTO Customers (FullName, Phone, CreditLimit, DueBalance, AllowCredit, CreditPeriodType, CreditPeriodDays) 
                           VALUES (@FullName, @Phone, @CreditLimit, @DueBalance, @AllowCredit, @CreditPeriodType, @CreditPeriodDays)";
            await connection.ExecuteAsync(sql, customer);
        }

        public async Task UpdateDebtAsync(int customerId, decimal debtAmount)
        {
            using var connection = new MySqlConnection(_connectionString);
            string sql = "UPDATE Customers SET DueBalance = DueBalance + @DebtAmount WHERE Id = @Id";
            await connection.ExecuteAsync(sql, new { DebtAmount = debtAmount, Id = customerId });
        }
    }
}