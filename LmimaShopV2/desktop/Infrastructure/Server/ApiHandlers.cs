using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using LmimaShopV2.Desktop.Core.Interfaces;
using LmimaShopV2.Desktop.Core.Models;
using LmimaShopV2.Desktop.Helpers;
using LmimaShopV2.Desktop.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace LmimaShopV2.Desktop.Infrastructure.Server;

/// <summary>Implements the HTTP contract from docs/API_SPEC.md.</summary>
public sealed class ApiHandlers
{
    private readonly ICheckoutService _checkout;
    private readonly IReturnsService _returns;
    private readonly IProductService _products;
    private readonly ICustomerService _customers;
    private readonly ISyncService _sync;
    private readonly PairingService _pairing;
    private readonly PairingRateLimiter _pairLimiter = new();
    private readonly SseHub _sse;
    private readonly Db _db;
    private readonly ILogger<ApiHandlers> _logger;

    public ApiHandlers(
        ICheckoutService checkout,
        IReturnsService returns,
        IProductService products,
        ICustomerService customers,
        ISyncService sync,
        PairingService pairing,
        SseHub sse,
        Db db,
        ILogger<ApiHandlers> logger)
    {
        _checkout = checkout;
        _returns = returns;
        _products = products;
        _customers = customers;
        _sync = sync;
        _pairing = pairing;
        _sse = sse;
        _db = db;
        _logger = logger;
    }

    public async Task<bool> TryHandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var request = context.Request;
        var path = (request.Url?.AbsolutePath ?? "/").TrimEnd('/');
        if (!path.Equals("/api", StringComparison.OrdinalIgnoreCase) &&
            !path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
            return false;
        if (path.Length == 0) path = "/api";

        var isGet = string.Equals(request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase);
        var isPost = string.Equals(request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase);
        var query = HttpUtil.ParseQuery(request.Url?.Query);

        try
        {
            if (path.Equals("/api/ping", StringComparison.OrdinalIgnoreCase))
            {
                if (!isGet)
                    await HttpUtil.WriteJsonAsync(context.Response, 405, new ErrorResponse("method_not_allowed"), cancellationToken);
                else
                    await HttpUtil.WriteJsonAsync(context.Response, 200, new PingResponse(), cancellationToken);
                return true;
            }

            if (path.Equals("/api/pair", StringComparison.OrdinalIgnoreCase))
            {
                if (!isPost)
                    await HttpUtil.WriteJsonAsync(context.Response, 405, new ErrorResponse("method_not_allowed"), cancellationToken);
                else
                    await HandlePairAsync(context, cancellationToken);
                return true;
            }

            var device = await AuthenticateAsync(context, cancellationToken);
            if (device is null) return true;

            if (path.Equals("/api/sync", StringComparison.OrdinalIgnoreCase) && isGet)
            {
                query.TryGetValue("since", out var since);
                var delta = await _sync.GetDeltaAsync(since, cancellationToken);
                await HttpUtil.WriteJsonAsync(context.Response, 200, delta, cancellationToken);
                return true;
            }

            if (path.Equals("/api/sync/push", StringComparison.OrdinalIgnoreCase) && isPost)
            {
                var body = await HttpUtil.ReadBodyAsync(request, cancellationToken);
                var push = HttpUtil.Deserialize<SyncPushRequest>(body)
                           ?? throw new BusinessRuleException("Corps JSON invalide.");
                var result = await _sync.PushAsync(push, device.Name, cancellationToken);
                await HttpUtil.WriteJsonAsync(context.Response, 200, result, cancellationToken);
                return true;
            }

            if (path.Equals("/api/checkout", StringComparison.OrdinalIgnoreCase) && isPost)
            {
                await HandleCheckoutAsync(context, device, cancellationToken);
                return true;
            }

            if (path.Equals("/api/returns", StringComparison.OrdinalIgnoreCase) && isPost)
            {
                await HandleReturnAsync(context, device, cancellationToken);
                return true;
            }

            if (path.Equals("/api/products/by-barcode", StringComparison.OrdinalIgnoreCase) && isGet)
            {
                query.TryGetValue("code", out var code);
                if (string.IsNullOrWhiteSpace(code))
                    throw new BusinessRuleException("Parametre code manquant.");
                var product = await _products.GetByBarcodeAsync(code, cancellationToken);
                if (product is null)
                {
                    await HttpUtil.WriteJsonAsync(context.Response, 404, new ErrorResponse("product_not_found"), cancellationToken);
                    return true;
                }

                await HttpUtil.WriteJsonAsync(context.Response, 200, new BarcodeResponseDto
                {
                    Uuid = product.Uuid,
                    Name = product.Name,
                    Barcode = product.Barcode,
                    SellPrice = product.SellPrice,
                    StockQty = product.StockQuantity,
                    TvaRate = product.TvaRate,
                    Category = product.Category,
                    IsWeighed = product.IsWeighed
                }, cancellationToken);
                return true;
            }

            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 4 &&
                segments[0].Equals("api", StringComparison.OrdinalIgnoreCase) &&
                segments[1].Equals("customers", StringComparison.OrdinalIgnoreCase) &&
                segments[3].Equals("statement", StringComparison.OrdinalIgnoreCase) && isGet)
            {
                var statement = await _customers.GetStatementAsync(segments[2], cancellationToken);
                await HttpUtil.WriteJsonAsync(context.Response, 200, MapStatement(statement), cancellationToken);
                return true;
            }

            if (path.Equals("/api/events", StringComparison.OrdinalIgnoreCase) && isGet)
            {
                await ServeEventsAsync(context, cancellationToken);
                return true;
            }

            await HttpUtil.WriteJsonAsync(context.Response, 404, new ErrorResponse("not_found"), cancellationToken);
            return true;
        }
        catch (PayloadTooLargeException)
        {
            await TryWriteErrorAsync(context, 413, "payload_too_large", cancellationToken);
        }
        catch (DomainException ex)
        {
            await TryWriteErrorAsync(context, 400, ex.Message, cancellationToken);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Malformed JSON for {Path}", path);
            await TryWriteErrorAsync(context, 400, "invalid_json", cancellationToken);
        }
        catch (HttpListenerException ex)
        {
            _logger.LogDebug(ex, "Client disconnected during request {Path}", path);
        }
        catch (IOException ex)
        {
            _logger.LogDebug(ex, "Client IO failure during request {Path}", path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled API error for {Path}", path);
            await TryWriteErrorAsync(context, 500, "internal_error", cancellationToken);
        }

        return true;
    }

