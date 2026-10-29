using System;

namespace LmimaShopApp
{
    public static class AppSession
    {
        public static int CurrentUserId { get; set; }
        public static string CurrentUserName { get; set; } = string.Empty;
        public static string CurrentUserRole { get; set; } = string.Empty;
        public static UserPermissions Permissions { get; set; } = new UserPermissions();
    }

    public class UserPermissions
    {
        public bool CanDeleteInvoice { get; set; } = false;
        public bool CanChangePrice { get; set; } = false;
        public bool CanDeleteProduct { get; set; } = false;
        public bool CanViewReports { get; set; } = false;
        public bool CanEditSettings { get; set; } = false;
        public bool CanManageUsers { get; set; } = false;
        
        // Helper to load defaults based on role if no JSON is available
        public static UserPermissions GetDefault(string role)
        {
            bool isManager = role.Equals("مدير", StringComparison.OrdinalIgnoreCase) || role.Equals("admin", StringComparison.OrdinalIgnoreCase);
            return new UserPermissions
            {
                CanDeleteInvoice = isManager,
                CanChangePrice = isManager,
                CanDeleteProduct = isManager,
                CanViewReports = isManager,
                CanEditSettings = isManager,
                CanManageUsers = isManager
            };
        }
    }
}