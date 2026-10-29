using System.Collections.Generic;
using System.Threading.Tasks;
using LmimaShopApp.Core.Domain.Models;

namespace LmimaShopApp.Core.Domain.Repositories
{
    public interface ICustomerRepository
    {
        Task<IEnumerable<Customer>> GetAllAsync();
        Task<Customer?> GetByIdAsync(int id);
        Task<Customer?> GetByNameAsync(string fullName);
        Task AddAsync(Customer customer);
        Task UpdateDebtAsync(int customerId, decimal debtAmount);
    }
}