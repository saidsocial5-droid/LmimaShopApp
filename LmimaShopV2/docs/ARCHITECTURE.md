# LmimaShop V2 — معمارية النظام

> وثيقة المعمارية الداخلية للنظام. عقد الـ API التفصيلي موجود في
> [`API_SPEC.md`](API_SPEC.md) وهو المرجع النهائي لأشكال الطلبات والاستجابات.

---

## 1. الهدف والنطاق

V2 إعادة كتابة كاملة لنظام الكاشير بهدف:

- مسار واحد موثوق لعمليات البيع (بدون تكرار منطق بين الواجهات).
- دعم الهاتف (PWA/APK) مع مزامنة تفاضلية وعمل مؤقت دون اتصال.
- أمان حقيقي: مصادقة على كل نقاط الـ API، اقتران مؤقت، وحماية الملفات.
- قابلية النشر على جهاز واحد: تطبيق سطح مكتب + خادم مدمج + قاعدة MySQL.

خارج النطاق: تعدد الفروع، المزامنة بين عدة خوادم، الدفع الإلكتروني، و TLS
(موثّق كخطوة مستقبلية؛ الشبكة المحلية فقط حالياً).

---

## 2. طبقات التطبيق

يعتمد النظام على ثلاث طبقات واضحة، واتجاه الاعتماد دائماً من الخارج إلى الداخل:

```text
┌─────────────────────────────────────────────────────────┐
│ Presentation (WPF MVVM)                                 │
│  Views / ViewModels / Commands / Converters             │
│  لا تحتوي قواعد عمل ولا SQL                             │
└───────────────┬─────────────────────────────────────────┘
                │ يستدعي واجهات Core
┌───────────────▼─────────────────────────────────────────┐
│ Core (Domain + Application Services)                    │
│  CheckoutService, SyncService, AuthService,             │
│  PairingService, ReturnService, ReportService,          │
│  Domain Models + Interfaces (Repositories, Clock, ...)  │
└───────────────┬─────────────────────────────────────────┘
                │ تنفّذ الواجهات
┌───────────────▼─────────────────────────────────────────┐
│ Infrastructure                                          │
│  Data: MySQL + Migrations + Repositories                │
│  Server: HttpListener API + SSE + Static File Server    │
│  Security: DPAPI config, BCrypt, token hashing          │
│  Devices: printing, barcode, backup                     │
└─────────────────────────────────────────────────────────┘
```

### 2.1 Presentation — WPF / MVVM

- **Views:** شاشات XAML فقط (بيع، منتجات، عملاء، موردون، تقارير، إعدادات، Z-report).
- **ViewModels:** حالة الشاشة والأوامر، تستدعي خدمات `Core` عبر واجهات.
- **لا منطق عمل في الـ View/ViewModel:** حساب الإجماليات، الخصومات، الولاء،
  والمخزون يتم في `CheckoutService` حصراً.
- **Threading:** العمليات الطويلة (مزامنة، طباعة، تقارير) عبر `async/await` مع
  تفادي تجميد واجهة المستخدم.
- **Composition Root:** `App.xaml.cs` يربط الواجهات بالتنفيذات (DI بسيط يدوي أو
  حاوية خفيفة)، ويبدأ الخادم المدمج بعد نجاح تسجيل الدخول/التهيئة.

### 2.2 Core — الخدمات وقواعد العمل

| الخدمة | المسؤولية |
|---|---|
| `CheckoutService` | **المسار الوحيد** لإنشاء عملية بيع: التحقق من المخزون والائتمان، الدفع، الولاء، التدقيق، وإصدار الحدث. |
| `ReturnService` | المرتجعات: إعادة المخزون وتخفيض رصيد الآجل بما يوازي المبلغ المرتجع. |
| `SyncService` | إنتاج دفعة Delta (`updated_at > since`) وتطبيق عمليات الرفع (push) بترتيب آمن. |
| `AuthService` | تسجيل دخول سطح المكتب (BCrypt) وإدارة جلسة المستخدم للتدقيق. |
| `PairingService` | توليد رمز الاقتران، التحقق منه، إنشاء الجهاز والرمز، وتحديد المحاولات. |
| `ReportService` | تقارير المبيعات، الأرباح، الجرد، وZ-report لكل وردية. |
| `InventoryService` | تعديلات المخزون غير المرتبطة ببيع (جرد/تسويات) مع تدقيق. |

