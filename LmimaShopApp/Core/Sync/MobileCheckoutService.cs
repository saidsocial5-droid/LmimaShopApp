using System;
using System.Collections.Generic;
using System.Text.Json;
using MySqlConnector;
using Serilog;

namespace LmimaShopApp.Core.Sync
{
    public class MobileCheckoutResult
    {
        public bool Success { get; set; }
        public bool Duplicate { get; set; }
        public int InvoiceId { get; set; }
        public string Error { get; set; } = "";
        public List<string> Warnings { get; set; } = new List<string>();
    }

    /// <summary>
    /// المسار الموحد لتسجيل مبيعات الهاتف (مباشرة أو من طابور عدم الاتصال).
    /// - يمنع تكرار الفاتورة عبر op_id في جدول processed_operations
    /// - يتحقق من المخزون والكريدي داخل نفس المعاملة
    /// - في وضع strict (بيع مباشر) يرفض العملية عند نقص المخزون أو تجاوز الكريدي
    /// - في وضع عدم الاتصال تكون السلعة قد سُلّمت فعلاً، فتُسجّل العملية مع تحذير في sync_conflicts
    /// </summary>
    public static class MobileCheckoutService
    {
        public static readonly string[] WalkInCustomerNames = { "زبون نقدي عابر", "زبون نقدي افتراضي", "زبون نقدي" };

        public static bool IsWalkInCustomer(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return true;
            return Array.IndexOf(WalkInCustomerNames, name.Trim()) >= 0;
        }

        public static MobileCheckoutResult Process(JsonElement root, string? opId, string deviceId, bool strict)
        {
            var result = new MobileCheckoutResult();

            string cashier = GetString(root, "cashierName", "تطبيق الهاتف");
            string customer = GetString(root, "customerName", "زبون نقدي عابر").Trim();
            if (string.IsNullOrWhiteSpace(customer)) customer = "زبون نقدي عابر";
            decimal total = GetDecimal(root, "totalAmount", 0);
            decimal paid = GetDecimal(root, "paidAmount", total);
            decimal debt = GetDecimal(root, "debtAmount", Math.Max(0, total - paid));
            if (debt < 0) debt = 0;

            var items = new List<(int productId, string name, decimal qty, decimal subTotal)>();
            if (root.TryGetProperty("items", out var itemsElem) && itemsElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var it in itemsElem.EnumerateArray())
                {
                    int prodId = (int)GetDecimal(it, "productId", 0);
                    string prodName = GetString(it, "productName", "");
                    decimal qty = GetDecimal(it, "quantity", 1);
                    decimal unitPrice = GetDecimal(it, "unitPrice", 0);
                    decimal subTotal = GetDecimal(it, "total", qty * unitPrice);
                    if (qty <= 0) continue;
                    items.Add((prodId, prodName, qty, subTotal));
                }
            }

            if (items.Count == 0)
            {
                result.Error = "الفاتورة لا تحتوي على أي سلعة";
                return result;
            }

            if (total <= 0)
            {
                foreach (var it in items) total += it.subTotal;
            }

            bool isWalkIn = IsWalkInCustomer(customer);
            if (debt > 0 && isWalkIn)
            {
                if (strict)
                {
                    result.Error = "الزبون النقدي العابر لا يمكنه الشراء بالكريدي. اختر زبوناً مسجلاً.";
                    return result;
                }
                result.Warnings.Add("بيع بالكريدي بدون زبون مسجل");
            }

