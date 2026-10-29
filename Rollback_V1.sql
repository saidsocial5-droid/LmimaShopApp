USE LmimaShopDB;

-- Drop Triggers
DROP TRIGGER IF EXISTS tr_products_update;
DROP TRIGGER IF EXISTS tr_customers_update;
DROP TRIGGER IF EXISTS tr_sales_update;
DROP TRIGGER IF EXISTS tr_salesdetails_update;
DROP TRIGGER IF EXISTS tr_customerpayments_update;

-- Drop Indexes
DROP INDEX idx_products_updated ON Products;
DROP INDEX idx_customers_updated ON Customers;
DROP INDEX idx_sales_updated ON Sales;
DROP INDEX idx_salesdetails_updated ON SalesDetails;
DROP INDEX idx_customerpayments_updated ON CustomerPayments;

-- Drop Columns
ALTER TABLE Products DROP COLUMN uuid, DROP COLUMN updated_at, DROP COLUMN deleted_at, DROP COLUMN version;
ALTER TABLE Customers DROP COLUMN uuid, DROP COLUMN updated_at, DROP COLUMN deleted_at, DROP COLUMN version;
ALTER TABLE Sales DROP COLUMN uuid, DROP COLUMN updated_at, DROP COLUMN deleted_at, DROP COLUMN version;
ALTER TABLE SalesDetails DROP COLUMN uuid, DROP COLUMN updated_at, DROP COLUMN deleted_at, DROP COLUMN version;
ALTER TABLE CustomerPayments DROP COLUMN uuid, DROP COLUMN updated_at, DROP COLUMN deleted_at, DROP COLUMN version;

-- Drop new tables
DROP TABLE IF EXISTS inventory_movements;
DROP TABLE IF EXISTS processed_operations;
DROP TABLE IF EXISTS devices;