قواعد العمل الأساسية (كما في `API_SPEC.md`):

- خصم المخزون بشكل ذرّي: `UPDATE products SET StockQuantity = StockQuantity - @qty
  WHERE Id = @id AND StockQuantity >= @qty`، وإن كانت النتيجة 0 صفوف تُرفض
  العملية كاملة وتُلغى المعاملة.
- دفع `credit` يزيد `DueBalance` ويخضع لـ `CreditLimit` (0 = غير محدود) داخل
  نفس المعاملة.
- الدفع المختلط مسموح؛ الباقي (`change`) يُحسب من النقد فقط ولا يخفّض الإيراد.
- نقاط الولاء على المبلغ المدفوع فعلاً: `floor(paid / 10)` افتراضياً.
- كل كتابة تُحدّث `updated_at` و`version` عبر Trigger، وتضيف سجلاً في
  `audit_logs`، وتُطلق حدث SSE **بعد** نجاح الـ commit.

### 2.3 Infrastructure — البيانات والخادم والأمان

- **Data:** `MySqlConnector` + استعلامات/Repositories، معاملات صريحة، ومخطط
  مهيّأ عبر `db/schema.sql` أو إنشاء تلقائي عند أول تشغيل.
- **Server:** `HttpListener` مدمج على المنفذ **8090**، يقدّم:
  - نقاط JSON تحت `/api/*`.
  - ملفات PWA الثابتة من `mobile/`.
  - بث SSE على `/api/events`.
  - صور المنتجات من مسار آمن داخل مجلد البيانات.
- **Security:**
  - إعدادات الاتصال المحلية مشفّرة بـ **DPAPI** (Machine/User scope) ولا تُحفظ
    كلمة مرور قاعدة البيانات نصياً.
  - كلمات مرور المستخدمين بـ **BCrypt**.
  - رموز الأجهزة تُحفظ كـ **SHA-256** فقط.
  - مقارنة الرموز وأكواد الاقتران بزمن ثابت.
- **Devices:** طباعة الإيصالات، قارئ الباركود، والنسخ الاحتياطي عبر
  `mysqldump` (انظر `scripts/backup_db.ps1`).

---

## 3. تدفق عملية البيع (مسار واحد)

واجهة سطح المكتب والـ API والمزامنة كلها تنتهي عند نفس الدالة
`CheckoutService.CheckoutAsync`.

```text
[WPF: شاشة البيع]                [PWA/APK]
      │  إضافة أصناف                │  POST /api/checkout
      │  حساب مبدئي للعرض           │  أو op type = sale_create
      └──────────────┬──────────────┘
                     ▼
            CheckoutService (Core)
                     │
        ┌────────────┴─────────────────────────────┐
        │  BEGIN TRANSACTION                       │
        │  1. تحقق idempotency عبر op_id            │
        │  2. لكل سطر: خصم مخزون ذرّي مع شرط الكمية │
        │  3. تحقق حد الائتمان للدفع الآجل          │
        │  4. إدراج Sale + SaleItems                │
        │  5. حساب الإجمالي/TVA/الباقي/الولاء       │
        │  6. تحديث رصيد العميل ونقاط الولاء         │
        │  7. audit_logs + processed_operations     │
        │  COMMIT                                   │
        └────────────┬─────────────────────────────┘
                     │
          Triggers: updated_at + version
                     │
          SSE: data_changed (بعد الـ commit)
```

- إذا فشل أي سطر (كمية غير كافية، حد ائتمان، خطأ قاعدة بيانات) تُلغى المعاملة
  كاملة ولا يُسجّل بيع جزئي.
- `op_id` (UUID) هو مفتاح منع التكرار: إعادة إرسال نفس العملية من الهاتف تُرجع
  `duplicate` بدل بيع مزدوج.
- المرتجع يمر بمسار مشابه في `ReturnService`: إعادة الكمية للمخزون، تخفيض
  `DueBalance` للبيع الآجل بنسبة المبلغ المرتجع، وإضافة سجل تدقيق.

---

## 4. بروتوكول المزامنة التفاضلية (Delta)

### 4.1 الحقول المشتركة

كل صف عمل (Products, Customers, CustomerPayments, Suppliers, Sales, SaleItems,
Returns) يحمل:

