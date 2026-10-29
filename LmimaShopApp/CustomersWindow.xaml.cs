using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Diagnostics;
using MySqlConnector;

namespace LmimaShopApp
{
    public partial class CustomersWindow : UserControl
    {
        private readonly DatabaseHelper dbHelper = new DatabaseHelper();
        private List<CustomerModel> allCustomers = new List<CustomerModel>();
        private bool isEditMode = false;
        private int editingCustomerId = 0;
        private int debtCustomerId = 0;
        private decimal debtCustomerMax = 0;
        private CustomerModel? selectedCustomerForDetails;
        private List<CustomerPurchaseModel> currentPurchasesList = new List<CustomerPurchaseModel>();
        private List<CustomerPaymentRecordModel> currentPaymentsList = new List<CustomerPaymentRecordModel>();
        private DateTime? detailsFromDate = null;
        private DateTime? detailsToDate = null;
        private decimal currentDetailsPurchasesTotal = 0;
        private decimal currentDetailsPaymentsTotal = 0;
        private decimal currentDetailsDueBalance = 0;

        public CustomersWindow()
        {
            InitializeComponent();
            
            // MVVM Transition
            try
            {
                var vm = AppHost.GetService<LmimaShopApp.Presentation.ViewModels.CustomersViewModel>();
                this.DataContext = vm;
            }
            catch { /* Ignore */ }

            LoadCustomers();
            KeyboardLayoutHelper.BindToTextBox(txtSearch);
            KeyboardLayoutHelper.BindToTextBox(txtCustomerName);

            txtCustomerName.GotFocus += (s, e) => activeCustomerModalTextBox = txtCustomerName;
            txtPhone.GotFocus += (s, e) => activeCustomerModalTextBox = txtPhone;
            txtCreditLimit.GotFocus += (s, e) => activeCustomerModalTextBox = txtCreditLimit;
            if (txtCreditPeriodDays != null)
            {
                txtCreditPeriodDays.GotFocus += (s, e) => activeCustomerModalTextBox = txtCreditPeriodDays;
            }
        }

        private bool HasColumn(MySqlDataReader reader, string columnName)
        {
            for (int i = 0; i < reader.FieldCount; i++)
            {
                if (reader.GetName(i).Equals(columnName, StringComparison.InvariantCultureIgnoreCase))
                    return true;
            }
            return false;
        }