            using var conn = new DatabaseHelper().GetConnection();
            conn.Open();
            using var tx = conn.BeginTransaction();
            try
            {
                if (!string.IsNullOrWhiteSpace(opId))
                {
                    using var dupCmd = new MySqlCommand("SELECT result FROM processed_operations WHERE op_id = @op FOR UPDATE", conn, tx);
                    dupCmd.Parameters.AddWithValue("@op", opId);
                    var prev = dupCmd.ExecuteScalar() as string;
                    if (prev != null)
                    {
                        tx.Rollback();
                        result.Success = true;
                        result.Duplicate = true;
                        try
                        {
                            using var prevDoc = JsonDocument.Parse(prev);
                            if (prevDoc.RootElement.TryGetProperty("invoiceId", out var inv)) result.InvoiceId = inv.GetInt32();
                        }
                        catch { }
                        return result;
                    }
                }

                // 1. التحقق من الزبون وحد الكريدي
                if (!isWalkIn)
                {
                    using var custCmd = new MySqlCommand("SELECT AllowCredit, CreditLimit, DueBalance FROM Customers WHERE FullName = @name AND deleted_at IS NULL LIMIT 1 FOR UPDATE", conn, tx);
                    custCmd.Parameters.AddWithValue("@name", customer);
                    using var r = custCmd.ExecuteReader();
                    if (!r.Read())
                    {
                        r.Close();
                        if (strict)
                        {
                            tx.Rollback();
                            result.Error = $"الزبون '{customer}' غير مسجل في الحاسوب";
                            return result;
                        }
                        result.Warnings.Add($"الزبون '{customer}' غير موجود");
                    }
                    else
                    {
                        bool allowCredit = r["AllowCredit"] != DBNull.Value && Convert.ToBoolean(r["AllowCredit"]);
                        decimal limit = r["CreditLimit"] != DBNull.Value ? Convert.ToDecimal(r["CreditLimit"]) : 0;
                        decimal due = r["DueBalance"] != DBNull.Value ? Convert.ToDecimal(r["DueBalance"]) : 0;
                        r.Close();

                        if (debt > 0)
                        {
                            string? creditError = null;
                            if (!allowCredit) creditError = $"الزبون '{customer}' غير مسموح له بالشراء بالكريدي";
                            else if (due + debt > limit) creditError = $"ديون الزبون ستصبح {due + debt:N2} د.م. وتتجاوز الحد المسموح ({limit:N2} د.م.)";

                            if (creditError != null)
                            {
                                if (strict)
                                {
                                    tx.Rollback();
                                    result.Error = creditError;
                                    return result;
                                }
                                result.Warnings.Add(creditError);
                            }
                        }
                    }
                }

                // 2. التحقق من المخزون
                foreach (var it in items)
                {
                    if (it.productId <= 0) continue;
                    using var stockCmd = new MySqlCommand("SELECT StockQuantity, ProductName, deleted_at FROM Products WHERE Id = @id FOR UPDATE", conn, tx);
                    stockCmd.Parameters.AddWithValue("@id", it.productId);
                    using var sr = stockCmd.ExecuteReader();
                    if (!sr.Read())
                    {
                        sr.Close();
                        if (strict)
                        {
                            tx.Rollback();
                            result.Error = $"المنتج '{it.name}' غير موجود في الحاسوب";
                            return result;
                        }
                        result.Warnings.Add($"المنتج '{it.name}' غير موجود");
                        continue;
                    }
                    decimal stock = sr["StockQuantity"] != DBNull.Value ? Convert.ToDecimal(sr["StockQuantity"]) : 0;
                    sr.Close();
                    if (stock < it.qty)
                    {
                        string msg = $"المنتج '{it.name}' لا يحتوي على كمية كافية. المتاح: {stock:0.##}";
                        if (strict)
                        {
                            tx.Rollback();
                            result.Error = msg;
                            return result;
                        }
                        result.Warnings.Add(msg);
                    }
                }

                // 3. تسجيل الفاتورة
                string paymentMethod = debt > 0 ? (paid > 0 ? "مختلط (نقداً + كريدي)" : "كريدي (Crédit)") : "نقداً (Cash)";
                int invoiceId;
                using (var cmd = new MySqlCommand("INSERT INTO Sales (CashierName, TotalAmount, Date, PaymentMethod) VALUES (@cashier, @total, @date, @pm); SELECT LAST_INSERT_ID();", conn, tx))
                {
                    cmd.Parameters.AddWithValue("@cashier", $"{cashier} ({customer})");
                    cmd.Parameters.AddWithValue("@total", total);
                    cmd.Parameters.AddWithValue("@date", GetSaleDate(root));
                    cmd.Parameters.AddWithValue("@pm", paymentMethod);
                    invoiceId = Convert.ToInt32(cmd.ExecuteScalar());
                }

                foreach (var it in items)
                {
                    bool hasProduct = it.productId > 0;
                    using (var cmd = new MySqlCommand("INSERT INTO SalesDetails (InvoiceId, ProductId, Quantity, SubTotal, CustomName) VALUES (@inv, @pid, @qty, @sub, @cname)", conn, tx))
                    {
                        cmd.Parameters.AddWithValue("@inv", invoiceId);
                        cmd.Parameters.AddWithValue("@pid", hasProduct ? it.productId : DBNull.Value);
                        cmd.Parameters.AddWithValue("@qty", it.qty);
                        cmd.Parameters.AddWithValue("@sub", it.subTotal);
                        cmd.Parameters.AddWithValue("@cname", hasProduct ? DBNull.Value : (string.IsNullOrWhiteSpace(it.name) ? "سلعة" : it.name));
                        cmd.ExecuteNonQuery();
                    }

                    if (hasProduct)
                    {
                        using var cmd = new MySqlCommand("UPDATE Products SET StockQuantity = StockQuantity - @qty WHERE Id = @pid", conn, tx);
                        cmd.Parameters.AddWithValue("@qty", it.qty);
                        cmd.Parameters.AddWithValue("@pid", it.productId);
                        cmd.ExecuteNonQuery();
                    }
                }

                // 4. تحديث رصيد الزبون ونقاط الولاء (نفس قواعد شاشة الكاشير: نقطة لكل 10 دراهم)
                if (!isWalkIn)
                {
                    int points = (int)(total / 10m);
                    string sql = debt > 0
                        ? "UPDATE Customers SET DueBalance = DueBalance + @debt, InvoiceCount = InvoiceCount + 1, LastCreditDate = @now, LoyaltyPoints = LoyaltyPoints + @pts WHERE FullName = @name AND deleted_at IS NULL"
                        : "UPDATE Customers SET InvoiceCount = InvoiceCount + 1, LoyaltyPoints = LoyaltyPoints + @pts WHERE FullName = @name AND deleted_at IS NULL";
                    using var cmd = new MySqlCommand(sql, conn, tx);
                    cmd.Parameters.AddWithValue("@debt", debt);
                    cmd.Parameters.AddWithValue("@now", DateTime.Now);
                    cmd.Parameters.AddWithValue("@pts", points);
                    cmd.Parameters.AddWithValue("@name", customer);
                    cmd.ExecuteNonQuery();
                }

                // 5. تسجيل التحذيرات (بيع دون اتصال تجاوز المخزون أو الكريدي) لمراجعتها في نافذة التعارضات
                if (result.Warnings.Count > 0)
                {
                    using var cmd = new MySqlCommand("INSERT INTO sync_conflicts (table_name, record_uuid, mobile_data, server_data, conflict_time) VALUES ('Sales', @uuid, @mobile, @server, @time)", conn, tx);
                    cmd.Parameters.AddWithValue("@uuid", opId ?? invoiceId.ToString());
                    cmd.Parameters.AddWithValue("@mobile", root.GetRawText());
                    cmd.Parameters.AddWithValue("@server", $"فاتورة رقم {invoiceId}: " + string.Join(" | ", result.Warnings));
                    cmd.Parameters.AddWithValue("@time", DateTime.UtcNow);
                    cmd.ExecuteNonQuery();
                }

                if (!string.IsNullOrWhiteSpace(opId))
                {
                    using var cmd = new MySqlCommand("INSERT INTO processed_operations (op_id, device_id, result) VALUES (@op, @dev, @res)", conn, tx);
                    cmd.Parameters.AddWithValue("@op", opId);
                    cmd.Parameters.AddWithValue("@dev", deviceId);
                    cmd.Parameters.AddWithValue("@res", JsonSerializer.Serialize(new { status = "applied", invoiceId }));
                    cmd.ExecuteNonQuery();
                }

                tx.Commit();
                result.Success = true;
                result.InvoiceId = invoiceId;
                Log.Information("Mobile sale {InvoiceId} recorded from device {Device} (op {Op}, strict={Strict})", invoiceId, deviceId, opId, strict);
                return result;
            }
            catch (Exception ex)
            {
                try { tx.Rollback(); } catch { }
                Log.Error(ex, "Mobile checkout failed (op {Op})", opId);
                result.Error = ex.Message;
                return result;
            }
        }

        private static DateTime GetSaleDate(JsonElement root)
        {
            // البيع دون اتصال يحتفظ بتوقيته الأصلي على الهاتف
            if (root.TryGetProperty("saleDate", out var d) && d.ValueKind == JsonValueKind.String &&
                DateTime.TryParse(d.GetString(), null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
            {
                dt = dt.Kind == DateTimeKind.Utc ? dt.ToLocalTime() : dt;
                if (dt <= DateTime.Now.AddMinutes(5) && dt > DateTime.Now.AddDays(-60)) return dt;
            }
            return DateTime.Now;
        }

        public static string GetString(JsonElement el, string name, string fallback)
        {
            if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var p))
            {
                if (p.ValueKind == JsonValueKind.String) return p.GetString() ?? fallback;
                if (p.ValueKind == JsonValueKind.Number) return p.GetRawText();
            }
            return fallback;
        }

        public static decimal GetDecimal(JsonElement el, string name, decimal fallback)
        {
            if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(name, out var p)) return fallback;
            if (p.ValueKind == JsonValueKind.Number && p.TryGetDecimal(out var d)) return d;
            if (p.ValueKind == JsonValueKind.String &&
                decimal.TryParse(p.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var s)) return s;
            return fallback;
        }
    }
}