| الحقل | النوع | الغرض |
|---|---|---|
| `uuid` | `CHAR(36) UNIQUE` | هوية عالمية ثابتة؛ يولّدها العميل عند الإنشاء لدعم العمل دون اتصال. |
| `updated_at` | `DATETIME(3)` | آخر تعديل؛ يحدّثه Trigger عند كل `UPDATE`. |
| `deleted_at` | `DATETIME(3) NULL` | حذف ناعم (Tombstone). الصفوف المحذوفة تُرسل أيضاً للمزامنة. |
| `version` | `INT DEFAULT 1` | يزيد مع كل تعديل؛ للتشخيص ومنع التعارضات مستقبلاً. |

الأوقات تُنقل بصيغة `yyyy-MM-dd HH:mm:ss.fff` (وقت محلي).

### 4.2 السحب (Pull)

1. العميل يحتفظ بآخر `cursor` استلمه من الخادم.
2. `GET /api/sync?since=<cursor>`.
3. الخادم يعيد فقط الصفوف التي `updated_at > since`، كل صف بصيغة
   `{ "d": <row>, "deleted": <bool> }`.
4. العميل يطبّق التغييرات على نسخته المحلية (Insert/Update أو حذف ناعم محلي)
   ثم يحفظ `cursor` الجديد.
5. `cursor` قيمة معتمة من الخادم (وقت الخادم)؛ لا يجوز للعميل تفسيرها أو
   توليدها بنفسه.

### 4.3 الرفع (Push)

1. العميل يحفظ العمليات محلياً في قائمة انتظار، كل عملية بـ `op_id` (UUID)
   و`type` و`created_at` و`payload`.
2. `POST /api/sync/push` بمجموعة عمليات.
3. الخادم يتحقق من `processed_operations`:
   - موجود مسبقاً ← `duplicate`.
   - جديد ← يُطبّق عبر الخدمة المختصة داخل معاملة، ثم يُسجّل ← `applied`.
   - خطأ عمل (كمية غير كافية مثلاً) ← `error` مع رسالة، ويُترك للعميل القرار.
4. الاستجابة تحتوي نتيجة لكل عملية، وقد تنجح بعض العمليات وتفشل أخرى.
5. أنواع العمليات: `product_save/delete`, `customer_save/delete`,
   `customer_payment_add`, `supplier_save/delete`, `sale_create`,
   `return_create`.

### 4.4 البث الفوري (SSE)

- `GET /api/events` بث نصي `text/event-stream` يتطلب Bearer token.
- عند كل كتابة ناجحة يُرسل الخادم `data: {"type":"data_changed","at":"..."}`.
- تعليق `:` كل 25 ثانية للإبقاء على الاتصال حياً عبر الوسطاء.
- الحدث **إشعار فقط** وليس بيانات: العميل يستجيب بإعادة سحب Delta. هذا يمنع
  فقدان بيانات عند انقطاع البث.

### 4.5 التعارضات

- الخادم هو المصدر النهائي للحقيقة (Server authoritative).
- الهوية `uuid` تمنع التكرار، وسياسة الحسم آخر تعديل يفوز بحسب `updated_at`
  (Last-Write-Wins) دون دمج على مستوى الحقول.
- الحذف لا يمحو الصف فعلياً حتى تصل الـ Tombstone إلى الأجهزة، ثم يجوز تنظيف
  دوري منفصل.

---

## 5. نموذج المصادقة

| السيناريو | الآلية |
|---|---|
| سطح المكتب | تسجيل دخول محلي (username/password)، تحقق BCrypt، جلسة داخلية للتدقيق. لا يحتاج token للوصول إلى خادمه المحلي. |
| الهاتف — الاقتران | Desktop يولّد 6 أرقام عشوائية تشفيرياً، يخزّنها في `settings` مع `pairing_code_expires_at` (120 ثانية). |
| الهاتف — التبادل | `POST /api/pair` بالرمز؛ تحقق بزمن ثابت + صلاحية + حد 5 محاولات/دقيقة/IP. النجاح ينشئ صفاً في `devices` ويعيد `device_id` + `token` (32 بايت عشوائية hex). |
| كل النقاط المحمية | `Authorization: Bearer <token>` فقط؛ الرمز في Query غير مدعوم. الخادم يقارن SHA-256(token) المخزّن. |
| الملفات الثابتة | `/`, `/app.js`, ... عامة لأنها لا تحتوي بيانات. |

