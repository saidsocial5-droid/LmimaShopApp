using System;
using System.Data;
using System.Windows;
using MySqlConnector;
using Polly;
using Polly.Retry;

namespace LmimaShopApp
{
    public class DatabaseHelper
    {
        // إعدادات الاتصال المشفرة (TLS مطلوب)
        private readonly string connectionString = SecurityHelper.GetConnectionString() + (SecurityHelper.GetConnectionString().Contains("SslMode") ? "" : ";SslMode=Preferred;");

        // سياسة إعادة المحاولة الشاملة (Polly) لمعالجة فشل الاتصال المؤقت
        public static readonly RetryPolicy GlobalRetryPolicy = Policy
            .Handle<MySqlException>(ex => ex.IsTransient)
            .WaitAndRetry(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
            (exception, timeSpan, retryCount, context) =>
            {
                Serilog.Log.Warning($"انقطع الاتصال بقاعدة البيانات. المحاولة رقم {retryCount} بعد {timeSpan.TotalSeconds} ثانية. السبب: {exception.Message}");
            });

        // دالة لإنشاء الاتصال
        public MySqlConnection GetConnection()
        {
            return new MySqlConnection(connectionString);
        }

        // دالة لتعديل القيود في قاعدة البيانات للسماح بحذف المنتجات
        public void UpdateDatabaseConstraints()
        {
            using (MySqlConnection conn = GetConnection())
            {
                try
                {
                    conn.Open();
                    
                    // 1. Get constraint name
                    string getConstraintQuery = @"
                        SELECT CONSTRAINT_NAME 
                        FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE 
                        WHERE LOWER(TABLE_SCHEMA) = 'lmimashopdb' 
                          AND LOWER(TABLE_NAME) = 'salesdetails' 
                          AND LOWER(COLUMN_NAME) = 'productid' 
                          AND LOWER(REFERENCED_TABLE_NAME) = 'products'
                        LIMIT 1;";
                    
                    string constraintName = "";
                    using (MySqlCommand cmd = new MySqlCommand(getConstraintQuery, conn))
                    {
                        var result = cmd.ExecuteScalar();
                        if (result != null)
                        {
                            constraintName = result.ToString();
                        }
                    }

                    if (!string.IsNullOrEmpty(constraintName))
                    {
                        // 2. Drop the old constraint
                        string dropQuery = $"ALTER TABLE SalesDetails DROP FOREIGN KEY `{constraintName}`;";
                        using (MySqlCommand cmd = new MySqlCommand(dropQuery, conn))
                        {
                            cmd.ExecuteNonQuery();
                        }
                    }

                    // 3. Add the new constraint with ON DELETE CASCADE
                    string addQuery = @"
                        ALTER TABLE SalesDetails 
                        ADD CONSTRAINT FK_SalesDetails_Products 
                        FOREIGN KEY (ProductId) 
                        REFERENCES Products(Id) 
                        ON DELETE CASCADE;";
                    using (MySqlCommand cmd = new MySqlCommand(addQuery, conn))
                    {
                        cmd.ExecuteNonQuery();
                    }

                    // 4. Alter columns to DECIMAL(10,3) to support weight decimal quantities
                    try
                    {
                        string alterProductsQuery = "ALTER TABLE Products MODIFY COLUMN StockQuantity DECIMAL(10,3) NOT NULL;";
                        using (MySqlCommand cmd = new MySqlCommand(alterProductsQuery, conn))
                        {
                            cmd.ExecuteNonQuery();
                        }

                        string alterDetailsQuery = "ALTER TABLE SalesDetails MODIFY COLUMN Quantity DECIMAL(10,3) NOT NULL;";
                        using (MySqlCommand cmd = new MySqlCommand(alterDetailsQuery, conn))
                        {
                            cmd.ExecuteNonQuery();
                        }

                        // 5. Add CustomName column to SalesDetails to support custom names for manual products
                        try
                        {
                            string checkColQuery = @"
                                SELECT COUNT(*) 
                                FROM INFORMATION_SCHEMA.COLUMNS 
                                WHERE LOWER(TABLE_SCHEMA) = 'lmimashopdb' 
                                  AND LOWER(TABLE_NAME) = 'salesdetails' 
                                  AND LOWER(COLUMN_NAME) = 'customname';";
                            
                            bool hasCustomName = false;
                            using (MySqlCommand cmd = new MySqlCommand(checkColQuery, conn))
                            {
                                hasCustomName = Convert.ToInt64(cmd.ExecuteScalar()) > 0;
                            }

                            if (!hasCustomName)
                            {
                                string addColQuery = "ALTER TABLE SalesDetails ADD COLUMN CustomName VARCHAR(255) NULL;";
                                using (MySqlCommand cmd = new MySqlCommand(addColQuery, conn))
                                {
                                    cmd.ExecuteNonQuery();
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("Error adding CustomName column: " + ex.Message);
                        }

                        // Phase 6 Schema Upgrades
                        try
                        {
                            string[] phase6Queries = new[]
                            {
                                "ALTER TABLE Customers ADD COLUMN LoyaltyPoints INT DEFAULT 0;",
                                "ALTER TABLE Sales ADD COLUMN PaymentMethod VARCHAR(50) DEFAULT 'نقداً';"
                            };
                            foreach (var q in phase6Queries)
                            {
                                try
                                {
                                    using (MySqlCommand cmd = new MySqlCommand(q, conn))
                                        cmd.ExecuteNonQuery();
                                }
                                catch (MySqlException ex) when (ex.Number == 1060) // Duplicate column
                                { }
                            }
                        }
                        catch {}

                        // 6. Create Returns table if not exists
                        try
                        {
                            string createReturnsQuery = @"
                                CREATE TABLE IF NOT EXISTS Returns (
                                    Id INT AUTO_INCREMENT PRIMARY KEY,
                                    InvoiceId INT NULL,
                                    ProductName VARCHAR(255) NOT NULL,
                                    Quantity DECIMAL(10,3) NOT NULL,
                                    RefundAmount DECIMAL(10,2) NOT NULL,
                                    ReturnDate DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                                    CashierName VARCHAR(100) NOT NULL,
                                    Reason VARCHAR(255) NULL
                                );";
                            using (MySqlCommand cmd = new MySqlCommand(createReturnsQuery, conn))
                            {
                                cmd.ExecuteNonQuery();
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("Error creating Returns table: " + ex.Message);
                        }

                        // 7. Create CustomerPayments table if not exists
                        try
                        {
                            string createPaymentsQuery = @"
                                CREATE TABLE IF NOT EXISTS CustomerPayments (
                                    Id INT AUTO_INCREMENT PRIMARY KEY,
                                    CustomerId INT NOT NULL,
                                    CustomerName VARCHAR(100) NOT NULL,
                                    Amount DECIMAL(10,2) NOT NULL,
                                    PaymentDate DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                                    Note VARCHAR(255) NULL,
                                    CashierName VARCHAR(100) NULL,
                                    INDEX idx_cust (CustomerId),
                                    INDEX idx_cust_name (CustomerName),
                                    INDEX idx_date (PaymentDate)
                                );";
                            using (MySqlCommand cmd = new MySqlCommand(createPaymentsQuery, conn))
                            {
                                cmd.ExecuteNonQuery();
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("Error creating CustomerPayments table: " + ex.Message);
                        }

                        // 8. Create CustomerRequestedProducts table if not exists
                        try
                        {
                            string createRequestedProductsQuery = @"
                                CREATE TABLE IF NOT EXISTS CustomerRequestedProducts (
                                    Id INT AUTO_INCREMENT PRIMARY KEY,
                                    ProductName VARCHAR(255) NOT NULL,
                                    Barcode VARCHAR(100) NULL,
                                    Quantity DECIMAL(10,3) NOT NULL DEFAULT 1.000,
                                    CustomerName VARCHAR(150) NULL,
                                    CustomerPhone VARCHAR(50) NULL,
                                    SupplierId INT NULL,
                                    SupplierName VARCHAR(150) NULL,
                                    RequestDate DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                                    Status VARCHAR(50) NOT NULL DEFAULT 'قيد الانتظار',
                                    Priority VARCHAR(50) NOT NULL DEFAULT 'عادي',
                                    Notes VARCHAR(500) NULL,
                                    EstimatedPrice DECIMAL(10,2) NOT NULL DEFAULT 0.00,
                                    INDEX idx_status (Status),
                                    INDEX idx_supplier (SupplierId),
                                    INDEX idx_date (RequestDate)
                                );";
                            using (MySqlCommand cmd = new MySqlCommand(createRequestedProductsQuery, conn))
                            {
                                cmd.ExecuteNonQuery();
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("Error creating CustomerRequestedProducts table: " + ex.Message);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Error modifying column types to decimal: " + ex.Message);
                    }

                    // Create AuditLogs table if not exists
                    try
                    {
                        string createAuditQuery = @"
                            CREATE TABLE IF NOT EXISTS AuditLogs (
                                Id INT AUTO_INCREMENT PRIMARY KEY,
                                UserId INT NOT NULL,
                                Username VARCHAR(255),
                                Action VARCHAR(100),
                                Entity VARCHAR(100),
                                EntityId VARCHAR(100),
                                OldValue TEXT,
                                NewValue TEXT,
                                Timestamp DATETIME DEFAULT CURRENT_TIMESTAMP,
                                DeviceInfo VARCHAR(255)
                            );";
                        using (MySqlCommand cmd = new MySqlCommand(createAuditQuery, conn))
                        {
                            cmd.ExecuteNonQuery();
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Error creating AuditLogs table: " + ex.Message);
                    }

                    // Create StockMovements table (Phase 2)
                    try
                    {
                        string createStockQuery = @"
                            CREATE TABLE IF NOT EXISTS StockMovements (
                                Id INT AUTO_INCREMENT PRIMARY KEY,
                                ProductId INT NOT NULL,
                                MovementType VARCHAR(50) NOT NULL, -- Sale, Return, Adjustment, Purchase
                                Quantity DECIMAL(10,3) NOT NULL,
                                ReferenceId VARCHAR(100), -- InvoiceId or ReturnId
                                Notes VARCHAR(255),
                                UserId INT,
                                Timestamp DATETIME DEFAULT CURRENT_TIMESTAMP,
                                INDEX idx_product (ProductId),
                                INDEX idx_type (MovementType)
                            );";
                        using (MySqlCommand cmd = new MySqlCommand(createStockQuery, conn))
                        {
                            cmd.ExecuteNonQuery();
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Error creating StockMovements table: " + ex.Message);
                    }
                    try
                    {
                        string createShiftsQuery = @"
                            CREATE TABLE IF NOT EXISTS CashRegisterShifts (
                                Id INT AUTO_INCREMENT PRIMARY KEY,
                                OpenedBy INT NOT NULL,
                                OpenedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                                OpeningBalance DECIMAL(10,2) NOT NULL DEFAULT 0,
                                ClosedAt DATETIME NULL,
                                ClosedBy INT NULL,
                                CashSales DECIMAL(10,2) NOT NULL DEFAULT 0,
                                CreditSales DECIMAL(10,2) NOT NULL DEFAULT 0,
                                ExpectedCash DECIMAL(10,2) NOT NULL DEFAULT 0,
                                ActualCash DECIMAL(10,2) NOT NULL DEFAULT 0,
                                Discrepancy DECIMAL(10,2) NOT NULL DEFAULT 0,
                                Status VARCHAR(20) NOT NULL DEFAULT 'Open'
                            );";
                        using (MySqlCommand cmd = new MySqlCommand(createShiftsQuery, conn))
                        {
                            cmd.ExecuteNonQuery();
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Error creating CashRegisterShifts table: " + ex.Message);
                    }

                    // Phase 4: Add ICE and TVA fields to existing tables
                    try
                    {
                        string[] alterQueries = {
                            "ALTER TABLE Customers ADD COLUMN ICE VARCHAR(20) DEFAULT '';",
                            "ALTER TABLE Products ADD COLUMN TvaRate DECIMAL(5,2) DEFAULT 20.00;",
                            "ALTER TABLE Sales ADD COLUMN AmountHT DECIMAL(10,2) DEFAULT 0.00;",
                            "ALTER TABLE Sales ADD COLUMN TvaAmount DECIMAL(10,2) DEFAULT 0.00;"
                        };
                        foreach(var q in alterQueries)
                        {
                            try
                            {
                                using (MySqlCommand cmd = new MySqlCommand(q, conn)) { cmd.ExecuteNonQuery(); }
                            }
                            catch (MySqlException ex)
                            {
                                // Error 1060: Duplicate column name
                                if (ex.Number != 1060)
                                {
                                    Console.WriteLine($"Error executing {q}: " + ex.Message);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Error altering tables for Phase 4: " + ex.Message);
                    }
                }
                catch (Exception ex)
                {
                    // Fail silently or log it
                    Console.WriteLine("Error updating constraints: " + ex.Message);
                }
            }
        }

        public static void LogAudit(string action, string entity, string entityId, string oldValue = "", string newValue = "")
        {
            try
            {
                using (MySqlConnection conn = new DatabaseHelper().GetConnection())
                {
                    conn.Open();
                    string query = @"INSERT INTO AuditLogs (UserId, Username, Action, Entity, EntityId, OldValue, NewValue, DeviceInfo) 
                                     VALUES (@userId, @username, @action, @entity, @entityId, @oldValue, @newValue, @device)";
                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@userId", AppSession.CurrentUserId);
                        cmd.Parameters.AddWithValue("@username", AppSession.CurrentUserName);
                        cmd.Parameters.AddWithValue("@action", action);
                        cmd.Parameters.AddWithValue("@entity", entity);
                        cmd.Parameters.AddWithValue("@entityId", entityId);
                        cmd.Parameters.AddWithValue("@oldValue", oldValue);
                        cmd.Parameters.AddWithValue("@newValue", newValue);
                        cmd.Parameters.AddWithValue("@device", Environment.MachineName);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch { /* Ignore audit failures to not crash the app */ }
        }

        // دالة لاختبار نجاح الاتصال
        public bool TestConnection()
        {
            using (MySqlConnection conn = GetConnection())
            {
                try
                {
                    conn.Open();
                    return true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show("فشل الاتصال بقاعدة البيانات: " + ex.Message, "خطأ في الاتصال", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }
            }
        }

        public void MigrateProductImages()
        {
            using (MySqlConnection conn = GetConnection())
            {
                try
                {
                    conn.Open();
                    string selectQuery = "SELECT Id, Barcode, Category, ImagePath FROM Products WHERE ImagePath IS NOT NULL AND ImagePath <> ''";
                    
                    var productsToMigrate = new System.Collections.Generic.List<(int Id, string Barcode, string Category, string ImagePath)>();
                    using (MySqlCommand cmd = new MySqlCommand(selectQuery, conn))
                    {
                        using (MySqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                int id = Convert.ToInt32(reader["Id"]);
                                string barcode = reader["Barcode"]?.ToString() ?? "";
                                string category = reader["Category"]?.ToString() ?? "";
                                string imagePath = reader["ImagePath"]?.ToString() ?? "";

                                if (System.IO.Path.IsPathRooted(imagePath))
                                {
                                    productsToMigrate.Add((id, barcode, category, imagePath));
                                }
                            }
                        }
                    }

                    string appBaseDir = AppDomain.CurrentDomain.BaseDirectory;

                    foreach (var product in productsToMigrate)
                    {
                        try
                        {
                            string safeCategory = product.Category;
                            foreach (char c in System.IO.Path.GetInvalidFileNameChars())
                            {
                                safeCategory = safeCategory.Replace(c, '_');
                            }
                            if (string.IsNullOrEmpty(safeCategory)) safeCategory = "Uncategorized";

                            string ext = System.IO.Path.GetExtension(product.ImagePath);
                            if (string.IsNullOrEmpty(ext)) ext = ".jpg";

                            string safeBarcode = product.Barcode;
                            foreach (char c in System.IO.Path.GetInvalidFileNameChars())
                            {
                                safeBarcode = safeBarcode.Replace(c, '_');
                            }

                            string targetFileName = safeBarcode + ext;
                            string relativePath = System.IO.Path.Combine("ProductImages", safeCategory, targetFileName);
                            string currentAbsolutePath = System.IO.Path.Combine(appBaseDir, relativePath);

                            bool isMigrated = false;

                            // 1. Check if the file exists at the old absolute path
                            if (System.IO.File.Exists(product.ImagePath))
                            {
                                string targetDir = System.IO.Path.Combine(appBaseDir, "ProductImages", safeCategory);
                                if (!System.IO.Directory.Exists(targetDir))
                                {
                                    System.IO.Directory.CreateDirectory(targetDir);
                                }

                                string targetPath = System.IO.Path.Combine(targetDir, targetFileName);
                                System.IO.File.Copy(product.ImagePath, targetPath, true);
                                isMigrated = true;
                            }
                            // 2. Check if the file exists at the new relative path (already moved)
                            else if (System.IO.File.Exists(currentAbsolutePath))
                            {
                                isMigrated = true;
                            }
                            // 3. Extract relative path structure starting with "ProductImages" if present
                            else
                            {
                                int index = product.ImagePath.IndexOf("ProductImages", StringComparison.OrdinalIgnoreCase);
                                if (index >= 0)
                                {
                                    string extractedRelative = product.ImagePath.Substring(index);
                                    string newAbsolutePath = System.IO.Path.Combine(appBaseDir, extractedRelative);
                                    if (System.IO.File.Exists(newAbsolutePath))
                                    {
                                        relativePath = extractedRelative;
                                        isMigrated = true;
                                    }
                                }
                            }

                            if (isMigrated)
                            {
                                string updateQuery = "UPDATE Products SET ImagePath = @imagePath WHERE Id = @id";
                                using (MySqlCommand updateCmd = new MySqlCommand(updateQuery, conn))
                                {
                                    string formattedRelativePath = relativePath.Replace('\\', '/');
                                    updateCmd.Parameters.AddWithValue("@imagePath", formattedRelativePath);
                                    updateCmd.Parameters.AddWithValue("@id", product.Id);
                                    updateCmd.ExecuteNonQuery();
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Failed to migrate image for product {product.Barcode}: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error running image migration: {ex.Message}");
                }
            }
        }
    }
}