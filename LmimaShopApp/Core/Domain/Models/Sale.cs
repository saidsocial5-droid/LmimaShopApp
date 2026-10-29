using System;

namespace LmimaShopApp.Core.Domain.Models
{
    public class Sale
    {
        public int Id { get; set; }
        public decimal TotalAmount { get; set; }
        public string CashierName { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public string Status { get; set; } = string.Empty;
    }
}