إضافات أمنية:

- رمز الاقتران قصير العمر يقلّل أثر التصوير/التسريب.
- الجهاز يمكن إلغاؤه (`revoked_at`) من الإعدادات، فيُرفض رمزه فوراً.
- لا تُسجَّل الرموز أو كلمات المرور في السجلات.

---

## 6. مخطط قاعدة البيانات (LmimaShopV2DB)

> **تسمية الحقول:** حقول العمل مكتوبة PascalCase (`StockQuantity`,
> `DueBalance`, `CreditLimit`, `InvoiceNo`...) كما في مواصفة الـ API، بينما
> حقول المزامنة والتدقيق snake_case (`uuid`, `updated_at`, `deleted_at`,
> `version`). يحوّل الـ API أسماء الحقول إلى snake_case في JSON
> (`stock_qty`, `sell_price`, ...).

### 6.1 الجداول الرئيسية

| الجدول | الحقول الرئيسية |
|---|---|
| `products` | `Id` PK, `uuid` UNIQUE, `Barcode` UNIQUE, `Name`, `Category`, `CostPrice`, `SellPrice`, `StockQuantity`, `TvaRate`, `IsWeighed`, `ImagePath`, `updated_at`, `deleted_at`, `version` |
| `categories` | `Id` PK, `uuid` UNIQUE, `Name`, `updated_at`, `deleted_at`, `version` |
| `customers` | `Id` PK, `uuid` UNIQUE, `FullName`, `Phone`, `Address`, `CreditLimit`, `DueBalance`, `LoyaltyPoints`, `Notes`, `updated_at`, `deleted_at`, `version` |
| `customer_payments` | `Id` PK, `uuid` UNIQUE, `CustomerUuid`/`CustomerId`, `Amount`, `Method`, `Notes`, `CreatedAt`, `updated_at`, `deleted_at`, `version` |
| `suppliers` | `Id` PK, `uuid` UNIQUE, `Name`/`FullName`, `Phone`, `Address`, `CreditLimit`, `DueBalance`, `Notes`, `updated_at`, `deleted_at`, `version` |
| `sales` | `Id` PK, `uuid` UNIQUE, `InvoiceNo` UNIQUE, `Cashier`, `CustomerUuid` NULL, `Subtotal`, `DiscountPct`, `Total`, `Tva`, `Paid`, `ChangeAmount`, `Due`, `LoyaltyEarned`, `ShiftId`, `CreatedAt`, `updated_at`, `deleted_at`, `version` |
| `sale_items` | `Id` PK, `uuid` UNIQUE, `SaleUuid`/`SaleId`, `ProductUuid`/`ProductId`, `Qty`, `UnitPrice`, `DiscountPct`, `LineTotal`, `TvaRate`, `updated_at`, `deleted_at`, `version` |
| `returns` | `Id` PK, `uuid` UNIQUE, `SaleUuid`, `Reason`, `RefundTotal`, `CreatedAt`, `updated_at`, `deleted_at`, `version` |
| `return_items` | `Id` PK, `uuid` UNIQUE, `ReturnUuid`, `SaleItemUuid`, `Qty`, `UnitPrice`, `LineRefund`, `updated_at`, `deleted_at`, `version` |
| `settings` | `SettingKey` PK, `Value` — ومنها `vat_rate`, `loyalty_rate`, `pairing_code`, `pairing_code_expires_at` |
| `devices` | `Id` PK, `Name`, `TokenHash` UNIQUE, `PairedAt`, `LastSeenAt`, `RevokedAt` |
| `users` | `Id` PK, `Username` UNIQUE, `PasswordHash` (BCrypt), `Role`, `IsActive` |
| `audit_logs` | `Id` PK, `UserId`, `Action`, `Entity`, `EntityId`, `Details`, `CreatedAt` |
| `cash_register_shifts` | `Id` PK, `OpenedBy`, `OpenedAt`, `OpeningFloat`, `ClosedAt`, `CountedCash`, `ExpectedCash`, `Difference`, `TvaCollected`, `LoyaltyGranted` |
| `processed_operations` | `OpId` CHAR(36) PK, `Type`, `ProcessedAt`, `Result` |

ملاحظات:

- `categories`, `users`, `return_items` جداول داعمة مستنتجة من متطلبات المزامنة
  وتسجيل الدخول والمرتجعات؛ الحقول الدقيقة تُثبَّت في `db/schema.sql`.
