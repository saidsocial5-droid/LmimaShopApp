-- V1_DeltaSync.sql (Idempotent Migration)

-- 1. Create new tables
CREATE TABLE IF NOT EXISTS inventory_movements (
    id INT AUTO_INCREMENT PRIMARY KEY,
    uuid CHAR(36) UNIQUE NOT NULL,
    product_uuid CHAR(36) NOT NULL,
    qty_delta DECIMAL(10,3) NOT NULL,
    reason VARCHAR(50) NOT NULL,
    source_device_id VARCHAR(50),
    created_at DATETIME(3) NOT NULL
);

CREATE TABLE IF NOT EXISTS processed_operations (
    op_id CHAR(36) PRIMARY KEY,
    device_id VARCHAR(50) NOT NULL,
    processed_at DATETIME(3) NOT NULL,
    result VARCHAR(50) NOT NULL
);

CREATE TABLE IF NOT EXISTS devices (
    device_id VARCHAR(50) PRIMARY KEY,
    name VARCHAR(100) NOT NULL,
    token_hash VARCHAR(255) NOT NULL,
    last_seen DATETIME(3),
    revoked TINYINT(1) DEFAULT 0
);

-- 2. Add columns to Products
SET @dbname = DATABASE();
SET @tablename = 'Products';
SET @columnname = 'uuid';
SET @preparedStatement = (SELECT IF(
  (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE table_name = @tablename AND table_schema = @dbname AND column_name = @columnname) > 0,
  'SELECT 1',
  'ALTER TABLE Products ADD COLUMN uuid CHAR(36) UNIQUE, ADD COLUMN updated_at DATETIME(3) DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3), ADD COLUMN deleted_at DATETIME(3) NULL, ADD COLUMN version INT DEFAULT 1;'
));
PREPARE alterIfNotExists FROM @preparedStatement;
EXECUTE alterIfNotExists;
DEALLOCATE PREPARE alterIfNotExists;

-- Update existing Products
UPDATE Products SET uuid = UUID() WHERE uuid IS NULL;
ALTER TABLE Products MODIFY COLUMN uuid CHAR(36) NOT NULL;

-- 3. Add columns to Customers
SET @tablename = 'Customers';
SET @columnname = 'uuid';
SET @preparedStatement = (SELECT IF(
  (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE table_name = @tablename AND table_schema = @dbname AND column_name = @columnname) > 0,
  'SELECT 1',
  'ALTER TABLE Customers ADD COLUMN uuid CHAR(36) UNIQUE, ADD COLUMN updated_at DATETIME(3) DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3), ADD COLUMN deleted_at DATETIME(3) NULL, ADD COLUMN version INT DEFAULT 1;'
));
PREPARE alterIfNotExists FROM @preparedStatement;
EXECUTE alterIfNotExists;
DEALLOCATE PREPARE alterIfNotExists;

UPDATE Customers SET uuid = UUID() WHERE uuid IS NULL;
ALTER TABLE Customers MODIFY COLUMN uuid CHAR(36) NOT NULL;

-- 4. Add columns to Sales (Invoices)
SET @tablename = 'Sales';
SET @columnname = 'uuid';
SET @preparedStatement = (SELECT IF(
  (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE table_name = @tablename AND table_schema = @dbname AND column_name = @columnname) > 0,
  'SELECT 1',
  'ALTER TABLE Sales ADD COLUMN uuid CHAR(36) UNIQUE, ADD COLUMN updated_at DATETIME(3) DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3), ADD COLUMN deleted_at DATETIME(3) NULL, ADD COLUMN version INT DEFAULT 1;'
));
PREPARE alterIfNotExists FROM @preparedStatement;
EXECUTE alterIfNotExists;
DEALLOCATE PREPARE alterIfNotExists;

UPDATE Sales SET uuid = UUID() WHERE uuid IS NULL;
ALTER TABLE Sales MODIFY COLUMN uuid CHAR(36) NOT NULL;

