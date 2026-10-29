using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MySqlConnector;

namespace LmimaShopApp
{
    /// <summary>
    /// خادم محلي فائق السرعة مدمج داخل البرنامج (Port 8080)
    /// يدعم المزامنة الفورية ثنائية الاتجاه مع تطبيق الهاتف المحمول:
    /// - قراءة المبيعات والمنتجات والزبناء والممونين والتقارير
    /// - تنفيذ عمليات البيع الفورية (POS Checkout) وتحديث المخزون والديون
    /// - إضافة وتعديل وحذف المنتجات
    /// - إدارة الزبناء وتسديد الكريدي
    /// - إدارة الممونين والشركات
    /// </summary>
    public static class MobileSyncHttpServer
    {
        // صور المنتجات تُرسل كـ Base64 لذلك نسمح بحجم معقول فقط
        private const int MaxRequestBodyBytes = 20 * 1024 * 1024;
        private const int MaxHeaderBytes = 64 * 1024;

        private static TcpListener? _listener;
        private static CancellationTokenSource? _cts;
        private static bool _isRunning;
        public static int Port { get; private set; } = 8080;
        public static string LocalIp { get; private set; } = "127.0.0.1";
        public static bool IsRunning => _isRunning;

        public static void Start(int port = 8080)
        {
            if (_isRunning) return;

            Port = port;
            LocalIp = DetectLocalIp();
            _cts = new CancellationTokenSource();

            Task.Run(async () =>
            {
                try
                {
                    _listener = new TcpListener(IPAddress.Any, Port);
                    _listener.Start();
                    LmimaShopApp.Core.Sync.SseManager.StartHeartbeat();
                    _ = LmimaShopApp.Core.Sync.DeltaSyncApi.EnsureTablesExistAsync();
                    _isRunning = true;
                    System.Diagnostics.Debug.WriteLine($"[MobileSyncServer] Started on http://{LocalIp}:{Port}/");

                    while (!_cts.Token.IsCancellationRequested)
                    {
                        var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                        _ = Task.Run(() => HandleClientAsync(client));
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    System.Diagnostics.Debug.WriteLine($"[MobileSyncServer] Stopped: {ex.Message}");
                }
                finally
                {
                    _isRunning = false;
                }
            });
        }

        public static void Stop()
        {
            try
            {
                _cts?.Cancel();
                _listener?.Stop();
                _isRunning = false;
            }
            catch {}
        }

        public static void Restart(int port = 8080)
        {
            Stop();
            Thread.Sleep(300);
            Start(port);
        }

        private static async Task HandleClientAsync(TcpClient client)
        {
            using (client)
            using (var stream = client.GetStream())
            {
                try
                {
                    stream.ReadTimeout = 5000;
                    stream.WriteTimeout = 5000;

                    var request = await ReadRequestAsync(stream);
                    if (request == null) return;

                    string method = request.Value.method;
                    string path = request.Value.path;
                    string rawUrl = request.Value.rawUrl;
                    string body = request.Value.body;

                    // 1. دعم طلبات CORS Preflight
                    if (method == "OPTIONS")
                    {
                        byte[] corsHeaders = Encoding.UTF8.GetBytes(
                            "HTTP/1.1 200 OK\r\n" +
                            "Access-Control-Allow-Origin: *\r\n" +
                            "Access-Control-Allow-Methods: GET, POST, PUT, DELETE, OPTIONS\r\n" +
                            "Access-Control-Allow-Headers: Content-Type, Authorization, X-Requested-With\r\n" +
                            "Content-Length: 0\r\n\r\n"
                        );
                        await stream.WriteAsync(corsHeaders, 0, corsHeaders.Length);
                        return;
                    }

                    // 2. مسارات المزامنة والاقتران والبث اللحظي (تتحقق من التوكن بنفسها)
                    if (path.StartsWith("/api/v2/", StringComparison.OrdinalIgnoreCase) ||
                        path.Equals("/api/health", StringComparison.OrdinalIgnoreCase) ||
                        path.Equals("/api/pair", StringComparison.OrdinalIgnoreCase) ||
                        path.Equals("/api/events", StringComparison.OrdinalIgnoreCase) ||
                        path.Equals("/api/sync", StringComparison.OrdinalIgnoreCase) ||
                        path.Equals("/api/sync/push", StringComparison.OrdinalIgnoreCase))
                    {
                        bool handled = await LmimaShopApp.Core.Sync.DeltaSyncApi.HandleRequestAsync(method, path, rawUrl, request.Value.headers, body, stream);
                        if (handled) return;
                    }

                    // 3. باقي نقاط الـ API تتطلب هاتفاً مقترناً، باستثناء الفحص والتحديث (حتى تستطيع الإصدارات القديمة التحديث)
                    bool isPublicEndpoint =
                        path.Equals("/api/ping", StringComparison.OrdinalIgnoreCase) ||
                        path.Equals("/api/version", StringComparison.OrdinalIgnoreCase) ||
                        path.Equals("/api/check-update", StringComparison.OrdinalIgnoreCase) ||
                        path.Equals("/LmimaShop.apk", StringComparison.OrdinalIgnoreCase) ||
                        path.Equals("/download/apk", StringComparison.OrdinalIgnoreCase);
                    bool isDataEndpoint = path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) ||
                                          path.StartsWith("/lmima_mobile_sync", StringComparison.OrdinalIgnoreCase);
                    string deviceId = "";
                    if (isDataEndpoint && !isPublicEndpoint &&
                        !LmimaShopApp.Core.Sync.MobileAuth.TryAuthenticate(request.Value.headers, rawUrl, false, out deviceId))
                    {
                        await SendJsonResponseAsync(stream, 401, "Unauthorized", "{\"success\":false,\"need_pairing\":true,\"error\":\"هذا الهاتف غير مقترن بالحاسوب. أدخل رمز الاقتران من نافذة اقتران الهاتف.\"}");
                        return;
                    }

                    if (method == "GET")
                    {
                        if (path.Equals("/api/customers/statement", StringComparison.OrdinalIgnoreCase))
                        {
                            await HandleCustomerStatementAsync(stream, rawUrl);
                            return;
                        }

                        if (path.Equals("/lmima_mobile_sync.json", StringComparison.OrdinalIgnoreCase))
                        {
                            string json = CloudSyncService.GetLatestSyncJson();
                            await SendJsonResponseAsync(stream, 200, "OK", json);
                            return;
                        }

                        if (path.Equals("/api/ping", StringComparison.OrdinalIgnoreCase))
                        {
                            string pingJson = JsonSerializer.Serialize(new
                            {
                                status = "online",
                                store = "متجر LMIMA SHOP",
                                ip = LocalIp,
                                port = Port,
                                version = AppVersion.Current,
                                time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                            });
                            await SendJsonResponseAsync(stream, 200, "OK", pingJson);
                            return;
                        }

                        if (path.Equals("/api/version", StringComparison.OrdinalIgnoreCase) ||
                            path.Equals("/api/check-update", StringComparison.OrdinalIgnoreCase))
                        {
                            string mobileVersion = UpdateService.GetServedApkVersion();
                            string versionJson = JsonSerializer.Serialize(new
                            {
                                version = mobileVersion,
                                desktopVersion = AppVersion.Current,
                                apkAvailable = File.Exists(UpdateService.ServedApkPath),
                                store = "متجر LMIMA SHOP",
                                apkUrl = "/LmimaShop.apk",
                                message = UpdateService.GetServedApkNotes()
                            });
                            await SendJsonResponseAsync(stream, 200, "OK", versionJson);
                            return;
                        }

                        if (path.Equals("/LmimaShop.apk", StringComparison.OrdinalIgnoreCase) ||
                            path.Equals("/download/apk", StringComparison.OrdinalIgnoreCase))
                        {
                            string apkPath = UpdateService.ServedApkPath;

                            if (File.Exists(apkPath))
                            {
                                byte[] apkBytes = await File.ReadAllBytesAsync(apkPath);
                                string apkHeaders =
                                    "HTTP/1.1 200 OK\r\n" +
                                    "Content-Type: application/vnd.android.package-archive\r\n" +
                                    "Content-Disposition: attachment; filename=\"LmimaShop.apk\"\r\n" +
                                    "Access-Control-Allow-Origin: *\r\n" +
                                    $"Content-Length: {apkBytes.Length}\r\n\r\n";

                                byte[] hBytes = Encoding.UTF8.GetBytes(apkHeaders);
                                await stream.WriteAsync(hBytes, 0, hBytes.Length);
                                await stream.WriteAsync(apkBytes, 0, apkBytes.Length);
                                return;
                            }
                        }
                    }

                    // 3. فحص مسارات الكتابة والمزامنة الفورية (POST)
                    if (method == "POST")
                    {
                        if (path.Equals("/api/pos/checkout", StringComparison.OrdinalIgnoreCase))
                        {
                            await HandlePosCheckoutAsync(stream, body, deviceId);
                            return;
                        }

                        if (path.Equals("/api/categories/save", StringComparison.OrdinalIgnoreCase))
                        {
                            await HandleCategorySaveAsync(stream, body);
                            return;
                        }

                        if (path.Equals("/api/products/save", StringComparison.OrdinalIgnoreCase))
                        {
                            await HandleProductSaveAsync(stream, body);
                            return;
                        }

                        if (path.Equals("/api/products/delete", StringComparison.OrdinalIgnoreCase))
                        {
                            await HandleProductDeleteAsync(stream, body);
                            return;
                        }

                        if (path.Equals("/api/customers/save", StringComparison.OrdinalIgnoreCase))
                        {
                            await HandleCustomerSaveAsync(stream, body);
                            return;
                        }

                        if (path.Equals("/api/customers/pay-debt", StringComparison.OrdinalIgnoreCase))
                        {
                            await HandleCustomerPayDebtAsync(stream, body);
                            return;
                        }

                        if (path.Equals("/api/customers/delete", StringComparison.OrdinalIgnoreCase))
                        {
                            await HandleCustomerDeleteAsync(stream, body);
                            return;
                        }

                        if (path.Equals("/api/suppliers/save", StringComparison.OrdinalIgnoreCase))
                        {
                            await HandleSupplierSaveAsync(stream, body);
                            return;
                        }

                        if (path.Equals("/api/suppliers/delete", StringComparison.OrdinalIgnoreCase))
                        {
                            await HandleSupplierDeleteAsync(stream, body);
                            return;
                        }

                        if (path.Equals("/api/barcode", StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                using var doc = JsonDocument.Parse(body);
                                string barcode = "";
                                if (doc.RootElement.TryGetProperty("barcode", out var bcProp))
                                {
                                    barcode = bcProp.GetString() ?? "";
                                }
                                if (!string.IsNullOrWhiteSpace(barcode))
                                {
                                    SignalRServer.NotifyBarcodeReceived(barcode.Trim());
                                    await SendJsonResponseAsync(stream, 200, "OK", "{\"success\":true,\"message\":\"تم إرسال الباركود للكاشير بنجاح\"}");
                                }
                                else
                                {
                                    await SendJsonResponseAsync(stream, 400, "Bad Request", "{\"success\":false,\"error\":\"الباركود فارغ\"}");
                                }
                            }
                            catch (Exception ex)
                            {
                                await SendJsonResponseAsync(stream, 500, "Internal Server Error", "{\"success\":false,\"error\":\"" + EscapeJson(ex.Message) + "\"}");
                            }
                            return;
                        }
                    }

                    // 4. تقديم ملفات تطبيق الويب المتنقل LmimaShopMobile (للقراءة فقط وداخل مجلده حصراً)
                    string webRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LmimaShopMobile"));
                    string? filePath = method == "GET" ? ResolveStaticFile(webRoot, path) : null;

                    if (filePath != null && File.Exists(filePath))
                    {
                        string ext = Path.GetExtension(filePath).ToLower();
                        string contentType = ext switch
                        {
                            ".html" => "text/html; charset=utf-8",
                            ".css" => "text/css; charset=utf-8",
                            ".js" => "application/javascript; charset=utf-8",
                            ".json" => "application/json; charset=utf-8",
                            ".png" => "image/png",
                            ".jpg" or ".jpeg" => "image/jpeg",
                            ".svg" => "image/svg+xml",
                            ".ico" => "image/x-icon",
                            ".apk" => "application/vnd.android.package-archive",
                            _ => "application/octet-stream"
                        };

                        byte[] fileBytes = await File.ReadAllBytesAsync(filePath);
                        string headers =
                            "HTTP/1.1 200 OK\r\n" +
                            $"Content-Type: {contentType}\r\n" +
                            "Access-Control-Allow-Origin: *\r\n" +
                            $"Content-Length: {fileBytes.Length}\r\n\r\n";

                        byte[] hBytes = Encoding.UTF8.GetBytes(headers);
                        await stream.WriteAsync(hBytes, 0, hBytes.Length);
                        await stream.WriteAsync(fileBytes, 0, fileBytes.Length);
                        return;
                    }

                    // 404 Not Found
                    byte[] notFound = Encoding.UTF8.GetBytes("HTTP/1.1 404 Not Found\r\nAccess-Control-Allow-Origin: *\r\nContent-Length: 9\r\n\r\nNot Found");
                    await stream.WriteAsync(notFound, 0, notFound.Length);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[MobileSyncServer] Error handling client: {ex.Message}");
                }
            }
        }

