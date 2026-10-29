using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace LmimaShopApp
{
    public static class SecurityHelper
    {
        private static readonly string SecureFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LmimaShop", "secure_db.dat");

        public static void SaveConnectionString(string connectionString)
        {
            try
            {
                string directory = Path.GetDirectoryName(SecureFilePath);
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

                byte[] plainBytes = Encoding.UTF8.GetBytes(connectionString);
                byte[] encryptedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(SecureFilePath, encryptedBytes);
            }
            catch (Exception ex)
            {
                throw new Exception("فشل حفظ سلسلة الاتصال المشفرة: " + ex.Message);
            }
        }

        public static string GetConnectionString()
        {
            try
            {
                if (!File.Exists(SecureFilePath))
                {
                    // Fallback to initial plain text for migration if needed, but in production we should prompt for it.
                    // For now, return a default safe string or throw.
                    return "Server=127.0.0.1;Port=3306;Database=LmimaShopDB;Uid=root;Pwd=;";
                }

                byte[] encryptedBytes = File.ReadAllBytes(SecureFilePath);
                byte[] plainBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plainBytes);
            }
            catch
            {
                return "Server=127.0.0.1;Port=3306;Database=LmimaShopDB;Uid=root;Pwd=;";
            }
        }
    }
}