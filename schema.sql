-- إنشاء قاعدة البيانات إذا لم تكن موجودة
CREATE DATABASE IF NOT EXISTS LmimaShopDB;
USE LmimaShopDB;

-- 1. جدول المستخدمين (لغرض التحقق وتجنب الأخطاء إذا لم يكن موجوداً)
CREATE TABLE IF NOT EXISTS Users (
    UserID INT AUTO_INCREMENT PRIMARY KEY,
    FullName VARCHAR(100) NOT NULL,
    Username VARCHAR(50) NOT NULL UNIQUE,
    Password VARCHAR(255) NOT NULL,
    Role VARCHAR(20) NOT NULL
);

-- 2. جدول المنتجات
DROP TABLE IF EXISTS Products;
CREATE TABLE Products (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    Barcode VARCHAR(50) NOT NULL UNIQUE,
    ProductName VARCHAR(100) NOT NULL,
    Category VARCHAR(50),
    SubCategory VARCHAR(50),
    PurchasePrice DECIMAL(10,2) NOT NULL,
    SellPrice DECIMAL(10,2) NOT NULL,
    StockQuantity DECIMAL(10,3) NOT NULL,
    ImagePath VARCHAR(255)
);

-- 3. جدول المبيعات
CREATE TABLE IF NOT EXISTS Sales (
    InvoiceId INT AUTO_INCREMENT PRIMARY KEY,
    CashierName VARCHAR(100) NOT NULL,
    TotalAmount DECIMAL(10,2) NOT NULL,
    Date DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- 4. جدول تفاصيل المبيعات
CREATE TABLE IF NOT EXISTS SalesDetails (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    InvoiceId INT NOT NULL,
    ProductId INT NOT NULL,
    Quantity DECIMAL(10,3) NOT NULL,
    SubTotal DECIMAL(10,2) NOT NULL,
    FOREIGN KEY (InvoiceId) REFERENCES Sales(InvoiceId) ON DELETE CASCADE,
    FOREIGN KEY (ProductId) REFERENCES Products(Id) ON DELETE CASCADE
);

-- 5. جدول العملاء
CREATE TABLE IF NOT EXISTS Customers (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    FullName VARCHAR(100) NOT NULL UNIQUE,
    Phone VARCHAR(20) DEFAULT '-',
    CreditLimit DECIMAL(10,2) DEFAULT 0.00,
    DueBalance DECIMAL(10,2) DEFAULT 0.00,
    AllowCredit TINYINT(1) DEFAULT 0,
    InvoiceCount INT DEFAULT 0,
    CreditPeriodType VARCHAR(20) DEFAULT 'Open',
    CreditPeriodDays INT DEFAULT 0,
    LastCreditDate DATETIME NULL
);

-- إضافة زبون افتراضي غير محدد
INSERT INTO Customers (Id, FullName, Phone, CreditLimit, DueBalance, AllowCredit, InvoiceCount, CreditPeriodType, CreditPeriodDays)
SELECT 1, 'زبون نقدي عابر', '-', 0.00, 0.00, 0, 0, 'Open', 0
WHERE NOT EXISTS (SELECT 1 FROM Customers WHERE Id = 1);

-- 6. جدول الموردين (الممونين)
CREATE TABLE IF NOT EXISTS Suppliers (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    FullName VARCHAR(100) NOT NULL,
    Phone VARCHAR(50),
    Email VARCHAR(100),
    Address VARCHAR(200),
    Company VARCHAR(100),
    DueBalance DECIMAL(10,2) NOT NULL DEFAULT 0.00
);

-- إضافة مستخدم افتراضي إذا كان جدول المستخدمين فارغاً
INSERT INTO Users (FullName, Username, Password, Role)
SELECT 'مدير النظام', 'admin', '1234', 'Admin'
WHERE NOT EXISTS (SELECT 1 FROM Users);

-- إضافة بعض المنتجات التجريبية لتسهيل الاختبار
INSERT INTO Products (Barcode, ProductName, Category, PurchasePrice, SellPrice, StockQuantity)
VALUES 
('11111111', 'حليب لتر', 'مواد غذائية', 8.00, 10.00, 50),
('22222222', 'خبز توست', 'مخبوزات', 3.50, 5.00, 30),
('33333333', 'عصير برتقال', 'مشروبات', 4.00, 6.00, 40)
ON DUPLICATE KEY UPDATE Barcode=Barcode;
