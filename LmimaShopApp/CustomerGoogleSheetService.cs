using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using ClosedXML.Excel;
using MySqlConnector;

namespace LmimaShopApp
{
    /// <summary>
    /// خدمة تصدير ومزامنة سجل الزبناء وحسابات الكريدي في ملف Google Sheets / Excel
    /// داخل مجلد Google Drive تلقائياً ويدوياً
    /// </summary>
    public static class CustomerGoogleSheetService
    {
        private static readonly object _lock = new object();
        private static DateTime _lastSyncTime = DateTime.MinValue;

        /// <summary>
        /// الحصول على مسار مجلد Google Drive المعتمد للنظام
        /// </summary>
        public static List<string> GetGoogleDriveTargetFolders()
        {
            var folders = new List<string>();

            // 1. المسار المسجل في إعدادات البرنامج (settings.json)
            try
            {
                string settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
                if (File.Exists(settingsPath))
                {
                    string json = File.ReadAllText(settingsPath);
                    using (var doc = JsonDocument.Parse(json))
                    {
                        if (doc.RootElement.TryGetProperty("BackupFolderPath", out var prop))
                        {
                            string configured = prop.GetString() ?? "";
                            if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
                            {
                                folders.Add(configured.Trim());
                            }
                        }
                    }
                }
            }
            catch { }

            // 2. المسارات القياسية المعروفة لـ Google Drive على الجهاز
            string[] standardPaths = new[]
            {
                @"G:\Mon Drive\Lmima Shop",
                @"G:\My Drive\Lmima Shop",
                @"E:\Lmima Shop\Sauvegarde",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Google Drive", "Lmima Shop")
            };

            foreach (var p in standardPaths)
            {
                if (Directory.Exists(p) && !folders.Contains(p, StringComparer.OrdinalIgnoreCase))
                {
                    folders.Add(p);
                }
            }

            // إذا لم يتوفر أي مجلد، ننشئ مجلد GoogleDrive_Sync محلياً
            if (folders.Count == 0)
            {
                string localFallback = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GoogleDrive_Sync");
                if (!Directory.Exists(localFallback))
                {
                    Directory.CreateDirectory(localFallback);
                }
                folders.Add(localFallback);
            }

            return folders;
        }

        /// <summary>
        /// تشغيل التصدير بشكل غير متزامن في الخلفية (لتفادي تجميد واجهة المستخدم أثناء البيع أو الحفظ)
        /// </summary>
        public static Task<bool> SyncCustomersSheetAsync(bool force = false)
        {
            return Task.Run(() => SyncCustomersSheet(force, false, out _));
        }

