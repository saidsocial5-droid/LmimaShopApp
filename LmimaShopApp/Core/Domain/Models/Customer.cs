namespace LmimaShopApp.Core.Domain.Models
{
    public class Customer
    {
        public int Id { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public decimal CreditLimit { get; set; }
        public decimal DueBalance { get; set; }
        public bool AllowCredit { get; set; }
        public int InvoiceCount { get; set; }
        public string CreditPeriodType { get; set; } = "Open";
        public int CreditPeriodDays { get; set; }
        public string ICE { get; set; } = string.Empty;
    }
}