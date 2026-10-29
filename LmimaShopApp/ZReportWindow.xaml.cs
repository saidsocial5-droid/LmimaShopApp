using System;
using System.Windows;
using System.Windows.Media;
using MySqlConnector;

namespace LmimaShopApp
{
    public partial class ZReportWindow : Window
    {
        private decimal _expectedCash = 0m;
        private decimal _cashSales = 0m;
        private decimal _creditSales = 0m;
        private int _shiftId = 0;

        public ZReportWindow()
        {
            InitializeComponent();
            LoadShiftData();
        }

        private void LoadShiftData()
        {
            DatabaseHelper db = new DatabaseHelper();
            using (var conn = db.GetConnection())
            {
                conn.Open();
                // Find open shift
                string shiftQuery = "SELECT Id, OpeningBalance, OpenedAt FROM CashRegisterShifts WHERE Status = 'Open' ORDER BY Id DESC LIMIT 1";
                DateTime openedAt = DateTime.Today;
                decimal openingBalance = 0m;
                using (var cmd = new MySqlCommand(shiftQuery, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        _shiftId = Convert.ToInt32(reader["Id"]);
                        openingBalance = Convert.ToDecimal(reader["OpeningBalance"]);
                        openedAt = Convert.ToDateTime(reader["OpenedAt"]);
                    }
                    else
                    {
                        MessageBox.Show("لا يوجد وردية (Shift) مفتوحة حالياً. سيتم حساب المبيعات لليوم الحالي.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }

                // Calculate Cash and Credit Sales since openedAt
                string salesQuery = @"SELECT 
                                        SUM(CASE WHEN IsCredit = 0 THEN TotalAmount ELSE 0 END) as CashSales,
                                        SUM(CASE WHEN IsCredit = 1 THEN TotalAmount ELSE 0 END) as CreditSales
                                      FROM Invoices 
                                      WHERE Date >= @openedAt";
                
                using (var cmd = new MySqlCommand(salesQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@openedAt", openedAt);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            _cashSales = reader["CashSales"] != DBNull.Value ? Convert.ToDecimal(reader["CashSales"]) : 0m;
                            _creditSales = reader["CreditSales"] != DBNull.Value ? Convert.ToDecimal(reader["CreditSales"]) : 0m;
                        }
                    }
                }

                _expectedCash = openingBalance + _cashSales;

                txtCashSales.Text = $"{_cashSales:N2} درهم";
                txtCreditSales.Text = $"{_creditSales:N2} درهم";
                txtExpectedCash.Text = $"{_expectedCash:N2} درهم";
            }
        }

        private void txtActualCash_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (decimal.TryParse(txtActualCash.Text, out decimal actual))
            {
                decimal discrepancy = actual - _expectedCash;
                txtDiscrepancy.Text = $"الفارق: {Math.Abs(discrepancy):N2} درهم";
                if (discrepancy < 0)
                {
                    txtDiscrepancy.Foreground = new SolidColorBrush(Colors.Red);
                    txtDiscrepancy.Text = $"عجز: {Math.Abs(discrepancy):N2} درهم";
                }
                else if (discrepancy > 0)
                {
                    txtDiscrepancy.Foreground = new SolidColorBrush(Colors.Green);
                    txtDiscrepancy.Text = $"زيادة: {discrepancy:N2} درهم";
                }
                else
                {
                    txtDiscrepancy.Foreground = new SolidColorBrush(Colors.Gray);
                    txtDiscrepancy.Text = "مطابق تماماً";
                }
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void BtnCloseRegister_Click(object sender, RoutedEventArgs e)
        {
            if (!decimal.TryParse(txtActualCash.Text, out decimal actual))
            {
                MessageBox.Show("الرجاء إدخال المبلغ الفعلي في الدرج بشكل صحيح.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            decimal discrepancy = actual - _expectedCash;

            DatabaseHelper db = new DatabaseHelper();
            using (var conn = db.GetConnection())
            {
                conn.Open();
                if (_shiftId > 0)
                {
                    string updateQuery = @"UPDATE CashRegisterShifts 
                                           SET ClosedAt = NOW(), ClosedBy = @userId, CashSales = @cash, CreditSales = @credit,
                                               ExpectedCash = @expected, ActualCash = @actual, Discrepancy = @discrepancy, Status = 'Closed'
                                           WHERE Id = @id";
                    using (var cmd = new MySqlCommand(updateQuery, conn))
                    {
                        cmd.Parameters.AddWithValue("@userId", AppSession.CurrentUserId);
                        cmd.Parameters.AddWithValue("@cash", _cashSales);
                        cmd.Parameters.AddWithValue("@credit", _creditSales);
                        cmd.Parameters.AddWithValue("@expected", _expectedCash);
                        cmd.Parameters.AddWithValue("@actual", actual);
                        cmd.Parameters.AddWithValue("@discrepancy", discrepancy);
                        cmd.Parameters.AddWithValue("@id", _shiftId);
                        cmd.ExecuteNonQuery();
                    }
                }
                else
                {
                    // Create a closed shift record
                    string insertQuery = @"INSERT INTO CashRegisterShifts 
                                           (OpenedBy, OpenedAt, ClosedAt, ClosedBy, CashSales, CreditSales, ExpectedCash, ActualCash, Discrepancy, Status)
                                           VALUES (@userId, @date, NOW(), @userId, @cash, @credit, @expected, @actual, @discrepancy, 'Closed')";
                    using (var cmd = new MySqlCommand(insertQuery, conn))
                    {
                        cmd.Parameters.AddWithValue("@userId", AppSession.CurrentUserId);
                        cmd.Parameters.AddWithValue("@date", DateTime.Today);
                        cmd.Parameters.AddWithValue("@cash", _cashSales);
                        cmd.Parameters.AddWithValue("@credit", _creditSales);
                        cmd.Parameters.AddWithValue("@expected", _expectedCash);
                        cmd.Parameters.AddWithValue("@actual", actual);
                        cmd.Parameters.AddWithValue("@discrepancy", discrepancy);
                        cmd.ExecuteNonQuery();
                    }
                }
            }

            MessageBox.Show("تم إغلاق الصندوق بنجاح!", "إغلاق الصندوق", MessageBoxButton.OK, MessageBoxImage.Information);
            this.Close();
        }
    }
}