        /// <summary>
        /// توليد وتحديث ملف Google Sheet لجميع الزبناء وحسابات الكريدي
        /// </summary>
        public static bool SyncCustomersSheet(bool force, bool showMessage, out string primaryFilePath)
        {
            primaryFilePath = string.Empty;

            lock (_lock)
            {
                try
                {
                    // فحص فترة التهدئة إذا لم يكن الطلب إجبارياً
                    if (!force && (DateTime.Now - _lastSyncTime).TotalSeconds < 5)
                    {
                        return true;
                    }

                    var dbHelper = new DatabaseHelper();
                    var customers = new List<CustomerSheetRecord>();

                    using (var conn = dbHelper.GetConnection())
                    {
                        conn.Open();
                        string query = @"
                            SELECT Id, FullName, Phone, CreditLimit, DueBalance, AllowCredit, 
                                   InvoiceCount, CreditPeriodType, CreditPeriodDays, LastCreditDate
                            FROM Customers
                            WHERE deleted_at IS NULL
                            ORDER BY DueBalance DESC, FullName ASC;";

                        using (var cmd = new MySqlCommand(query, conn))
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                int id = Convert.ToInt32(reader["Id"]);
                                string name = reader["FullName"]?.ToString() ?? "";
                                string phone = reader["Phone"]?.ToString() ?? "";
                                decimal creditLimit = reader["CreditLimit"] != DBNull.Value ? Convert.ToDecimal(reader["CreditLimit"]) : 0;
                                decimal dueBalance = reader["DueBalance"] != DBNull.Value ? Convert.ToDecimal(reader["DueBalance"]) : 0;
                                bool allowCredit = reader["AllowCredit"] != DBNull.Value && Convert.ToBoolean(reader["AllowCredit"]);
                                int invoiceCount = reader["InvoiceCount"] != DBNull.Value ? Convert.ToInt32(reader["InvoiceCount"]) : 0;
                                string periodType = reader["CreditPeriodType"]?.ToString() ?? "Open";
                                int periodDays = reader["CreditPeriodDays"] != DBNull.Value ? Convert.ToInt32(reader["CreditPeriodDays"]) : 0;
                                DateTime? lastCreditDate = reader["LastCreditDate"] != DBNull.Value ? Convert.ToDateTime(reader["LastCreditDate"]) : null;

                                customers.Add(new CustomerSheetRecord
                                {
                                    Id = id,
                                    FullName = name,
                                    Phone = phone,
                                    CreditLimit = creditLimit,
                                    DueBalance = dueBalance,
                                    AllowCredit = allowCredit,
                                    InvoiceCount = invoiceCount,
                                    CreditPeriodType = periodType,
                                    CreditPeriodDays = periodDays,
                                    LastCreditDate = lastCreditDate
                                });
                            }
                        }
                    }

                    // بناء مصنف إكسل / Google Sheets
                    using (var wb = new XLWorkbook())
                    {
                        BuildCustomersWorksheet(wb, customers);
                        BuildSummaryWorksheet(wb, customers);

                        var targetFolders = GetGoogleDriveTargetFolders();
                        bool anySuccess = false;

                        foreach (var folder in targetFolders)
                        {
                            try
                            {
                                if (!Directory.Exists(folder))
                                {
                                    Directory.CreateDirectory(folder);
                                }

                                // حفظ بنسختين للاستخدام: بالاسم العربي وبالاسم اللاتيني
                                string fileArabic = Path.Combine(folder, "سجل_الزبناء_GoogleSheets.xlsx");
                                string fileLatin = Path.Combine(folder, "LmimaShop_Customers.xlsx");

                                SaveWorkbookSafely(wb, fileArabic);
                                SaveWorkbookSafely(wb, fileLatin);

                                if (string.IsNullOrEmpty(primaryFilePath))
                                {
                                    primaryFilePath = fileArabic;
                                }

                                anySuccess = true;
                            }
                            catch (Exception exFolder)
                            {
                                System.Diagnostics.Debug.WriteLine($"Error saving to folder {folder}: {exFolder.Message}");
                            }
                        }

                        if (anySuccess)
                        {
                            _lastSyncTime = DateTime.Now;

                            if (showMessage)
                            {
                                MessageBox.Show(
                                    $"تم بنجاح تحديث ملف سجل الزبناء لـ Google Sheets! 📊\n\n" +
                                    $"• المسار المحدث: {primaryFilePath}\n" +
                                    $"• إجمالي الزبناء: {customers.Count} زبون\n" +
                                    $"• مجموع ديون الكريدي: {customers.Sum(c => c.DueBalance):N2} د.م. ({customers.Sum(c => c.DueBalance) * 20:N0} ريال)\n\n" +
                                    $"تمت المزامنة السحابية داخل مجلد Google Drive فوراً.",
                                    "نجاح مزامنة Google Sheets",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Information);
                            }

                            return true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error syncing customers sheet: {ex.Message}");
                    if (showMessage)
                    {
                        MessageBox.Show("حدث خطأ أثناء تحديث ملف Google Sheets: " + ex.Message, "خطأ مزامنة", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// حفظ الملف بشكل آمن مع التعامل مع حالات قفل الملف إذا كان مفتوحاً في برنامج آخر
        /// </summary>
        private static void SaveWorkbookSafely(XLWorkbook wb, string filePath)
        {
            try
            {
                wb.SaveAs(filePath);
            }
            catch (IOException)
            {
                // إذا كان الملف مفتوحاً حالياً في Excel، نحفظ نسخة مؤقتة أو محدثة لتفادي الانهيار
                string dir = Path.GetDirectoryName(filePath) ?? "";
                string fileNameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
                string altPath = Path.Combine(dir, $"{fileNameWithoutExt}_محدث.xlsx");
                wb.SaveAs(altPath);
            }
        }

        /// <summary>
        /// بناء ورقة العمل الرئيسية: سجل الزبناء وحسابات الكريدي
        /// </summary>
        private static void BuildCustomersWorksheet(XLWorkbook wb, List<CustomerSheetRecord> customers)
        {
            var ws = wb.Worksheets.Add("سجل الزبناء والكريدي");
            ws.RightToLeft = true;
            ws.ShowGridLines = true;

            // 1. شريط العنوان الرئيسي (Banner)
            ws.Range("A1:L1").Merge();
            var titleCell = ws.Cell("A1");
            titleCell.Value = "📊 سجل الزبناء وحسابات الكريدي - متجر LMIMA SHOP";
            titleCell.Style.Font.Bold = true;
            titleCell.Style.Font.FontSize = 16;
            titleCell.Style.Font.FontColor = XLColor.White;
            titleCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#0F9D58"); // Google Sheets Material Green
            titleCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            titleCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Row(1).Height = 36;

            // 2. معلومات المزامنة وملخص الأرقام
            decimal totalDebt = customers.Sum(c => c.DueBalance);
            decimal totalDebtRial = totalDebt * 20;
            int debtorCount = customers.Count(c => c.DueBalance > 0);

            ws.Range("A2:L2").Merge();
            var infoCell = ws.Cell("A2");
            infoCell.Value = $"تاريخ آخر تحديث: {DateTime.Now:yyyy-MM-dd HH:mm:ss}  |  إجمالي الزبناء: {customers.Count}  |  الزبناء المدينين: {debtorCount}  |  مجموع الديون: {totalDebt:N2} د.م. ({totalDebtRial:N0} ريال)";
            infoCell.Style.Font.Bold = true;
            infoCell.Style.Font.FontSize = 11;
            infoCell.Style.Font.FontColor = XLColor.FromHtml("#1B5E20");
            infoCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8F5E9");
            infoCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            infoCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Row(2).Height = 24;

            // فاصل
            ws.Row(3).Height = 8;

            // 3. عناوين الأعمدة
            string[] headers = new[]
            {
                "معرف الزبون",
                "اسم الزبون الكامل",
                "رقم الهاتف",
                "الدين بالدرهم (د.م.)",
                "الدين بالريال المغربي",
                "سقف الكريدي (د.م.)",
                "يسمح بالأجل",
                "عدد الفواتير",
                "حالة الحساب",
                "نوع الأجل",
                "تاريخ آخر دين",
                "تاريخ الاستحقاق"
            };

            int headerRow = 4;
            ws.Row(headerRow).Height = 28;

            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(headerRow, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontSize = 11;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1E8E3E");
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                cell.Style.Border.OutsideBorderColor = XLColor.FromHtml("#B7E1CD");
            }

            // 4. بيانات الزبناء
            int startRow = 5;
            int currentRow = startRow;

            foreach (var c in customers)
            {
                ws.Row(currentRow).Height = 22;

                ws.Cell(currentRow, 1).Value = c.Id;
                ws.Cell(currentRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                ws.Cell(currentRow, 2).Value = c.FullName;
                ws.Cell(currentRow, 2).Style.Font.Bold = true;
                ws.Cell(currentRow, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                // الهاتف بصيغة نصية للحفاظ على الصفر الأولي
                var phoneCell = ws.Cell(currentRow, 3);
                phoneCell.SetValue(c.Phone);
                phoneCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                // الرصيد بالدرهم
                var balanceCell = ws.Cell(currentRow, 4);
                balanceCell.Value = c.DueBalance;
                balanceCell.Style.NumberFormat.Format = "#,##0.00 \"د.م.\"";
                balanceCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                balanceCell.Style.Font.Bold = true;

                // الرصيد بالريال
                var rialCell = ws.Cell(currentRow, 5);
                rialCell.Value = c.DueBalance * 20;
                rialCell.Style.NumberFormat.Format = "#,##0 \"ريال\"";
                rialCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                // سقف الكريدي
                var limitCell = ws.Cell(currentRow, 6);
                limitCell.Value = c.CreditLimit;
                limitCell.Style.NumberFormat.Format = "#,##0.00 \"د.م.\"";
                limitCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                // السماح بالأجل
                ws.Cell(currentRow, 7).Value = c.AllowCredit ? "نعم" : "لا";
                ws.Cell(currentRow, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                // عدد الفواتير
                ws.Cell(currentRow, 8).Value = c.InvoiceCount;
                ws.Cell(currentRow, 8).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                // حالة الحساب وتلوين الصف حسب المديونية
                var statusCell = ws.Cell(currentRow, 9);
                statusCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                statusCell.Style.Font.Bold = true;

                var rowRange = ws.Range(currentRow, 1, currentRow, headers.Length);

                if (c.DueBalance > 0)
                {
                    if (c.IsCreditOverdue)
                    {
                        statusCell.Value = "⚠️ متأخر في الأداء";
                        statusCell.Style.Font.FontColor = XLColor.FromHtml("#C0392B");
                        rowRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#FDEDEC"); // أحمر فاتح
                    }
                    else
                    {
                        statusCell.Value = "عليه كريدي";
                        statusCell.Style.Font.FontColor = XLColor.FromHtml("#D35400");
                        rowRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#FEF9E7"); // أصفر هادئ
                    }
                }
                else
                {
                    statusCell.Value = "خالص (بدون دين)";
                    statusCell.Style.Font.FontColor = XLColor.FromHtml("#27AE60");
                    rowRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#F9FCF9"); // أخضر فاتح نقي
                }

                // نوع الأجل
                ws.Cell(currentRow, 10).Value = c.CreditPeriodType == "Limited" ? $"{c.CreditPeriodDays} يوم" : "مفتوح";
                ws.Cell(currentRow, 10).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                // تاريخ آخر دين
                ws.Cell(currentRow, 11).Value = c.LastCreditDate.HasValue ? c.LastCreditDate.Value.ToString("yyyy-MM-dd HH:mm") : "-";
                ws.Cell(currentRow, 11).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                // تاريخ الاستحقاق
                ws.Cell(currentRow, 12).Value = c.CreditDueDate;
                ws.Cell(currentRow, 12).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                // حدود الخلايا
                rowRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                rowRange.Style.Border.OutsideBorderColor = XLColor.FromHtml("#E0E0E0");
                rowRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                rowRange.Style.Border.InsideBorderColor = XLColor.FromHtml("#E0E0E0");

                currentRow++;
            }

            // 5. صف الإجمالي العام في الأسفل
            int totalRow = currentRow;
            ws.Row(totalRow).Height = 30;

            ws.Range(totalRow, 1, totalRow, 3).Merge();
            var totalLabel = ws.Cell(totalRow, 1);
            totalLabel.Value = $"المجموع العام ({customers.Count} زبون)";
            totalLabel.Style.Font.Bold = true;
            totalLabel.Style.Font.FontSize = 12;
            totalLabel.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            totalLabel.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            // معادلة مجموع الديون
            var sumDebtCell = ws.Cell(totalRow, 4);
            if (customers.Count > 0)
            {
                sumDebtCell.FormulaA1 = $"SUM(D{startRow}:D{currentRow - 1})";
            }
            else
            {
                sumDebtCell.Value = 0;
            }
            sumDebtCell.Style.Font.Bold = true;
            sumDebtCell.Style.Font.FontSize = 12;
            sumDebtCell.Style.NumberFormat.Format = "#,##0.00 \"د.م.\"";
            sumDebtCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

            // معادلة مجموع الديون بالريال
            var sumRialCell = ws.Cell(totalRow, 5);
            if (customers.Count > 0)
            {
                sumRialCell.FormulaA1 = $"SUM(E{startRow}:E{currentRow - 1})";
            }
            else
            {
                sumRialCell.Value = 0;
            }
            sumRialCell.Style.Font.Bold = true;
            sumRialCell.Style.Font.FontSize = 12;
            sumRialCell.Style.NumberFormat.Format = "#,##0 \"ريال\"";
            sumRialCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

            // مجموع أسقف الائتمان
            var sumLimitCell = ws.Cell(totalRow, 6);
            if (customers.Count > 0)
            {
                sumLimitCell.FormulaA1 = $"SUM(F{startRow}:F{currentRow - 1})";
            }
            else
            {
                sumLimitCell.Value = 0;
            }
            sumLimitCell.Style.Font.Bold = true;
            sumLimitCell.Style.NumberFormat.Format = "#,##0.00 \"د.م.\"";
            sumLimitCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

            ws.Cell(totalRow, 7).Value = "";

            // مجموع الفواتير
            var sumInvoicesCell = ws.Cell(totalRow, 8);
            if (customers.Count > 0)
            {
                sumInvoicesCell.FormulaA1 = $"SUM(H{startRow}:H{currentRow - 1})";
            }
            else
            {
                sumInvoicesCell.Value = 0;
            }
            sumInvoicesCell.Style.Font.Bold = true;
            sumInvoicesCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            ws.Range(totalRow, 9, totalRow, 12).Merge();
            ws.Cell(totalRow, 9).Value = "— حسابات مؤمنة ومحدثة —";
            ws.Cell(totalRow, 9).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            // تنسيق شريط الإجمالي
            var totalRange = ws.Range(totalRow, 1, totalRow, headers.Length);
            totalRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#C8E6C9");
            totalRange.Style.Font.FontColor = XLColor.FromHtml("#1B5E20");
            totalRange.Style.Border.TopBorder = XLBorderStyleValues.Medium;
            totalRange.Style.Border.BottomBorder = XLBorderStyleValues.Double;
            totalRange.Style.Border.TopBorderColor = XLColor.FromHtml("#2E7D32");
            totalRange.Style.Border.BottomBorderColor = XLColor.FromHtml("#2E7D32");

            // تفعيل ميزة التصفية التلقائية (AutoFilter) في Google Sheets و Excel
            if (customers.Count > 0)
            {
                ws.Range(headerRow, 1, currentRow - 1, headers.Length).SetAutoFilter();
            }

            // ضبط اتساع الأعمدة تلقائياً
            ws.Columns().AdjustToContents(headerRow, totalRow);
            for (int col = 1; col <= headers.Length; col++)
            {
                if (ws.Column(col).Width < 14)
                {
                    ws.Column(col).Width = 14;
                }
            }
        }

        /// <summary>
        /// بناء ورقة العمل الثانية: لوحة الإحصائيات السريعة للديون
        /// </summary>
        private static void BuildSummaryWorksheet(XLWorkbook wb, List<CustomerSheetRecord> customers)
        {
            var ws = wb.Worksheets.Add("إحصائيات ديون الكريدي");
            ws.RightToLeft = true;
            ws.ShowGridLines = true;

            // العنوان
            ws.Range("A1:D1").Merge();
            var title = ws.Cell("A1");
            title.Value = "📈 ملخص وإحصائيات الكريدي (Google Sheets Dashboard)";
            title.Style.Font.Bold = true;
            title.Style.Font.FontSize = 14;
            title.Style.Font.FontColor = XLColor.White;
            title.Style.Fill.BackgroundColor = XLColor.FromHtml("#1B5E20");
            title.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            title.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Row(1).Height = 32;

            decimal totalDebt = customers.Sum(c => c.DueBalance);
            int debtorCount = customers.Count(c => c.DueBalance > 0);
            int paidCount = customers.Count(c => c.DueBalance <= 0);
            int overdueCount = customers.Count(c => c.IsCreditOverdue);

            var stats = new List<(string Label, string Value, string Color)>
            {
                ("إجمالي ديون الكريدي بالدرهم", $"{totalDebt:N2} د.م.", "#C0392B"),
                ("إجمالي ديون الكريدي بالريال المغربي", $"{totalDebt * 20:N0} ريال", "#D35400"),
                ("عدد الزبناء الإجمالي", $"{customers.Count} زبون", "#2980B9"),
                ("عدد الزبناء المدينين حالياً", $"{debtorCount} زبون", "#E67E22"),
                ("عدد الزبناء الخالصين (بدون دين)", $"{paidCount} زبون", "#27AE60"),
                ("عدد الديون المتأخرة عن موعدها", $"{overdueCount} زبون", "#962D22")
            };

            int row = 3;
            foreach (var st in stats)
            {
                ws.Row(row).Height = 26;

                ws.Range(row, 1, row, 2).Merge();
                var lblCell = ws.Cell(row, 1);
                lblCell.Value = st.Label;
                lblCell.Style.Font.Bold = true;
                lblCell.Style.Font.FontSize = 11;
                lblCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#F2F4F4");
                lblCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                lblCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                lblCell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                lblCell.Style.Border.OutsideBorderColor = XLColor.FromHtml("#BDC3C7");

                ws.Range(row, 3, row, 4).Merge();
                var valCell = ws.Cell(row, 3);
                valCell.Value = st.Value;
                valCell.Style.Font.Bold = true;
                valCell.Style.Font.FontSize = 12;
                valCell.Style.Font.FontColor = XLColor.FromHtml(st.Color);
                valCell.Style.Fill.BackgroundColor = XLColor.White;
                valCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                valCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                valCell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                valCell.Style.Border.OutsideBorderColor = XLColor.FromHtml("#BDC3C7");

                row++;
            }

            // جدول أعلى الزبناء مديونية
            row += 2;
            ws.Range(row, 1, row, 4).Merge();
            var topTitle = ws.Cell(row, 1);
            topTitle.Value = "🏆 كشف أعلى الزبناء مديونية";
            topTitle.Style.Font.Bold = true;
            topTitle.Style.Font.FontSize = 12;
            topTitle.Style.Font.FontColor = XLColor.White;
            topTitle.Style.Fill.BackgroundColor = XLColor.FromHtml("#2C3E50");
            topTitle.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Row(row).Height = 26;
            row++;

            string[] topHeaders = new[] { "#", "الزبون", "الهاتف", "مبلغ الدين (د.م.)" };
            for (int h = 0; h < topHeaders.Length; h++)
            {
                var hCell = ws.Cell(row, h + 1);
                hCell.Value = topHeaders[h];
                hCell.Style.Font.Bold = true;
                hCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#BDC3C7");
                hCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                hCell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            }
            ws.Row(row).Height = 22;
            row++;

            var topDebtors = customers.Where(c => c.DueBalance > 0).OrderByDescending(c => c.DueBalance).Take(10).ToList();
            int rank = 1;
            foreach (var d in topDebtors)
            {
                ws.Row(row).Height = 20;
                ws.Cell(row, 1).Value = rank++;
                ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                ws.Cell(row, 2).Value = d.FullName;
                ws.Cell(row, 2).Style.Font.Bold = true;
                ws.Cell(row, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                ws.Cell(row, 3).Value = d.Phone;
                ws.Cell(row, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                ws.Cell(row, 4).Value = d.DueBalance;
                ws.Cell(row, 4).Style.NumberFormat.Format = "#,##0.00 \"د.م.\"";
                ws.Cell(row, 4).Style.Font.Bold = true;
                ws.Cell(row, 4).Style.Font.FontColor = XLColor.FromHtml("#C0392B");
                ws.Cell(row, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                ws.Range(row, 1, row, 4).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                ws.Range(row, 1, row, 4).Style.Border.OutsideBorderColor = XLColor.FromHtml("#E0E0E0");

                row++;
            }

            ws.Columns().AdjustToContents();
            ws.Column(1).Width = 10;
            ws.Column(2).Width = 25;
            ws.Column(3).Width = 18;
            ws.Column(4).Width = 22;
        }
    }

    /// <summary>
    /// نموذج بيانات الزبون للتصدير في Google Sheet
    /// </summary>
    public class CustomerSheetRecord
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
        public DateTime? LastCreditDate { get; set; }

        public bool IsCreditOverdue
        {
            get
            {
                if (!AllowCredit || DueBalance <= 0 || CreditPeriodType != "Limited" || LastCreditDate == null)
                    return false;

                return LastCreditDate.Value.AddDays(CreditPeriodDays) < DateTime.Now;
            }
        }

        public string CreditDueDate
        {
            get
            {
                if (!AllowCredit || DueBalance <= 0 || CreditPeriodType != "Limited" || LastCreditDate == null)
                    return "-";

                return LastCreditDate.Value.AddDays(CreditPeriodDays).ToString("yyyy-MM-dd");
            }
        }
    }
}