-- 5. Add columns to SalesDetails
SET @tablename = 'SalesDetails';
SET @columnname = 'uuid';
SET @preparedStatement = (SELECT IF(
  (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE table_name = @tablename AND table_schema = @dbname AND column_name = @columnname) > 0,
  'SELECT 1',
  'ALTER TABLE SalesDetails ADD COLUMN uuid CHAR(36) UNIQUE, ADD COLUMN updated_at DATETIME(3) DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3), ADD COLUMN deleted_at DATETIME(3) NULL, ADD COLUMN version INT DEFAULT 1;'
));
PREPARE alterIfNotExists FROM @preparedStatement;
EXECUTE alterIfNotExists;
DEALLOCATE PREPARE alterIfNotExists;

UPDATE SalesDetails SET uuid = UUID() WHERE uuid IS NULL;
ALTER TABLE SalesDetails MODIFY COLUMN uuid CHAR(36) NOT NULL;

-- 6. Add columns to CustomerPayments
SET @tablename = 'CustomerPayments';
SET @columnname = 'uuid';
SET @preparedStatement = (SELECT IF(
  (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE table_name = @tablename AND table_schema = @dbname AND column_name = @columnname) > 0,
  'SELECT 1',
  'ALTER TABLE CustomerPayments ADD COLUMN uuid CHAR(36) UNIQUE, ADD COLUMN updated_at DATETIME(3) DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3), ADD COLUMN deleted_at DATETIME(3) NULL, ADD COLUMN version INT DEFAULT 1;'
));
PREPARE alterIfNotExists FROM @preparedStatement;
EXECUTE alterIfNotExists;
DEALLOCATE PREPARE alterIfNotExists;

UPDATE CustomerPayments SET uuid = UUID() WHERE uuid IS NULL;
ALTER TABLE CustomerPayments MODIFY COLUMN uuid CHAR(36) NOT NULL;

-- Create Triggers to auto-increment version (MySQL doesn't support DEFAULT 1 ON UPDATE version + 1 easily for INT)
DROP TRIGGER IF EXISTS tr_products_update;
CREATE TRIGGER tr_products_update BEFORE UPDATE ON Products
FOR EACH ROW SET NEW.version = OLD.version + 1, NEW.updated_at = UTC_TIMESTAMP(3);

DROP TRIGGER IF EXISTS tr_customers_update;
CREATE TRIGGER tr_customers_update BEFORE UPDATE ON Customers
FOR EACH ROW SET NEW.version = OLD.version + 1, NEW.updated_at = UTC_TIMESTAMP(3);

DROP TRIGGER IF EXISTS tr_sales_update;
CREATE TRIGGER tr_sales_update BEFORE UPDATE ON Sales
FOR EACH ROW SET NEW.version = OLD.version + 1, NEW.updated_at = UTC_TIMESTAMP(3);

DROP TRIGGER IF EXISTS tr_salesdetails_update;
CREATE TRIGGER tr_salesdetails_update BEFORE UPDATE ON SalesDetails
FOR EACH ROW SET NEW.version = OLD.version + 1, NEW.updated_at = UTC_TIMESTAMP(3);

DROP TRIGGER IF EXISTS tr_customerpayments_update;
CREATE TRIGGER tr_customerpayments_update BEFORE UPDATE ON CustomerPayments
FOR EACH ROW SET NEW.version = OLD.version + 1, NEW.updated_at = UTC_TIMESTAMP(3);

-- Add Indexes for faster sync querying
CREATE INDEX idx_products_updated ON Products(updated_at);
CREATE INDEX idx_customers_updated ON Customers(updated_at);
CREATE INDEX idx_sales_updated ON Sales(updated_at);
CREATE INDEX idx_salesdetails_updated ON SalesDetails(updated_at);
CREATE INDEX idx_customerpayments_updated ON CustomerPayments(updated_at);

