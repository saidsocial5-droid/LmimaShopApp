using LmimaShopV2.Desktop.Core.Models;

namespace LmimaShopV2.Desktop.Core.Interfaces;

public interface IProductService
{
    Task<List<Product>> GetAllAsync(string? search = null, long? categoryId = null, CancellationToken cancellationToken = default);
    Task<List<Category>> GetCategoriesAsync(CancellationToken cancellationToken = default);
    Task<Product?> GetByBarcodeAsync(string barcode, CancellationToken cancellationToken = default);
    Task<Product?> GetByUuidAsync(string uuid, CancellationToken cancellationToken = default);
    Task<long> SaveAsync(Product product, string? actor = null, CancellationToken cancellationToken = default);
    Task DeleteAsync(string uuid, string? actor = null, CancellationToken cancellationToken = default);
    Task<long> SaveCategoryAsync(Category category, string? actor = null, CancellationToken cancellationToken = default);
    Task<long> EnsureCategoryAsync(string name, string? actor = null, CancellationToken cancellationToken = default);
}

public interface ICustomerService
{
    Task<List<Customer>> SearchAsync(string? search = null, CancellationToken cancellationToken = default);
    Task<Customer?> GetByUuidAsync(string uuid, CancellationToken cancellationToken = default);
    Task<long> SaveAsync(Customer customer, string? actor = null, CancellationToken cancellationToken = default);
    Task DeleteAsync(string uuid, string? actor = null, CancellationToken cancellationToken = default);
    Task<long> AddPaymentAsync(
        string customerUuid,
        decimal amount,
        string method,
        string? notes,
        long? userId = null,
        string? actor = null,
        CancellationToken cancellationToken = default);
    Task<CustomerStatement> GetStatementAsync(string customerUuid, CancellationToken cancellationToken = default);
}

public interface ISupplierService
{
    Task<List<Supplier>> SearchAsync(string? search = null, CancellationToken cancellationToken = default);
    Task<long> SaveAsync(Supplier supplier, string? actor = null, CancellationToken cancellationToken = default);
    Task DeleteAsync(string uuid, string? actor = null, CancellationToken cancellationToken = default);
}
