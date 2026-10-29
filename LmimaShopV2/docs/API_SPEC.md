# LmimaShop V2 — API Contract (Authoritative)

This document is the single source of truth for the desktop embedded server and the mobile PWA.
Base URL: `http://{host}:8090` (HTTP on LAN; TLS is a documented future step — all endpoints require Bearer auth, which is the fix over V1).

## Conventions

- JSON only. Currency amounts are decimals (MAD). VAT rate default 20% (configurable in `settings` table). Loyalty: 1 point per 10 MAD paid (configurable). Rial conversion (`MAD * 20`) is a display-only helper.
- Every business row (Products, Customers, CustomerPayments, Suppliers, Sales, SaleItems, Returns) carries:
  - `uuid CHAR(36) NOT NULL UNIQUE` (client may generate on create for offline support)
  - `updated_at DATETIME(3) NOT NULL` (bumped by DB trigger on UPDATE)
  - `deleted_at DATETIME(3) NULL` (soft delete; a sync row with `deleted_at` non-null means "delete locally")
  - `version INT NOT NULL DEFAULT 1` (bumped by trigger on UPDATE)
- Timestamps in payloads: ISO-8601 local time string `yyyy-MM-dd HH:mm:ss.fff`.

## Auth model

1. **Desktop**: local login (username/password, BCrypt verify). No token needed for the embedded server on localhost; the desktop session token is the user context for audit logs.
2. **Mobile pairing**:
   - Desktop generates a pairing code: 6 crypto-random digits, stored in `settings` (`pairing_code`, `pairing_code_expires_at`), valid 120 seconds, regenerated on demand. Displayed in Settings with a QR containing `http://{ip}:8090/#pair={code}`.
   - Mobile: `POST /api/pair` with `{ "code": "123456" }`.
   - Server validates code (constant-time compare) + expiry, rate-limited to 5 attempts per minute per IP (in-memory counter, 429 on exceed). On success: creates row in `devices`, returns `device_id` and `token` (32 random bytes, hex). Server stores **SHA-256 hash** of the token only.
   - All other endpoints require `Authorization: Bearer <token>`. Invalid/missing → 401. Token in query string is NOT supported.
3. Static PWA assets (`/`, `/app.js`, …) are public; they contain no data.

## Endpoints

### Public

| Method | Path | Body / Query | Response |
|---|---|---|---|
| GET | `/api/ping` | — | `{ "app":"LmimaShopV2", "version":"2.0.0" }` |
| POST | `/api/pair` | `{ "code":"123456" }` | `{ "device_id":1, "token":"<hex>" }` |

### Authenticated (Bearer)

| Method | Path | Body / Query | Response |
|---|---|---|---|
| GET | `/api/sync` | `?since=<cursor>` (opaque server time string, may be empty) | `{ "cursor":"<server time>", "products":[...], "categories":[...], "customers":[...], "customer_payments":[...], "suppliers":[...], "sales":[...], "sale_items":[...], "returns":[...] }` — only rows with `updated_at > since`, each wrapped `{ "d": <row>, "deleted": bool }` where `deleted=true` if `deleted_at` set. |
| POST | `/api/sync/push` | `{ "ops":[ { "op_id":"<uuid>", "type":"<op type>", "created_at":"...", "payload":{...} } ] }` | `{ "results":[ { "op_id":"...", "status":"applied\|duplicate\|error", "message":"..." } ] }` |
| POST | `/api/checkout` | `{ "op_id":"<uuid>", "cashier":"...", "customer_uuid":null\|"...", "items":[{"product_uuid":"...","qty":1.0,"unit_price":25.0,"discount_pct":0}], "payments":[{"method":"cash\|card\|credit","amount":100.0}], "discount_pct":0 }` | `{ "sale_uuid":"...", "invoice_no":"...", "total":..., "tva":..., "paid":..., "change":..., "due":..., "loyalty_earned":... }` |
| POST | `/api/returns` | `{ "op_id":"<uuid>", "sale_uuid":"...", "items":[{"sale_item_uuid":"...","qty":1.0}], "reason":"..." }` | `{ "return_uuid":"...", "refund_total":... }` |
| GET | `/api/products/by-barcode` | `?code=<barcode>` | `{ "uuid","name","barcode","sell_price","stock_qty","tva_rate","category","is_weighed" }` or 404 |
| GET | `/api/customers/{uuid}/statement` | — | `{ "customer":{...}, "opening_balance":..., "entries":[{"date","type":"sale\|payment\|return","ref","debit","credit","balance"}] }` |
| GET | `/api/events` | SSE stream | `data: {"type":"data_changed","at":"<time>"}` on every committed write. Heartbeat comment every 25 s. |

### Push op types (payload)

- `product_save`: full product row minus server fields (`{uuid, barcode, name, category, cost_price, sell_price, stock_qty, tva_rate, is_weighed, image_path?}`)
- `product_delete`: `{uuid}`
- `customer_save`: `{uuid, full_name, phone?, address?, credit_limit, notes?}`
- `customer_delete`: `{uuid}`
- `customer_payment_add`: `{uuid, customer_uuid, amount, method, notes?}`
- `supplier_save` / `supplier_delete`: same shape as customer
- `sale_create`: same shape as `/api/checkout` body (server applies the same single `CheckoutService` logic; stock guard `StockQuantity >= qty`, credit limit check for `credit` payments, idempotency on `op_id`)
- `return_create`: same shape as `/api/returns`

## Business rules (single implementation — `CheckoutService` used by BOTH UI and API)

- Stock deduction uses `UPDATE ... SET StockQuantity = StockQuantity - @qty WHERE Id=@id AND StockQuantity >= @qty`; 0 rows affected → reject whole sale (transaction rollback).
- `credit` payments count toward customer `DueBalance += due`, subject to `CreditLimit` (0 = unlimited) — checked in the same transaction.
- Payment methods may be mixed in one sale; `change = max(0, paid_cash - total_due_after_discount)` given on cash only; change never reduces recorded revenue.
- Loyalty points accrue on the actually-paid amount (`floor(paid / 10)` by default), added to `Customers.LoyaltyPoints`.
- Every write bumps `updated_at` + `version` via DB triggers, appends `audit_logs`, and emits SSE `data_changed` **after** commit.
- Returns restock inventory and reduce `DueBalance` for credit sales proportionally to the refunded amount.

## Z-report (desktop only, no API)

Per shift (`cash_register_shifts`): opening float, cash sales, card sales, credit sales, returns refund, expected cash, counted cash, difference, TVA collected, loyalty granted. Opening/closing a shift is required before selling (POS enforces).
