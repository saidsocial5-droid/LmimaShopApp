using System;
using System.Collections.Generic;
using System.Linq;
using MySqlConnector;

namespace LmimaShopApp
{
    public static class SalesService
    {
        public static (bool Success, string Message, int InvoiceId) ProcessSaleTransaction(
            List<CartItem> cartItems, 
            decimal totalAmount, 
            decimal paidAmount, 
            decimal debtAmount, 
            string customerName, 
            string cashierName)
        {
            using (MySqlConnection conn = new DatabaseHelper().GetConnection())
            {
                conn.Open();
                using (MySqlTransaction transaction = conn.BeginTransaction())
                {
                    try
                    {
                        // 1. Check Customer Debt limits if applicable
                        int customerId = 0;
                        if (debtAmount > 0)
                        {
                            string getCustQuery = "SELECT Id, AllowCredit FROM Customers WHERE FullName = @cname LIMIT 1";
                            using (MySqlCommand getCustCmd = new MySqlCommand(getCustQuery, conn, transaction))
                            {
                                getCustCmd.Parameters.AddWithValue("@cname", customerName);
                                using (MySqlDataReader reader = getCustCmd.ExecuteReader())
                                {
                                    if (reader.Read())
                                    {
                                        customerId = Convert.ToInt32(reader["Id"]);
                                        bool allowCredit = Convert.ToBoolean(reader["AllowCredit"]);
                                        if (!allowCredit)
                                        {
                                            return (false, "هذا العميل غير مسموح له بالدين.", 0);
                                        }
                                    }
                                    else
                                    {
                                        return (false, "يرجى تحديد عميل صالح لتسجيل الدين.", 0);
                                    }
                                }
                            }
                        }

                        // 2. Insert Sale (Invoice)
                        int invoiceId = 0;
                        string insertSaleQuery = @"INSERT INTO Sales (TotalAmount, CashierName, CustomerName, Date, Status) 
                                                   VALUES (@total, @cashier, @customer, NOW(), 'Completed');
                                                   SELECT LAST_INSERT_ID();";
                        using (MySqlCommand saleCmd = new MySqlCommand(insertSaleQuery, conn, transaction))
                        {
                            saleCmd.Parameters.AddWithValue("@total", totalAmount);
                            saleCmd.Parameters.AddWithValue("@cashier", cashierName);
                            saleCmd.Parameters.AddWithValue("@customer", customerName);
                            invoiceId = Convert.ToInt32(saleCmd.ExecuteScalar());
                        }

                        // 3. Insert Details, Deduct Stock, Add Stock Movement
                        string insertDetailQuery = @"INSERT INTO SalesDetails (SaleId, ProductId, ProductName, Quantity, UnitPrice, SubTotal) 
                                                     VALUES (@saleId, @prodId, @prodName, @qty, @price, @subTotal)";
                        string updateStockQuery = @"UPDATE Products SET StockQuantity = StockQuantity - @qty WHERE Id = @prodId AND StockQuantity >= @qty";
                        string insertMovementQuery = @"INSERT INTO StockMovements (ProductId, MovementType, Quantity, ReferenceId, Notes, UserId) 
                                                       VALUES (@prodId, 'Sale', @negativeQty, @refId, 'فاتورة مبيعات', @userId)";

                        foreach (var item in cartItems)
                        {
                            // A. Details
                            using (MySqlCommand detailCmd = new MySqlCommand(insertDetailQuery, conn, transaction))
                            {
                                detailCmd.Parameters.AddWithValue("@saleId", invoiceId);
                                detailCmd.Parameters.AddWithValue("@prodId", item.ProductId);
                                detailCmd.Parameters.AddWithValue("@prodName", item.ProductName);
                                detailCmd.Parameters.AddWithValue("@qty", item.Quantity);
                                detailCmd.Parameters.AddWithValue("@price", item.SellPrice);
                                detailCmd.Parameters.AddWithValue("@subTotal", item.Total);
                                detailCmd.ExecuteNonQuery();
                            }

                            // B. Deduct Stock with Concurrency Check
                            using (MySqlCommand stockCmd = new MySqlCommand(updateStockQuery, conn, transaction))
                            {
                                stockCmd.Parameters.AddWithValue("@qty", item.Quantity);
                                stockCmd.Parameters.AddWithValue("@prodId", item.ProductId);
                                int rowsAffected = stockCmd.ExecuteNonQuery();
                                if (rowsAffected == 0)
                                {
                                    // Negative stock prevention
                                    throw new Exception($"الكمية المطلوبة للمنتج {item.ProductName} غير متوفرة في المخزون بشكل كافٍ.");
                                }
                            }

                            // C. Stock Ledger Movement
                            using (MySqlCommand movCmd = new MySqlCommand(insertMovementQuery, conn, transaction))
                            {
                                movCmd.Parameters.AddWithValue("@prodId", item.ProductId);
                                movCmd.Parameters.AddWithValue("@negativeQty", -item.Quantity);
                                movCmd.Parameters.AddWithValue("@refId", invoiceId.ToString());
                                movCmd.Parameters.AddWithValue("@userId", AppSession.CurrentUserId);
                                movCmd.ExecuteNonQuery();
                            }
                        }

                        // 4. Update Customer Debt if any
                        if (debtAmount > 0 && customerId > 0)
                        {
                            string updateDebtQuery = "UPDATE Customers SET TotalDebt = TotalDebt + @debt WHERE Id = @custId";
                            using (MySqlCommand debtCmd = new MySqlCommand(updateDebtQuery, conn, transaction))
                            {
                                debtCmd.Parameters.AddWithValue("@debt", debtAmount);
                                debtCmd.Parameters.AddWithValue("@custId", customerId);
                                debtCmd.ExecuteNonQuery();
                            }
                        }

                        // 5. Commit Transaction!
                        transaction.Commit();
                        return (true, "تم حفظ الفاتورة بنجاح.", invoiceId);
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        return (false, "فشل حفظ الفاتورة وتم التراجع عن كافة العمليات:\n" + ex.Message, 0);
                    }
                }
            }
        }
    }
}