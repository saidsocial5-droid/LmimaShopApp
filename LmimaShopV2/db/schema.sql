-- ============================================================================
-- LmimaShop V2 - MySQL / MariaDB schema (single source of truth)
-- Database: LmimaShopV2DB
-- Conventions: PascalCase columns map directly to Dapper models.
-- Every business row: Uuid, UpdatedAt (trigger), DeletedAt, Version (trigger).
-- ============================================================================

CREATE TABLE IF NOT EXISTS users (
  Id BIGINT NOT NULL AUTO_INCREMENT,
  Uuid CHAR(36) NOT NULL,
  Username VARCHAR(60) NOT NULL,
  PasswordHash VARCHAR(100) NOT NULL,
  FullName VARCHAR(120) NOT NULL,
  Role VARCHAR(20) NOT NULL DEFAULT 'Cashier',
  IsActive TINYINT(1) NOT NULL DEFAULT 1,
  CreatedAt DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  UpdatedAt DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  DeletedAt DATETIME(3) NULL,
  Version INT NOT NULL DEFAULT 1,
  PRIMARY KEY (Id),
  UNIQUE KEY uq_users_uuid (Uuid),
  UNIQUE KEY uq_users_username (Username),
  KEY ix_users_updated (UpdatedAt)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS categories (
  Id BIGINT NOT NULL AUTO_INCREMENT,
  Uuid CHAR(36) NOT NULL,
  Name VARCHAR(120) NOT NULL,
  CreatedAt DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  UpdatedAt DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  DeletedAt DATETIME(3) NULL,
  Version INT NOT NULL DEFAULT 1,
  PRIMARY KEY (Id),
  UNIQUE KEY uq_categories_uuid (Uuid),
  KEY ix_categories_updated (UpdatedAt)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS products (
  Id BIGINT NOT NULL AUTO_INCREMENT,
  Uuid CHAR(36) NOT NULL,
  Barcode VARCHAR(64) NULL,
  Name VARCHAR(160) NOT NULL,
  CategoryId BIGINT NULL,
  CostPrice DECIMAL(12,2) NOT NULL DEFAULT 0,
  SellPrice DECIMAL(12,2) NOT NULL DEFAULT 0,
  StockQuantity DECIMAL(12,3) NOT NULL DEFAULT 0,
  TvaRate DECIMAL(5,2) NOT NULL DEFAULT 20,
  IsWeighed TINYINT(1) NOT NULL DEFAULT 0,
  ImagePath VARCHAR(500) NULL,
  CreatedAt DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  UpdatedAt DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  DeletedAt DATETIME(3) NULL,
  Version INT NOT NULL DEFAULT 1,
  PRIMARY KEY (Id),
  UNIQUE KEY uq_products_uuid (Uuid),
  KEY ix_products_barcode (Barcode),
  KEY ix_products_category (CategoryId),
  KEY ix_products_updated (UpdatedAt),
  CONSTRAINT fk_products_category FOREIGN KEY (CategoryId) REFERENCES categories (Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS customers (
  Id BIGINT NOT NULL AUTO_INCREMENT,
  Uuid CHAR(36) NOT NULL,
  FullName VARCHAR(160) NOT NULL,
  Phone VARCHAR(40) NULL,
  Address VARCHAR(255) NULL,
  CreditLimit DECIMAL(12,2) NOT NULL DEFAULT 0,
  DueBalance DECIMAL(12,2) NOT NULL DEFAULT 0,
  LoyaltyPoints INT NOT NULL DEFAULT 0,
  Notes VARCHAR(500) NULL,
  CreatedAt DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  UpdatedAt DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  DeletedAt DATETIME(3) NULL,
  Version INT NOT NULL DEFAULT 1,
  PRIMARY KEY (Id),
  UNIQUE KEY uq_customers_uuid (Uuid),
  KEY ix_customers_updated (UpdatedAt)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS suppliers (
  Id BIGINT NOT NULL AUTO_INCREMENT,
  Uuid CHAR(36) NOT NULL,
  Name VARCHAR(160) NOT NULL,
  Phone VARCHAR(40) NULL,
  Address VARCHAR(255) NULL,
  Notes VARCHAR(500) NULL,
  CreatedAt DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  UpdatedAt DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  DeletedAt DATETIME(3) NULL,
  Version INT NOT NULL DEFAULT 1,
  PRIMARY KEY (Id),
  UNIQUE KEY uq_suppliers_uuid (Uuid),
  KEY ix_suppliers_updated (UpdatedAt)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS cash_register_shifts (
  Id BIGINT NOT NULL AUTO_INCREMENT,
  Uuid CHAR(36) NOT NULL,
  UserId BIGINT NOT NULL,
  OpenedAt DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  ClosedAt DATETIME(3) NULL,
  OpeningFloat DECIMAL(12,2) NOT NULL DEFAULT 0,
  CashSales DECIMAL(12,2) NOT NULL DEFAULT 0,
  CardSales DECIMAL(12,2) NOT NULL DEFAULT 0,
  CreditSales DECIMAL(12,2) NOT NULL DEFAULT 0,
  DebtPaymentsCash DECIMAL(12,2) NOT NULL DEFAULT 0,
  ReturnsRefund DECIMAL(12,2) NOT NULL DEFAULT 0,
  ExpectedCash DECIMAL(12,2) NOT NULL DEFAULT 0,
  CountedCash DECIMAL(12,2) NULL,
  Difference DECIMAL(12,2) NULL,
  TvaCollected DECIMAL(12,2) NOT NULL DEFAULT 0,
  LoyaltyGranted INT NOT NULL DEFAULT 0,
  Notes VARCHAR(500) NULL,
  OpenFlag TINYINT GENERATED ALWAYS AS (CASE WHEN ClosedAt IS NULL THEN 1 ELSE NULL END) STORED,
  CreatedAt DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  UpdatedAt DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  DeletedAt DATETIME(3) NULL,
  Version INT NOT NULL DEFAULT 1,
  PRIMARY KEY (Id),
  UNIQUE KEY uq_shifts_uuid (Uuid),
  UNIQUE KEY uq_shifts_open (OpenFlag),
  KEY ix_shifts_closed (ClosedAt),
  KEY ix_shifts_updated (UpdatedAt),
  CONSTRAINT fk_shifts_user FOREIGN KEY (UserId) REFERENCES users (Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS sales (
  Id BIGINT NOT NULL AUTO_INCREMENT,
  Uuid CHAR(36) NOT NULL,
  InvoiceNo VARCHAR(40) NOT NULL,
  CustomerId BIGINT NULL,
  UserId BIGINT NULL,
  ShiftId BIGINT NOT NULL,
  CashierName VARCHAR(120) NOT NULL,
  Subtotal DECIMAL(12,2) NOT NULL DEFAULT 0,
  DiscountPct DECIMAL(5,2) NOT NULL DEFAULT 0,
  DiscountAmount DECIMAL(12,2) NOT NULL DEFAULT 0,
  TvaAmount DECIMAL(12,2) NOT NULL DEFAULT 0,
  Total DECIMAL(12,2) NOT NULL DEFAULT 0,
  PaidAmount DECIMAL(12,2) NOT NULL DEFAULT 0,
  ChangeAmount DECIMAL(12,2) NOT NULL DEFAULT 0,
  DueAmount DECIMAL(12,2) NOT NULL DEFAULT 0,
  LoyaltyEarned INT NOT NULL DEFAULT 0,
  CreatedAt DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  UpdatedAt DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  DeletedAt DATETIME(3) NULL,
  Version INT NOT NULL DEFAULT 1,
  PRIMARY KEY (Id),
  UNIQUE KEY uq_sales_uuid (Uuid),
  UNIQUE KEY uq_sales_invoice (InvoiceNo),
  KEY ix_sales_shift (ShiftId),
  KEY ix_sales_customer (CustomerId),
  KEY ix_sales_created (CreatedAt),
  KEY ix_sales_updated (UpdatedAt),
  CONSTRAINT fk_sales_customer FOREIGN KEY (CustomerId) REFERENCES customers (Id),
  CONSTRAINT fk_sales_user FOREIGN KEY (UserId) REFERENCES users (Id),
  CONSTRAINT fk_sales_shift FOREIGN KEY (ShiftId) REFERENCES cash_register_shifts (Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS sale_items (
  Id BIGINT NOT NULL AUTO_INCREMENT,
  Uuid CHAR(36) NOT NULL,
  SaleId BIGINT NOT NULL,
  ProductId BIGINT NOT NULL,
  ProductName VARCHAR(160) NOT NULL,
  Qty DECIMAL(12,3) NOT NULL,
  UnitPrice DECIMAL(12,2) NOT NULL,
  DiscountPct DECIMAL(5,2) NOT NULL DEFAULT 0,
  LineTotal DECIMAL(12,2) NOT NULL,
  CreatedAt DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  UpdatedAt DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  DeletedAt DATETIME(3) NULL,
  Version INT NOT NULL DEFAULT 1,
  PRIMARY KEY (Id),
  UNIQUE KEY uq_sale_items_uuid (Uuid),
  KEY ix_sale_items_sale (SaleId),
  KEY ix_sale_items_product (ProductId),
  KEY ix_sale_items_updated (UpdatedAt),
  CONSTRAINT fk_sale_items_sale FOREIGN KEY (SaleId) REFERENCES sales (Id),
  CONSTRAINT fk_sale_items_product FOREIGN KEY (ProductId) REFERENCES products (Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS sale_payments (
  Id BIGINT NOT NULL AUTO_INCREMENT,
  SaleId BIGINT NOT NULL,
  Method VARCHAR(20) NOT NULL,
  Amount DECIMAL(12,2) NOT NULL,
  PRIMARY KEY (Id),
  KEY ix_sale_payments_sale (SaleId),
  CONSTRAINT fk_sale_payments_sale FOREIGN KEY (SaleId) REFERENCES sales (Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS customer_payments (
  Id BIGINT NOT NULL AUTO_INCREMENT,
  Uuid CHAR(36) NOT NULL,
  CustomerId BIGINT NOT NULL,
  UserId BIGINT NULL,
  ShiftId BIGINT NULL,
  Amount DECIMAL(12,2) NOT NULL,
  Method VARCHAR(20) NOT NULL DEFAULT 'cash',
  Notes VARCHAR(500) NULL,
  CreatedAt DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  UpdatedAt DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  DeletedAt DATETIME(3) NULL,
  Version INT NOT NULL DEFAULT 1,
  PRIMARY KEY (Id),
  UNIQUE KEY uq_customer_payments_uuid (Uuid),
  KEY ix_customer_payments_customer (CustomerId),
  KEY ix_customer_payments_shift (ShiftId),
  KEY ix_customer_payments_updated (UpdatedAt),
  CONSTRAINT fk_customer_payments_customer FOREIGN KEY (CustomerId) REFERENCES customers (Id),
  CONSTRAINT fk_customer_payments_user FOREIGN KEY (UserId) REFERENCES users (Id),
  CONSTRAINT fk_customer_payments_shift FOREIGN KEY (ShiftId) REFERENCES cash_register_shifts (Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS returns (
  Id BIGINT NOT NULL AUTO_INCREMENT,
  Uuid CHAR(36) NOT NULL,
  SaleId BIGINT NOT NULL,
  ShiftId BIGINT NULL,
  UserId BIGINT NULL,
  CashierName VARCHAR(120) NOT NULL,
  Reason VARCHAR(500) NULL,
  RefundTotal DECIMAL(12,2) NOT NULL DEFAULT 0,
  CashRefund DECIMAL(12,2) NOT NULL DEFAULT 0,
  DueReduction DECIMAL(12,2) NOT NULL DEFAULT 0,
  CreatedAt DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  UpdatedAt DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  DeletedAt DATETIME(3) NULL,
  Version INT NOT NULL DEFAULT 1,
  PRIMARY KEY (Id),
  UNIQUE KEY uq_returns_uuid (Uuid),
  KEY ix_returns_sale (SaleId),
  KEY ix_returns_shift (ShiftId),
  KEY ix_returns_updated (UpdatedAt),
  CONSTRAINT fk_returns_sale FOREIGN KEY (SaleId) REFERENCES sales (Id),
  CONSTRAINT fk_returns_shift FOREIGN KEY (ShiftId) REFERENCES cash_register_shifts (Id),
  CONSTRAINT fk_returns_user FOREIGN KEY (UserId) REFERENCES users (Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS return_items (
  Id BIGINT NOT NULL AUTO_INCREMENT,
  Uuid CHAR(36) NOT NULL,
  ReturnId BIGINT NOT NULL,
  SaleItemId BIGINT NOT NULL,
  ProductId BIGINT NOT NULL,
  Qty DECIMAL(12,3) NOT NULL,
  UnitNetPrice DECIMAL(12,2) NOT NULL,
  LineRefund DECIMAL(12,2) NOT NULL,
  CreatedAt DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  UpdatedAt DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  DeletedAt DATETIME(3) NULL,
  Version INT NOT NULL DEFAULT 1,
  PRIMARY KEY (Id),
  UNIQUE KEY uq_return_items_uuid (Uuid),
  KEY ix_return_items_return (ReturnId),
  KEY ix_return_items_sale_item (SaleItemId),
  CONSTRAINT fk_return_items_return FOREIGN KEY (ReturnId) REFERENCES returns (Id),
  CONSTRAINT fk_return_items_sale_item FOREIGN KEY (SaleItemId) REFERENCES sale_items (Id),
  CONSTRAINT fk_return_items_product FOREIGN KEY (ProductId) REFERENCES products (Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS devices (
  Id BIGINT NOT NULL AUTO_INCREMENT,
  Uuid CHAR(36) NOT NULL,
  Name VARCHAR(80) NOT NULL,
  TokenHash CHAR(64) NOT NULL,
  IpAddress VARCHAR(64) NULL,
  PairedAt DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  LastSeenAt DATETIME(3) NULL,
  RevokedAt DATETIME(3) NULL,
  PRIMARY KEY (Id),
  UNIQUE KEY uq_devices_uuid (Uuid),
  UNIQUE KEY uq_devices_token (TokenHash)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS settings (
  SettingKey VARCHAR(80) NOT NULL,
  SettingValue TEXT NULL,
  UpdatedAt DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
  PRIMARY KEY (SettingKey)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS processed_operations (
  OpId VARCHAR(64) NOT NULL,
  OpType VARCHAR(40) NOT NULL,
  ResultJson LONGTEXT NULL,
  Message VARCHAR(500) NULL,
  CreatedAt DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  CompletedAt DATETIME(3) NULL,
  PRIMARY KEY (OpId),
  KEY ix_processed_type (OpType)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS audit_logs (
  Id BIGINT NOT NULL AUTO_INCREMENT,
  UserId BIGINT NULL,
  Username VARCHAR(120) NULL,
  Action VARCHAR(80) NOT NULL,
  EntityType VARCHAR(60) NOT NULL,
  EntityUuid VARCHAR(64) NULL,
  Details TEXT NULL,
  CreatedAt DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  PRIMARY KEY (Id),
  KEY ix_audit_created (CreatedAt),
  KEY ix_audit_entity (EntityType, EntityUuid)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS counters (
  Name VARCHAR(40) NOT NULL,
  Value BIGINT NOT NULL DEFAULT 0,
  PRIMARY KEY (Name)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ---------------------------------------------------------------------------
-- updated_at / version triggers (single-statement bodies, no DELIMITER needed)
-- ---------------------------------------------------------------------------
DROP TRIGGER IF EXISTS trg_users_bu;
CREATE TRIGGER trg_users_bu BEFORE UPDATE ON users FOR EACH ROW
SET NEW.UpdatedAt = NOW(3), NEW.Version = OLD.Version + 1;

DROP TRIGGER IF EXISTS trg_categories_bu;
CREATE TRIGGER trg_categories_bu BEFORE UPDATE ON categories FOR EACH ROW
SET NEW.UpdatedAt = NOW(3), NEW.Version = OLD.Version + 1;

DROP TRIGGER IF EXISTS trg_products_bu;
CREATE TRIGGER trg_products_bu BEFORE UPDATE ON products FOR EACH ROW
SET NEW.UpdatedAt = NOW(3), NEW.Version = OLD.Version + 1;

DROP TRIGGER IF EXISTS trg_customers_bu;
CREATE TRIGGER trg_customers_bu BEFORE UPDATE ON customers FOR EACH ROW
SET NEW.UpdatedAt = NOW(3), NEW.Version = OLD.Version + 1;

DROP TRIGGER IF EXISTS trg_suppliers_bu;
CREATE TRIGGER trg_suppliers_bu BEFORE UPDATE ON suppliers FOR EACH ROW
SET NEW.UpdatedAt = NOW(3), NEW.Version = OLD.Version + 1;

DROP TRIGGER IF EXISTS trg_shifts_bu;
CREATE TRIGGER trg_shifts_bu BEFORE UPDATE ON cash_register_shifts FOR EACH ROW
SET NEW.UpdatedAt = NOW(3), NEW.Version = OLD.Version + 1;

DROP TRIGGER IF EXISTS trg_sales_bu;
CREATE TRIGGER trg_sales_bu BEFORE UPDATE ON sales FOR EACH ROW
SET NEW.UpdatedAt = NOW(3), NEW.Version = OLD.Version + 1;

DROP TRIGGER IF EXISTS trg_sale_items_bu;
CREATE TRIGGER trg_sale_items_bu BEFORE UPDATE ON sale_items FOR EACH ROW
SET NEW.UpdatedAt = NOW(3), NEW.Version = OLD.Version + 1;

DROP TRIGGER IF EXISTS trg_customer_payments_bu;
CREATE TRIGGER trg_customer_payments_bu BEFORE UPDATE ON customer_payments FOR EACH ROW
SET NEW.UpdatedAt = NOW(3), NEW.Version = OLD.Version + 1;

DROP TRIGGER IF EXISTS trg_returns_bu;
CREATE TRIGGER trg_returns_bu BEFORE UPDATE ON returns FOR EACH ROW
SET NEW.UpdatedAt = NOW(3), NEW.Version = OLD.Version + 1;

DROP TRIGGER IF EXISTS trg_return_items_bu;
CREATE TRIGGER trg_return_items_bu BEFORE UPDATE ON return_items FOR EACH ROW
SET NEW.UpdatedAt = NOW(3), NEW.Version = OLD.Version + 1;

-- ---------------------------------------------------------------------------
-- Seed data
-- ---------------------------------------------------------------------------
INSERT IGNORE INTO users (Uuid, Username, PasswordHash, FullName, Role, IsActive)
VALUES ('00000000-0000-0000-0000-000000000001', 'admin', '$2b$12$fAtVNHJpAMR/iP3jjQvI1e4dDH0.6GLQYZcAc2BSh5eDklYgvexNy', 'Administrateur', 'Admin', 1);

INSERT IGNORE INTO categories (Uuid, Name)
VALUES ('10000000-0000-0000-0000-000000000001', 'General');

INSERT IGNORE INTO settings (SettingKey, SettingValue) VALUES ('tva_rate', '20');
INSERT IGNORE INTO settings (SettingKey, SettingValue) VALUES ('prices_include_tva', 'true');
INSERT IGNORE INTO settings (SettingKey, SettingValue) VALUES ('loyalty_mad_per_point', '10');
INSERT IGNORE INTO settings (SettingKey, SettingValue) VALUES ('shop_name', 'LmimaShop');
INSERT IGNORE INTO settings (SettingKey, SettingValue) VALUES ('shop_ice', '000000000000000');
INSERT IGNORE INTO settings (SettingKey, SettingValue) VALUES ('shop_rc', 'RC 000');
INSERT IGNORE INTO settings (SettingKey, SettingValue) VALUES ('shop_if', 'IF 000');
INSERT IGNORE INTO settings (SettingKey, SettingValue) VALUES ('shop_patente', 'PAT 000');
INSERT IGNORE INTO settings (SettingKey, SettingValue) VALUES ('shop_address', 'Maroc');
INSERT IGNORE INTO settings (SettingKey, SettingValue) VALUES ('shop_phone', '');
INSERT IGNORE INTO settings (SettingKey, SettingValue) VALUES ('receipt_footer', 'Merci de votre visite');
INSERT IGNORE INTO settings (SettingKey, SettingValue) VALUES ('printer_name', '');
INSERT IGNORE INTO settings (SettingKey, SettingValue) VALUES ('receipt_code_page', '864');
INSERT IGNORE INTO settings (SettingKey, SettingValue) VALUES ('theme', 'light');
INSERT IGNORE INTO settings (SettingKey, SettingValue) VALUES ('pairing_code', '');
INSERT IGNORE INTO settings (SettingKey, SettingValue) VALUES ('pairing_code_expires_at', '');
INSERT IGNORE INTO settings (SettingKey, SettingValue) VALUES ('default_admin_notice_shown', 'false');
INSERT IGNORE INTO settings (SettingKey, SettingValue) VALUES ('server_enabled', 'true');
INSERT IGNORE INTO settings (SettingKey, SettingValue) VALUES ('receipt_print_auto', 'false');

INSERT IGNORE INTO counters (Name, Value) VALUES ('invoice', 0);
