using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MySqlConnector;

namespace LmimaShopApp
{
    public static class CloudSyncService
    {
        private static DateTime _lastSyncTime = DateTime.MinValue;
        private static readonly object _syncLock = new object();

        /// <summary>
        /// مزامنة وتصدير بيانات الهاتف السحابية بشكل غير متزامن في الخلفية
        /// </summary>
        public static Task<bool> SyncMobileDataAsync(bool force = false)
        {
            return Task.Run(() => SyncMobileData(force));
        }

        /// <summary>
        /// الحصول على كائن بيانات المزامنة محدثاً في الذاكرة
        /// </summary>
        /// <summary>
        /// قراءة إعدادات المتجر من settings.json بجانب البرنامج، ثم من النسخة المستمرة في AppData
        /// </summary>
        public static AppSettings LoadAppSettings()
        {
            string[] candidates =
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LmimaShop", "settings.json")
            };

            foreach (var path in candidates)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path));
                        if (s != null) return s;
                    }
                }
                catch { }
            }
            return new AppSettings();
        }

        public static MobileSyncPayload GenerateSyncPayload()
        {
            AppSettings settings = LoadAppSettings();

            var dbHelper = new DatabaseHelper();
            DateTime today = DateTime.Today;
            DateTime tomorrow = today.AddDays(1);

            var syncPayload = new MobileSyncPayload
            {
                StoreInfo = new MobileStoreInfo
                {
                    StoreName = settings.StoreName,
                    StorePhone = settings.StorePhone,
                    StoreAddress = settings.StoreAddress,
                    LastSyncTimestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                }
            };

            using (var conn = dbHelper.GetConnection())
            {
                conn.Open();

                // 1. حساب ملخص مبيعات وأرباح اليوم (Today's Summary)
                decimal totalSales = 0;
                decimal cashSales = 0;
                decimal creditSales = 0;
                int invoiceCount = 0;
                decimal totalProfit = 0;

                string todayQuery = @"
                    SELECT s.InvoiceId, s.CashierName, s.TotalAmount, s.Date,
                           COALESCE((
                               SELECT SUM(sd.SubTotal - (sd.Quantity * COALESCE(p.PurchasePrice, 0)))
                               FROM SalesDetails sd
                               LEFT JOIN Products p ON sd.ProductId = p.Id
                               WHERE sd.InvoiceId = s.InvoiceId
                           ), 0) AS Profit
                    FROM Sales s
                    WHERE s.Date >= @from AND s.Date < @to
                    ORDER BY s.Date DESC;";

                using (var cmd = new MySqlCommand(todayQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@from", today);
                    cmd.Parameters.AddWithValue("@to", tomorrow);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            decimal amt = Convert.ToDecimal(reader["TotalAmount"]);
                            decimal prof = Convert.ToDecimal(reader["Profit"]);
                            string rawCashier = reader["CashierName"]?.ToString() ?? "";

                            totalSales += amt;
                            totalProfit += prof;
                            invoiceCount++;

                            bool isCredit = false;
                            if (rawCashier.Contains("(") && rawCashier.Contains(")"))
                            {
                                int o = rawCashier.IndexOf('(');
                                int c = rawCashier.IndexOf(')');
                                if (o >= 0 && c > o)
                                {
                                    string cust = rawCashier.Substring(o + 1, c - o - 1).Trim();
                                    if (cust != "زبون نقدي عابر" && cust != "زبون نقدي افتراضي" && cust != "زبون نقدي")
                                    {
                                        isCredit = true;
                                    }
                                }
                            }

                            if (isCredit) creditSales += amt;
                            else cashSales += amt;
                        }
                    }
                }

                syncPayload.TodaySummary = new MobileTodaySummary
                {
                    Date = today.ToString("yyyy-MM-dd"),
                    TotalSales = totalSales,
                    TotalSalesRial = totalSales * 20,
                    CashSales = cashSales,
                    CreditSales = creditSales,
                    InvoicesCount = invoiceCount,
                    EstimatedProfit = totalProfit,
                    EstimatedProfitRial = totalProfit * 20
                };

                // 2. جلب آخر 50 فاتورة حديثة (Recent Invoices)
                string recentInvoicesQuery = @"
                    SELECT s.InvoiceId, s.CashierName, s.TotalAmount, s.Date,
                           COALESCE((SELECT SUM(Quantity) FROM SalesDetails WHERE InvoiceId = s.InvoiceId), 0) AS TotalItems
                    FROM Sales s
                    ORDER BY s.Date DESC
                    LIMIT 50;";

                using (var cmd = new MySqlCommand(recentInvoicesQuery, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        int invId = Convert.ToInt32(reader["InvoiceId"]);
                        string rawCashier = reader["CashierName"]?.ToString() ?? "";
                        decimal amt = Convert.ToDecimal(reader["TotalAmount"]);
                        DateTime dt = Convert.ToDateTime(reader["Date"]);
                        decimal items = Convert.ToDecimal(reader["TotalItems"]);

                        string cashier = rawCashier;
                        string customer = "زبون نقدي";
                        if (rawCashier.Contains("(") && rawCashier.Contains(")"))
                        {
                            int o = rawCashier.IndexOf('(');
                            int c = rawCashier.IndexOf(')');
                            if (o >= 0 && c > o)
                            {
                                cashier = rawCashier.Substring(0, o).Trim();
                                customer = rawCashier.Substring(o + 1, c - o - 1).Trim();
                            }
                        }

                        string method = (customer == "زبون نقدي عابر" || customer == "زبون نقدي افتراضي" || customer == "زبون نقدي") ? "كاش" : "كريدي (أجل)";

                        syncPayload.RecentInvoices.Add(new MobileInvoiceItem
                        {
                            InvoiceId = invId,
                            Date = dt.ToString("yyyy-MM-dd HH:mm"),
                            Cashier = cashier,
                            Customer = customer,
                            TotalAmount = amt,
                            TotalAmountRial = amt * 20,
                            PaymentMethod = method,
                            ItemsCount = items
                        });
                    }
                }

                // 2.1 جلب أسماء وأثمنة السلع لكل فاتورة من الفواتير الحديثة
                if (syncPayload.RecentInvoices.Count > 0)
                {
                    string idList = string.Join(",", syncPayload.RecentInvoices.Select(i => i.InvoiceId));
                    string detailsQuery = $@"
                        SELECT sd.InvoiceId, COALESCE(sd.CustomName, p.ProductName) AS ProdName,
                               sd.Quantity, sd.SubTotal,
                               COALESCE(p.SellPrice, CASE WHEN sd.Quantity > 0 THEN sd.SubTotal / sd.Quantity ELSE 0 END) AS UnitPrice
                        FROM SalesDetails sd
                        LEFT JOIN Products p ON sd.ProductId = p.Id
                        WHERE sd.InvoiceId IN ({idList})
                        ORDER BY sd.Id ASC;";

                    var detailsMap = new Dictionary<int, List<MobileInvoiceDetailItem>>();
                    using (var cmdDetails = new MySqlCommand(detailsQuery, conn))
                    using (var readerDetails = cmdDetails.ExecuteReader())
                    {
                        while (readerDetails.Read())
                        {
                            int invId = Convert.ToInt32(readerDetails["InvoiceId"]);
                            string pName = readerDetails["ProdName"]?.ToString() ?? "سلعة";
                            decimal qty = readerDetails["Quantity"] != DBNull.Value ? Convert.ToDecimal(readerDetails["Quantity"]) : 1;
                            decimal subTotal = readerDetails["SubTotal"] != DBNull.Value ? Convert.ToDecimal(readerDetails["SubTotal"]) : 0;
                            decimal unitPrice = readerDetails["UnitPrice"] != DBNull.Value ? Convert.ToDecimal(readerDetails["UnitPrice"]) : (qty > 0 ? subTotal / qty : 0);

                            if (!detailsMap.ContainsKey(invId))
                            {
                                detailsMap[invId] = new List<MobileInvoiceDetailItem>();
                            }

                            detailsMap[invId].Add(new MobileInvoiceDetailItem
                            {
                                ProductName = pName,
                                Quantity = qty,
                                UnitPrice = unitPrice,
                                SubTotal = subTotal
                            });
                        }
                    }

                    foreach (var inv in syncPayload.RecentInvoices)
                    {
                        if (detailsMap.TryGetValue(inv.InvoiceId, out var items))
                        {
                            inv.Items = items;
                        }
                    }
                }

                // 3. جلب ديون الزبائن "الكريدي" (Customer Debts)
                string customersQuery = @"
                    SELECT Id, FullName, Phone, DueBalance, CreditLimit, InvoiceCount, LastCreditDate
                    FROM Customers
                    WHERE DueBalance > 0 AND deleted_at IS NULL
                    ORDER BY DueBalance DESC;";

                using (var cmd = new MySqlCommand(customersQuery, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        int id = Convert.ToInt32(reader["Id"]);
                        string name = reader["FullName"]?.ToString() ?? "";
                        string phone = reader["Phone"]?.ToString() ?? "";
                        decimal balance = Convert.ToDecimal(reader["DueBalance"]);
                        decimal limit = reader["CreditLimit"] != DBNull.Value ? Convert.ToDecimal(reader["CreditLimit"]) : 0;
                        int invCount = Convert.ToInt32(reader["InvoiceCount"]);
                        string lastDate = reader["LastCreditDate"] != DBNull.Value ? Convert.ToDateTime(reader["LastCreditDate"]).ToString("yyyy-MM-dd") : "";

                        syncPayload.CustomerDebts.Add(new MobileCustomerDebt
                        {
                            Id = id,
                            FullName = name,
                            Phone = phone,
                            DueBalance = balance,
                            DueBalanceRial = balance * 20,
                            CreditLimit = limit,
                            InvoiceCount = invCount,
                            LastCreditDate = lastDate
                        });
                    }
                }

                // 3.1 جلب السلع المشتراة بالكريدي مؤخراً لكل زبون مدين
                if (syncPayload.CustomerDebts.Count > 0)
                {
                    string custDebtsItemsQuery = @"
                        SELECT s.CashierName, COALESCE(sd.CustomName, p.ProductName) AS ProdName,
                               sd.Quantity, sd.SubTotal, s.Date
                        FROM SalesDetails sd
                        JOIN Sales s ON sd.InvoiceId = s.InvoiceId
                        LEFT JOIN Products p ON sd.ProductId = p.Id
                        WHERE s.Date >= DATE_SUB(NOW(), INTERVAL 180 DAY)
                        ORDER BY s.Date DESC;";

                    var custPurchasesMap = new Dictionary<string, List<MobileCustomerDebtItem>>(StringComparer.OrdinalIgnoreCase);
                    using (var cmdCust = new MySqlCommand(custDebtsItemsQuery, conn))
                    using (var readerCust = cmdCust.ExecuteReader())
                    {
                        while (readerCust.Read())
                        {
                            string rawCashier = readerCust["CashierName"]?.ToString() ?? "";
                            string customerName = "";
                            if (rawCashier.Contains("(") && rawCashier.Contains(")"))
                            {
                                int o = rawCashier.IndexOf('(');
                                int c = rawCashier.IndexOf(')');
                                if (o >= 0 && c > o)
                                {
                                    customerName = rawCashier.Substring(o + 1, c - o - 1).Trim();
                                }
                            }

                            if (string.IsNullOrEmpty(customerName) || customerName.Contains("نقدي")) continue;

                            string prodName = readerCust["ProdName"]?.ToString() ?? "سلعة";
                            decimal qty = readerCust["Quantity"] != DBNull.Value ? Convert.ToDecimal(readerCust["Quantity"]) : 1;
                            decimal subTotal = readerCust["SubTotal"] != DBNull.Value ? Convert.ToDecimal(readerCust["SubTotal"]) : 0;
                            string dt = Convert.ToDateTime(readerCust["Date"]).ToString("yyyy-MM-dd HH:mm");

                            if (!custPurchasesMap.ContainsKey(customerName))
                            {
                                custPurchasesMap[customerName] = new List<MobileCustomerDebtItem>();
                            }

                            if (custPurchasesMap[customerName].Count < 50)
                            {
                                custPurchasesMap[customerName].Add(new MobileCustomerDebtItem
                                {
                                    ProductName = prodName,
                                    Quantity = qty,
                                    Price = qty > 0 ? (subTotal / qty) : subTotal,
                                    SubTotal = subTotal,
                                    Date = dt
                                });
                            }
                        }
                    }

                    foreach (var cust in syncPayload.CustomerDebts)
                    {
                        if (custPurchasesMap.TryGetValue(cust.FullName, out var items))
                        {
                            cust.RecentPurchases = items;
                        }
                    }

                    // 3.2 جلب المدفوعات والتسديدات مؤخراً لكل زبون مدين
                    string custPaymentsQuery = @"
                        SELECT CustomerId, CustomerName, Amount, PaymentDate, COALESCE(Note, 'تسديد نقدي للكريدي') AS Note, COALESCE(CashierName, 'المسؤول') AS CashierName
                        FROM CustomerPayments
                        WHERE PaymentDate >= DATE_SUB(NOW(), INTERVAL 180 DAY)
                        ORDER BY PaymentDate DESC;";

                    var custPaymentsMap = new Dictionary<string, List<MobileCustomerPaymentItem>>(StringComparer.OrdinalIgnoreCase);
                    try
                    {
                        using (var cmdPay = new MySqlCommand(custPaymentsQuery, conn))
                        using (var readerPay = cmdPay.ExecuteReader())
                        {
                            while (readerPay.Read())
                            {
                                string custName = readerPay["CustomerName"]?.ToString() ?? "";
                                if (string.IsNullOrEmpty(custName)) continue;

                                decimal amt = readerPay["Amount"] != DBNull.Value ? Convert.ToDecimal(readerPay["Amount"]) : 0;
                                string dt = Convert.ToDateTime(readerPay["PaymentDate"]).ToString("yyyy-MM-dd HH:mm");
                                string note = readerPay["Note"]?.ToString() ?? "تسديد نقدي";
                                string cashier = readerPay["CashierName"]?.ToString() ?? "المسؤول";

                                if (!custPaymentsMap.ContainsKey(custName))
                                {
                                    custPaymentsMap[custName] = new List<MobileCustomerPaymentItem>();
                                }

                                if (custPaymentsMap[custName].Count < 50)
                                {
                                    custPaymentsMap[custName].Add(new MobileCustomerPaymentItem
                                    {
                                        Amount = amt,
                                        Date = dt,
                                        Note = note,
                                        CashierName = cashier
                                    });
                                }
                            }
                        }

                        foreach (var cust in syncPayload.CustomerDebts)
                        {
                            if (custPaymentsMap.TryGetValue(cust.FullName, out var pList))
                            {
                                cust.RecentPayments = pList;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Error reading CustomerPayments: " + ex.Message);
                    }
                }

                // 4. جلب جميع المنتجات للبحث والباركود وقائمة الأسعار (Products Catalog)
                string productsQuery = @"
                    SELECT Id, uuid, Barcode, ProductName, SellPrice, PurchasePrice, StockQuantity, Category, ImagePath
                    FROM Products
                    WHERE Barcode <> 'MANUAL' AND deleted_at IS NULL
                    ORDER BY ProductName ASC;";

                using (var cmd = new MySqlCommand(productsQuery, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        decimal sellPrice = reader["SellPrice"] != DBNull.Value ? Convert.ToDecimal(reader["SellPrice"]) : 0;
                        decimal buyPrice = reader["PurchasePrice"] != DBNull.Value ? Convert.ToDecimal(reader["PurchasePrice"]) : 0;
                        decimal stock = reader["StockQuantity"] != DBNull.Value ? Convert.ToDecimal(reader["StockQuantity"]) : 0;

                        syncPayload.Products.Add(new MobileProductItem
                        {
                            Id = Convert.ToInt32(reader["Id"]),
                            Uuid = reader["uuid"]?.ToString() ?? "",
                            Barcode = reader["Barcode"]?.ToString() ?? "",
                            Name = reader["ProductName"]?.ToString() ?? "",
                            Price = sellPrice,
                            PriceRial = sellPrice * 20,
                            BuyPrice = buyPrice,
                            StockQuantity = stock,
                            Category = reader["Category"]?.ToString() ?? "",
                            ImagePath = reader["ImagePath"]?.ToString() ?? ""
                        });
                    }
                }

                // 5. جلب جميع الزبناء (للاختيار في نقطة البيع وإدارة الزبناء)
                string allCustQuery = @"
                    SELECT Id, uuid, FullName, Phone, DueBalance, CreditLimit, AllowCredit, InvoiceCount, LastCreditDate
                    FROM Customers
                    WHERE deleted_at IS NULL
                    ORDER BY FullName ASC;";

                using (var cmd = new MySqlCommand(allCustQuery, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        int id = Convert.ToInt32(reader["Id"]);
                        string name = reader["FullName"]?.ToString() ?? "";
                        string phone = reader["Phone"]?.ToString() ?? "";
                        decimal bal = Convert.ToDecimal(reader["DueBalance"]);
                        decimal limit = reader["CreditLimit"] != DBNull.Value ? Convert.ToDecimal(reader["CreditLimit"]) : 0;
                        bool allow = reader["AllowCredit"] != DBNull.Value && Convert.ToBoolean(reader["AllowCredit"]);
                        int invCount = Convert.ToInt32(reader["InvoiceCount"]);
                        string lastDate = reader["LastCreditDate"] != DBNull.Value ? Convert.ToDateTime(reader["LastCreditDate"]).ToString("yyyy-MM-dd") : "";

                        syncPayload.Customers.Add(new MobileCustomerItem
                        {
                            Id = id,
                            Uuid = reader["uuid"]?.ToString() ?? "",
                            FullName = name,
                            Phone = phone,
                            DueBalance = bal,
                            DueBalanceRial = bal * 20,
                            CreditLimit = limit,
                            AllowCredit = allow,
                            InvoiceCount = invCount,
                            LastCreditDate = lastDate
                        });
                    }
                }

                // 6. جلب جميع الممونين والشركات
                try
                {
                    string suppliersQuery = @"
                        SELECT Id, FullName, Company, Phone, Email, Address, DueBalance
                        FROM Suppliers
                        ORDER BY FullName ASC;";

                    using (var cmd = new MySqlCommand(suppliersQuery, conn))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            decimal due = reader["DueBalance"] != DBNull.Value ? Convert.ToDecimal(reader["DueBalance"]) : 0;
                            syncPayload.Suppliers.Add(new MobileSupplierItem
                            {
                                Id = Convert.ToInt32(reader["Id"]),
                                FullName = reader["FullName"]?.ToString() ?? "",
                                Company = reader["Company"]?.ToString() ?? "",
                                Phone = reader["Phone"]?.ToString() ?? "",
                                Email = reader["Email"]?.ToString() ?? "",
                                Address = reader["Address"]?.ToString() ?? "",
                                DueBalance = due,
                                DueBalanceRial = due * 20
                            });
                        }
                    }
                }
                catch {}

                // 7. استخراج قائمة الفئات ديناميكياً
                var allCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    using (var catCmd = new MySqlCommand("SELECT CategoryName FROM Categories ORDER BY CategoryName ASC", conn))
                    using (var catReader = catCmd.ExecuteReader())
                    {
                        while (catReader.Read())
                        {
                            string cName = catReader.GetString(0);
                            if (!string.IsNullOrWhiteSpace(cName)) allCategories.Add(cName.Trim());
                        }
                    }
                }
                catch {}

                foreach (var p in syncPayload.Products)
                {
                    if (!string.IsNullOrWhiteSpace(p.Category)) allCategories.Add(p.Category.Trim());
                }

                syncPayload.Categories = allCategories.OrderBy(c => c).ToList();
            }

            return syncPayload;
        }

        /// <summary>
        /// الحصول على محتوى JSON المحدث بالكامل فورياً
        /// </summary>
        public static string GetLatestSyncJson()
        {
            try
            {
                var payload = GenerateSyncPayload();
                return JsonSerializer.Serialize(payload, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CloudSync] Error generating JSON: {ex.Message}");
                return "{}";
            }
        }

        /// <summary>
        /// تصدير ملف مزامنة الهاتف إلى مجلد Google Drive والمجلدات المحلية
        /// </summary>
        public static bool SyncMobileData(bool force = false)
        {
            lock (_syncLock)
            {
                try
                {
                    if (!force && (DateTime.Now - _lastSyncTime).TotalSeconds < 5)
                    {
                        return true;
                    }

                    string jsonString = GetLatestSyncJson();
                    if (string.IsNullOrEmpty(jsonString) || jsonString == "{}") return false;

                    // تحديد جميع مجلدات الحفظ المتزامنة مع Google Drive والمحلية
                    var targetFolders = new List<string>();

                    // 1. المسار المسجل في إعدادات البرنامج
                    try
                    {
                        string configuredBackup = BackupService.GetConfiguredBackupPath();
                        if (!string.IsNullOrWhiteSpace(configuredBackup) && Directory.Exists(configuredBackup) && !targetFolders.Contains(configuredBackup, StringComparer.OrdinalIgnoreCase))
                        {
                            targetFolders.Add(configuredBackup);
                        }
                    }
                    catch {}

                    // 2. المسارات القياسية المعروفة (لا نكتب أبداً داخل مجلد كود تطبيق الهاتف حتى لا تُضمَّن بيانات الزبائن في ملف APK)
                    string[] knownPaths = new[]
                    {
                        @"E:\Drive\Lmima Shop",
                        @"G:\Mon Drive\Lmima Shop",
                        @"G:\My Drive\Lmima Shop",
                        @"E:\Lmima Shop\Sauvegarde",
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MobileSync"),
                        @"C:\Users\Admin\Desktop\LmimaShop_Mobile_Package",
                        @"E:\Aplication\LmimaShop_Mobile_Package",
                        @"E:\Lmima Shop\LmimaShop_Mobile_Package"
                    };

                    foreach (var kp in knownPaths)
                    {
                        if (Directory.Exists(kp) && !targetFolders.Contains(kp, StringComparer.OrdinalIgnoreCase))
                        {
                            targetFolders.Add(kp);
                        }
                    }

                    if (targetFolders.Count == 0)
                    {
                        string fallback = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MobileSync");
                        Directory.CreateDirectory(fallback);
                        targetFolders.Add(fallback);
                    }

                    // كتابة الملفات في كل المجلدات
                    foreach (var folder in targetFolders)
                    {
                        try
                        {
                            if (!Directory.Exists(folder))
                            {
                                Directory.CreateDirectory(folder);
                            }

                            WriteFileAtomically(Path.Combine(folder, "lmima_mobile_sync.json"), jsonString);
                            WriteFileAtomically(Path.Combine(folder, "lmima_mobile_sync.js"), "window.LMIMA_SYNC_DATA = " + jsonString + ";");
                        }
                        catch (Exception exFolder)
                        {
                            Debug.WriteLine($"[CloudSync] Failed writing to {folder}: {exFolder.Message}");
                        }
                    }

                    // أيضاً تحديث ملف Google Sheets للزبناء
                    try
                    {
                        CustomerGoogleSheetService.SyncCustomersSheet(false, false, out _);
                    }
                    catch {}

                    _lastSyncTime = DateTime.Now;
                    Debug.WriteLine($"[CloudSync] Sync files exported successfully to {targetFolders.Count} locations.");

                    // استيراد أي صور جديدة معلقة في Google Drive وحذفها بعد الاستيراد
                    try
                    {
                        Task.Run(() => ImportPendingImagesFromGoogleDrive());
                    }
                    catch {}

                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[CloudSync] Error syncing mobile data: " + ex);
                    Debug.WriteLine("[CloudSync] Error syncing mobile data: " + ex.Message);
                    return false;
                }
            }
        }

        /// <summary>
        /// كتابة الملف في ملف مؤقت ثم استبداله دفعة واحدة، حتى لا يرفع Google Drive أو يقرأ الهاتف ملفاً نصف مكتوب
        /// </summary>
        private static void WriteFileAtomically(string path, string content)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, content, new UTF8Encoding(false));
            if (File.Exists(path))
            {
                File.Replace(tmp, path, null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tmp, path);
            }
        }

        private static readonly object _opsImportLock = new object();

        /// <summary>
        /// استيراد العمليات التي سجلها الهاتف خارج المحل وصدّرها إلى Google Drive (ملفات lmima_pending_ops*.json).
        /// كل عملية تحمل op_id فريداً، لذلك إعادة استيراد نفس الملف لا تكرر المبيعات أو التسديدات.
        /// الملف المعالج يُنقل إلى مجلد Processed ولا يُحذف.
        /// </summary>
        public static int ImportPendingOperationsFromGoogleDrive()
        {
            lock (_opsImportLock)
            {
                int applied = 0;
                try
                {
                    var folders = CustomerGoogleSheetService.GetGoogleDriveTargetFolders();
                    string configured = BackupService.GetConfiguredBackupPath();
                    if (!string.IsNullOrWhiteSpace(configured) && !folders.Contains(configured, StringComparer.OrdinalIgnoreCase))
                    {
                        folders.Add(configured);
                    }

                    foreach (var folder in folders)
                    {
                        if (!Directory.Exists(folder)) continue;
                        foreach (var file in Directory.GetFiles(folder, "lmima_pending_ops*.json", SearchOption.TopDirectoryOnly))
                        {
                            try
                            {
                                string content = File.ReadAllText(file, Encoding.UTF8);
                                var results = LmimaShopApp.Core.Sync.DeltaSyncApi.ProcessPushBody(content, "google-drive");
                                if (results == null)
                                {
                                    Serilog.Log.Warning("Ignored invalid pending ops file {File}", file);
                                    continue;
                                }

                                string processedDir = Path.Combine(folder, "Processed");
                                Directory.CreateDirectory(processedDir);
                                string resultJson = JsonSerializer.Serialize(results, new JsonSerializerOptions
                                {
                                    WriteIndented = true,
                                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                                });
                                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                                File.WriteAllText(Path.Combine(processedDir, Path.GetFileNameWithoutExtension(file) + "_result_" + stamp + ".json"), resultJson, Encoding.UTF8);
                                File.Move(file, Path.Combine(processedDir, Path.GetFileNameWithoutExtension(file) + "_" + stamp + ".json"));

                                applied += results.Count;
                                Serilog.Log.Information("Imported {Count} mobile operations from Google Drive file {File}", results.Count, file);
                            }
                            catch (Exception ex)
                            {
                                Serilog.Log.Error(ex, "Failed importing pending ops file {File}", file);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Serilog.Log.Error(ex, "ImportPendingOperationsFromGoogleDrive error");
                }

                if (applied > 0)
                {
                    _ = SyncMobileDataAsync(force: true);
                    _ = LmimaShopApp.Core.Sync.SseManager.BroadcastEventAsync("changed", DateTime.UtcNow.ToString("O"));
                }
                return applied;
            }
        }

        private static readonly List<FileSystemWatcher> _driveWatchers = new List<FileSystemWatcher>();
        private static System.Threading.Timer? _debounceTimer = null;
        private static System.Threading.Timer? _opsDebounceTimer = null;
        private static readonly object _imageImportLock = new object();

        /// <summary>
        /// بدء مراقبة مجلدات Google Drive لاستيراد صور المنتجات الجديدة فور وصولها
        /// </summary>
        public static void StartGoogleDriveWatcher()
        {
            try
            {
                var folders = CustomerGoogleSheetService.GetGoogleDriveTargetFolders();
                if (!folders.Contains(@"E:\Lmima Shop\Sauvegarde", StringComparer.OrdinalIgnoreCase) && Directory.Exists(@"E:\Lmima Shop\Sauvegarde"))
                {
                    folders.Add(@"E:\Lmima Shop\Sauvegarde");
                }

                foreach (var watcher in _driveWatchers)
                {
                    try { watcher.Dispose(); } catch {}
                }
                _driveWatchers.Clear();

                foreach (var folder in folders)
                {
                    if (!Directory.Exists(folder)) continue;
                    try
                    {
                        var watcher = new FileSystemWatcher(folder)
                        {
                            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                            IncludeSubdirectories = true,
                            EnableRaisingEvents = true
                        };

                        FileSystemEventHandler onTrigger = (s, e) =>
                        {
                            string ext = Path.GetExtension(e.FullPath).ToLowerInvariant();
                            string name = Path.GetFileName(e.FullPath).ToLowerInvariant();
                            if (name.StartsWith("lmima_pending_ops") && ext == ".json" && !e.FullPath.Contains(Path.DirectorySeparatorChar + "Processed" + Path.DirectorySeparatorChar))
                            {
                                lock (_imageImportLock)
                                {
                                    _opsDebounceTimer?.Dispose();
                                    _opsDebounceTimer = new System.Threading.Timer(_ => Task.Run(() => ImportPendingOperationsFromGoogleDrive()), null, 3000, System.Threading.Timeout.Infinite);
                                }
                                return;
                            }
                            if (ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".webp" || name.Contains("pending_photo") || name.Contains("pending_image"))
                            {
                                lock (_imageImportLock)
                                {
                                    _debounceTimer?.Dispose();
                                    _debounceTimer = new System.Threading.Timer(_ =>
                                    {
                                        Task.Run(() => ImportPendingImagesFromGoogleDrive());
                                    }, null, 1500, System.Threading.Timeout.Infinite);
                                }
                            }
                        };

                        watcher.Created += onTrigger;
                        watcher.Changed += onTrigger;
                        watcher.Renamed += (s, e) => onTrigger(s, e);
                        _driveWatchers.Add(watcher);
                        Debug.WriteLine($"[CloudSync] Google Drive watcher started on: {folder}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[CloudSync] Failed to start watcher on {folder}: {ex.Message}");
                    }
                }

                // تنفيذ فحص أولي عند بدء التشغيل
                Task.Run(() => ImportPendingImagesFromGoogleDrive());
                Task.Run(() => ImportPendingOperationsFromGoogleDrive());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CloudSync] Error initializing Drive watchers: {ex.Message}");
            }
        }

        /// <summary>
        /// فحص مجلدات Google Drive لاستيراد صور المنتجات الجديدة وحفظها في مجلد صور البرنامج، ثم حذفها من Google Drive
        /// </summary>
        public static int ImportPendingImagesFromGoogleDrive()
        {
            lock (_imageImportLock)
            {
                int importedCount = 0;
                try
                {
                    var targetFolders = CustomerGoogleSheetService.GetGoogleDriveTargetFolders();
                    if (!targetFolders.Contains(@"E:\Lmima Shop\Sauvegarde", StringComparer.OrdinalIgnoreCase) && Directory.Exists(@"E:\Lmima Shop\Sauvegarde"))
                    {
                        targetFolders.Add(@"E:\Lmima Shop\Sauvegarde");
                    }

                    string appBaseDir = AppDomain.CurrentDomain.BaseDirectory;
                    var dbHelper = new DatabaseHelper();

                    foreach (var baseFolder in targetFolders)
                    {
                        if (!Directory.Exists(baseFolder)) continue;

                        // فحص المجلد الرئيسي ومجلدات الصور الفرعية المحتملة
                        var candidateDirs = new List<string> { baseFolder };
                        string pendingSub = Path.Combine(baseFolder, "PendingImages");
                        if (Directory.Exists(pendingSub)) candidateDirs.Add(pendingSub);
                        string pendingSub2 = Path.Combine(baseFolder, "PendingPhotos");
                        if (Directory.Exists(pendingSub2)) candidateDirs.Add(pendingSub2);

                        foreach (var dir in candidateDirs)
                        {
                            // 1. معالجة ملفات الحزم JSON: lmima_pending_photos.json أو lmima_pending_images.json
                            var jsonFiles = Directory.GetFiles(dir, "*pending*photo*.json", SearchOption.TopDirectoryOnly)
                                .Concat(Directory.GetFiles(dir, "*pending*image*.json", SearchOption.TopDirectoryOnly))
                                .Distinct()
                                .ToList();

                            foreach (var jf in jsonFiles)
                            {
                                try
                                {
                                    string content = File.ReadAllText(jf, Encoding.UTF8);
                                    if (string.IsNullOrWhiteSpace(content)) continue;

                                    using var doc = JsonDocument.Parse(content);
                                    var elements = doc.RootElement.ValueKind == JsonValueKind.Array
                                        ? doc.RootElement.EnumerateArray().ToList()
                                        : new List<JsonElement> { doc.RootElement };

                                    using var conn = dbHelper.GetConnection();
                                    conn.Open();

                                    foreach (var el in elements)
                                    {
                                        string barcode = el.TryGetProperty("barcode", out var bp) ? bp.GetString() ?? "" : "";
                                        string category = el.TryGetProperty("category", out var cp) ? cp.GetString() ?? "" : "";
                                        string imageBase64 = el.TryGetProperty("imageBase64", out var ip) ? ip.GetString() ?? "" : "";

                                        if (string.IsNullOrWhiteSpace(barcode) || string.IsNullOrWhiteSpace(imageBase64)) continue;

                                        int commaIdx = imageBase64.IndexOf(',');
                                        string pureBase64 = commaIdx >= 0 ? imageBase64.Substring(commaIdx + 1) : imageBase64;
                                        byte[] bytes = Convert.FromBase64String(pureBase64);

                                        // إذا لم تتوفر الفئة، نستعلم عنها من قاعدة البيانات
                                        if (string.IsNullOrWhiteSpace(category))
                                        {
                                            using var cmdCat = new MySqlCommand("SELECT Category FROM Products WHERE Barcode = @b LIMIT 1;", conn);
                                            cmdCat.Parameters.AddWithValue("@b", barcode);
                                            var dbCat = cmdCat.ExecuteScalar()?.ToString();
                                            if (!string.IsNullOrWhiteSpace(dbCat)) category = dbCat;
                                        }
                                        if (string.IsNullOrWhiteSpace(category)) category = "عام";

                                        string safeCategory = string.Join("_", category.Split(Path.GetInvalidFileNameChars()));
                                        string safeBarcode = string.Join("_", barcode.Split(Path.GetInvalidFileNameChars()));

                                        string targetDir = Path.Combine(appBaseDir, "ProductImages", safeCategory);
                                        if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

                                        string targetFile = Path.Combine(targetDir, safeBarcode + ".jpg");
                                        File.WriteAllBytes(targetFile, bytes);

                                        string relPath = Path.Combine("ProductImages", safeCategory, safeBarcode + ".jpg").Replace('\\', '/');

                                        using var updateCmd = new MySqlCommand("UPDATE Products SET ImagePath = @img WHERE Barcode = @b;", conn);
                                        updateCmd.Parameters.AddWithValue("@img", relPath);
                                        updateCmd.Parameters.AddWithValue("@b", barcode);
                                        updateCmd.ExecuteNonQuery();

                                        importedCount++;
                                        Debug.WriteLine($"[GoogleDrivePhotoSync] Imported image for product {barcode} from JSON.");
                                    }

                                    // حذف ملف JSON من Google Drive بعد اكتمال الاستيراد بنجاح
                                    File.Delete(jf);
                                    Debug.WriteLine($"[GoogleDrivePhotoSync] Deleted processed JSON file from Drive: {jf}");
                                }
                                catch (Exception exJson)
                                {
                                    Debug.WriteLine($"[GoogleDrivePhotoSync] Error processing JSON file {jf}: {exJson.Message}");
                                }
                            }

                            // 2. معالجة الصور المفردة (.jpg, .jpeg, .png, .webp)
                            string[] exts = new[] { "*.jpg", "*.jpeg", "*.png", "*.webp" };
                            var imageFiles = new List<string>();
                            foreach (var ext in exts)
                            {
                                imageFiles.AddRange(Directory.GetFiles(dir, ext, SearchOption.TopDirectoryOnly));
                            }

                            foreach (var imgFile in imageFiles)
                            {
                                try
                                {
                                    string fileNameWithoutExt = Path.GetFileNameWithoutExtension(imgFile);
                                    // تخطي الملفات النظامية أو الخاصة
                                    if (fileNameWithoutExt.StartsWith(".") || fileNameWithoutExt.StartsWith("~")) continue;

                                    string barcode = fileNameWithoutExt;
                                    string category = "";

                                    // دعم التسمية مثل: Barcode___Category.jpg
                                    if (fileNameWithoutExt.Contains("___"))
                                    {
                                        var parts = fileNameWithoutExt.Split(new[] { "___" }, StringSplitOptions.RemoveEmptyEntries);
                                        if (parts.Length >= 2)
                                        {
                                            barcode = parts[0];
                                            category = parts[1];
                                        }
                                    }

                                    // التأكد من أن المنتج موجود في قاعدة البيانات
                                    using var conn = dbHelper.GetConnection();
                                    conn.Open();

                                    using var chkCmd = new MySqlCommand("SELECT Id, Category FROM Products WHERE Barcode = @b LIMIT 1;", conn);
                                    chkCmd.Parameters.AddWithValue("@b", barcode);
                                    using var reader = chkCmd.ExecuteReader();
                                    if (reader.Read())
                                    {
                                        if (string.IsNullOrWhiteSpace(category))
                                        {
                                            category = reader["Category"]?.ToString() ?? "عام";
                                        }
                                        reader.Close();

                                        string safeCategory = string.IsNullOrWhiteSpace(category) ? "عام" : string.Join("_", category.Split(Path.GetInvalidFileNameChars()));
                                        string safeBarcode = string.Join("_", barcode.Split(Path.GetInvalidFileNameChars()));

                                        string targetDir = Path.Combine(appBaseDir, "ProductImages", safeCategory);
                                        if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

                                        string targetFile = Path.Combine(targetDir, safeBarcode + ".jpg");
                                        
                                        // نسخ الصورة إلى مجلد البرنامج
                                        File.Copy(imgFile, targetFile, true);

                                        string relPath = Path.Combine("ProductImages", safeCategory, safeBarcode + ".jpg").Replace('\\', '/');

                                        using var updateCmd = new MySqlCommand("UPDATE Products SET ImagePath = @img WHERE Barcode = @b;", conn);
                                        updateCmd.Parameters.AddWithValue("@img", relPath);
                                        updateCmd.Parameters.AddWithValue("@b", barcode);
                                        updateCmd.ExecuteNonQuery();

                                        // حذف الصورة المؤقتة من Google Drive فوراً كما طلب المستخدم!
                                        try
                                        {
                                            File.Delete(imgFile);
                                            Debug.WriteLine($"[GoogleDrivePhotoSync] Deleted processed image from Drive: {imgFile}");
                                        }
                                        catch (Exception delEx)
                                        {
                                            Debug.WriteLine($"[GoogleDrivePhotoSync] Could not delete image immediately: {delEx.Message}");
                                        }

                                        importedCount++;
                                        Debug.WriteLine($"[GoogleDrivePhotoSync] Successfully imported product image: {barcode}");
                                    }
                                }
                                catch (Exception exImg)
                                {
                                    Debug.WriteLine($"[GoogleDrivePhotoSync] Error importing image file {imgFile}: {exImg.Message}");
                                }
                            }
                        }
                    }

                    if (importedCount > 0)
                    {
                        Debug.WriteLine($"[GoogleDrivePhotoSync] Total images imported from Drive: {importedCount}. Updating sync...");
                        _ = SyncMobileDataAsync(force: true);
                        _ = LmimaShopApp.Core.Sync.SseManager.BroadcastEventAsync("changed", DateTime.UtcNow.ToString("O"));
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[GoogleDrivePhotoSync] General error in ImportPendingImagesFromGoogleDrive: {ex.Message}");
                }

                return importedCount;
            }
        }
    }

    #region Data Transfer Models for Mobile Companion
    public class MobileSyncPayload
    {
        public MobileStoreInfo StoreInfo { get; set; } = new MobileStoreInfo();
        public MobileTodaySummary TodaySummary { get; set; } = new MobileTodaySummary();
        public List<MobileInvoiceItem> RecentInvoices { get; set; } = new List<MobileInvoiceItem>();
        public List<MobileCustomerDebt> CustomerDebts { get; set; } = new List<MobileCustomerDebt>();
        public List<MobileCustomerItem> Customers { get; set; } = new List<MobileCustomerItem>();
        public List<MobileSupplierItem> Suppliers { get; set; } = new List<MobileSupplierItem>();
        public List<MobileProductItem> Products { get; set; } = new List<MobileProductItem>();
        public List<string> Categories { get; set; } = new List<string>();
    }

    public class MobileStoreInfo
    {
        public string StoreName { get; set; } = "متجر LMIMA SHOP";
        public string StorePhone { get; set; } = "";
        public string StoreAddress { get; set; } = "";
        public string LastSyncTimestamp { get; set; } = "";
    }

    public class MobileTodaySummary
    {
        public string Date { get; set; } = "";
        public decimal TotalSales { get; set; }
        public decimal TotalSalesRial { get; set; }
        public decimal CashSales { get; set; }
        public decimal CreditSales { get; set; }
        public int InvoicesCount { get; set; }
        public decimal EstimatedProfit { get; set; }
        public decimal EstimatedProfitRial { get; set; }
    }

    public class MobileInvoiceItem
    {
        public int InvoiceId { get; set; }
        public string Date { get; set; } = "";
        public string Cashier { get; set; } = "";
        public string Customer { get; set; } = "";
        public decimal TotalAmount { get; set; }
        public decimal TotalAmountRial { get; set; }
        public string PaymentMethod { get; set; } = "";
        public decimal ItemsCount { get; set; }
        public List<MobileInvoiceDetailItem> Items { get; set; } = new List<MobileInvoiceDetailItem>();
    }

    public class MobileInvoiceDetailItem
    {
        public string ProductName { get; set; } = "";
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal UnitPriceRial => UnitPrice * 20;
        public decimal SubTotal { get; set; }
        public decimal SubTotalRial => SubTotal * 20;
    }

    public class MobileCustomerDebt
    {
        public int Id { get; set; }
        public string FullName { get; set; } = "";
        public string Phone { get; set; } = "";
        public decimal DueBalance { get; set; }
        public decimal DueBalanceRial { get; set; }
        public decimal CreditLimit { get; set; }
        public int InvoiceCount { get; set; }
        public string LastCreditDate { get; set; } = "";
        public List<MobileCustomerDebtItem> RecentPurchases { get; set; } = new List<MobileCustomerDebtItem>();
        public List<MobileCustomerPaymentItem> RecentPayments { get; set; } = new List<MobileCustomerPaymentItem>();
    }

    public class MobileCustomerDebtItem
    {
        public string ProductName { get; set; } = "";
        public decimal Quantity { get; set; }
        public decimal Price { get; set; }
        public decimal SubTotal { get; set; }
        public string Date { get; set; } = "";
    }

    public class MobileCustomerPaymentItem
    {
        public decimal Amount { get; set; }
        public string Date { get; set; } = "";
        public string Note { get; set; } = "";
        public string CashierName { get; set; } = "";
    }

    public class MobileProductItem
    {
        public int Id { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("uuid")]
        public string Uuid { get; set; } = "";
        public string Barcode { get; set; } = "";
        public string Name { get; set; } = "";
        public decimal Price { get; set; }
        public decimal PriceRial { get; set; }
        public decimal BuyPrice { get; set; }
        public decimal StockQuantity { get; set; }
        public string Category { get; set; } = "";
        public string ImagePath { get; set; } = "";
    }

    public class MobileCustomerItem
    {
        public int Id { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("uuid")]
        public string Uuid { get; set; } = "";
        public string FullName { get; set; } = "";
        public string Phone { get; set; } = "";
        public decimal DueBalance { get; set; }
        public decimal DueBalanceRial { get; set; }
        public decimal CreditLimit { get; set; }
        public bool AllowCredit { get; set; } = true;
        public int InvoiceCount { get; set; }
        public string LastCreditDate { get; set; } = "";
    }

    public class MobileSupplierItem
    {
        public int Id { get; set; }
        public string FullName { get; set; } = "";
        public string Company { get; set; } = "";
        public string Phone { get; set; } = "";
        public string Email { get; set; } = "";
        public string Address { get; set; } = "";
        public decimal DueBalance { get; set; }
        public decimal DueBalanceRial { get; set; }
    }
    #endregion
}

