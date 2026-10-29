using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using MySqlConnector;
using Serilog;

namespace LmimaShopApp.Core.Sync
{
    public static class DeltaSyncApi
    {
        private static bool _tablesEnsured = false;

        private static string ConnectionString => SecurityHelper.GetConnectionString();

        public static async Task EnsureTablesExistAsync()
        {
            if (_tablesEnsured) return;
            try
            {
                using var conn = new MySqlConnection(ConnectionString);
                await conn.OpenAsync();

                string ddl = @"
                    CREATE TABLE IF NOT EXISTS devices (
                        device_id VARCHAR(100) PRIMARY KEY,
                        name VARCHAR(255),
                        token_hash VARCHAR(64),
                        last_seen DATETIME,
                        revoked TINYINT(1) DEFAULT 0
                    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

                    CREATE TABLE IF NOT EXISTS processed_operations (
                        op_id VARCHAR(100) PRIMARY KEY,
                        device_id VARCHAR(100),
                        result TEXT,
                        processed_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
                    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

                    CREATE TABLE IF NOT EXISTS sync_conflicts (
                        id INT AUTO_INCREMENT PRIMARY KEY,
                        table_name VARCHAR(100),
                        record_uuid VARCHAR(100),
                        mobile_data LONGTEXT,
                        server_data LONGTEXT,
                        conflict_time DATETIME
                    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

                    CREATE TABLE IF NOT EXISTS inventory_movements (
                        uuid VARCHAR(100) PRIMARY KEY,
                        product_uuid VARCHAR(100),
                        qty_delta DECIMAL(10,2),
                        reason VARCHAR(255),
                        source_device_id VARCHAR(100),
                        created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
                    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
                ";
                using var cmd = new MySqlCommand(ddl, conn);
                cmd.CommandTimeout = 30;
                await cmd.ExecuteNonQueryAsync();
                _tablesEnsured = true;
            }
            catch (Exception ex)
            {
                Log.Warning("EnsureTablesExistAsync error: " + ex.Message);
            }
        }

        public static async Task<bool> HandleRequestAsync(string method, string path, string rawUrl, Dictionary<string, string> headers, string body, NetworkStream stream)
        {
            if (path.Equals("/api/health", StringComparison.OrdinalIgnoreCase))
            {
                bool authed = MobileAuth.TryAuthenticate(headers, rawUrl, false, out _);
                await SendJsonResponseAsync(stream, 200, "OK", JsonSerializer.Serialize(new
                {
                    status = "ok",
                    version = AppVersion.Current,
                    paired = authed,
                    server_time_utc = DateTime.UtcNow.ToString("O")
                }));
                return true;
            }

            if (path.Equals("/api/pair", StringComparison.OrdinalIgnoreCase) && method == "POST")
            {
                await HandlePairAsync(body, stream);
                return true;
            }

            // EventSource في المتصفح لا يرسل ترويسات، لذلك يُقبل التوكن في الرابط لهذا المسار فقط
            bool isEvents = path.Equals("/api/events", StringComparison.OrdinalIgnoreCase) && method == "GET";
            if (!MobileAuth.TryAuthenticate(headers, rawUrl, isEvents, out string deviceId))
            {
                await SendJsonResponseAsync(stream, 401, "Unauthorized", "{\"success\":false,\"error\":\"هذا الهاتف غير مقترن. أدخل رمز الاقتران الظاهر في الحاسوب.\",\"need_pairing\":true}");
                return true;
            }

            _ = TouchDeviceAsync(deviceId);

            if (isEvents)
            {
                byte[] headersBytes = Encoding.UTF8.GetBytes(
                    "HTTP/1.1 200 OK\r\n" +
                    "Content-Type: text/event-stream; charset=utf-8\r\n" +
                    "Cache-Control: no-cache, no-transform\r\n" +
                    "Connection: keep-alive\r\n" +
                    "Access-Control-Allow-Origin: *\r\n\r\n");
                await stream.WriteAsync(headersBytes, 0, headersBytes.Length);
                await stream.FlushAsync();

                using var cts = new CancellationTokenSource();
                _ = Task.Run(async () =>
                {
                    byte[] buf = new byte[1];
                    try
                    {
                        while (await stream.ReadAsync(buf, 0, 1, cts.Token) > 0) { }
                    }
                    catch { }
                    try { cts.Cancel(); } catch { }
                });

                await SseManager.AddClientAsync(Guid.NewGuid(), stream, cts.Token);
                return true;
            }

            if (path.Equals("/api/sync", StringComparison.OrdinalIgnoreCase) && method == "GET")
            {
                try
                {
                    string syncJson = CloudSyncService.GetLatestSyncJson();
                    await SendJsonResponseAsync(stream, 200, "OK", syncJson);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Sync error");
                    await SendJsonResponseAsync(stream, 500, "Server Error", "{\"error\":\"Internal Error\"}");
                }
                return true;
            }

            if (path.Equals("/api/sync/push", StringComparison.OrdinalIgnoreCase) && method == "POST")
            {
                try
                {
                    var results = ProcessPushBody(body, deviceId);
                    if (results == null)
                    {
                        await SendJsonResponseAsync(stream, 400, "Bad Request", "{\"error\":\"Invalid payload\"}");
                        return true;
                    }
                    await SendJsonResponseAsync(stream, 200, "OK", JsonSerializer.Serialize(new { results }));
                    _ = CloudSyncService.SyncMobileDataAsync(true);
                    _ = SseManager.BroadcastEventAsync("changed", DateTime.UtcNow.ToString("O"));
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Push error");
                    await SendJsonResponseAsync(stream, 500, "Server Error", "{\"error\":\"Internal Error\"}");
                }
                return true;
            }

            return false;
        }

        private static async Task HandlePairAsync(string body, NetworkStream stream)
        {
            string code = "";
            string deviceName = "هاتف محمول";
            string deviceId = "mobile_" + Guid.NewGuid().ToString("N").Substring(0, 12);

            try
            {
                using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
                var root = doc.RootElement;
                if (root.TryGetProperty("pairing_code", out var cVal)) code = cVal.ToString().Trim();
                if (root.TryGetProperty("device_name", out var dnVal) && dnVal.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(dnVal.GetString()))
                    deviceName = dnVal.GetString()!.Trim();
                if (root.TryGetProperty("device_id", out var didVal) && didVal.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(didVal.GetString()))
                    deviceId = didVal.GetString()!.Trim();
            }
            catch
            {
                await SendJsonResponseAsync(stream, 400, "Bad Request", "{\"error\":\"Bad Request\"}");
                return;
            }

            if (deviceName.Length > 200) deviceName = deviceName.Substring(0, 200);
            if (deviceId.Length > 100) deviceId = deviceId.Substring(0, 100);

            if (!MobileAuth.ValidatePairingCode(code, out string error))
            {
                Log.Warning("Rejected pairing attempt from device {Device}: {Error}", deviceId, error);
                await SendJsonResponseAsync(stream, 401, "Unauthorized", JsonSerializer.Serialize(new { error = "Invalid or expired pairing code", message = error }));
                return;
            }

            string token = MobileAuth.IssueToken();
            try
            {
                await EnsureTablesExistAsync();
                using var conn = new MySqlConnection(ConnectionString);
                await conn.ExecuteAsync(@"
                    INSERT INTO devices (device_id, name, token_hash, last_seen, revoked)
                    VALUES (@Id, @Name, @Hash, @Seen, 0)
                    ON DUPLICATE KEY UPDATE name = @Name, token_hash = @Hash, last_seen = @Seen, revoked = 0",
                    new { Id = deviceId, Name = deviceName, Hash = MobileAuth.Sha256Hex(token), Seen = DateTime.UtcNow });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Persist device token error");
                await SendJsonResponseAsync(stream, 500, "Server Error", "{\"error\":\"تعذر حفظ الجهاز في قاعدة البيانات\"}");
                return;
            }

            MobileAuth.ClearTokenCache();
            Log.Information("Device paired: {Name} ({Id})", deviceName, deviceId);
            await SendJsonResponseAsync(stream, 200, "OK", JsonSerializer.Serialize(new
            {
                device_id = deviceId,
                token,
                status = "paired",
                message = "تم اقتران الجهاز بنجاح"
            }));
        }

        private static async Task TouchDeviceAsync(string deviceId)
        {
            try
            {
                using var conn = new MySqlConnection(ConnectionString);
                await conn.ExecuteAsync("UPDATE devices SET last_seen = @Seen WHERE device_id = @Id", new { Seen = DateTime.UtcNow, Id = deviceId });
            }
            catch { }
        }

        /// <summary>
        /// معالجة دفعة عمليات من الهاتف (عبر الشبكة أو عبر ملف Google Drive). تعيد null إذا كان المحتوى غير صالح.
        /// </summary>
        public static List<object>? ProcessPushBody(string body, string deviceId)
        {
            PushPayload? pushPayload;
            try
            {
                pushPayload = JsonSerializer.Deserialize<PushPayload>(body);
            }
            catch
            {
                return null;
            }
            if (pushPayload?.operations == null) return null;

            var results = new List<object>();
            foreach (var op in pushPayload.operations)
            {
                if (string.IsNullOrWhiteSpace(op.op_id) || string.IsNullOrWhiteSpace(op.type))
                {
                    results.Add(new { op_id = op.op_id, status = "error", message = "عملية بدون معرف أو نوع" });
                    continue;
                }

                try
                {
                    results.Add(ApplyOperation(op, deviceId));
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error processing sync push op {Op} ({Type})", op.op_id, op.type);
                    results.Add(new { op_id = op.op_id, status = "error", message = ex.Message });
                }
            }
            return results;
        }

        private static object ApplyOperation(SyncOperation op, string deviceId)
        {
            // البيع الكامل يمر عبر نفس مسار البيع المباشر (مع منع التكرار داخلياً)
            if (op.type == "pos_checkout")
            {
                var res = MobileCheckoutService.Process(op.payload, op.op_id, deviceId, strict: false);
                if (!res.Success) return new { op_id = op.op_id, status = "error", message = res.Error };
                return new
                {
                    op_id = op.op_id,
                    status = res.Duplicate ? "duplicate" : "applied",
                    result = new { invoiceId = res.InvoiceId, warnings = res.Warnings }
                };
            }

            using var conn = new MySqlConnection(ConnectionString);
            conn.Open();
            using var tx = conn.BeginTransaction();

            var processed = conn.QueryFirstOrDefault<string>("SELECT result FROM processed_operations WHERE op_id = @OpId FOR UPDATE", new { OpId = op.op_id }, tx);
            if (processed != null)
            {
                tx.Commit();
                return new { op_id = op.op_id, status = "duplicate" };
            }

            string status;
            string message = "";
            switch (op.type)
            {
                case "create_invoice":
                    {
                        // صيغة قديمة من إصدارات سابقة للتطبيق: يبقى الخصم من المخزون عبر عمليات stock_movement المرافقة
                        var invoice = JsonSerializer.Deserialize<SalesOperation>(op.payload.GetRawText());
                        if (invoice == null || string.IsNullOrWhiteSpace(invoice.uuid) || invoice.Items == null || invoice.Items.Count == 0 || invoice.TotalAmount <= 0)
                        {
                            tx.Rollback();
                            return new { op_id = op.op_id, status = "error", message = "فاتورة غير مكتملة" };
                        }
                        var exists = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM Sales WHERE uuid = @uuid", new { invoice.uuid }, tx);
                        if (exists == 0)
                        {
                            int invoiceId = conn.ExecuteScalar<int>("INSERT INTO Sales (uuid, CashierName, TotalAmount, Date) VALUES (@uuid, @CashierName, @TotalAmount, @Date); SELECT LAST_INSERT_ID();",
                                new { invoice.uuid, CashierName = invoice.CashierName ?? "تطبيق الهاتف", invoice.TotalAmount, Date = invoice.Date == default ? DateTime.Now : invoice.Date.ToLocalTime() }, tx);
                            foreach (var item in invoice.Items)
                            {
                                conn.Execute("INSERT INTO SalesDetails (uuid, InvoiceId, ProductId, Quantity, SubTotal) VALUES (@uuid, @InvoiceId, @ProductId, @Quantity, @SubTotal)",
                                    new { uuid = string.IsNullOrWhiteSpace(item.uuid) ? Guid.NewGuid().ToString() : item.uuid, InvoiceId = invoiceId, ProductId = item.ProductId > 0 ? (int?)item.ProductId : null, item.Quantity, item.SubTotal }, tx);
                            }
                        }
                        status = "applied";
                        break;
                    }

                case "stock_movement":
                    {
                        var mov = JsonSerializer.Deserialize<StockMovementOperation>(op.payload.GetRawText());
                        if (mov == null || string.IsNullOrWhiteSpace(mov.product_uuid))
                        {
                            tx.Rollback();
                            return new { op_id = op.op_id, status = "error", message = "حركة مخزون بدون منتج" };
                        }
                        int updated = conn.Execute("UPDATE Products SET StockQuantity = StockQuantity + @qty WHERE uuid = @puuid", new { qty = mov.qty_delta, puuid = mov.product_uuid }, tx);
                        if (updated == 0)
                        {
                            tx.Rollback();
                            return new { op_id = op.op_id, status = "error", message = "المنتج غير موجود في الحاسوب" };
                        }
                        conn.Execute("INSERT INTO inventory_movements (uuid, product_uuid, qty_delta, reason, source_device_id, created_at) VALUES (@uuid, @product_uuid, @qty_delta, @reason, @device_id, NOW(3))",
                            new { uuid = op.op_id, mov.product_uuid, mov.qty_delta, reason = mov.reason ?? "mobile", device_id = deviceId }, tx);
                        status = "applied";
                        break;
                    }

                case "upsert_customer":
                    {
                        var cust = JsonSerializer.Deserialize<CustomerOperation>(op.payload.GetRawText());
                        if (cust == null || string.IsNullOrWhiteSpace(cust.FullName))
                        {
                            tx.Rollback();
                            return new { op_id = op.op_id, status = "error", message = "اسم الزبون مطلوب" };
                        }
                        if (string.IsNullOrWhiteSpace(cust.uuid)) cust.uuid = Guid.NewGuid().ToString();

                        var existing = conn.QueryFirstOrDefault("SELECT Id, updated_at FROM Customers WHERE uuid = @uuid", new { cust.uuid }, tx);
                        if (existing == null)
                        {
                            int sameName = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM Customers WHERE FullName = @FullName AND deleted_at IS NULL", new { cust.FullName }, tx);
                            if (sameName > 0)
                            {
                                conn.Execute("UPDATE Customers SET Phone = @Phone WHERE FullName = @FullName AND deleted_at IS NULL", new { Phone = cust.Phone ?? "", cust.FullName }, tx);
                            }
                            else
                            {
                                conn.Execute("INSERT INTO Customers (uuid, FullName, Phone, CreditLimit, AllowCredit, DueBalance, InvoiceCount, CreditPeriodType, CreditPeriodDays) VALUES (@uuid, @FullName, @Phone, 0, 1, 0, 0, 'Unlimited', 0)",
                                    new { cust.uuid, cust.FullName, Phone = cust.Phone ?? "" }, tx);
                            }
                            status = "applied";
                        }
                        else
                        {
                            DateTime serverTime = existing.updated_at is DateTime t ? t : DateTime.MinValue;
                            if (cust.updated_at.ToLocalTime() >= serverTime)
                            {
                                conn.Execute("UPDATE Customers SET FullName = @FullName, Phone = @Phone WHERE uuid = @uuid", new { cust.FullName, Phone = cust.Phone ?? "", cust.uuid }, tx);
                                status = "applied";
                            }
                            else
                            {
                                conn.Execute("INSERT INTO sync_conflicts (table_name, record_uuid, mobile_data, conflict_time) VALUES ('Customers', @uuid, @data, @time)",
                                    new { cust.uuid, data = op.payload.GetRawText(), time = DateTime.UtcNow }, tx);
                                status = "rejected_conflict";
                                message = "تم تعديل هذا الزبون في الحاسوب بعد تعديل الهاتف";
                            }
                        }
                        break;
                    }

                case "add_debt_payment":
                    {
                        var debt = JsonSerializer.Deserialize<DebtPaymentOperation>(op.payload.GetRawText());
                        if (debt == null || debt.amount <= 0)
                        {
                            tx.Rollback();
                            return new { op_id = op.op_id, status = "error", message = "مبلغ التسديد غير صحيح" };
                        }

                        var customer = !string.IsNullOrWhiteSpace(debt.customer_uuid)
                            ? conn.QueryFirstOrDefault("SELECT Id, FullName FROM Customers WHERE uuid = @u FOR UPDATE", new { u = debt.customer_uuid }, tx)
                            : conn.QueryFirstOrDefault("SELECT Id, FullName FROM Customers WHERE Id = @id FOR UPDATE", new { id = debt.customer_id }, tx);
                        if (customer == null)
                        {
                            tx.Rollback();
                            return new { op_id = op.op_id, status = "error", message = "الزبون غير موجود في الحاسوب" };
                        }

                        DateTime payDate = debt.date == default ? DateTime.Now : debt.date.ToLocalTime();
                        conn.Execute(@"INSERT INTO CustomerPayments (uuid, CustomerId, CustomerName, Amount, PaymentDate, Note, CashierName)
                                       VALUES (@uuid, @cid, @cname, @amount, @date, 'تسديد عبر تطبيق الهاتف (دون اتصال)', 'تطبيق الهاتف')",
                            new { uuid = op.op_id, cid = (int)customer.Id, cname = (string)customer.FullName, amount = debt.amount, date = payDate }, tx);
                        conn.Execute("UPDATE Customers SET DueBalance = GREATEST(0, DueBalance - @amount) WHERE Id = @cid",
                            new { amount = debt.amount, cid = (int)customer.Id }, tx);
                        status = "applied";
                        break;
                    }

                default:
                    tx.Rollback();
                    return new { op_id = op.op_id, status = "error", message = "نوع عملية غير مدعوم: " + op.type };
            }

            conn.Execute("INSERT INTO processed_operations (op_id, device_id, result) VALUES (@op_id, @device_id, @result)",
                new { op_id = op.op_id, device_id = deviceId, result = JsonSerializer.Serialize(new { status }) }, tx);
            tx.Commit();
            return new { op_id = op.op_id, status, message };
        }

        public static async Task SendJsonResponseAsync(NetworkStream stream, int statusCode, string statusMsg, string json)
        {
            byte[] body = Encoding.UTF8.GetBytes(json);
            string headers = $"HTTP/1.1 {statusCode} {statusMsg}\r\n" +
                             "Content-Type: application/json; charset=utf-8\r\n" +
                             "Access-Control-Allow-Origin: *\r\n" +
                             "Cache-Control: no-cache, no-store, must-revalidate\r\n" +
                             $"Content-Length: {body.Length}\r\n\r\n";
            byte[] hBytes = Encoding.UTF8.GetBytes(headers);
            await stream.WriteAsync(hBytes, 0, hBytes.Length);
            await stream.WriteAsync(body, 0, body.Length);
        }
    }

    public class PushPayload
    {
        public string? device_id { get; set; }
        public List<SyncOperation>? operations { get; set; }
    }

    public class SyncOperation
    {
        public string op_id { get; set; } = "";
        public string type { get; set; } = "";
        public JsonElement payload { get; set; }
        public string? client_created_at { get; set; }
    }

    public class SalesOperation
    {
        public string uuid { get; set; } = "";
        public string? CashierName { get; set; }
        public decimal TotalAmount { get; set; }
        public DateTime Date { get; set; }
        public List<SalesItemOperation>? Items { get; set; }
    }

    public class SalesItemOperation
    {
        public string? uuid { get; set; }
        public int ProductId { get; set; }
        public decimal Quantity { get; set; }
        public decimal SubTotal { get; set; }
    }

    public class StockMovementOperation
    {
        public string? product_uuid { get; set; }
        public decimal qty_delta { get; set; }
        public string? reason { get; set; }
    }

    public class CustomerOperation
    {
        public string uuid { get; set; } = "";
        public string FullName { get; set; } = "";
        public string? Phone { get; set; }
        public DateTime updated_at { get; set; }
    }

    public class DebtPaymentOperation
    {
        public string? customer_uuid { get; set; }
        public int customer_id { get; set; }
        public decimal amount { get; set; }
        public DateTime date { get; set; }
    }
}