        #region Endpoint Handlers
        private static async Task HandlePosCheckoutAsync(NetworkStream stream, string body, string deviceId)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                string? opId = root.TryGetProperty("opId", out var op) && op.ValueKind == JsonValueKind.String ? op.GetString() : null;

                var res = LmimaShopApp.Core.Sync.MobileCheckoutService.Process(root, opId, deviceId, strict: true);
                if (!res.Success)
                {
                    // 422 = رفض منطقي (مخزون/كريدي): الهاتف يعرض الرسالة ولا يضع البيع في طابور عدم الاتصال
                    await SendJsonResponseAsync(stream, 422, "Unprocessable Entity", JsonSerializer.Serialize(new { success = false, rejected = true, error = res.Error }));
                    return;
                }

                if (!res.Duplicate)
                {
                    _ = CloudSyncService.SyncMobileDataAsync(true);
                    _ = LmimaShopApp.Core.Sync.SseManager.BroadcastEventAsync("changed", DateTime.UtcNow.ToString("O"));
                }
                await SendJsonResponseAsync(stream, 200, "OK", JsonSerializer.Serialize(new
                {
                    success = true,
                    invoiceId = res.InvoiceId,
                    duplicate = res.Duplicate,
                    message = "تم تسجيل البيع بنجاح!"
                }));
            }
            catch (Exception ex)
            {
                await SendJsonResponseAsync(stream, 400, "Bad Request", JsonSerializer.Serialize(new { success = false, error = ex.Message }));
            }
        }

                private static async Task HandleProductSaveAsync(NetworkStream stream, string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                int id = root.TryGetProperty("id", out var idProp) ? idProp.GetInt32() : 0;
                string barcode = root.TryGetProperty("barcode", out var bp) ? bp.GetString() ?? "" : "";
                string name = root.TryGetProperty("name", out var np) ? np.GetString() ?? "" : "";
                string category = root.TryGetProperty("category", out var cp) ? cp.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(category)) category = "عام";
                decimal buyPrice = root.TryGetProperty("purchasePrice", out var bpp) ? bpp.GetDecimal() : (root.TryGetProperty("buyPrice", out var bpp2) ? bpp2.GetDecimal() : 0);
                decimal sellPrice = root.TryGetProperty("sellPrice", out var spp) ? spp.GetDecimal() : (root.TryGetProperty("price", out var spp2) ? spp2.GetDecimal() : 0);
                decimal stock = root.TryGetProperty("stockQuantity", out var sqp) ? sqp.GetDecimal() : (root.TryGetProperty("stock", out var sqp2) ? sqp2.GetDecimal() : 0);
                string imageBase64 = root.TryGetProperty("imageBase64", out var imgProp) ? imgProp.GetString() ?? "" : "";

                if (string.IsNullOrWhiteSpace(name))
                {
                    await SendJsonResponseAsync(stream, 400, "Bad Request", "{\"success\":false,\"error\":\"اسم المنتج مطلوب\"}");
                    return;
                }

                string imagePath = null;
                if (!string.IsNullOrWhiteSpace(imageBase64))
                {
                    try
                    {
                        int commaIdx = imageBase64.IndexOf(',');
                        string pureBase64 = commaIdx >= 0 ? imageBase64.Substring(commaIdx + 1) : imageBase64;
                        byte[] imgBytes = Convert.FromBase64String(pureBase64);

                        string safeCategory = string.IsNullOrWhiteSpace(category) ? "Uncategorized" : string.Join("_", category.Split(Path.GetInvalidFileNameChars()));
                        string safeBarcode = string.IsNullOrWhiteSpace(barcode) ? ("prod_" + DateTime.Now.Ticks) : string.Join("_", barcode.Split(Path.GetInvalidFileNameChars()));
                        
                        string appBaseDir = AppDomain.CurrentDomain.BaseDirectory;
                        string targetDir = Path.Combine(appBaseDir, "ProductImages", safeCategory);
                        if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

                        string targetFile = Path.Combine(targetDir, safeBarcode + ".jpg");
                        File.WriteAllBytes(targetFile, imgBytes);
                        imagePath = Path.Combine("ProductImages", safeCategory, safeBarcode + ".jpg").Replace('\\', '/');
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine("Error saving mobile image: " + ex.Message);
                    }
                }

                var dbHelper = new DatabaseHelper();
                using (var conn = dbHelper.GetConnection())
                {
                    conn.Open();

                    if (!string.IsNullOrWhiteSpace(category))
                    {
                        try
                        {
                            using var catCmd = new MySqlCommand("INSERT IGNORE INTO Categories (CategoryName) VALUES (@cat);", conn);
                            catCmd.Parameters.AddWithValue("@cat", category);
                            catCmd.ExecuteNonQuery();
                        }
                        catch {}
                    }

                    string query;
                    if (id > 0)
                    {
                        if (imagePath != null)
                            query = "UPDATE Products SET Barcode = @b, ProductName = @n, Category = @c, PurchasePrice = @p, SellPrice = @s, StockQuantity = @st, ImagePath = @img WHERE Id = @id";
                        else
                            query = "UPDATE Products SET Barcode = @b, ProductName = @n, Category = @c, PurchasePrice = @p, SellPrice = @s, StockQuantity = @st WHERE Id = @id";
                    }
                    else
                    {
                        query = "INSERT INTO Products (Barcode, ProductName, Category, PurchasePrice, SellPrice, StockQuantity, ImagePath) VALUES (@b, @n, @c, @p, @s, @st, @img); SELECT LAST_INSERT_ID();";
                    }

                    using (var cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@b", barcode);
                        cmd.Parameters.AddWithValue("@n", name);
                        cmd.Parameters.AddWithValue("@c", category);
                        cmd.Parameters.AddWithValue("@p", buyPrice);
                        cmd.Parameters.AddWithValue("@s", sellPrice);
                        cmd.Parameters.AddWithValue("@st", stock);
                        if (imagePath != null || id <= 0)
                            cmd.Parameters.AddWithValue("@img", (object?)imagePath ?? DBNull.Value);
                        if (id > 0) cmd.Parameters.AddWithValue("@id", id);

                        if (id > 0)
                        {
                            cmd.ExecuteNonQuery();
                        }
                        else
                        {
                            id = Convert.ToInt32(cmd.ExecuteScalar());
                        }
                    }
                }

                _ = CloudSyncService.SyncMobileDataAsync(true);
                await SendJsonResponseAsync(stream, 200, "OK", "{\"success\":true,\"id\":" + id + ",\"imagePath\":\"" + (imagePath ?? "") + "\",\"message\":\"تم حفظ المنتج بنجاح!\"}");
            }
            catch (Exception ex)
            {
                await SendJsonResponseAsync(stream, 500, "Internal Server Error", "{\"success\":false,\"error\":\"" + EscapeJson(ex.Message) + "\"}");
            }
        }

