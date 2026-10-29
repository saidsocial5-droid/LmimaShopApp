using LmimaShopV2.Desktop.Core.Interfaces;
using LmimaShopV2.Desktop.Core.Models;
using LmimaShopV2.Desktop.Helpers;
using LmimaShopV2.Desktop.Infrastructure.Data;

namespace LmimaShopV2.Desktop.Core.Services;

public sealed class ProductService : IProductService
{
    private readonly Db _db;
    private readonly IAuditLogger _audit;
    private readonly ISseHub _sse;

    public ProductService(Db db, IAuditLogger audit, ISseHub sse)
    {
        _db = db;
        _audit = audit;
        _sse = sse;
    }

    public async Task<List<Product>> GetAllAsync(string? search = null, long? categoryId = null, CancellationToken cancellationToken = default)
    {
        var term = string.IsNullOrWhiteSpace(search) ? null : $"%{search.Trim()}%";
        return await _db.QueryAsync<Product>(
            """
            SELECT p.*, c.Name AS Category
            FROM products p
            LEFT JOIN categories c ON c.Id = p.CategoryId
            WHERE p.DeletedAt IS NULL
              AND (@term IS NULL OR p.Name LIKE @term OR p.Barcode LIKE @term)
              AND (@categoryId IS NULL OR p.CategoryId = @categoryId)
            ORDER BY p.Name
            LIMIT 3000
            """,
            new { term, categoryId },
            cancellationToken: cancellationToken);
    }

    public async Task<List<Category>> GetCategoriesAsync(CancellationToken cancellationToken = default) =>
        await _db.QueryAsync<Category>(
            "SELECT * FROM categories WHERE DeletedAt IS NULL ORDER BY Name",
            cancellationToken: cancellationToken);

    public async Task<Product?> GetByBarcodeAsync(string barcode, CancellationToken cancellationToken = default)
    {
        var code = InputValidator.NormalizeBarcode(barcode);
        if (code is null) return null;
        return await _db.QueryFirstOrDefaultAsync<Product>(
            """
            SELECT p.*, c.Name AS Category
            FROM products p LEFT JOIN categories c ON c.Id = p.CategoryId
            WHERE p.Barcode = @code AND p.DeletedAt IS NULL
            ORDER BY p.Id DESC LIMIT 1
            """,
            new { code },
            cancellationToken: cancellationToken);
    }

    public async Task<Product?> GetByUuidAsync(string uuid, CancellationToken cancellationToken = default)
    {
        var id = InputValidator.RequireUuid(uuid, "Produit");
        return await _db.QueryFirstOrDefaultAsync<Product>(
            """
            SELECT p.*, c.Name AS Category
            FROM products p LEFT JOIN categories c ON c.Id = p.CategoryId
            WHERE p.Uuid = @id AND p.DeletedAt IS NULL LIMIT 1
            """,
            new { id },
            cancellationToken: cancellationToken);
    }

    public async Task<long> SaveAsync(Product product, string? actor = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(product);
        var uuid = string.IsNullOrWhiteSpace(product.Uuid)
            ? Db.NewUuid()
            : InputValidator.RequireUuid(product.Uuid, "Produit");
        var name = InputValidator.RequireText(product.Name, "Nom du produit", 160);
        var barcode = InputValidator.NormalizeBarcode(product.Barcode);
        if (product.CostPrice < 0 || product.SellPrice < 0)
            throw new BusinessRuleException("Les prix ne peuvent pas etre negatifs.");
        if (product.StockQuantity < 0)
            throw new BusinessRuleException("Le stock ne peut pas etre negatif.");
        InputValidator.ValidatePercentage(product.TvaRate, "TVA");
        if (product.TvaRate > 100) throw new BusinessRuleException("Taux TVA invalide.");

        await _db.ExecuteAsync(
            """
            INSERT INTO products (Uuid, Barcode, Name, CategoryId, CostPrice, SellPrice, StockQuantity, TvaRate, IsWeighed, ImagePath)
            VALUES (@uuid, @barcode, @name, @categoryId, @costPrice, @sellPrice, @stock, @tva, @weighed, @imagePath)
            ON DUPLICATE KEY UPDATE
              Barcode = VALUES(Barcode), Name = VALUES(Name), CategoryId = VALUES(CategoryId),
              CostPrice = VALUES(CostPrice), SellPrice = VALUES(SellPrice), StockQuantity = VALUES(StockQuantity),
              TvaRate = VALUES(TvaRate), IsWeighed = VALUES(IsWeighed), ImagePath = VALUES(ImagePath)
            """,
            new
            {
                uuid,
                barcode,
                name,
                categoryId = product.CategoryId,
                costPrice = product.CostPrice,
                sellPrice = product.SellPrice,
                stock = product.StockQuantity,
                tva = product.TvaRate,
                weighed = product.IsWeighed,
                imagePath = InputValidator.OptionalText(product.ImagePath, 500)
            },
            cancellationToken: cancellationToken);

        var id = await _db.ExecuteScalarAsync<long>(
            "SELECT Id FROM products WHERE Uuid = @uuid",
            new { uuid },
            cancellationToken: cancellationToken);
        await _audit.LogAsync("product_saved", "products", uuid, $"Produit: {name}", username: actor, cancellationToken: cancellationToken);
        _sse.BroadcastDataChanged();
        return id;
    }

    public async Task DeleteAsync(string uuid, string? actor = null, CancellationToken cancellationToken = default)
    {
        var id = InputValidator.RequireUuid(uuid, "Produit");
        var affected = await _db.ExecuteAsync(
            "UPDATE products SET DeletedAt = NOW(3) WHERE Uuid = @id AND DeletedAt IS NULL",
            new { id },
            cancellationToken: cancellationToken);
        if (affected == 0) throw new BusinessRuleException("Produit introuvable.");
        await _audit.LogAsync("product_deleted", "products", id, null, username: actor, cancellationToken: cancellationToken);
        _sse.BroadcastDataChanged();
    }

    public async Task<long> SaveCategoryAsync(Category category, string? actor = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(category);
        var uuid = string.IsNullOrWhiteSpace(category.Uuid) ? Db.NewUuid() : InputValidator.RequireUuid(category.Uuid, "Categorie");
        var name = InputValidator.RequireText(category.Name, "Nom de categorie", 120);
        await _db.ExecuteAsync(
            """
            INSERT INTO categories (Uuid, Name) VALUES (@uuid, @name)
            ON DUPLICATE KEY UPDATE Name = VALUES(Name)
            """,
            new { uuid, name },
            cancellationToken: cancellationToken);
        var id = await _db.ExecuteScalarAsync<long>("SELECT Id FROM categories WHERE Uuid = @uuid", new { uuid }, cancellationToken: cancellationToken);
        await _audit.LogAsync("category_saved", "categories", uuid, $"Categorie: {name}", username: actor, cancellationToken: cancellationToken);
        _sse.BroadcastDataChanged();
        return id;
    }

    public async Task<long> EnsureCategoryAsync(string name, string? actor = null, CancellationToken cancellationToken = default)
    {
        var normalized = InputValidator.RequireText(name, "Nom de categorie", 120);
        var existing = await _db.QueryFirstOrDefaultAsync<Category>(
            "SELECT * FROM categories WHERE Name = @name AND DeletedAt IS NULL LIMIT 1",
            new { name = normalized },
            cancellationToken: cancellationToken);
        if (existing is not null) return existing.Id;

        var category = new Category { Uuid = Db.NewUuid(), Name = normalized };
        return await SaveCategoryAsync(category, actor, cancellationToken);
    }
}