    private async Task HandlePairAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var ip = context.Request.RemoteEndPoint?.Address.ToString() ?? "unknown";
        if (!_pairLimiter.TryAcquire(ip))
        {
            context.Response.Headers["Retry-After"] = "60";
            await HttpUtil.WriteJsonAsync(context.Response, 429, new ErrorResponse("too_many_attempts"), cancellationToken);
            return;
        }

        var body = await HttpUtil.ReadBodyAsync(context.Request, cancellationToken);
        var request = HttpUtil.Deserialize<PairRequest>(body) ?? new PairRequest();
        if (!await _pairing.ValidateAndConsumeAsync(request.Code, cancellationToken))
        {
            _logger.LogWarning("Rejected pairing attempt from {Ip}", ip);
            await HttpUtil.WriteJsonAsync(context.Response, 401, new ErrorResponse("invalid_code"), cancellationToken);
            return;
        }

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
        var name = HttpUtil.NormalizeDeviceName(context.Request.Headers["X-Device-Name"]);

        await using var connection = await _db.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO devices (Uuid, Name, TokenHash, IpAddress)
            VALUES (@uuid, @name, @tokenHash, @ip)
            """,
            new { uuid = Db.NewUuid(), name, tokenHash, ip = ip.Length > 64 ? ip[..64] : ip },
            cancellationToken: cancellationToken));
        var deviceId = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT LAST_INSERT_ID()", cancellationToken: cancellationToken));

        _logger.LogInformation("Device {Name} paired from {Ip}", name, ip);
        await HttpUtil.WriteJsonAsync(context.Response, 200,
            new PairResponse { DeviceId = deviceId, Token = token }, cancellationToken);
    }

    private async Task HandleCheckoutAsync(HttpListenerContext context, Device device, CancellationToken cancellationToken)
    {
        var body = await HttpUtil.ReadBodyAsync(context.Request, cancellationToken);
        var dto = HttpUtil.Deserialize<CheckoutRequestDto>(body)
                  ?? throw new BusinessRuleException("Corps JSON invalide.");
        var request = new CheckoutRequest
        {
            OpId = RequireOpId(dto.OpId),
            CustomerUuid = dto.CustomerUuid,
            DiscountPct = dto.DiscountPct,
            Cashier = NormalizeActor(dto.Cashier, device.Name),
            Items = (dto.Items ?? []).Select(i => new CheckoutItem
            {
                ProductUuid = i.ProductUuid,
                Qty = i.Qty,
                UnitPrice = i.UnitPrice,
                DiscountPct = i.DiscountPct
            }).ToList(),
            Payments = (dto.Payments ?? []).Select(p => new CheckoutPayment
            {
                Method = p.Method,
                Amount = p.Amount
            }).ToList()
        };

        var result = await _checkout.CheckoutAsync(request,
            new CheckoutContext(request.Cashier ?? device.Name, null), cancellationToken);
        await HttpUtil.WriteJsonAsync(context.Response, 200, new CheckoutResponseDto
        {
            SaleUuid = result.SaleUuid,
            InvoiceNo = result.InvoiceNo,
            Total = result.Total,
            Tva = result.Tva,
            Paid = result.Paid,
            Change = result.Change,
            Due = result.Due,
            LoyaltyEarned = result.LoyaltyEarned
        }, cancellationToken);
    }

    private async Task HandleReturnAsync(HttpListenerContext context, Device device, CancellationToken cancellationToken)
    {
        var body = await HttpUtil.ReadBodyAsync(context.Request, cancellationToken);
        var dto = HttpUtil.Deserialize<ReturnRequestDto>(body)
                  ?? throw new BusinessRuleException("Corps JSON invalide.");
        var request = new ReturnRequest
        {
            OpId = RequireOpId(dto.OpId),
            SaleUuid = InputValidator.RequireUuid(dto.SaleUuid, "Vente"),
            Reason = dto.Reason,
            Cashier = NormalizeActor(dto.Cashier, device.Name),
            Items = (dto.Items ?? []).Select(i => new ReturnLineRequest
            {
                SaleItemUuid = i.SaleItemUuid,
                Qty = i.Qty
            }).ToList()
        };

        var result = await _returns.ReturnAsync(request,
            new ReturnContext(request.Cashier ?? device.Name, null), cancellationToken);
        await HttpUtil.WriteJsonAsync(context.Response, 200, new ReturnResponseDto
        {
            ReturnUuid = result.ReturnUuid,
            RefundTotal = result.RefundTotal
        }, cancellationToken);
    }

    private async Task<Device?> AuthenticateAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var header = context.Request.Headers["Authorization"];
        if (string.IsNullOrWhiteSpace(header))
        {
            await WriteUnauthorizedAsync(context, cancellationToken);
            return null;
        }

        var parts = header.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !parts[0].Equals("Bearer", StringComparison.OrdinalIgnoreCase))
        {
            await WriteUnauthorizedAsync(context, cancellationToken);
            return null;
        }

        var token = parts[1];
        if (token.Length != 64 || !token.All(Uri.IsHexDigit))
        {
            await WriteUnauthorizedAsync(context, cancellationToken);
            return null;
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
        var device = await _db.QueryFirstOrDefaultAsync<Device>(
            "SELECT * FROM devices WHERE TokenHash = @hash AND RevokedAt IS NULL LIMIT 1",
            new { hash }, cancellationToken: cancellationToken);
        if (device is null)
        {
            await WriteUnauthorizedAsync(context, cancellationToken);
            return null;
        }

        if (device.LastSeenAt is null || device.LastSeenAt < DateTime.Now.AddMinutes(-1))
        {
            await _db.ExecuteAsync(
                "UPDATE devices SET LastSeenAt = NOW(3) WHERE Id = @id",
                new { id = device.Id }, cancellationToken: cancellationToken);
        }

        return device;
    }

    private static Task WriteUnauthorizedAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        context.Response.Headers["WWW-Authenticate"] = "Bearer";
        return HttpUtil.WriteJsonAsync(context.Response, 401, new ErrorResponse("unauthorized"), cancellationToken);
    }

    private async Task ServeEventsAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var response = context.Response;
        response.StatusCode = 200;
        response.ContentType = "text/event-stream; charset=utf-8";
        response.SendChunked = true;
        response.Headers["Cache-Control"] = "no-cache";
        response.Headers["X-Accel-Buffering"] = "no";
        response.Headers["X-Content-Type-Options"] = "nosniff";

        var subscription = _sse.Subscribe();
        using var writer = new StreamWriter(response.OutputStream, new UTF8Encoding(false), leaveOpen: true)
        {
            AutoFlush = true
        };
        try
        {
            await writer.WriteAsync(": connected\n\n");
            await writer.FlushAsync(cancellationToken);

            while (!cancellationToken.IsCancellationRequested)
            {
                var readTask = subscription.Reader.WaitToReadAsync(cancellationToken).AsTask();
                var heartbeat = Task.Delay(TimeSpan.FromSeconds(25), cancellationToken);
                var completed = await Task.WhenAny(readTask, heartbeat);
                if (completed == readTask)
                {
                    if (!await readTask) break;
                    while (subscription.Reader.TryRead(out var message))
                        await writer.WriteAsync(message);
                }
                else
                {
                    await writer.WriteAsync(": heartbeat\n\n");
                }

                await writer.FlushAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown / client disconnect.
        }
        catch (Exception ex) when (ex is IOException or HttpListenerException or ObjectDisposedException)
        {
            _logger.LogDebug(ex, "SSE connection closed");
        }
        finally
        {
            _sse.Unsubscribe(subscription.Id);
        }
    }

    private async Task TryWriteErrorAsync(HttpListenerContext context, int statusCode, string message,
        CancellationToken cancellationToken)
    {
        try
        {
            await HttpUtil.WriteJsonAsync(context.Response, statusCode, new ErrorResponse(message), cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or HttpListenerException or ObjectDisposedException or InvalidOperationException)
        {
            _logger.LogDebug(ex, "Unable to send error response");
        }
    }

    private static StatementResponseDto MapStatement(CustomerStatement statement)
    {
        var customer = statement.Customer;
        return new StatementResponseDto
        {
            Customer = new CustomerSyncDto
            {
                Uuid = customer.Uuid,
                FullName = customer.FullName,
                Phone = customer.Phone,
                Address = customer.Address,
                CreditLimit = customer.CreditLimit,
                DueBalance = customer.DueBalance,
                LoyaltyPoints = customer.LoyaltyPoints,
                Notes = customer.Notes,
                UpdatedAt = Db.FormatTimestamp(customer.UpdatedAt),
                DeletedAt = Db.FormatTimestamp(customer.DeletedAt),
                Version = customer.Version
            },
            OpeningBalance = statement.OpeningBalance,
            Entries = statement.Entries.Select(e => new StatementEntryDto
            {
                Date = Db.FormatTimestamp(e.Date),
                Type = e.Type,
                Ref = e.Ref,
                Debit = e.Debit,
                Credit = e.Credit,
                Balance = e.Balance
            }).ToList()
        };
    }

    private static string RequireOpId(string? opId)
    {
        if (string.IsNullOrWhiteSpace(opId) || !Guid.TryParse(opId, out var guid))
            throw new BusinessRuleException("op_id invalide.");
        return guid.ToString("D");
    }

    private static string NormalizeActor(string? requested, string fallback)
    {
        var value = string.IsNullOrWhiteSpace(requested) ? fallback : requested;
        var cleaned = new string((value ?? string.Empty).Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (cleaned.Length == 0) cleaned = fallback;
        return cleaned.Length > 120 ? cleaned[..120] : cleaned;
    }
}