private static async Task HandleProductDeleteAsync(NetworkStream stream, string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                int id = doc.RootElement.TryGetProperty("id", out var idProp) ? idProp.GetInt32() : 0;
                if (id <= 0)
                {
                    await SendJsonResponseAsync(stream, 400, "Bad Request", "{\"success\":false,\"error\":\"معرف المنتج غير صحيح\"}");
                    return;
                }

                var dbHelper = new DatabaseHelper();
                using (var conn = dbHelper.GetConnection())
                {
                    conn.Open();
                    string query = "UPDATE Products SET deleted_at = UTC_TIMESTAMP(3) WHERE Id = @id";
                    using (var cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@id", id);
                        cmd.ExecuteNonQuery();
                    }
                }

                _ = CloudSyncService.SyncMobileDataAsync(true);
                await SendJsonResponseAsync(stream, 200, "OK", "{\"success\":true,\"message\":\"تم حذف المنتج بنجاح!\"}");
            }
            catch (Exception ex)
            {
                await SendJsonResponseAsync(stream, 500, "Internal Server Error", $"{{\"success\":false,\"error\":\"{EscapeJson(ex.Message)}\"}}");
            }
        }

        private static async Task HandleCustomerSaveAsync(NetworkStream stream, string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                int id = root.TryGetProperty("id", out var idProp) ? idProp.GetInt32() : 0;
                string name = root.TryGetProperty("fullName", out var np) ? np.GetString() ?? "" : "";
                string phone = root.TryGetProperty("phone", out var pp) ? pp.GetString() ?? "" : "";
                decimal limit = root.TryGetProperty("creditLimit", out var lp) ? lp.GetDecimal() : 0;
                bool allowCredit = root.TryGetProperty("allowCredit", out var ap) ? ap.GetBoolean() : true;

                if (string.IsNullOrWhiteSpace(name))
                {
                    await SendJsonResponseAsync(stream, 400, "Bad Request", "{\"success\":false,\"error\":\"اسم الزبون مطلوب\"}");
                    return;
                }

                var dbHelper = new DatabaseHelper();
                using (var conn = dbHelper.GetConnection())
                {
                    conn.Open();
                    string query;
                    if (id > 0)
                    {
                        query = "UPDATE Customers SET FullName = @n, Phone = @p, CreditLimit = @l, AllowCredit = @a WHERE Id = @id";
                    }
                    else
                    {
                        query = "INSERT INTO Customers (FullName, Phone, CreditLimit, AllowCredit, DueBalance, InvoiceCount, CreditPeriodType, CreditPeriodDays) VALUES (@n, @p, @l, @a, 0.00, 0, 'Unlimited', 0); SELECT LAST_INSERT_ID();";
                    }

                    using (var cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@n", name);
                        cmd.Parameters.AddWithValue("@p", phone);
                        cmd.Parameters.AddWithValue("@l", limit);
                        cmd.Parameters.AddWithValue("@a", allowCredit);
                        if (id > 0) cmd.Parameters.AddWithValue("@id", id);

                        if (id > 0)
                        {
                            cmd.ExecuteNonQuery();
                        }
                        else
                        {
                            id = Convert.ToInt32(cmd.ExecuteScalar());
                        }
                    }
                }

                _ = CloudSyncService.SyncMobileDataAsync(true);
                await SendJsonResponseAsync(stream, 200, "OK", $"{{\"success\":true,\"id\":{id},\"message\":\"تم حفظ الزبون بنجاح!\"}}");
            }
            catch (Exception ex)
            {
                await SendJsonResponseAsync(stream, 500, "Internal Server Error", $"{{\"success\":false,\"error\":\"{EscapeJson(ex.Message)}\"}}");
            }
        }

        private static async Task HandleCustomerPayDebtAsync(NetworkStream stream, string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                int id = root.TryGetProperty("id", out var idProp) ? idProp.GetInt32() : 0;
                decimal amount = root.TryGetProperty("amount", out var ap) ? ap.GetDecimal() : 0;

                if (id <= 0 || amount <= 0)
                {
                    await SendJsonResponseAsync(stream, 400, "Bad Request", "{\"success\":false,\"error\":\"المبلغ أو الزبون غير صحيح\"}");
                    return;
                }

                var dbHelper = new DatabaseHelper();
                using (var conn = dbHelper.GetConnection())
                {
                    conn.Open();
                    string query = @"
                        UPDATE Customers SET DueBalance = GREATEST(0, DueBalance - @amount) WHERE Id = @id;
                        INSERT INTO CustomerPayments (CustomerId, CustomerName, Amount, PaymentDate, Note, CashierName) 
                        VALUES (@id, (SELECT FullName FROM Customers WHERE Id = @id), @amount, NOW(), 'تسديد نقدي عبر تطبيق الهاتف', 'تطبيق الهاتف');";
                    using (var cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@amount", amount);
                        cmd.Parameters.AddWithValue("@id", id);
                        cmd.ExecuteNonQuery();
                    }
                }

                _ = CloudSyncService.SyncMobileDataAsync(true);
                await SendJsonResponseAsync(stream, 200, "OK", "{\"success\":true,\"message\":\"تم تسديد الدين وخصمه بنجاح!\"}");
            }
            catch (Exception ex)
            {
                await SendJsonResponseAsync(stream, 500, "Internal Server Error", $"{{\"success\":false,\"error\":\"{EscapeJson(ex.Message)}\"}}");
            }
        }

        private static async Task HandleCustomerDeleteAsync(NetworkStream stream, string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                int id = doc.RootElement.TryGetProperty("id", out var idProp) ? idProp.GetInt32() : 0;
                if (id <= 0)
                {
                    await SendJsonResponseAsync(stream, 400, "Bad Request", "{\"success\":false,\"error\":\"معرف الزبون غير صحيح\"}");
                    return;
                }

                var dbHelper = new DatabaseHelper();
                using (var conn = dbHelper.GetConnection())
                {
                    conn.Open();
                    string query = "UPDATE Customers SET deleted_at = UTC_TIMESTAMP(3) WHERE Id = @id";
                    using (var cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@id", id);
                        cmd.ExecuteNonQuery();
                    }
                }

                _ = CloudSyncService.SyncMobileDataAsync(true);
                await SendJsonResponseAsync(stream, 200, "OK", "{\"success\":true,\"message\":\"تم حذف الزبون بنجاح!\"}");
            }
            catch (Exception ex)
            {
                await SendJsonResponseAsync(stream, 500, "Internal Server Error", $"{{\"success\":false,\"error\":\"{EscapeJson(ex.Message)}\"}}");
            }
        }

        private static async Task HandleSupplierSaveAsync(NetworkStream stream, string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                int id = root.TryGetProperty("id", out var idProp) ? idProp.GetInt32() : 0;
                string name = root.TryGetProperty("fullName", out var np) ? np.GetString() ?? "" : "";
                string company = root.TryGetProperty("company", out var cp) ? cp.GetString() ?? "" : "";
                string phone = root.TryGetProperty("phone", out var pp) ? pp.GetString() ?? "" : "";
                string email = root.TryGetProperty("email", out var ep) ? ep.GetString() ?? "" : "";
                string address = root.TryGetProperty("address", out var ap) ? ap.GetString() ?? "" : "";
                decimal balance = root.TryGetProperty("dueBalance", out var bp) ? bp.GetDecimal() : 0;

                if (string.IsNullOrWhiteSpace(name))
                {
                    await SendJsonResponseAsync(stream, 400, "Bad Request", "{\"success\":false,\"error\":\"اسم الممون مطلوب\"}");
                    return;
                }

                var dbHelper = new DatabaseHelper();
                using (var conn = dbHelper.GetConnection())
                {
                    conn.Open();
                    string query;
                    if (id > 0)
                    {
                        query = "UPDATE Suppliers SET FullName = @name, Company = @company, Phone = @phone, Email = @email, Address = @address, DueBalance = @balance WHERE Id = @id";
                    }
                    else
                    {
                        query = "INSERT INTO Suppliers (FullName, Company, Phone, Email, Address, DueBalance) VALUES (@name, @company, @phone, @email, @address, @balance); SELECT LAST_INSERT_ID();";
                    }

                    using (var cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@name", name);
                        cmd.Parameters.AddWithValue("@company", company);
                        cmd.Parameters.AddWithValue("@phone", phone);
                        cmd.Parameters.AddWithValue("@email", email);
                        cmd.Parameters.AddWithValue("@address", address);
                        cmd.Parameters.AddWithValue("@balance", balance);
                        if (id > 0) cmd.Parameters.AddWithValue("@id", id);

                        if (id > 0)
                        {
                            cmd.ExecuteNonQuery();
                        }
                        else
                        {
                            id = Convert.ToInt32(cmd.ExecuteScalar());
                        }
                    }
                }

                _ = CloudSyncService.SyncMobileDataAsync(true);
                await SendJsonResponseAsync(stream, 200, "OK", $"{{\"success\":true,\"id\":{id},\"message\":\"تم حفظ الممون بنجاح!\"}}");
            }
            catch (Exception ex)
            {
                await SendJsonResponseAsync(stream, 500, "Internal Server Error", $"{{\"success\":false,\"error\":\"{EscapeJson(ex.Message)}\"}}");
            }
        }

        private static async Task HandleSupplierDeleteAsync(NetworkStream stream, string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                int id = doc.RootElement.TryGetProperty("id", out var idProp) ? idProp.GetInt32() : 0;
                if (id <= 0)
                {
                    await SendJsonResponseAsync(stream, 400, "Bad Request", "{\"success\":false,\"error\":\"معرف الممون غير صحيح\"}");
                    return;
                }

                var dbHelper = new DatabaseHelper();
                using (var conn = dbHelper.GetConnection())
                {
                    conn.Open();
                    string query = "DELETE FROM Suppliers WHERE Id = @id";
                    using (var cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@id", id);
                        cmd.ExecuteNonQuery();
                    }
                }

                _ = CloudSyncService.SyncMobileDataAsync(true);
                await SendJsonResponseAsync(stream, 200, "OK", "{\"success\":true,\"message\":\"تم حذف الممون بنجاح!\"}");
            }
            catch (Exception ex)
            {
                await SendJsonResponseAsync(stream, 500, "Internal Server Error", $"{{\"success\":false,\"error\":\"{EscapeJson(ex.Message)}\"}}");
            }
        }

        private static async Task HandleCustomerStatementAsync(NetworkStream stream, string rawUrl)
        {
            try
            {
                var query = ParseQueryString(rawUrl);
                int customerId = 0;
                if (query.TryGetValue("id", out var idStr)) int.TryParse(idStr, out customerId);
                string customerName = query.TryGetValue("name", out var nStr) ? nStr.Trim() : "";
                string fromStr = query.TryGetValue("from", out var fStr) ? fStr.Trim() : "";
                string toStr = query.TryGetValue("to", out var tStr) ? tStr.Trim() : "";

                DateTime? fromDate = null;
                DateTime? toDate = null;
                if (DateTime.TryParse(fromStr, out var fd)) fromDate = fd.Date;
                if (DateTime.TryParse(toStr, out var td)) toDate = td.Date.AddDays(1).AddTicks(-1);

                var dbHelper = new DatabaseHelper();
                using var conn = dbHelper.GetConnection();
                conn.Open();

                // 1. استرجاع بيانات الزبون
                string custSql = "SELECT Id, FullName, Phone, CreditLimit, DueBalance, AllowCredit, LastCreditDate FROM Customers WHERE deleted_at IS NULL AND ";
                if (customerId > 0) custSql += "Id = @id LIMIT 1;";
                else if (!string.IsNullOrEmpty(customerName)) custSql += "FullName = @name LIMIT 1;";
                else
                {
                    await SendJsonResponseAsync(stream, 400, "Bad Request", "{\"success\":false,\"error\":\"يجب تحديد رقم أو اسم الزبون\"}");
                    return;
                }

                int custId = 0;
                string fullName = "";
                string phone = "";
                decimal creditLimit = 0;
                decimal dueBalance = 0;
                bool allowCredit = true;

                using (var cmd = new MySqlCommand(custSql, conn))
                {
                    if (customerId > 0) cmd.Parameters.AddWithValue("@id", customerId);
                    else cmd.Parameters.AddWithValue("@name", customerName);

                    using var r = cmd.ExecuteReader();
                    if (!r.Read())
                    {
                        await SendJsonResponseAsync(stream, 404, "Not Found", "{\"success\":false,\"error\":\"الزبون المطلوب غير مسجل\"}");
                        return;
                    }

                    custId = Convert.ToInt32(r["Id"]);
                    fullName = r["FullName"]?.ToString() ?? "";
                    phone = r["Phone"]?.ToString() ?? "";
                    creditLimit = r["CreditLimit"] != DBNull.Value ? Convert.ToDecimal(r["CreditLimit"]) : 0;
                    dueBalance = r["DueBalance"] != DBNull.Value ? Convert.ToDecimal(r["DueBalance"]) : 0;
                    allowCredit = r["AllowCredit"] != DBNull.Value && Convert.ToBoolean(r["AllowCredit"]);
                }

                // 2. استرجاع لائحة المشتريات ضمن الفترة
                var purchases = new List<object>();
                decimal totalPurchases = 0;
                string purchSql = @"
                    SELECT sd.Id AS DetailId, sd.InvoiceId, sd.ProductId,
                           COALESCE(sd.CustomName, p.ProductName, 'سلعة') AS ProductName,
                           sd.Quantity,
                           CASE WHEN sd.Quantity = 0 THEN 0 ELSE sd.SubTotal / sd.Quantity END AS UnitPrice,
                           sd.SubTotal, s.Date AS PurchaseDate
                    FROM SalesDetails sd
                    JOIN Sales s ON sd.InvoiceId = s.InvoiceId
                    LEFT JOIN Products p ON sd.ProductId = p.Id
                    WHERE s.CashierName LIKE @pattern ";

                if (fromDate.HasValue) purchSql += " AND s.Date >= @from ";
                if (toDate.HasValue) purchSql += " AND s.Date <= @to ";
                purchSql += " ORDER BY s.Date DESC;";

                using (var cmd = new MySqlCommand(purchSql, conn))
                {
                    cmd.Parameters.AddWithValue("@pattern", "%(" + fullName + ")");
                    if (fromDate.HasValue) cmd.Parameters.AddWithValue("@from", fromDate.Value);
                    if (toDate.HasValue) cmd.Parameters.AddWithValue("@to", toDate.Value);

                    using var r = cmd.ExecuteReader();
                    while (r.Read())
                    {
                        decimal sub = r["SubTotal"] != DBNull.Value ? Convert.ToDecimal(r["SubTotal"]) : 0;
                        decimal qty = r["Quantity"] != DBNull.Value ? Convert.ToDecimal(r["Quantity"]) : 1;
                        decimal price = r["UnitPrice"] != DBNull.Value ? Convert.ToDecimal(r["UnitPrice"]) : 0;
                        DateTime dt = Convert.ToDateTime(r["PurchaseDate"]);

                        totalPurchases += sub;
                        purchases.Add(new
                        {
                            id = Convert.ToInt32(r["DetailId"]),
                            invoiceId = Convert.ToInt32(r["InvoiceId"]),
                            productName = r["ProductName"]?.ToString() ?? "سلعة",
                            quantity = qty,
                            unitPrice = price,
                            subTotal = sub,
                            date = dt.ToString("yyyy-MM-dd HH:mm")
                        });
                    }
                }

                // 3. استرجاع لائحة المبالغ المدفوعة (التسديدات)
                var payments = new List<object>();
                decimal totalPaid = 0;
                string paySql = @"
                    SELECT Id, Amount, PaymentDate, COALESCE(Note, 'تسديد نقدي للكريدي') AS Note, COALESCE(CashierName, 'المسؤول') AS CashierName
                    FROM CustomerPayments
                    WHERE (CustomerId = @cid OR CustomerName = @cname) ";

                if (fromDate.HasValue) paySql += " AND PaymentDate >= @from ";
                if (toDate.HasValue) paySql += " AND PaymentDate <= @to ";
                paySql += " ORDER BY PaymentDate DESC;";

                using (var cmd = new MySqlCommand(paySql, conn))
                {
                    cmd.Parameters.AddWithValue("@cid", custId);
                    cmd.Parameters.AddWithValue("@cname", fullName);
                    if (fromDate.HasValue) cmd.Parameters.AddWithValue("@from", fromDate.Value);
                    if (toDate.HasValue) cmd.Parameters.AddWithValue("@to", toDate.Value);

                    using var r = cmd.ExecuteReader();
                    while (r.Read())
                    {
                        decimal amt = r["Amount"] != DBNull.Value ? Convert.ToDecimal(r["Amount"]) : 0;
                        DateTime dt = Convert.ToDateTime(r["PaymentDate"]);
                        string note = r["Note"]?.ToString() ?? "تسديد نقدي";
                        string cashier = r["CashierName"]?.ToString() ?? "المسؤول";

                        totalPaid += amt;
                        payments.Add(new
                        {
                            id = Convert.ToInt32(r["Id"]),
                            amount = amt,
                            date = dt.ToString("yyyy-MM-dd HH:mm"),
                            note = note,
                            cashier = cashier
                        });
                    }
                }

                string periodLabel = (fromDate.HasValue && toDate.HasValue)
                    ? $"من {fromStr} إلى {toStr}"
                    : (fromDate.HasValue ? $"من {fromStr}" : (toDate.HasValue ? $"إلى {toStr}" : "كافة التواريخ (منذ البداية)"));

                var store = CloudSyncService.LoadAppSettings();
                var resp = new
                {
                    success = true,
                    storeInfo = new
                    {
                        storeName = string.IsNullOrWhiteSpace(store.StoreName) ? "متجر LMIMA SHOP" : store.StoreName,
                        phone = store.StorePhone,
                        address = string.IsNullOrWhiteSpace(store.StoreAddress) ? "المملكة المغربية" : store.StoreAddress,
                        generatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                    },
                    customer = new
                    {
                        id = custId,
                        name = fullName,
                        phone = phone,
                        creditLimit = creditLimit,
                        dueBalance = dueBalance,
                        dueBalanceRial = dueBalance * 20,
                        allowCredit = allowCredit
                    },
                    filter = new
                    {
                        from = fromStr,
                        to = toStr,
                        periodLabel = periodLabel
                    },
                    purchases = purchases,
                    payments = payments,
                    summary = new
                    {
                        totalPurchases = totalPurchases,
                        totalPurchasesRial = totalPurchases * 20,
                        totalPaid = totalPaid,
                        totalPaidRial = totalPaid * 20,
                        currentBalance = dueBalance,
                        currentBalanceRial = dueBalance * 20
                    }
                };

                string json = JsonSerializer.Serialize(resp, new JsonSerializerOptions { WriteIndented = true });
                await SendJsonResponseAsync(stream, 200, "OK", json);
            }
            catch (Exception ex)
            {
                await SendJsonResponseAsync(stream, 500, "Internal Server Error", $"{{\"success\":false,\"error\":\"{EscapeJson(ex.Message)}\"}}");
            }
        }
        #endregion

        #region HTTP Protocol Helpers
        private static async Task<(string method, string path, string rawUrl, Dictionary<string, string> headers, string body)?> ReadRequestAsync(NetworkStream stream)
        {
            using var memStream = new MemoryStream();
            byte[] buffer = new byte[4096];
            int headerEnd = -1;
            int contentLength = 0;
            string method = "";
            string path = "";
            string rawUrl = "";
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            while (true)
            {
                int read = await stream.ReadAsync(buffer, 0, buffer.Length);
                if (read <= 0) break;
                memStream.Write(buffer, 0, read);
                if (headerEnd == -1 && memStream.Length > MaxHeaderBytes) return null;

                if (headerEnd == -1)
                {
                    byte[] currentBytes = memStream.ToArray();
                    for (int i = 0; i < currentBytes.Length - 3; i++)
                    {
                        if (currentBytes[i] == '\r' && currentBytes[i + 1] == '\n' && currentBytes[i + 2] == '\r' && currentBytes[i + 3] == '\n')
                        {
                            headerEnd = i + 4;
                            break;
                        }
                    }

                    if (headerEnd != -1)
                    {
                        string headerText = Encoding.UTF8.GetString(currentBytes, 0, headerEnd);
                        var lines = headerText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                        if (lines.Length > 0)
                        {
                            var reqParts = lines[0].Split(' ');
                            if (reqParts.Length >= 2)
                            {
                                method = reqParts[0].ToUpperInvariant();
                                rawUrl = reqParts[1];
                                path = reqParts[1];
                                if (path.Contains("?")) path = path.Substring(0, path.IndexOf('?'));
                            }
                        }
                        for (int i = 1; i < lines.Length; i++)
                        {
                            int colon = lines[i].IndexOf(':');
                            if (colon > 0)
                            {
                                string k = lines[i].Substring(0, colon).Trim();
                                string v = lines[i].Substring(colon + 1).Trim();
                                headers[k] = v;
                            }
                        }
                        if (headers.TryGetValue("Content-Length", out var clStr) && int.TryParse(clStr, out var cl))
                        {
                            if (cl < 0 || cl > MaxRequestBodyBytes) return null;
                            contentLength = cl;
                        }
                    }
                }

                if (headerEnd != -1)
                {
                    int bodyReceived = (int)memStream.Length - headerEnd;
                    if (bodyReceived >= contentLength)
                    {
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(method)) return null;

            string body = "";
            if (contentLength > 0 && headerEnd != -1)
            {
                byte[] all = memStream.ToArray();
                body = Encoding.UTF8.GetString(all, headerEnd, Math.Min(contentLength, all.Length - headerEnd));
            }

            return (method, path, rawUrl, headers, body);
        }

        /// <summary>
        /// تحويل مسار الطلب إلى ملف داخل مجلد تطبيق الهاتف، مع رفض أي محاولة للخروج من المجلد (../)
        /// ومنع تقديم ملفات البيانات التي قد تُترك في المجلد
        /// </summary>
        private static string? ResolveStaticFile(string webRoot, string requestPath)
        {
            string relative;
            try
            {
                relative = Uri.UnescapeDataString(requestPath).TrimStart('/');
            }
            catch
            {
                return null;
            }
            if (string.IsNullOrEmpty(relative)) relative = "index.html";
            if (relative.Contains("..") || relative.Contains(':') || relative.Contains('\\') || relative.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                return null;

            string fileName = Path.GetFileName(relative);
            if (fileName.StartsWith("lmima_mobile_sync", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(".apk", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                return null;

            string full = Path.GetFullPath(Path.Combine(webRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
            string rootWithSep = webRoot.EndsWith(Path.DirectorySeparatorChar) ? webRoot : webRoot + Path.DirectorySeparatorChar;
            return full.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase) ? full : null;
        }

        private static Dictionary<string, string> ParseQueryString(string rawUrl)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(rawUrl)) return dict;
            int qIndex = rawUrl.IndexOf('?');
            if (qIndex >= 0 && qIndex < rawUrl.Length - 1)
            {
                string qs = rawUrl.Substring(qIndex + 1);
                string[] pairs = qs.Split('&');
                foreach (var pair in pairs)
                {
                    int eq = pair.IndexOf('=');
                    if (eq > 0)
                    {
                        string key = Uri.UnescapeDataString(pair.Substring(0, eq));
                        string val = Uri.UnescapeDataString(pair.Substring(eq + 1));
                        dict[key] = val;
                    }
                }
            }
            return dict;
        }

        private static async Task SendJsonResponseAsync(NetworkStream stream, int statusCode, string statusText, string json)
        {
            byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
            string headers =
                $"HTTP/1.1 {statusCode} {statusText}\r\n" +
                "Content-Type: application/json; charset=utf-8\r\n" +
                "Access-Control-Allow-Origin: *\r\n" +
                "Access-Control-Allow-Methods: GET, POST, PUT, DELETE, OPTIONS\r\n" +
                "Access-Control-Allow-Headers: Content-Type, Authorization, X-Requested-With\r\n" +
                "Cache-Control: no-cache, no-store, must-revalidate\r\n" +
                $"Content-Length: {jsonBytes.Length}\r\n\r\n";

            byte[] hBytes = Encoding.UTF8.GetBytes(headers);
            await stream.WriteAsync(hBytes, 0, hBytes.Length);
            await stream.WriteAsync(jsonBytes, 0, jsonBytes.Length);
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", " ");
        }

        private static string DetectLocalIp()
        {
            try
            {
                using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
                {
                    socket.Connect("8.8.8.8", 65530);
                    if (socket.LocalEndPoint is IPEndPoint endPoint)
                    {
                        return endPoint.Address.ToString();
                    }
                }
            }
            catch {}

            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                    {
                        string str = ip.ToString();
                        if (str.StartsWith("192.168.") || str.StartsWith("10.") || str.StartsWith("172."))
                        {
                            return str;
                        }
                    }
                }
            }
            catch {}

            return "127.0.0.1";
        }

        public static List<string> GetAllLocalIps()
        {
            var list = new List<string>();
            try
            {
                var interfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
                foreach (var ni in interfaces)
                {
                    if (ni.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up &&
                        ni.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                    {
                        var props = ni.GetIPProperties();
                        foreach (var addr in props.UnicastAddresses)
                        {
                            if (addr.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(addr.Address))
                            {
                                string ipStr = addr.Address.ToString();
                                if (!list.Contains(ipStr)) list.Add(ipStr);
                            }
                        }
                    }
                }
            }
            catch {}

            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                    {
                        string ipStr = ip.ToString();
                        if (!list.Contains(ipStr)) list.Add(ipStr);
                    }
                }
            }
            catch {}

            if (!list.Contains(LocalIp) && !string.IsNullOrEmpty(LocalIp) && LocalIp != "127.0.0.1")
            {
                list.Insert(0, LocalIp);
            }
            if (list.Count == 0) list.Add(LocalIp);
            return list;
        }

        public static bool OpenWindowsFirewall()
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = "advfirewall firewall add rule name=\"LmimaShop Mobile Sync API\" dir=in action=allow protocol=TCP localport=8080,8081",
                    UseShellExecute = true,
                    Verb = "runas",
                    CreateNoWindow = true
                };
                var proc = System.Diagnostics.Process.Start(psi);
                proc?.WaitForExit(5000);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Firewall] Error: {ex.Message}");
                return false;
            }
        }

        private static async Task HandleCategorySaveAsync(NetworkStream stream, string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                string name = doc.RootElement.TryGetProperty("name", out var np) ? np.GetString()?.Trim() ?? "" : "";
                if (string.IsNullOrWhiteSpace(name))
                {
                    await SendJsonResponseAsync(stream, 400, "Bad Request", "{\"success\":false,\"error\":\"اسم الفئة مطلوب\"}");
                    return;
                }

                var dbHelper = new DatabaseHelper();
                using (var conn = dbHelper.GetConnection())
                {
                    conn.Open();
                    using var cmd = new MySqlCommand("INSERT IGNORE INTO Categories (CategoryName) VALUES (@n);", conn);
                    cmd.Parameters.AddWithValue("@n", name);
                    cmd.ExecuteNonQuery();
                }

                _ = CloudSyncService.SyncMobileDataAsync(true);
                await SendJsonResponseAsync(stream, 200, "OK", "{\"success\":true,\"message\":\"تمت إضافة الفئة بنجاح!\"}");
            }
            catch (Exception ex)
            {
                await SendJsonResponseAsync(stream, 500, "Internal Server Error", $"{{\"success\":false,\"error\":\"{EscapeJson(ex.Message)}\"}}");
            }
        }
        #endregion
    }
}




