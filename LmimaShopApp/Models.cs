namespace LmimaShopApp
{
    /// <summary>
    /// كائن المنتج بالمخزن
    /// </summary>
    public class Product
    {
        public int Id { get; set; }
        public string Barcode { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string SubCategory { get; set; } = string.Empty;
        public decimal PurchasePrice { get; set; }
        public decimal SellPrice { get; set; }
        public decimal StockQuantity { get; set; }
        public string? ImagePath { get; set; }
    }

    /// <summary>
    /// كائن عنصر سلة المشتريات
    /// </summary>
    public class CartItem
    {
        public int ProductId { get; set; }
        public string Barcode { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public decimal SellPrice { get; set; }
        public decimal Quantity { get; set; }
        public decimal DiscountPercent { get; set; } // نسبة الخصم
        public decimal Total => (SellPrice * Quantity) * (1 - DiscountPercent / 100);
    }

    /// <summary>
    /// كائن المنتوج المطلوب من الزبائن وغير المتوفر بالمخزن
    /// </summary>
    public class RequestedProduct
    {
        public int Id { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string Barcode { get; set; } = string.Empty;
        public decimal Quantity { get; set; } = 1m;
        public string CustomerName { get; set; } = string.Empty;
        public string CustomerPhone { get; set; } = string.Empty;
        public int? SupplierId { get; set; }
        public string SupplierName { get; set; } = string.Empty;
        public DateTime RequestDate { get; set; } = DateTime.Now;
        public string Status { get; set; } = "قيد الانتظار"; // قيد الانتظار، تم الطلب من الممون، تم التوفير بالمخزن، ملغى
        public string Priority { get; set; } = "عادي"; // عادي، عاجل، مهم
        public decimal EstimatedPrice { get; set; }
        public string Notes { get; set; } = string.Empty;

        // خصائص مساعدة للعرض
        public string FormattedDate => RequestDate.ToString("yyyy-MM-dd HH:mm");

        public string StatusBackground => Status switch
        {
            "قيد الانتظار" => "#FEF3C7",
            "تم الطلب من الممون" => "#DBEAFE",
            "تم التوفير بالمخزن" => "#D1FAE5",
            "ملغى" => "#FEE2E2",
            _ => "#F3F4F6"
        };

        public string StatusForeground => Status switch
        {
            "قيد الانتظار" => "#B45309",
            "تم الطلب من الممون" => "#1D4ED8",
            "تم التوفير بالمخزن" => "#047857",
            "ملغى" => "#B91C1C",
            _ => "#374151"
        };

        public string PriorityBackground => Priority switch
        {
            "عاجل" => "#FEE2E2",
            "مهم" => "#FFEDD5",
            _ => "#F1F5F9"
        };

        public string PriorityForeground => Priority switch
        {
            "عاجل" => "#DC2626",
            "مهم" => "#C2410C",
            _ => "#475569"
        };
    }
}