- الفهارس المطلوبة: `UNIQUE(uuid)` لكل جدول عمل، `INDEX(updated_at)` لعملية
  السحب، `UNIQUE(Barcode)` للمنتجات، `UNIQUE(InvoiceNo)` للمبيعات، و
  `UNIQUE(TokenHash)` للأجهزة.
- جميع الجداول `utf8mb4` / `utf8mb4_unicode_ci` لدعم العربية والإيموجي.
- المبالغ `DECIMAL(12,2)`، نسب الضريبة/الخصم `DECIMAL(5,2)`، والكميات
  `DECIMAL(12,3)` لدعم المواد الموزونة.

### 6.2 العلاقات المختصرة

```text
customers 1 ──< sales 1 ──< sale_items >── 1 products
customers 1 ──< customer_payments
suppliers 1 ──< (مشتريات/التزامات — حسب المخطط النهائي)
sales     1 ──< returns 1 ──< return_items
users     1 ──< audit_logs
users     1 ──< cash_register_shifts
```

---

## 7. قرارات التصميم وأسبابها

| القرار | السبب |
|---|---|
| خادم HTTP مدمج داخل تطبيق WPF بدل خدمة منفصلة | نشر أبسط (برنامج واحد)، لا منافذ/خدمات إضافية، ووصول مباشر لنفس طبقة الخدمات. |
| `CheckoutService` واحد للواجهة والـ API والمزامنة | يمنع ازدواج قواعد المخزون والائتمان والولاء، ويضمن نتائج متطابقة وتدقيقاً موحداً. |
| UUID يولّده العميل + مزامنة Delta | يدعم إنشاء البيانات دون اتصال، ويمنع تكرار الصفوف عند إعادة الإرسال. |
| Soft delete (`deleted_at`) بدل الحذف الفعلي | ضروري لإيصال الحذف إلى الأجهزة المتصلة لاحقاً، ويحافظ على سلامة التقارير التاريخية. |
| Triggers لتحديث `updated_at` و`version` | يضمن عدم نسيان التحديث من أي مسار كتابة، ويجعل المزامنة موثوقة. |
| `processed_operations` + `op_id` | Idempotency: منع البيع المزدوج عند إعادة المحاولة بسبب انقطاع الشبكة. |
| SSE بدل WebSocket/SignalR | أبسط، يعمل عبر HTTP، ويكفي كإشعار "تغيّرت البيانات" مع سحب Delta منفصل. |
| Bearer token مخزّن كـ SHA-256 | سرقة قاعدة البيانات لا تكشف رموز الأجهزة الصالحة. |
| رمز اقتران 6 أرقام / 120 ثانية / حد محاولات | يوازن بين سهولة الإدخال والأمان؛ لا سر مشترك دائم. |
| حماية Path Traversal في خادم الملفات | منع قراءة ملفات خارج `mobile/` أو مجلد صور المنتجات. |
| DPAPI لإعدادات الاتصال | لا كلمات مرور نصية على القرص، ولا أسرار في Git. |
| تقديم PWA من نفس المنفذ 8090 | لا حاجة لخادم ثانٍ على الجهاز، ويمنع مشاكل CORS في الإنتاج. |
| نسخ احتياطي خارج المستودع مع الاحتفاظ بـ 14 نسخة | حماية بيانات العملاء من التسرب إلى Git، وحد عملي لمساحة القرص. |
| عدم إرفاق أي بيانات أو نسخ احتياطية في المستودع | التزام صريح بالخصوصية ومنع تسرب PII. |

---

## 8. حدود معروفة / أعمال مستقبلية

- **TLS:** الاتصال الحالي HTTP داخل الشبكة المحلية فقط. الخطوة التالية: شهادة
  محلية موثوقة أو نفق HTTPS.
- **تعدد الأجهزة المتزامنة:** سياسة LWW كافية حالياً؛ إن زادت الحاجة يمكن إضافة
  optimistic concurrency باستخدام `version` في شروط `UPDATE`.
- **تنظيف Tombstones:** يحتاج مهمة صيانة دورية بعد التأكد من مزامنة كل الأجهزة.
- **النسخ الاحتياطي:** سكربت `mysqldump` يدوي/مجدول؛ لا نسخ تلقائي داخل التطبيق
  في هذا الإصدار.