        private void LoadCustomers()
        {
            try
            {
                allCustomers.Clear();
                using (MySqlConnection conn = dbHelper.GetConnection())
                {
                    conn.Open();
                    string query = "SELECT Id, FullName, Phone, CreditLimit, DueBalance, AllowCredit, InvoiceCount, CreditPeriodType, CreditPeriodDays, LastCreditDate FROM Customers WHERE deleted_at IS NULL ORDER BY Id DESC";
                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    {
                        using (MySqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                allCustomers.Add(new CustomerModel
                                {
                                    Id = Convert.ToInt32(reader["Id"]),
                                    FullName = reader["FullName"].ToString() ?? "",
                                    Phone = reader["Phone"].ToString() ?? "",
                                    CreditLimit = Convert.ToDecimal(reader["CreditLimit"]),
                                    DueBalance = Convert.ToDecimal(reader["DueBalance"]),
                                    AllowCredit = Convert.ToBoolean(reader["AllowCredit"]),
                                    InvoiceCount = Convert.ToInt32(reader["InvoiceCount"]),
                                    CreditPeriodType = reader["CreditPeriodType"]?.ToString() ?? "Open",
                                    CreditPeriodDays = reader["CreditPeriodDays"] != DBNull.Value ? Convert.ToInt32(reader["CreditPeriodDays"]) : 0,
                                    LastCreditDate = reader["LastCreditDate"] != DBNull.Value ? Convert.ToDateTime(reader["LastCreditDate"]) : (DateTime?)null,
                                    ICE = HasColumn(reader, "ICE") ? (reader["ICE"]?.ToString() ?? "") : ""
                                });
                            }
                        }
                    }
                }

                ApplyFilter();
            }
            catch (Exception ex)
            {
                MessageBox.Show("فشل تحميل الزبناء: " + ex.Message, "خطأ قاعدة بيانات", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplyFilter()
        {
            if (allCustomers == null || txtSearch == null || cmbStatus == null || cmbBalance == null || dgCustomers == null) return;

            string searchText = txtSearch.Text.Trim();
            var filtered = allCustomers.AsEnumerable();

            // 1. فلترة السماح بالأجل
            if (cmbStatus.SelectedItem is ComboBoxItem statusItem)
            {
                string status = statusItem.Content.ToString() ?? "";
                if (status == "يسمح بالأجل")
                {
                    filtered = filtered.Where(c => c.AllowCredit);
                }
                else if (status == "لا يسمح بالأجل")
                {
                    filtered = filtered.Where(c => !c.AllowCredit);
                }
            }

            // 2. فلترة الرصيد
            if (cmbBalance.SelectedItem is ComboBoxItem balanceItem)
            {
                string bal = balanceItem.Content.ToString() ?? "";
                if (bal == "عليها ديون")
                {
                    filtered = filtered.Where(c => c.DueBalance > 0);
                }
                else if (bal == "بلا ديون")
                {
                    filtered = filtered.Where(c => c.DueBalance == 0);
                }
            }

            // 3. فلترة نص البحث
            if (!string.IsNullOrEmpty(searchText))
            {
                filtered = filtered.Where(c =>
                    c.FullName.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                    c.Phone.Contains(searchText, StringComparison.OrdinalIgnoreCase)
                );
            }

            var filteredList = filtered.ToList();

            var vm = this.DataContext as LmimaShopApp.Presentation.ViewModels.CustomersViewModel;
            if (vm != null)
            {
                vm.Customers.Clear();
                foreach (var c in filteredList)
                {
                    vm.Customers.Add(new LmimaShopApp.Core.Domain.Models.Customer 
                    {
                        Id = c.Id,
                        FullName = c.FullName,
                        Phone = c.Phone,
                        CreditLimit = c.CreditLimit,
                        DueBalance = c.DueBalance,
                        AllowCredit = c.AllowCredit,
                        InvoiceCount = c.InvoiceCount,
                        CreditPeriodType = c.CreditPeriodType,
                        CreditPeriodDays = c.CreditPeriodDays
                    });
                }
            }
            else
            {
                dgCustomers.ItemsSource = null;
                dgCustomers.ItemsSource = filteredList;
            }

            // تحديث المجموع العام لديون الزبناء
            decimal totalAll = allCustomers.Sum(c => c.DueBalance);
            decimal totalFiltered = filteredList.Sum(c => c.DueBalance);

            if (lblTotalDebts != null)
            {
                if (filteredList.Count == allCustomers.Count)
                {
                    lblTotalDebts.Text = $"{totalAll:N2} درهم";
                }
                else
                {
                    lblTotalDebts.Text = $"{totalAll:N2} درهم (المفلتر: {totalFiltered:N2} درهم)";
                }
            }
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void Filter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void ChkAllowCredit_Changed(object sender, RoutedEventArgs e)
        {
            if (panelCreditPeriod != null && chkAllowCredit != null)
            {
                panelCreditPeriod.Visibility = (chkAllowCredit.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void CmbCreditPeriodType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (panelCreditDays != null && cmbCreditPeriodType != null)
            {
                bool isLimited = (cmbCreditPeriodType.SelectedItem is ComboBoxItem item && item.Tag?.ToString() == "Limited");
                panelCreditDays.Visibility = isLimited ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void BtnSyncGoogleSheet_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                CustomerGoogleSheetService.SyncCustomersSheet(force: true, showMessage: true, out string primaryPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show("حدث خطأ أثناء مزامنة ملف الزبناء مع Google Sheets: " + ex.Message, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnAddCustomer_Click(object sender, RoutedEventArgs e)
        {
            isEditMode = false;
            editingCustomerId = 0;
            lblModalTitle.Text = "إضافة زبون جديد";
            
            txtCustomerName.Clear();
            txtPhone.Text = "-";
            txtCustomerICE.Clear();
            txtCreditLimit.Text = "0.00";
            chkAllowCredit.IsChecked = false;

            if (cmbCreditPeriodType != null && cmbCreditPeriodType.Items.Count > 0)
            {
                cmbCreditPeriodType.SelectedIndex = 0; // Open
            }
            if (txtCreditPeriodDays != null)
            {
                txtCreditPeriodDays.Text = "30";
            }

            modalOverlay.Visibility = Visibility.Visible;
            activeCustomerModalTextBox = txtCustomerName;
            txtCustomerName.Focus();
        }

        private void BtnEditCustomer_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext != null)
            {
                dynamic customer = btn.DataContext;
                int id = customer.Id;
                string fullName = customer.FullName;
                string phone = customer.Phone ?? "";
                decimal dueBalance = customer.DueBalance;
                isEditMode = true;
                editingCustomerId = id;
                lblModalTitle.Text = "تعديل بيانات الزبون";

                txtCustomerName.Text = fullName;
                txtPhone.Text = phone;
                txtCustomerICE.Text = customer.ICE;
                txtCreditLimit.Text = customer.CreditLimit.ToString("0.00");
                chkAllowCredit.IsChecked = customer.AllowCredit;

                // Load Credit Period Type
                string type = customer.CreditPeriodType ?? "Open";
                foreach (ComboBoxItem item in cmbCreditPeriodType.Items)
                {
                    if (item.Tag?.ToString() == type)
                    {
                        cmbCreditPeriodType.SelectedItem = item;
                        break;
                    }
                }

                txtCreditPeriodDays.Text = customer.CreditPeriodDays.ToString();

                // Trigger panel visibility manually
                if (panelCreditPeriod != null)
                {
                    panelCreditPeriod.Visibility = customer.AllowCredit ? Visibility.Visible : Visibility.Collapsed;
                }
                if (panelCreditDays != null)
                {
                    panelCreditDays.Visibility = (customer.AllowCredit && type == "Limited") ? Visibility.Visible : Visibility.Collapsed;
                }

                modalOverlay.Visibility = Visibility.Visible;
                activeCustomerModalTextBox = txtCustomerName;
                txtCustomerName.Focus();
                txtCustomerName.SelectAll();
            }
        }

        private void BtnDeleteCustomer_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext != null)
            {
                dynamic customer = btn.DataContext;
                int id = customer.Id;
                string fullName = customer.FullName;
                string phone = customer.Phone ?? "";
                decimal dueBalance = customer.DueBalance;
                if (id == 1) return; // الحماية المزدوجة

                MessageBoxResult result = MessageBox.Show($"هل تود حذف الزبون '{fullName}' نهائياً؟", "تأكيد الحذف", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes) return;

                using (MySqlConnection conn = dbHelper.GetConnection())
                {
                    try
                    {
                        conn.Open();
                        string query = "DELETE FROM Customers WHERE Id = @id";
                        using (MySqlCommand cmd = new MySqlCommand(query, conn))
                        {
                            cmd.Parameters.AddWithValue("@id", id);
                            cmd.ExecuteNonQuery();
                        }

                        MessageBox.Show("تم حذف الزبون بنجاح!", "تم العملية", MessageBoxButton.OK, MessageBoxImage.Information);
                        LoadCustomers();
                        _ = CustomerGoogleSheetService.SyncCustomersSheetAsync(true);
                        _ = CloudSyncService.SyncMobileDataAsync(true);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("فشل حذف الزبون: " + ex.Message, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void BtnDetailsCustomer_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext != null)
            {
                dynamic customer = btn.DataContext;
                int id = customer.Id;
                string fullName = customer.FullName;
                string phone = customer.Phone ?? "";
                decimal dueBalance = customer.DueBalance;
                selectedCustomerForDetails = new CustomerModel { Id = id, FullName = fullName, Phone = phone, DueBalance = dueBalance };
                lblDetailsTitle.Text = $"قائمة المشتريات وكشف حساب الزبون: {fullName}";
                lblDetailsPhone.Text = !string.IsNullOrEmpty(phone) ? $"📱 {phone}" : "";

                detailsFromDate = null;
                detailsToDate = null;
                if (dpDetailsFrom != null) dpDetailsFrom.SelectedDate = null;
                if (dpDetailsTo != null) dpDetailsTo.SelectedDate = null;
                ResetFilterButtonStyles(btnFilterAll);
                if (rbShowPurchases != null) rbShowPurchases.IsChecked = true;

                LoadCustomerPurchases(fullName);
                detailsOverlay.Visibility = Visibility.Visible;
            }
        }

        private void LoadCustomerPurchases(string customerName, DateTime? fromDate = null, DateTime? toDate = null)
        {
            currentPurchasesList.Clear();
            currentPaymentsList.Clear();
            currentDetailsPurchasesTotal = 0;
            currentDetailsPaymentsTotal = 0;
            currentDetailsDueBalance = 0;

            DateTime? queryFrom = fromDate.HasValue ? fromDate.Value.Date : null;
            DateTime? queryTo = toDate.HasValue ? toDate.Value.Date.AddDays(1).AddTicks(-1) : null;

            using (MySqlConnection conn = dbHelper.GetConnection())
            {
                try
                {
                    conn.Open();

                    // 1. Fetch current due balance (مجموع الكريدي فقط الصافي)
                    string custQuery = "SELECT Id, DueBalance, Phone FROM Customers WHERE FullName = @name LIMIT 1";
                    int custId = selectedCustomerForDetails?.Id ?? 0;
                    using (MySqlCommand custCmd = new MySqlCommand(custQuery, conn))
                    {
                        custCmd.Parameters.AddWithValue("@name", customerName);
                        using (var r = custCmd.ExecuteReader())
                        {
                            if (r.Read())
                            {
                                custId = Convert.ToInt32(r["Id"]);
                                currentDetailsDueBalance = r["DueBalance"] != DBNull.Value ? Convert.ToDecimal(r["DueBalance"]) : 0;
                            }
                        }
                    }

                    // 2. Fetch purchases in date range
                    string query = @"
                        SELECT 
                            sd.Id AS DetailId,
                            sd.InvoiceId,
                            sd.ProductId,
                            p.Barcode,
                            COALESCE(sd.CustomName, p.ProductName) AS ProductName,
                            sd.Quantity,
                            CASE WHEN sd.Quantity = 0 THEN 0 ELSE sd.SubTotal / sd.Quantity END AS SellPrice,
                            sd.SubTotal,
                            s.Date AS PurchaseDate
                        FROM SalesDetails sd
                        JOIN Sales s ON sd.InvoiceId = s.InvoiceId
                        JOIN Products p ON sd.ProductId = p.Id
                        WHERE s.CashierName LIKE @pattern ";

                    if (queryFrom.HasValue) query += " AND s.Date >= @from ";
                    if (queryTo.HasValue) query += " AND s.Date <= @to ";
                    query += " ORDER BY s.Date DESC;";

                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@pattern", "%(" + customerName + ")");
                        if (queryFrom.HasValue) cmd.Parameters.AddWithValue("@from", queryFrom.Value);
                        if (queryTo.HasValue) cmd.Parameters.AddWithValue("@to", queryTo.Value);

                        using (MySqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var item = new CustomerPurchaseModel
                                {
                                    DetailId = Convert.ToInt32(reader["DetailId"]),
                                    InvoiceId = Convert.ToInt32(reader["InvoiceId"]),
                                    ProductId = Convert.ToInt32(reader["ProductId"]),
                                    Barcode = reader["Barcode"]?.ToString() ?? "",
                                    ProductName = reader["ProductName"]?.ToString() ?? "",
                                    Quantity = Convert.ToDecimal(reader["Quantity"]),
                                    SellPrice = Convert.ToDecimal(reader["SellPrice"]),
                                    SubTotal = Convert.ToDecimal(reader["SubTotal"]),
                                    PurchaseDate = Convert.ToDateTime(reader["PurchaseDate"])
                                };
                                currentPurchasesList.Add(item);
                                currentDetailsPurchasesTotal += item.SubTotal;
                            }
                        }
                    }

                    // 3. Fetch customer payments in date range
                    string payQuery = @"
                        SELECT Id, Amount, PaymentDate, COALESCE(Note, 'تسديد نقدي للكريدي') AS Note, COALESCE(CashierName, 'المسؤول') AS CashierName
                        FROM CustomerPayments
                        WHERE (CustomerId = @cid OR CustomerName = @cname) ";

                    if (queryFrom.HasValue) payQuery += " AND PaymentDate >= @from ";
                    if (queryTo.HasValue) payQuery += " AND PaymentDate <= @to ";
                    payQuery += " ORDER BY PaymentDate DESC;";

                    using (MySqlCommand payCmd = new MySqlCommand(payQuery, conn))
                    {
                        payCmd.Parameters.AddWithValue("@cid", custId);
                        payCmd.Parameters.AddWithValue("@cname", customerName);
                        if (queryFrom.HasValue) payCmd.Parameters.AddWithValue("@from", queryFrom.Value);
                        if (queryTo.HasValue) payCmd.Parameters.AddWithValue("@to", queryTo.Value);

                        using (MySqlDataReader reader = payCmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var payment = new CustomerPaymentRecordModel
                                {
                                    Id = Convert.ToInt32(reader["Id"]),
                                    Amount = Convert.ToDecimal(reader["Amount"]),
                                    PaymentDate = Convert.ToDateTime(reader["PaymentDate"]),
                                    Note = reader["Note"]?.ToString() ?? "تسديد نقدي للكريدي",
                                    CashierName = reader["CashierName"]?.ToString() ?? "المسؤول"
                                };
                                currentPaymentsList.Add(payment);
                                currentDetailsPaymentsTotal += payment.Amount;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("خطأ أثناء تحميل بيانات الكشف: " + ex.Message, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

            dgPurchases.ItemsSource = null;
            dgPurchases.ItemsSource = currentPurchasesList;

            dgCustomerPayments.ItemsSource = null;
            dgCustomerPayments.ItemsSource = currentPaymentsList;

            lblGrandTotal.Text = $"{currentDetailsPurchasesTotal:N2} درهم";
            lblPaymentsTotal.Text = $"{currentDetailsPaymentsTotal:N2} درهم";
            lblCreditTotal.Text = $"{currentDetailsDueBalance:N2} درهم ({Math.Round(currentDetailsDueBalance * 20):N0} ريال)";

            if (selectedCustomerForDetails != null)
            {
                selectedCustomerForDetails.DueBalance = currentDetailsDueBalance;
            }
        }

        private void ResetFilterButtonStyles(Button? activeBtn)
        {
            var slateBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));
            var blueBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB"));

            if (btnFilterAll != null) btnFilterAll.Background = slateBrush;
            if (btnFilterToday != null) btnFilterToday.Background = slateBrush;
            if (btnFilterWeek != null) btnFilterWeek.Background = slateBrush;
            if (btnFilterMonth != null) btnFilterMonth.Background = slateBrush;

            if (activeBtn != null) activeBtn.Background = blueBrush;
        }

        private void BtnFilterAll_Click(object sender, RoutedEventArgs e)
        {
            if (selectedCustomerForDetails == null) return;
            ResetFilterButtonStyles(btnFilterAll);
            detailsFromDate = null;
            detailsToDate = null;
            if (dpDetailsFrom != null) dpDetailsFrom.SelectedDate = null;
            if (dpDetailsTo != null) dpDetailsTo.SelectedDate = null;
            LoadCustomerPurchases(selectedCustomerForDetails.FullName);
        }

        private void BtnFilterToday_Click(object sender, RoutedEventArgs e)
        {
            if (selectedCustomerForDetails == null) return;
            ResetFilterButtonStyles(btnFilterToday);
            detailsFromDate = DateTime.Today;
            detailsToDate = DateTime.Today;
            if (dpDetailsFrom != null) dpDetailsFrom.SelectedDate = detailsFromDate;
            if (dpDetailsTo != null) dpDetailsTo.SelectedDate = detailsToDate;
            LoadCustomerPurchases(selectedCustomerForDetails.FullName, detailsFromDate, detailsToDate);
        }

        private void BtnFilterWeek_Click(object sender, RoutedEventArgs e)
        {
            if (selectedCustomerForDetails == null) return;
            ResetFilterButtonStyles(btnFilterWeek);
            detailsFromDate = DateTime.Today.AddDays(-7);
            detailsToDate = DateTime.Today;
            if (dpDetailsFrom != null) dpDetailsFrom.SelectedDate = detailsFromDate;
            if (dpDetailsTo != null) dpDetailsTo.SelectedDate = detailsToDate;
            LoadCustomerPurchases(selectedCustomerForDetails.FullName, detailsFromDate, detailsToDate);
        }

        private void BtnFilterMonth_Click(object sender, RoutedEventArgs e)
        {
            if (selectedCustomerForDetails == null) return;
            ResetFilterButtonStyles(btnFilterMonth);
            detailsFromDate = DateTime.Today.AddDays(-30);
            detailsToDate = DateTime.Today;
            if (dpDetailsFrom != null) dpDetailsFrom.SelectedDate = detailsFromDate;
            if (dpDetailsTo != null) dpDetailsTo.SelectedDate = detailsToDate;
            LoadCustomerPurchases(selectedCustomerForDetails.FullName, detailsFromDate, detailsToDate);
        }

        private void BtnApplyDetailsFilter_Click(object sender, RoutedEventArgs e)
        {
            if (selectedCustomerForDetails == null) return;
            ResetFilterButtonStyles(null);
            detailsFromDate = dpDetailsFrom?.SelectedDate;
            detailsToDate = dpDetailsTo?.SelectedDate;
            LoadCustomerPurchases(selectedCustomerForDetails.FullName, detailsFromDate, detailsToDate);
        }

        private void RbShowPurchases_Checked(object sender, RoutedEventArgs e)
        {
            if (dgPurchases != null) dgPurchases.Visibility = Visibility.Visible;
            if (dgCustomerPayments != null) dgCustomerPayments.Visibility = Visibility.Collapsed;
        }

        private void RbShowPayments_Checked(object sender, RoutedEventArgs e)
        {
            if (dgPurchases != null) dgPurchases.Visibility = Visibility.Collapsed;
            if (dgCustomerPayments != null) dgCustomerPayments.Visibility = Visibility.Visible;
        }

        private string GenerateStatementHtmlContent()
        {
            if (selectedCustomerForDetails == null) return string.Empty;

            string custName = selectedCustomerForDetails.FullName;
            string phone = !string.IsNullOrEmpty(selectedCustomerForDetails.Phone) ? selectedCustomerForDetails.Phone : "غير مسجل";
            string periodStr = (detailsFromDate.HasValue && detailsToDate.HasValue)
                ? $"من {detailsFromDate:yyyy-MM-dd} إلى {detailsToDate:yyyy-MM-dd}"
                : (detailsFromDate.HasValue ? $"من {detailsFromDate:yyyy-MM-dd}" : (detailsToDate.HasValue ? $"حتى {detailsToDate:yyyy-MM-dd}" : "كافة التواريخ (منذ البداية)"));

            var purchasesRows = new StringBuilder();
            if (currentPurchasesList.Count == 0)
            {
                purchasesRows.Append("<tr><td colspan=\"5\" style=\"text-align:center; padding:10px; color:#64748b;\">لا توجد مشتريات مسجلة ضمن هذه الفترة</td></tr>");
            }
            else
            {
                foreach (var p in currentPurchasesList)
                {
                    purchasesRows.Append($@"
                        <tr>
                            <td style=""font-size:11px; color:#64748b;"">{p.PurchaseDate:yyyy-MM-dd HH:mm}</td>
                            <td style=""font-weight:600;"">{System.Net.WebUtility.HtmlEncode(p.ProductName)}</td>
                            <td style=""text-align:center; font-weight:700;"">{p.Quantity:0.###}</td>
                            <td style=""text-align:left;"">{p.SellPrice:N2} د.م.</td>
                            <td style=""text-align:left; font-weight:700; color:#1e3a8a;"">{p.SubTotal:N2} د.م.</td>
                        </tr>");
                }
            }

            var paymentsRows = new StringBuilder();
            if (currentPaymentsList.Count == 0)
            {
                paymentsRows.Append("<tr><td colspan=\"3\" style=\"text-align:center; padding:10px; color:#64748b;\">لا توجد تسديدات مسجلة ضمن هذه الفترة</td></tr>");
            }
            else
            {
                foreach (var pay in currentPaymentsList)
                {
                    paymentsRows.Append($@"
                        <tr>
                            <td style=""font-size:11px; color:#64748b;"">{pay.PaymentDate:yyyy-MM-dd HH:mm}</td>
                            <td>{System.Net.WebUtility.HtmlEncode(pay.Note)} ({System.Net.WebUtility.HtmlEncode(pay.CashierName)})</td>
                            <td style=""text-align:left; font-weight:700; color:#059669;"">{pay.Amount:N2} د.م.</td>
                        </tr>");
                }
            }

            decimal remainingDebt = currentDetailsDueBalance;
            decimal remainingRial = remainingDebt * 20;

            return $@"<!DOCTYPE html>
<html lang=""ar"" dir=""rtl"">
<head>
  <meta charset=""UTF-8"">
  <title>فاتورة وكشف حساب - {System.Net.WebUtility.HtmlEncode(custName)}</title>
  <style>
    * {{ box-sizing: border-box; margin: 0; padding: 0; }}
    body {{ font-family: 'Segoe UI', Tahoma, Arial, sans-serif; background: #f8fafc; color: #1e293b; padding: 20px; direction: rtl; }}
    .invoice-card {{ max-width: 750px; margin: 0 auto; background: white; border: 1px solid #cbd5e1; border-radius: 10px; padding: 25px; box-shadow: 0 4px 12px rgba(0,0,0,0.06); }}
    .header {{ display: flex; justify-content: space-between; align-items: flex-start; margin-bottom: 12px; }}
    .store-name {{ font-size: 20px; font-weight: 800; color: #1e3a8a; }}
    .store-sub {{ font-size: 12px; color: #64748b; margin-top: 3px; }}
    .badge {{ background: #1e3a8a; color: white; padding: 4px 10px; border-radius: 5px; font-size: 12px; font-weight: bold; }}
    .date-text {{ font-size: 11px; color: #64748b; margin-top: 4px; text-align: left; }}
    .divider {{ height: 2px; background: linear-gradient(90deg, #1e3a8a, #3b82f6); margin: 12px 0 16px 0; border-radius: 2px; }}
    .cust-box {{ background: #f1f5f9; border: 1px dashed #cbd5e1; border-radius: 6px; padding: 10px 14px; margin-bottom: 16px; font-size: 13px; }}
    .cust-row {{ display: flex; justify-content: space-between; margin-bottom: 4px; }}
    .section-title {{ font-size: 14px; font-weight: 800; color: #0f172a; margin: 16px 0 8px 0; }}
    table {{ width: 100%; border-collapse: collapse; font-size: 12px; margin-bottom: 12px; }}
    th {{ background: #f1f5f9; color: #334155; font-weight: bold; padding: 8px 10px; border: 1px solid #cbd5e1; text-align: right; }}
    td {{ padding: 8px 10px; border: 1px solid #e2e8f0; }}
    tfoot td {{ background: #f8fafc; font-weight: bold; border-top: 2px solid #cbd5e1; }}
    .summary-card {{ background: #f8fafc; border: 1.5px solid #cbd5e1; border-radius: 8px; padding: 12px 16px; margin-top: 16px; }}
    .sum-row {{ display: flex; justify-content: space-between; font-size: 13px; margin-bottom: 6px; }}
    .sum-divider {{ height: 1px; background: #cbd5e1; margin: 8px 0; }}
    .sum-highlight {{ font-size: 16px; font-weight: 800; color: #b91c1c; }}
    .footer-note {{ text-align: center; font-size: 11px; color: #64748b; margin-top: 20px; padding-top: 10px; border-top: 1px dashed #cbd5e1; }}
    .print-actions {{ text-align: center; margin-bottom: 15px; }}
    .btn-print {{ background: #1e3a8a; color: white; border: none; padding: 9px 20px; border-radius: 6px; font-size: 13px; font-weight: bold; cursor: pointer; }}
    @media print {{ .print-actions {{ display: none; }} body {{ padding: 0; background: white; }} .invoice-card {{ border: none; box-shadow: none; padding: 0; }} }}
  </style>
</head>
<body>
  <div class=""print-actions"">
    <button class=""btn-print"" onclick=""window.print()"">🖨️ طباعة الفاتورة أو حفظها كـ PDF</button>
  </div>
  <div class=""invoice-card"">
    <div class=""header"">
      <div>
        <div class=""store-name"">متجر LMIMA SHOP</div>
        <div class=""store-sub"">منظومة البيع وإدارة المتجر • كشف حساب وفاتورة الكريدي</div>
      </div>
      <div>
        <div class=""badge"">فاتورة كشف حساب</div>
        <div class=""date-text"">{DateTime.Now:yyyy-MM-dd HH:mm}</div>
      </div>
    </div>
    <div class=""divider""></div>
    <div class=""cust-box"">
      <div class=""cust-row"">
        <div>الزبون: <strong>{System.Net.WebUtility.HtmlEncode(custName)}</strong></div>
        <div>الهاتف: <strong dir=""ltr"">{phone}</strong></div>
      </div>
      <div class=""cust-row"" style=""margin-top:4px;"">
        <div>الفترة: <strong>{periodStr}</strong></div>
        <div>رقم الكشف: <strong>#CR-{selectedCustomerForDetails.Id:D4}</strong></div>
      </div>
    </div>

    <div class=""section-title"">🛒 لائحة المشتريات بالكريدي ({currentPurchasesList.Count}):</div>
    <table>
      <thead>
        <tr>
          <th style=""width:25%;"">التاريخ</th>
          <th style=""width:35%;"">السلعة</th>
          <th style=""width:12%; text-align:center;"">الكمية</th>
          <th style=""width:13%; text-align:left;"">الثمن</th>
          <th style=""width:15%; text-align:left;"">المجموع</th>
        </tr>
      </thead>
      <tbody>
        {purchasesRows}
      </tbody>
      <tfoot>
        <tr>
          <td colspan=""4"" style=""text-align:right;"">مجموع المشتريات في الفترة:</td>
          <td style=""text-align:left; color:#1e3a8a;"">{currentDetailsPurchasesTotal:N2} د.م.</td>
        </tr>
      </tfoot>
    </table>

    <div class=""section-title"">💵 المبالغ المسددة (المدفوعات) ({currentPaymentsList.Count}):</div>
    <table>
      <thead>
        <tr>
          <th style=""width:28%;"">تاريخ الدفع</th>
          <th style=""width:44%;"">البيان / طريقة الأداء</th>
          <th style=""width:28%; text-align:left;"">المبلغ المسدد</th>
        </tr>
      </thead>
      <tbody>
        {paymentsRows}
      </tbody>
      <tfoot>
        <tr>
          <td colspan=""2"" style=""text-align:right;"">مجموع المبالغ المسددة:</td>
          <td style=""text-align:left; color:#059669;"">{currentDetailsPaymentsTotal:N2} د.م.</td>
        </tr>
      </tfoot>
    </table>

    <div class=""summary-card"">
      <div class=""sum-row"">
        <span>إجمالي المشتريات في الفترة:</span>
        <strong>{currentDetailsPurchasesTotal:N2} د.م.</strong>
      </div>
      <div class=""sum-row"" style=""color:#059669;"">
        <span>إجمالي المبالغ المسددة:</span>
        <strong>- {currentDetailsPaymentsTotal:N2} د.م.</strong>
      </div>
      <div class=""sum-divider""></div>
      <div class=""sum-row sum-highlight"">
        <span>صافي الكريدي المتبقي حالياً:</span>
        <div style=""text-align:left;"">
          <div>{remainingDebt:N2} درهم</div>
          <div style=""font-size:12px; color:#c2410c; font-weight:bold;"">{remainingRial:N0} ريال</div>
        </div>
      </div>
    </div>

    <div class=""footer-note"">
      نشكركم على حسن تعاملكم ووفائكم • متجر LMIMA SHOP
    </div>
  </div>
</body>
</html>";
        }

        private void BtnPrintStatement_Click(object sender, RoutedEventArgs e)
        {
            if (selectedCustomerForDetails == null) return;
            try
            {
                string html = GenerateStatementHtmlContent();
                string tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"Facture_Credit_{selectedCustomerForDetails.Id}_{DateTime.Now:yyyyMMddHHmmss}.html");
                File.WriteAllText(tempFile, html, Encoding.UTF8);

                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = tempFile,
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show("خطأ أثناء فتح نافذة طباعة الفاتورة: " + ex.Message, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnExportStatement_Click(object sender, RoutedEventArgs e)
        {
            if (selectedCustomerForDetails == null) return;
            try
            {
                var sfd = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "HTML Files (*.html)|*.html",
                    FileName = $"Facture_Credit_{selectedCustomerForDetails.FullName.Replace(' ', '_')}_{DateTime.Now:yyyyMMdd}.html",
                    Title = "حفظ وتصدير فاتورة وكشف حساب الزبون"
                };

                if (sfd.ShowDialog() == true)
                {
                    string html = GenerateStatementHtmlContent();
                    File.WriteAllText(sfd.FileName, html, Encoding.UTF8);
                    MessageBox.Show("تم تصدير الفاتورة بنجاح في المسار المحدد!", "تم التصدير", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("خطأ أثناء تصدير الفاتورة: " + ex.Message, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnWhatsAppStatement_Click(object sender, RoutedEventArgs e)
        {
            if (selectedCustomerForDetails == null) return;

            string phone = selectedCustomerForDetails.Phone ?? "";
            string cleanPhone = phone.Replace("+", "").Replace(" ", "").Replace("-", "");
            if (cleanPhone.StartsWith("0")) cleanPhone = "212" + cleanPhone.Substring(1);

            string periodStr = (detailsFromDate.HasValue && detailsToDate.HasValue)
                ? $"من {detailsFromDate:yyyy-MM-dd} إلى {detailsToDate:yyyy-MM-dd}"
                : (detailsFromDate.HasValue ? $"من {detailsFromDate:yyyy-MM-dd}" : (detailsToDate.HasValue ? $"حتى {detailsToDate:yyyy-MM-dd}" : "كافة المشتريات"));

            var sb = new StringBuilder();
            sb.AppendLine("*متجر LMIMA SHOP* 🏪");
            sb.AppendLine("*فاتورة وكشف حساب الكريدي* 📑");
            sb.AppendLine("━━━━━━━━━━━━━━━━━━━━");
            sb.AppendLine($"👤 *الزبون:* {selectedCustomerForDetails.FullName}");
            if (!string.IsNullOrEmpty(phone)) sb.AppendLine($"📱 *الهاتف:* {phone}");
            sb.AppendLine($"📅 *الفترة:* {periodStr}");
            sb.AppendLine($"🕒 *التاريخ:* {DateTime.Now:yyyy-MM-dd HH:mm}");
            sb.AppendLine("━━━━━━━━━━━━━━━━━━━━");
            sb.AppendLine();
            sb.AppendLine($"🛒 *لائحة المشتريات ({currentPurchasesList.Count}):*");

            if (currentPurchasesList.Count == 0)
            {
                sb.AppendLine("(لا توجد مشتريات مسجلة خلال هذه الفترة)");
            }
            else
            {
                int count = 0;
                foreach (var p in currentPurchasesList)
                {
                    if (++count > 25)
                    {
                        sb.AppendLine($"... ومشتريات أخرى ({currentPurchasesList.Count - 25} بنداً إضافياً)");
                        break;
                    }
                    sb.AppendLine($"• {p.ProductName} × {p.Quantity:0.###} = {p.SubTotal:N2} د.م.");
                }
            }

            sb.AppendLine($"📊 *مجموع المشتريات:* {currentDetailsPurchasesTotal:N2} د.م. ({Math.Round(currentDetailsPurchasesTotal * 20):N0} ريال)");
            sb.AppendLine();
            sb.AppendLine($"💵 *المبالغ المسددة ({currentPaymentsList.Count}):*");

            if (currentPaymentsList.Count == 0)
            {
                sb.AppendLine("(لم يتم تسجيل دفعات خلال هذه الفترة)");
            }
            else
            {
                int count = 0;
                foreach (var pay in currentPaymentsList)
                {
                    if (++count > 10)
                    {
                        sb.AppendLine("... وتسديدات أخرى");
                        break;
                    }
                    sb.AppendLine($"• {pay.PaymentDate:yyyy-MM-dd}: {pay.Amount:N2} د.م. ({pay.Note})");
                }
            }

            sb.AppendLine($"✅ *إجمالي المسدد:* {currentDetailsPaymentsTotal:N2} د.م.");
            sb.AppendLine();
            sb.AppendLine("━━━━━━━━━━━━━━━━━━━━");
            sb.AppendLine("⚠️ *صافي الكريدي المتبقي بذمتكم حالياً:*");
            sb.AppendLine($"💰 *{currentDetailsDueBalance:N2} درهم مغربي*");
            sb.AppendLine($"🪙 *({Math.Round(currentDetailsDueBalance * 20):N0} ريال)*");
            sb.AppendLine("━━━━━━━━━━━━━━━━━━━━");
            sb.AppendLine("نشكركم على حسن تعاملكم ووفائكم! 🙏");
            sb.AppendLine("متجر LMIMA SHOP");

            string waUrl = !string.IsNullOrEmpty(cleanPhone) && cleanPhone.Length >= 9
                ? $"https://wa.me/{cleanPhone}?text={Uri.EscapeDataString(sb.ToString())}"
                : $"https://wa.me/?text={Uri.EscapeDataString(sb.ToString())}";

            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = waUrl,
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show("فشل فتح تطبيق الواتساب: " + ex.Message, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCloseDetails_Click(object sender, RoutedEventArgs e)
        {
            detailsOverlay.Visibility = Visibility.Collapsed;
            selectedCustomerForDetails = null;
        }

        private void BtnReturnProduct_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is CustomerPurchaseModel purchaseItem)
            {
                if (selectedCustomerForDetails == null) return;

                MessageBoxResult result = MessageBox.Show(
                    $"هل أنت متأكد من إرجاع وحذف السلعة:\n\n" +
                    $"{purchaseItem.ProductName} (الكمية: {purchaseItem.Quantity:G})؟\n\n" +
                    $"سيتم إرجاع الكمية للمخزن وتحديث حساب الزبون.", 
                    "تأكيد إرجاع السلعة", 
                    MessageBoxButton.YesNo, 
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    ReturnProduct(purchaseItem);
                }
            }
        }

        private void ReturnProduct(CustomerPurchaseModel item)
        {
            if (selectedCustomerForDetails == null) return;

            using (MySqlConnection conn = dbHelper.GetConnection())
            {
                MySqlTransaction? trans = null;
                try
                {
                    conn.Open();
                    trans = conn.BeginTransaction();

                    // 1. Delete SalesDetails record
                    string deleteDetailsQuery = "DELETE FROM SalesDetails WHERE Id = @detailId";
                    using (MySqlCommand cmd = new MySqlCommand(deleteDetailsQuery, conn, trans))
                    {
                        cmd.Parameters.AddWithValue("@detailId", item.DetailId);
                        cmd.ExecuteNonQuery();
                    }

                    // 2. Return stock if not manual
                    if (item.Barcode != "MANUAL")
                    {
                        string updateStockQuery = "UPDATE Products SET StockQuantity = StockQuantity + @qty WHERE Id = @productId";
                        using (MySqlCommand cmd = new MySqlCommand(updateStockQuery, conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@qty", item.Quantity);
                            cmd.Parameters.AddWithValue("@productId", item.ProductId);
                            cmd.ExecuteNonQuery();
                        }
                    }

                    // 3. Update Sales.TotalAmount
                    string updateSaleQuery = "UPDATE Sales SET TotalAmount = TotalAmount - @subTotal WHERE InvoiceId = @invId";
                    using (MySqlCommand cmd = new MySqlCommand(updateSaleQuery, conn, trans))
                    {
                        cmd.Parameters.AddWithValue("@subTotal", item.SubTotal);
                        cmd.Parameters.AddWithValue("@invId", item.InvoiceId);
                        cmd.ExecuteNonQuery();
                    }

                    // 4. If invoice is empty, delete the sale itself
                    string checkDetailsQuery = "SELECT COUNT(*) FROM SalesDetails WHERE InvoiceId = @invId";
                    bool isInvoiceEmpty = false;
                    using (MySqlCommand cmd = new MySqlCommand(checkDetailsQuery, conn, trans))
                    {
                        cmd.Parameters.AddWithValue("@invId", item.InvoiceId);
                        long count = Convert.ToInt64(cmd.ExecuteScalar());
                        if (count == 0)
                        {
                            isInvoiceEmpty = true;
                        }
                    }

                    if (isInvoiceEmpty)
                    {
                        string deleteSaleQuery = "DELETE FROM Sales WHERE InvoiceId = @invId";
                        using (MySqlCommand cmd = new MySqlCommand(deleteSaleQuery, conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@invId", item.InvoiceId);
                            cmd.ExecuteNonQuery();
                        }
                    }

                    // 5. Update Customer's DueBalance (if it's not a generic cash customer and they have a due balance)
                    if (selectedCustomerForDetails.FullName != "زبون نقدي عابر" && selectedCustomerForDetails.FullName != "عميل نقدي افتراضي")
                    {
                        string updateCustQuery = @"
                            UPDATE Customers 
                            SET DueBalance = CASE WHEN DueBalance >= @subTotal THEN DueBalance - @subTotal ELSE 0 END 
                            WHERE FullName = @custName";
                        
                        using (MySqlCommand cmd = new MySqlCommand(updateCustQuery, conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@subTotal", item.SubTotal);
                            cmd.Parameters.AddWithValue("@custName", selectedCustomerForDetails.FullName);
                            cmd.ExecuteNonQuery();
                        }
                    }

                    trans.Commit();
                    MessageBox.Show("تمت عملية إرجاع السلعة بنجاح وتحديث المخزون والديون.", "نجاح العملية", MessageBoxButton.OK, MessageBoxImage.Information);
                    
                    // Reload details
                    LoadCustomerPurchases(selectedCustomerForDetails.FullName);

                    // Refresh customers list
                    LoadCustomers();
                    _ = CustomerGoogleSheetService.SyncCustomersSheetAsync(true);
                }
                catch (Exception ex)
                {
                    if (trans != null)
                    {
                        try { trans.Rollback(); } catch {}
                    }
                    MessageBox.Show("فشل إرجاع السلعة: " + ex.Message, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnPayProduct_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is CustomerPurchaseModel purchaseItem)
            {
                if (selectedCustomerForDetails == null) return;

                MessageBoxResult result = MessageBox.Show(
                    $"هل أنت متأكد من تأدية ثمن السلعة:\n\n" +
                    $"{purchaseItem.ProductName} (المجموع: {purchaseItem.SubTotal:N2} د.م.)؟\n\n" +
                    $"سيتم خصم هذا المبلغ من ديون الزبون وحذفها من لائحة الكريدي دون إرجاعها للمخزن.", 
                    "تأكيد أداء ثمن السلعة", 
                    MessageBoxButton.YesNo, 
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    PayProduct(purchaseItem);
                }
            }
        }

        private void PayProduct(CustomerPurchaseModel item)
        {
            if (selectedCustomerForDetails == null) return;

            using (MySqlConnection conn = dbHelper.GetConnection())
            {
                MySqlTransaction? trans = null;
                try
                {
                    conn.Open();
                    trans = conn.BeginTransaction();

                    // 1. Delete SalesDetails record
                    string deleteDetailsQuery = "DELETE FROM SalesDetails WHERE Id = @detailId";
                    using (MySqlCommand cmd = new MySqlCommand(deleteDetailsQuery, conn, trans))
                    {
                        cmd.Parameters.AddWithValue("@detailId", item.DetailId);
                        cmd.ExecuteNonQuery();
                    }

                    // 2. Update Sales.TotalAmount
                    string updateSaleQuery = "UPDATE Sales SET TotalAmount = TotalAmount - @subTotal WHERE InvoiceId = @invId";
                    using (MySqlCommand cmd = new MySqlCommand(updateSaleQuery, conn, trans))
                    {
                        cmd.Parameters.AddWithValue("@subTotal", item.SubTotal);
                        cmd.Parameters.AddWithValue("@invId", item.InvoiceId);
                        cmd.ExecuteNonQuery();
                    }

                    // 3. If invoice is empty, delete the sale itself
                    string checkDetailsQuery = "SELECT COUNT(*) FROM SalesDetails WHERE InvoiceId = @invId";
                    bool isInvoiceEmpty = false;
                    using (MySqlCommand cmd = new MySqlCommand(checkDetailsQuery, conn, trans))
                    {
                        cmd.Parameters.AddWithValue("@invId", item.InvoiceId);
                        long count = Convert.ToInt64(cmd.ExecuteScalar());
                        if (count == 0)
                        {
                            isInvoiceEmpty = true;
                        }
                    }

                    if (isInvoiceEmpty)
                    {
                        string deleteSaleQuery = "DELETE FROM Sales WHERE InvoiceId = @invId";
                        using (MySqlCommand cmd = new MySqlCommand(deleteSaleQuery, conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@invId", item.InvoiceId);
                            cmd.ExecuteNonQuery();
                        }
                    }

                    // 4. Update Customer's DueBalance
                    if (selectedCustomerForDetails.FullName != "زبون نقدي عابر" && selectedCustomerForDetails.FullName != "عميل نقدي افتراضي")
                    {
                        string updateCustQuery = @"
                            UPDATE Customers 
                            SET DueBalance = CASE WHEN DueBalance >= @subTotal THEN DueBalance - @subTotal ELSE 0 END 
                            WHERE FullName = @custName";
                        
                        using (MySqlCommand cmd = new MySqlCommand(updateCustQuery, conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@subTotal", item.SubTotal);
                            cmd.Parameters.AddWithValue("@custName", selectedCustomerForDetails.FullName);
                            cmd.ExecuteNonQuery();
                        }
                    }

                    trans.Commit();
                    MessageBox.Show("تم تأدية ثمن المنتج بنجاح وتحديث حساب الزبون.", "نجاح العملية", MessageBoxButton.OK, MessageBoxImage.Information);
                    
                    // Reload details
                    LoadCustomerPurchases(selectedCustomerForDetails.FullName);

                    // Refresh customers list
                    LoadCustomers();
                    _ = CustomerGoogleSheetService.SyncCustomersSheetAsync(true);
                    _ = CloudSyncService.SyncMobileDataAsync(true);
                }
                catch (Exception ex)
                {
                    if (trans != null)
                    {
                        try { trans.Rollback(); } catch {}
                    }
                    MessageBox.Show("فشل إتمام عملية الدفع: " + ex.Message, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnIncreaseQty_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is CustomerPurchaseModel purchaseItem)
            {
                if (selectedCustomerForDetails == null) return;

                using (MySqlConnection conn = dbHelper.GetConnection())
                {
                    MySqlTransaction? trans = null;
                    try
                    {
                        conn.Open();
                        trans = conn.BeginTransaction();

                        if (purchaseItem.Barcode != "MANUAL")
                        {
                            string checkStockQuery = "SELECT StockQuantity FROM Products WHERE Id = @prodId FOR UPDATE";
                            decimal currentStock = 0;
                            using (MySqlCommand cmd = new MySqlCommand(checkStockQuery, conn, trans))
                            {
                                cmd.Parameters.AddWithValue("@prodId", purchaseItem.ProductId);
                                currentStock = Convert.ToDecimal(cmd.ExecuteScalar());
                            }

                            if (currentStock < 1)
                            {
                                MessageBox.Show($"المنتج '{purchaseItem.ProductName}' غير متوفر بكمية كافية في المخزن الحالي (المتبقي: {currentStock:G})", "عجز المخزن", MessageBoxButton.OK, MessageBoxImage.Warning);
                                return;
                            }

                            string deductStockQuery = "UPDATE Products SET StockQuantity = StockQuantity - 1 WHERE Id = @prodId";
                            using (MySqlCommand cmd = new MySqlCommand(deductStockQuery, conn, trans))
                            {
                                cmd.Parameters.AddWithValue("@prodId", purchaseItem.ProductId);
                                cmd.ExecuteNonQuery();
                            }
                        }

                        string updateDetailsQuery = @"
                            UPDATE SalesDetails 
                            SET Quantity = Quantity + 1, SubTotal = SubTotal + @unitPrice 
                            WHERE Id = @detailId";
                        
                        using (MySqlCommand cmd = new MySqlCommand(updateDetailsQuery, conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@unitPrice", purchaseItem.SellPrice);
                            cmd.Parameters.AddWithValue("@detailId", purchaseItem.DetailId);
                            cmd.ExecuteNonQuery();
                        }

                        string updateSaleQuery = "UPDATE Sales SET TotalAmount = TotalAmount + @unitPrice WHERE InvoiceId = @invId";
                        using (MySqlCommand cmd = new MySqlCommand(updateSaleQuery, conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@unitPrice", purchaseItem.SellPrice);
                            cmd.Parameters.AddWithValue("@invId", purchaseItem.InvoiceId);
                            cmd.ExecuteNonQuery();
                        }

                        if (selectedCustomerForDetails.FullName != "زبون نقدي عابر" && selectedCustomerForDetails.FullName != "عميل نقدي افتراضي")
                        {
                            string updateCustQuery = "UPDATE Customers SET DueBalance = DueBalance + @unitPrice WHERE FullName = @custName";
                            using (MySqlCommand cmd = new MySqlCommand(updateCustQuery, conn, trans))
                            {
                                cmd.Parameters.AddWithValue("@unitPrice", purchaseItem.SellPrice);
                                cmd.Parameters.AddWithValue("@custName", selectedCustomerForDetails.FullName);
                                cmd.ExecuteNonQuery();
                            }
                        }

                        trans.Commit();
                        LoadCustomerPurchases(selectedCustomerForDetails.FullName);
                        LoadCustomers();
                        _ = CloudSyncService.SyncMobileDataAsync(true);
                    }
                    catch (Exception ex)
                    {
                        if (trans != null)
                        {
                            try { trans.Rollback(); } catch {}
                        }
                        MessageBox.Show("خطأ أثناء زيادة الكمية: " + ex.Message, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void BtnDecreaseQty_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is CustomerPurchaseModel purchaseItem)
            {
                if (selectedCustomerForDetails == null) return;

                if (purchaseItem.Quantity <= 1)
                {
                    MessageBoxResult result = MessageBox.Show(
                        $"الكمية الحالية هي {purchaseItem.Quantity:G}. هل تريد إرجاع وحذف هذه السلعة بالكامل؟",
                        "تأكيد إرجاع السلعة",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        ReturnProduct(purchaseItem);
                    }
                    return;
                }

                using (MySqlConnection conn = dbHelper.GetConnection())
                {
                    MySqlTransaction? trans = null;
                    try
                    {
                        conn.Open();
                        trans = conn.BeginTransaction();

                        if (purchaseItem.Barcode != "MANUAL")
                        {
                            string addStockQuery = "UPDATE Products SET StockQuantity = StockQuantity + 1 WHERE Id = @prodId";
                            using (MySqlCommand cmd = new MySqlCommand(addStockQuery, conn, trans))
                            {
                                cmd.Parameters.AddWithValue("@prodId", purchaseItem.ProductId);
                                cmd.ExecuteNonQuery();
                            }
                        }

                        string updateDetailsQuery = @"
                            UPDATE SalesDetails 
                            SET Quantity = Quantity - 1, SubTotal = SubTotal - @unitPrice 
                            WHERE Id = @detailId";
                        
                        using (MySqlCommand cmd = new MySqlCommand(updateDetailsQuery, conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@unitPrice", purchaseItem.SellPrice);
                            cmd.Parameters.AddWithValue("@detailId", purchaseItem.DetailId);
                            cmd.ExecuteNonQuery();
                        }

                        string updateSaleQuery = "UPDATE Sales SET TotalAmount = TotalAmount - @unitPrice WHERE InvoiceId = @invId";
                        using (MySqlCommand cmd = new MySqlCommand(updateSaleQuery, conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@unitPrice", purchaseItem.SellPrice);
                            cmd.Parameters.AddWithValue("@invId", purchaseItem.InvoiceId);
                            cmd.ExecuteNonQuery();
                        }

                        if (selectedCustomerForDetails.FullName != "زبون نقدي عابر" && selectedCustomerForDetails.FullName != "عميل نقدي افتراضي")
                        {
                            string updateCustQuery = @"
                                UPDATE Customers 
                                SET DueBalance = CASE WHEN DueBalance >= @unitPrice THEN DueBalance - @unitPrice ELSE 0 END 
                                WHERE FullName = @custName";
                            
                            using (MySqlCommand cmd = new MySqlCommand(updateCustQuery, conn, trans))
                            {
                                cmd.Parameters.AddWithValue("@unitPrice", purchaseItem.SellPrice);
                                cmd.Parameters.AddWithValue("@custName", selectedCustomerForDetails.FullName);
                                cmd.ExecuteNonQuery();
                            }
                        }

                        trans.Commit();
                        LoadCustomerPurchases(selectedCustomerForDetails.FullName);
                        LoadCustomers();
                        _ = CloudSyncService.SyncMobileDataAsync(true);
                    }
                    catch (Exception ex)
                    {
                        if (trans != null)
                        {
                            try { trans.Rollback(); } catch {}
                        }
                        MessageBox.Show("خطأ أثناء تقليل الكمية: " + ex.Message, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void BtnSaveCustomer_Click(object sender, RoutedEventArgs e)
        {
            string name = txtCustomerName.Text.Trim();
            string phone = txtPhone.Text.Trim();
            string ice = txtCustomerICE.Text.Trim();
            
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("الرجاء إدخال اسم الزبون!", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!decimal.TryParse(txtCreditLimit.Text, out decimal creditLimit) || creditLimit < 0)
            {
                MessageBox.Show("الرجاء إدخال حد ائتمان صحيح وموجب!", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string periodType = "Open";
            int periodDays = 0;
            if (chkAllowCredit.IsChecked == true)
            {
                if (cmbCreditPeriodType.SelectedItem is ComboBoxItem selectedItem)
                {
                    periodType = selectedItem.Tag?.ToString() ?? "Open";
                }
                if (periodType == "Limited")
                {
                    if (!int.TryParse(txtCreditPeriodDays.Text.Trim(), out periodDays) || periodDays <= 0)
                    {
                        MessageBox.Show("الرجاء إدخال عدد أيام صحيح وموجب لأجل الكريدي المحدود!", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }
            }

            using (MySqlConnection conn = dbHelper.GetConnection())
            {
                try
                {
                    conn.Open();
                    string query;
                    if (isEditMode)
                    {
                        query = "UPDATE Customers SET FullName = @name, Phone = @phone, ICE = @ice, CreditLimit = @credit, AllowCredit = @allow, CreditPeriodType = @type, CreditPeriodDays = @days WHERE Id = @id";
                    }
                    else
                    {
                        query = "INSERT INTO Customers (FullName, Phone, ICE, CreditLimit, AllowCredit, DueBalance, InvoiceCount, CreditPeriodType, CreditPeriodDays) VALUES (@name, @phone, @ice, @credit, @allow, 0.00, 0, @type, @days)";
                    }

                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@name", name);
                        cmd.Parameters.AddWithValue("@phone", phone);
                        cmd.Parameters.AddWithValue("@ice", ice);
                        cmd.Parameters.AddWithValue("@credit", creditLimit);
                        cmd.Parameters.AddWithValue("@allow", chkAllowCredit.IsChecked == true);
                        cmd.Parameters.AddWithValue("@type", periodType);
                        cmd.Parameters.AddWithValue("@days", periodDays);
                        if (isEditMode)
                        {
                            cmd.Parameters.AddWithValue("@id", editingCustomerId);
                        }

                        cmd.ExecuteNonQuery();
                    }

                    MessageBox.Show("تم حفظ بيانات الزبون بنجاح!", "تم العملية", MessageBoxButton.OK, MessageBoxImage.Information);
                    modalOverlay.Visibility = Visibility.Collapsed;
                    LoadCustomers();
                    _ = CustomerGoogleSheetService.SyncCustomersSheetAsync(true);
                    _ = CloudSyncService.SyncMobileDataAsync(true);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("فشل حفظ الزبون (ربما الاسم مسجل مسبقاً): " + ex.Message, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnCancelCustomer_Click(object sender, RoutedEventArgs e)
        {
            modalOverlay.Visibility = Visibility.Collapsed;
        }

        private void BtnPayDebt_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext != null)
            {
                dynamic customer = btn.DataContext;
                int id = customer.Id;
                string fullName = customer.FullName;
                string phone = customer.Phone ?? "";
                decimal dueBalance = customer.DueBalance;
                debtCustomerId = id;
                debtCustomerMax = dueBalance;
                lblDebtInfo.Text = $"الزبون: {fullName}\nالدين المستحق عليه حالياً: {dueBalance:N2} د.م.";
                txtAmountToPay.Text = dueBalance.ToString("0.00");
                debtOverlay.Visibility = Visibility.Visible;
                txtAmountToPay.Focus();
                txtAmountToPay.SelectAll();
            }
        }

        private TextBox? activeCustomerModalTextBox = null;

        private TextBox GetActiveCustomerModalTextBox()
        {
            var focused = System.Windows.Input.FocusManager.GetFocusedElement(Window.GetWindow(this)) as TextBox;
            if (focused != null && (focused == txtCustomerName || focused == txtPhone || focused == txtCreditLimit || focused == txtCreditPeriodDays))
            {
                activeCustomerModalTextBox = focused;
            }
            return activeCustomerModalTextBox ?? txtPhone;
        }

        private void BtnToggleVirtualKeyboard_Click(object sender, RoutedEventArgs e)
        {
            MainWindow? mainWindow = null;
            foreach (Window win in Application.Current.Windows)
            {
                if (win is MainWindow mainWin)
                {
                    mainWindow = mainWin;
                    break;
                }
            }
            if (mainWindow != null)
            {
                mainWindow.ToggleVirtualKeyboard();
            }
        }

        private void CustomerModalKeypad_Click(object sender, RoutedEventArgs e)
        {
            var targetBox = GetActiveCustomerModalTextBox();
            if (targetBox == null) return;

            if (sender is Button { Content: not null } button)
            {
                string val = button.Content.ToString() ?? "";

                if (targetBox.SelectionLength > 0 && val != "⌫")
                {
                    int selStart = targetBox.SelectionStart;
                    targetBox.Text = targetBox.Text.Remove(selStart, targetBox.SelectionLength);
                    targetBox.SelectionStart = selStart;
                }

                if (val == "C")
                {
                    if (targetBox == txtPhone) targetBox.Text = "-";
                    else if (targetBox == txtCreditLimit) targetBox.Text = "0.00";
                    else targetBox.Text = "";
                    targetBox.SelectionStart = targetBox.Text.Length;
                }
                else if (val == "⌫" || val.Contains("⌫"))
                {
                    if (targetBox.SelectionLength > 0)
                    {
                        int selStart = targetBox.SelectionStart;
                        targetBox.Text = targetBox.Text.Remove(selStart, targetBox.SelectionLength);
                        targetBox.SelectionStart = selStart;
                    }
                    else if (targetBox.SelectionStart > 0)
                    {
                        int caret = targetBox.SelectionStart;
                        targetBox.Text = targetBox.Text.Remove(caret - 1, 1);
                        targetBox.SelectionStart = caret - 1;
                    }
                    else if (targetBox.Text.Length > 0)
                    {
                        targetBox.Text = targetBox.Text.Substring(0, targetBox.Text.Length - 1);
                        targetBox.SelectionStart = targetBox.Text.Length;
                    }
                }
                else if (val == "." || val == ",")
                {
                    if (targetBox == txtCreditLimit || targetBox == txtCustomerName)
                    {
                        if (!targetBox.Text.Contains(".") && !targetBox.Text.Contains(","))
                        {
                            targetBox.Text = string.IsNullOrEmpty(targetBox.Text) ? "0." : targetBox.Text + ".";
                            targetBox.SelectionStart = targetBox.Text.Length;
                        }
                    }
                }
                else
                {
                    if (targetBox == txtPhone && (targetBox.Text == "-" || targetBox.Text == ""))
                    {
                        targetBox.Text = val;
                        targetBox.SelectionStart = targetBox.Text.Length;
                    }
                    else if (targetBox == txtCreditLimit && (targetBox.Text == "0.00" || targetBox.Text == "0"))
                    {
                        targetBox.Text = val;
                        targetBox.SelectionStart = targetBox.Text.Length;
                    }
                    else
                    {
                        int caret = targetBox.SelectionStart;
                        targetBox.Text = targetBox.Text.Insert(caret, val);
                        targetBox.SelectionStart = caret + val.Length;
                    }
                }

                targetBox.Focus();
            }
        }

        private void DebtKeypad_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Content: not null } button)
            {
                string val = button.Content.ToString() ?? "";

                // If user selected all or part of text, clear selection first on digit or dot
                if (txtAmountToPay.SelectionLength > 0 && val != "⌫")
                {
                    int selStart = txtAmountToPay.SelectionStart;
                    txtAmountToPay.Text = txtAmountToPay.Text.Remove(selStart, txtAmountToPay.SelectionLength);
                    txtAmountToPay.SelectionStart = selStart;
                }

                if (val == "C")
                {
                    txtAmountToPay.Text = "";
                }
                else if (val == "⌫" || val.Contains("⌫"))
                {
                    if (txtAmountToPay.SelectionLength > 0)
                    {
                        int selStart = txtAmountToPay.SelectionStart;
                        txtAmountToPay.Text = txtAmountToPay.Text.Remove(selStart, txtAmountToPay.SelectionLength);
                        txtAmountToPay.SelectionStart = selStart;
                    }
                    else if (txtAmountToPay.Text.Length > 0)
                    {
                        txtAmountToPay.Text = txtAmountToPay.Text.Substring(0, txtAmountToPay.Text.Length - 1);
                    }
                }
                else if (val == "." || val == ",")
                {
                    if (!txtAmountToPay.Text.Contains(".") && !txtAmountToPay.Text.Contains(","))
                    {
                        txtAmountToPay.Text = string.IsNullOrEmpty(txtAmountToPay.Text) ? "0." : txtAmountToPay.Text + ".";
                    }
                }
                else
                {
                    txtAmountToPay.Text += val;
                }
                txtAmountToPay.SelectionStart = txtAmountToPay.Text.Length;
                txtAmountToPay.Focus();
            }
        }

        private void BtnConfirmPayDebt_Click(object sender, RoutedEventArgs e)
        {
            string rawAmount = txtAmountToPay.Text.Trim().Replace(',', '.');
            if (!decimal.TryParse(rawAmount, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal amountToPay) || amountToPay <= 0)
            {
                MessageBox.Show("الرجاء إدخال مبلغ تسديد صحيح وموجب!", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (amountToPay > debtCustomerMax)
            {
                MessageBox.Show("لا يمكن تسديد مبلغ أكبر من الدين المستحق!", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            using (MySqlConnection conn = dbHelper.GetConnection())
            {
                try
                {
                    conn.Open();
                    string query = "UPDATE Customers SET DueBalance = DueBalance - @amount WHERE Id = @id";
                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@amount", amountToPay);
                        cmd.Parameters.AddWithValue("@id", debtCustomerId);
                        cmd.ExecuteNonQuery();
                    }

                    string insertPay = "INSERT INTO CustomerPayments (CustomerId, CustomerName, Amount, PaymentDate, Note, CashierName) VALUES (@id, (SELECT FullName FROM Customers WHERE Id = @id), @amount, NOW(), 'تسديد نقدي للكريدي', 'المسؤول')";
                    using (MySqlCommand payCmd = new MySqlCommand(insertPay, conn))
                    {
                        payCmd.Parameters.AddWithValue("@amount", amountToPay);
                        payCmd.Parameters.AddWithValue("@id", debtCustomerId);
                        payCmd.ExecuteNonQuery();
                    }

                    MessageBox.Show("تم تسديد المبلغ وخصمه من ديون الزبون بنجاح!", "تمت العملية", MessageBoxButton.OK, MessageBoxImage.Information);
                    debtOverlay.Visibility = Visibility.Collapsed;
                    LoadCustomers();
                    _ = CustomerGoogleSheetService.SyncCustomersSheetAsync(true);
                    _ = CloudSyncService.SyncMobileDataAsync(true);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("فشل تسجيل عملية تسديد الدين: " + ex.Message, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnCancelPayDebt_Click(object sender, RoutedEventArgs e)
        {
            debtOverlay.Visibility = Visibility.Collapsed;
        }

        // Redundant sidebar methods removed since managed by MainWindow container
    }

    public class CustomerModel
    {
        public int Id { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public decimal CreditLimit { get; set; }
        public decimal DueBalance { get; set; }
        public bool AllowCredit { get; set; }
        public int InvoiceCount { get; set; }
        public string CreditPeriodType { get; set; } = "Open";
        public int CreditPeriodDays { get; set; }
        public DateTime? LastCreditDate { get; set; }
        public string ICE { get; set; } = string.Empty;

        public bool IsCreditOverdue
        {
            get
            {
                if (!AllowCredit || DueBalance <= 0 || CreditPeriodType != "Limited" || LastCreditDate == null)
                    return false;

                return LastCreditDate.Value.AddDays(CreditPeriodDays) < DateTime.Now;
            }
        }

        public string CreditDueDate
        {
            get
            {
                if (!AllowCredit || DueBalance <= 0 || CreditPeriodType != "Limited" || LastCreditDate == null)
                    return "-";

                return LastCreditDate.Value.AddDays(CreditPeriodDays).ToString("yyyy-MM-dd");
            }
        }
    }

    public class CustomerPurchaseModel
    {
        public int DetailId { get; set; }
        public int InvoiceId { get; set; }
        public int ProductId { get; set; }
        public string Barcode { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public decimal SellPrice { get; set; }
        public decimal SubTotal { get; set; }
        public DateTime PurchaseDate { get; set; }
    }

    public class CustomerPaymentRecordModel
    {
        public int Id { get; set; }
        public decimal Amount { get; set; }
        public DateTime PaymentDate { get; set; }
        public string Note { get; set; } = string.Empty;
        public string CashierName { get; set; } = string.Empty;
    }
}




