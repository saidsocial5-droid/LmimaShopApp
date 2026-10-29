using System.Text.Json;

namespace LmimaShopV2.Desktop.Core.Models;

public sealed class SyncRow<T>
{
    public T? D { get; set; }
    public bool Deleted { get; set; }
}

public sealed class SyncResponse
{
    public string Cursor { get; set; } = string.Empty;
    public List<SyncRow<ProductSyncDto>> Products { get; set; } = [];
    public List<SyncRow<CategorySyncDto>> Categories { get; set; } = [];
    public List<SyncRow<CustomerSyncDto>> Customers { get; set; } = [];
    public List<SyncRow<CustomerPaymentSyncDto>> CustomerPayments { get; set; } = [];
    public List<SyncRow<SupplierSyncDto>> Suppliers { get; set; } = [];
    public List<SyncRow<SaleSyncDto>> Sales { get; set; } = [];
    public List<SyncRow<SaleItemSyncDto>> SaleItems { get; set; } = [];
    public List<SyncRow<ReturnSyncDto>> Returns { get; set; } = [];
}

public sealed class ProductSyncDto
{
    public string Uuid { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public string Name { get; set; } = string.Empty;
    public long? CategoryId { get; set; }
    public string? Category { get; set; }
    public decimal CostPrice { get; set; }
    public decimal SellPrice { get; set; }
    public decimal StockQty { get; set; }
    public decimal TvaRate { get; set; }
    public bool IsWeighed { get; set; }
    public string? ImagePath { get; set; }
    public string UpdatedAt { get; set; } = string.Empty;
    public string? DeletedAt { get; set; }
    public int Version { get; set; }
}

public sealed class CategorySyncDto
{
    public string Uuid { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;
    public string? DeletedAt { get; set; }
    public int Version { get; set; }
}

public sealed class CustomerSyncDto
{
    public string Uuid { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public decimal CreditLimit { get; set; }
    public decimal DueBalance { get; set; }
    public int LoyaltyPoints { get; set; }
    public string? Notes { get; set; }
    public string UpdatedAt { get; set; } = string.Empty;
    public string? DeletedAt { get; set; }
    public int Version { get; set; }
}

public sealed class CustomerPaymentSyncDto
{
    public string Uuid { get; set; } = string.Empty;
    public string CustomerUuid { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Method { get; set; } = "cash";
    public string? Notes { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;
    public string? DeletedAt { get; set; }
    public int Version { get; set; }
}

public sealed class SupplierSyncDto
{
    public string Uuid { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? Notes { get; set; }
    public string UpdatedAt { get; set; } = string.Empty;
    public string? DeletedAt { get; set; }
    public int Version { get; set; }
}

public sealed class SaleSyncDto
{
    public string Uuid { get; set; } = string.Empty;
    public string InvoiceNo { get; set; } = string.Empty;
    public string? CustomerUuid { get; set; }
    public string CashierName { get; set; } = string.Empty;
    public decimal Subtotal { get; set; }
    public decimal DiscountPct { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TvaAmount { get; set; }
    public decimal Total { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal ChangeAmount { get; set; }
    public decimal DueAmount { get; set; }
    public int LoyaltyEarned { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;
    public string? DeletedAt { get; set; }
    public int Version { get; set; }
}

public sealed class SaleItemSyncDto
{
    public string Uuid { get; set; } = string.Empty;
    public string SaleUuid { get; set; } = string.Empty;
    public string ProductUuid { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal Qty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountPct { get; set; }
    public decimal LineTotal { get; set; }
    public string UpdatedAt { get; set; } = string.Empty;
    public string? DeletedAt { get; set; }
    public int Version { get; set; }
}

public sealed class ReturnSyncDto
{
    public string Uuid { get; set; } = string.Empty;
    public string SaleUuid { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public decimal RefundTotal { get; set; }
    public decimal CashRefund { get; set; }
    public decimal DueReduction { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;
    public string? DeletedAt { get; set; }
    public int Version { get; set; }
}

public sealed class SyncPushRequest
{
    public List<SyncOperation> Ops { get; set; } = [];
}

public sealed class SyncOperation
{
    public string OpId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? CreatedAt { get; set; }
    public JsonElement Payload { get; set; }
}

public sealed class SyncOpResult
{
    public string OpId { get; set; } = string.Empty;
    public string Status { get; set; } = "error";
    public string? Message { get; set; }
}

public sealed class SyncPushResponse
{
    public List<SyncOpResult> Results { get; set; } = [];
}

public sealed class ProductPushDto
{
    public string Uuid { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Category { get; set; }
    public long? CategoryId { get; set; }
    public decimal CostPrice { get; set; }
    public decimal SellPrice { get; set; }
    public decimal StockQty { get; set; }
    public decimal TvaRate { get; set; } = 20m;
    public bool IsWeighed { get; set; }
    public string? ImagePath { get; set; }
}

public sealed class CustomerPushDto
{
    public string Uuid { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public decimal CreditLimit { get; set; }
    public string? Notes { get; set; }
}

public sealed class SupplierPushDto
{
    public string Uuid { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? Notes { get; set; }
}

public sealed class CustomerPaymentPushDto
{
    public string Uuid { get; set; } = string.Empty;
    public string CustomerUuid { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Method { get; set; } = "cash";
    public string? Notes { get; set; }
}

public sealed class UuidPayload
{
    public string Uuid { get; set; } = string.Empty;
}

public static class SyncOpTypes
{
    public const string ProductSave = "product_save";
    public const string ProductDelete = "product_delete";
    public const string CustomerSave = "customer_save";
    public const string CustomerDelete = "customer_delete";
    public const string CustomerPaymentAdd = "customer_payment_add";
    public const string SupplierSave = "supplier_save";
    public const string SupplierDelete = "supplier_delete";
    public const string SaleCreate = "sale_create";
    public const string ReturnCreate = "return_create";
}
