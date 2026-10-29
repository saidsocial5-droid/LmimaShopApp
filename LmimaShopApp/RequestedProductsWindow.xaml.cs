using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ClosedXML.Excel;
using MySqlConnector;

namespace LmimaShopApp
{
    public partial class RequestedProductsWindow : UserControl
    {
        private readonly DatabaseHelper dbHelper = new DatabaseHelper();
        private List<RequestedProduct> allRequests = new List<RequestedProduct>();
        private List<string> supplierNames = new List<string>();
        private bool isEditMode = false;
        private int editingRequestId = 0;
        private TextBox? activeModalTextBox = null;

        public RequestedProductsWindow()
        {
            InitializeComponent();

            LoadSuppliersList();
            LoadRequestedProducts();

            KeyboardLayoutHelper.BindToTextBox(txtSearch);
            KeyboardLayoutHelper.BindToTextBox(txtProductName);
            KeyboardLayoutHelper.BindToTextBox(txtBarcode);
            KeyboardLayoutHelper.BindToTextBox(txtCustomerName);
            KeyboardLayoutHelper.BindToTextBox(txtNotes);

            txtProductName.GotFocus += (s, e) => activeModalTextBox = txtProductName;
            txtBarcode.GotFocus += (s, e) => activeModalTextBox = txtBarcode;
            txtQuantity.GotFocus += (s, e) => activeModalTextBox = txtQuantity;
            txtCustomerName.GotFocus += (s, e) => activeModalTextBox = txtCustomerName;
            txtCustomerPhone.GotFocus += (s, e) => activeModalTextBox = txtCustomerPhone;
            txtEstimatedPrice.GotFocus += (s, e) => activeModalTextBox = txtEstimatedPrice;
            txtNotes.GotFocus += (s, e) => activeModalTextBox = txtNotes;
        }

        private void LoadSuppliersList()
        {
            try
            {
                supplierNames.Clear();
                cmbSupplier.Items.Clear();

                // Preserve the "كل الممونين" item in filter
                while (cmbSupplierFilter.Items.Count > 1)
                {
                    cmbSupplierFilter.Items.RemoveAt(1);
                }

                using (MySqlConnection conn = dbHelper.GetConnection())
                {
                    conn.Open();
                    string query = "SELECT DISTINCT FullName FROM Suppliers WHERE FullName IS NOT NULL AND FullName <> '' ORDER BY FullName";
                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    {
                        using (MySqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                string name = reader["FullName"]?.ToString() ?? "";
                                if (!string.IsNullOrWhiteSpace(name))
                                {
                                    supplierNames.Add(name);
                                    cmbSupplierFilter.Items.Add(new ComboBoxItem { Content = name });
                                    cmbSupplier.Items.Add(name);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error loading suppliers list: " + ex.Message);
            }
        }

        public void LoadRequestedProducts()
        {
            try
            {
                allRequests.Clear();
                using (MySqlConnection conn = dbHelper.GetConnection())
                {
                    conn.Open();
                    string query = @"
                        SELECT Id, ProductName, Barcode, Quantity, CustomerName, CustomerPhone, 
                               SupplierId, SupplierName, RequestDate, Status, Priority, Notes, EstimatedPrice 
                        FROM CustomerRequestedProducts 
                        ORDER BY Id DESC";

                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    {
                        using (MySqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                allRequests.Add(new RequestedProduct
                                {
                                    Id = Convert.ToInt32(reader["Id"]),
                                    ProductName = reader["ProductName"]?.ToString() ?? "",
                                    Barcode = reader["Barcode"]?.ToString() ?? "",
                                    Quantity = reader["Quantity"] != DBNull.Value ? Convert.ToDecimal(reader["Quantity"]) : 1m,
                                    CustomerName = reader["CustomerName"]?.ToString() ?? "",
                                    CustomerPhone = reader["CustomerPhone"]?.ToString() ?? "",
                                    SupplierId = reader["SupplierId"] != DBNull.Value ? Convert.ToInt32(reader["SupplierId"]) : (int?)null,
                                    SupplierName = reader["SupplierName"]?.ToString() ?? "",
                                    RequestDate = reader["RequestDate"] != DBNull.Value ? Convert.ToDateTime(reader["RequestDate"]) : DateTime.Now,
                                    Status = reader["Status"]?.ToString() ?? "قيد الانتظار",
                                    Priority = reader["Priority"]?.ToString() ?? "عادي",
                                    Notes = reader["Notes"]?.ToString() ?? "",
                                    EstimatedPrice = reader["EstimatedPrice"] != DBNull.Value ? Convert.ToDecimal(reader["EstimatedPrice"]) : 0m
                                });
                            }
                        }
                    }
                }

                UpdateKPIs();
                ApplyFilter();
            }
            catch (Exception ex)
            {
                MessageBox.Show("فشل تحميل قائمة السلع المطلوبة: " + ex.Message, "خطأ قاعدة بيانات", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateKPIs()
        {
            if (lblStatTotal == null || lblStatPending == null || lblStatOrdered == null || lblStatFulfilled == null) return;

            lblStatTotal.Text = allRequests.Count.ToString();
            lblStatPending.Text = allRequests.Count(r => r.Status == "قيد الانتظار").ToString();
            lblStatOrdered.Text = allRequests.Count(r => r.Status == "تم الطلب من الممون").ToString();
            lblStatFulfilled.Text = allRequests.Count(r => r.Status == "تم التوفير بالمخزن").ToString();
        }

        private void ApplyFilter()
        {
            if (allRequests == null || dgRequestedProducts == null) return;

            string searchText = txtSearch?.Text.Trim() ?? "";
            var filtered = allRequests.AsEnumerable();

            // فلتر الحالة
            if (cmbStatusFilter?.SelectedItem is ComboBoxItem statusItem)
            {
                string status = statusItem.Content.ToString() ?? "";
                if (status != "كل الحالات")
                {
                    filtered = filtered.Where(r => r.Status.Equals(status, StringComparison.OrdinalIgnoreCase));
                }
            }

            // فلتر الممون
            if (cmbSupplierFilter?.SelectedItem is ComboBoxItem supItem)
            {
                string sup = supItem.Content.ToString() ?? "";
                if (sup != "كل الممونين")
                {
                    filtered = filtered.Where(r => r.SupplierName.Equals(sup, StringComparison.OrdinalIgnoreCase));
                }
            }

            // فلتر الأولوية
            if (cmbPriorityFilter?.SelectedItem is ComboBoxItem priItem)
            {
                string pri = priItem.Content.ToString() ?? "";
                if (pri != "كل الأولويات")
                {
                    filtered = filtered.Where(r => r.Priority.Equals(pri, StringComparison.OrdinalIgnoreCase));
                }
            }

            // البحث بالنص
            if (!string.IsNullOrEmpty(searchText))
            {
                filtered = filtered.Where(r =>
                    r.ProductName.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                    r.Barcode.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                    r.CustomerName.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                    r.CustomerPhone.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                    r.SupplierName.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                    r.Notes.Contains(searchText, StringComparison.OrdinalIgnoreCase)
                );
            }

            dgRequestedProducts.ItemsSource = filtered.ToList();
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void Filter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            LoadSuppliersList();
            LoadRequestedProducts();
        }

        // ================= إضافة وتعديل =================
        public void OpenAddModalWithPreset(string productName = "", string barcode = "")
        {
            isEditMode = false;
            editingRequestId = 0;
            lblModalTitle.Text = "تسجيل سلعة مطلوبة من الزبون";

            txtProductName.Text = productName;
            txtBarcode.Text = barcode;
            txtQuantity.Text = "1";
            txtCustomerName.Text = "";
            txtCustomerPhone.Text = "";
            cmbSupplier.Text = "";
            txtEstimatedPrice.Text = "0.00";
            txtNotes.Text = "";

            SetComboBoxItem(cmbPriority, "عادي");
            SetComboBoxItem(cmbStatus, "قيد الانتظار");

            modalOverlay.Visibility = Visibility.Visible;
            txtProductName.Focus();
            if (!string.IsNullOrEmpty(productName))
            {
                txtQuantity.Focus();
                txtQuantity.SelectAll();
            }
        }

        private void BtnAddRequest_Click(object sender, RoutedEventArgs e)
        {
            OpenAddModalWithPreset();
        }

        private void BtnEditRequest_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { DataContext: RequestedProduct req })
            {
                isEditMode = true;
                editingRequestId = req.Id;
                lblModalTitle.Text = "تعديل طلب السلعة #" + req.Id;

                txtProductName.Text = req.ProductName;
                txtBarcode.Text = req.Barcode;
                txtQuantity.Text = req.Quantity.ToString("0.###");
                txtCustomerName.Text = req.CustomerName;
                txtCustomerPhone.Text = req.CustomerPhone;
                cmbSupplier.Text = req.SupplierName;
                txtEstimatedPrice.Text = req.EstimatedPrice.ToString("N2");
                txtNotes.Text = req.Notes;

                SetComboBoxItem(cmbPriority, req.Priority);
                SetComboBoxItem(cmbStatus, req.Status);

                modalOverlay.Visibility = Visibility.Visible;
                txtProductName.Focus();
            }
        }

        private void SetComboBoxItem(ComboBox combo, string value)
        {
            foreach (var item in combo.Items)
            {
                if (item is ComboBoxItem cbi && cbi.Content.ToString() == value)
                {
                    combo.SelectedItem = cbi;
                    return;
                }
            }
            if (combo.Items.Count > 0)
                combo.SelectedIndex = 0;
        }

        private void BtnSaveRequest_Click(object sender, RoutedEventArgs e)
        {
            string pName = txtProductName.Text.Trim();
            if (string.IsNullOrEmpty(pName))
            {
                MessageBox.Show("يرجى كتابة اسم السلعة أو المنتج المطلوب أولاً!", "حقل مطلوب", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtProductName.Focus();
                return;
            }

            if (!decimal.TryParse(txtQuantity.Text.Trim().Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal qty) || qty <= 0)
            {
                MessageBox.Show("يرجى إدخال كمية صحيحة أكبر من الصفر!", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtQuantity.Focus();
                return;
            }

            decimal.TryParse(txtEstimatedPrice.Text.Trim().Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal estPrice);

            string barcode = txtBarcode.Text.Trim();
            string custName = txtCustomerName.Text.Trim();
            string custPhone = txtCustomerPhone.Text.Trim();
            string supplierName = cmbSupplier.Text.Trim();
            string priority = (cmbPriority.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "عادي";
            string status = (cmbStatus.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "قيد الانتظار";
            string notes = txtNotes.Text.Trim();

            try
            {
                using (MySqlConnection conn = dbHelper.GetConnection())
                {
                    conn.Open();

                    if (!isEditMode)
                    {
                        string insertQuery = @"
                            INSERT INTO CustomerRequestedProducts 
                            (ProductName, Barcode, Quantity, CustomerName, CustomerPhone, SupplierName, RequestDate, Status, Priority, Notes, EstimatedPrice)
                            VALUES (@ProductName, @Barcode, @Quantity, @CustomerName, @CustomerPhone, @SupplierName, NOW(), @Status, @Priority, @Notes, @EstimatedPrice)";

                        using (MySqlCommand cmd = new MySqlCommand(insertQuery, conn))
                        {
                            cmd.Parameters.AddWithValue("@ProductName", pName);
                            cmd.Parameters.AddWithValue("@Barcode", string.IsNullOrEmpty(barcode) ? (object)DBNull.Value : barcode);
                            cmd.Parameters.AddWithValue("@Quantity", qty);
                            cmd.Parameters.AddWithValue("@CustomerName", string.IsNullOrEmpty(custName) ? (object)DBNull.Value : custName);
                            cmd.Parameters.AddWithValue("@CustomerPhone", string.IsNullOrEmpty(custPhone) ? (object)DBNull.Value : custPhone);
                            cmd.Parameters.AddWithValue("@SupplierName", string.IsNullOrEmpty(supplierName) ? (object)DBNull.Value : supplierName);
                            cmd.Parameters.AddWithValue("@Status", status);
                            cmd.Parameters.AddWithValue("@Priority", priority);
                            cmd.Parameters.AddWithValue("@Notes", string.IsNullOrEmpty(notes) ? (object)DBNull.Value : notes);
                            cmd.Parameters.AddWithValue("@EstimatedPrice", estPrice);
                            cmd.ExecuteNonQuery();
                        }
                    }
                    else
                    {
                        string updateQuery = @"
                            UPDATE CustomerRequestedProducts 
                            SET ProductName = @ProductName,
                                Barcode = @Barcode,
                                Quantity = @Quantity,
                                CustomerName = @CustomerName,
                                CustomerPhone = @CustomerPhone,
                                SupplierName = @SupplierName,
                                Status = @Status,
                                Priority = @Priority,
                                Notes = @Notes,
                                EstimatedPrice = @EstimatedPrice
                            WHERE Id = @Id";

                        using (MySqlCommand cmd = new MySqlCommand(updateQuery, conn))
                        {
                            cmd.Parameters.AddWithValue("@ProductName", pName);
                            cmd.Parameters.AddWithValue("@Barcode", string.IsNullOrEmpty(barcode) ? (object)DBNull.Value : barcode);
                            cmd.Parameters.AddWithValue("@Quantity", qty);
                            cmd.Parameters.AddWithValue("@CustomerName", string.IsNullOrEmpty(custName) ? (object)DBNull.Value : custName);
                            cmd.Parameters.AddWithValue("@CustomerPhone", string.IsNullOrEmpty(custPhone) ? (object)DBNull.Value : custPhone);
                            cmd.Parameters.AddWithValue("@SupplierName", string.IsNullOrEmpty(supplierName) ? (object)DBNull.Value : supplierName);
                            cmd.Parameters.AddWithValue("@Status", status);
                            cmd.Parameters.AddWithValue("@Priority", priority);
                            cmd.Parameters.AddWithValue("@Notes", string.IsNullOrEmpty(notes) ? (object)DBNull.Value : notes);
                            cmd.Parameters.AddWithValue("@EstimatedPrice", estPrice);
                            cmd.Parameters.AddWithValue("@Id", editingRequestId);
                            cmd.ExecuteNonQuery();
                        }
                    }
                }

                modalOverlay.Visibility = Visibility.Collapsed;
                LoadRequestedProducts();
                MessageBox.Show("تم حفظ بيانات الطلب بنجاح!", "نجاح العملية", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("خطأ أثناء حفظ الطلب: " + ex.Message, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnDeleteRequest_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { DataContext: RequestedProduct req })
            {
                var result = MessageBox.Show($"هل أنت متأكد من حذف طلب السلعة \"{req.ProductName}\"؟", "تأكيد الحذف", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        using (MySqlConnection conn = dbHelper.GetConnection())
                        {
                            conn.Open();
                            string query = "DELETE FROM CustomerRequestedProducts WHERE Id = @Id";
                            using (MySqlCommand cmd = new MySqlCommand(query, conn))
                            {
                                cmd.Parameters.AddWithValue("@Id", req.Id);
                                cmd.ExecuteNonQuery();
                            }
                        }

                        LoadRequestedProducts();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("خطأ أثناء حذف الطلب: " + ex.Message, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        // إجراء التحديث السريع للحالة
        private void BtnQuickStatus_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { DataContext: RequestedProduct req })
            {
                string nextStatus = "";
                string message = "";

                if (req.Status == "قيد الانتظار")
                {
                    nextStatus = "تم الطلب من الممون";
                    message = $"هل تود تحويل حالة السلعة \"{req.ProductName}\" إلى [تم الطلب من الممون]؟";
                }
                else if (req.Status == "تم الطلب من الممون")
                {
                    nextStatus = "تم التوفير بالمخزن";
                    message = $"هل تم استلام السلعة وتوفيرها بالمخزن؟ تحويل الحالة إلى [تم التوفير بالمخزن]؟";
                }
                else
                {
                    nextStatus = "قيد الانتظار";
                    message = $"هل تود إعادة حالة السلعة \"{req.ProductName}\" إلى [قيد الانتظار]؟";
                }

                var res = MessageBox.Show(message, "تحديث الحالة السريع", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (res == MessageBoxResult.Yes)
                {
                    try
                    {
                        using (MySqlConnection conn = dbHelper.GetConnection())
                        {
                            conn.Open();
                            string query = "UPDATE CustomerRequestedProducts SET Status = @Status WHERE Id = @Id";
                            using (MySqlCommand cmd = new MySqlCommand(query, conn))
                            {
                                cmd.Parameters.AddWithValue("@Status", nextStatus);
                                cmd.Parameters.AddWithValue("@Id", req.Id);
                                cmd.ExecuteNonQuery();
                            }
                        }

                        LoadRequestedProducts();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("خطأ أثناء تحديث الحالة: " + ex.Message, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void BtnCancelModal_Click(object sender, RoutedEventArgs e)
        {
            modalOverlay.Visibility = Visibility.Collapsed;
        }

        // ================= لوحة المفاتيح الافتراضية =================
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

        private TextBox GetActiveModalTextBox()
        {
            IInputElement focused = FocusManager.GetFocusedElement(this);
            if (focused is TextBox tb && (tb == txtProductName || tb == txtBarcode || tb == txtQuantity || tb == txtCustomerName || tb == txtCustomerPhone || tb == txtEstimatedPrice || tb == txtNotes))
            {
                activeModalTextBox = tb;
            }
            return activeModalTextBox ?? txtQuantity;
        }

        private void BtnModalNum_Click(object sender, RoutedEventArgs e)
        {
            var targetBox = GetActiveModalTextBox();
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
                    if (targetBox == txtQuantity) targetBox.Text = "1";
                    else if (targetBox == txtEstimatedPrice) targetBox.Text = "0.00";
                    else targetBox.Text = "";
                    targetBox.SelectionStart = targetBox.Text.Length;
                }
                else if (val == "." || val == ",")
                {
                    if (!targetBox.Text.Contains("."))
                    {
                        int caret = targetBox.SelectionStart;
                        targetBox.Text = targetBox.Text.Insert(caret, ".");
                        targetBox.SelectionStart = caret + 1;
                    }
                }
                else
                {
                    if (targetBox == txtQuantity && (targetBox.Text == "0" || targetBox.Text == "1"))
                    {
                        targetBox.Text = val;
                        targetBox.SelectionStart = targetBox.Text.Length;
                    }
                    else if (targetBox == txtEstimatedPrice && (targetBox.Text == "0.00" || targetBox.Text == "0"))
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

        // ================= تصدير Excel =================
        private void BtnExportExcel_Click(object sender, RoutedEventArgs e)
        {
            var itemsToExport = (dgRequestedProducts.ItemsSource as IEnumerable<RequestedProduct>)?.ToList() ?? allRequests;
            if (itemsToExport.Count == 0)
            {
                MessageBox.Show("لا توجد بيانات لتصديرها!", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var sfd = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "Excel Workbook (*.xlsx)|*.xlsx",
                    FileName = $"لائحة_السلع_المطلوبة_للزبائن_{DateTime.Now:yyyyMMdd_HHmm}.xlsx",
                    DefaultExt = ".xlsx",
                    Title = "حفظ وتصدير لائحة السلع المطلوبة"
                };

                if (sfd.ShowDialog() == true)
                {
                    using (var workbook = new XLWorkbook())
                    {
                        var worksheet = workbook.Worksheets.Add("السلع المطلوبة");
                        worksheet.RightToLeft = true;

                        // الصف 1: عنوان رئيسي
                        worksheet.Cell(1, 1).Value = "متجر LMIMA SHOP - لائحة السلع والمنتجات المطلوبة من الزبائن (طلبيات النواقص)";
                        var titleRange = worksheet.Range(1, 1, 1, 11);
                        titleRange.Merge();
                        titleRange.Style.Font.Bold = true;
                        titleRange.Style.Font.FontSize = 14;
                        titleRange.Style.Font.FontColor = XLColor.White;
                        titleRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#1E3A8A");
                        titleRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        worksheet.Row(1).Height = 32;

                        // الصف 2: معلومات التصدير
                        worksheet.Cell(2, 1).Value = $"تاريخ التصدير: {DateTime.Now:yyyy-MM-dd HH:mm} | عدد العناصر: {itemsToExport.Count}";
                        var subRange = worksheet.Range(2, 1, 2, 11);
                        subRange.Merge();
                        subRange.Style.Font.Italic = true;
                        subRange.Style.Font.FontSize = 10;
                        subRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#F1F5F9");
                        subRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        worksheet.Row(2).Height = 22;

                        // الصف 3: الهيدر
                        string[] headers = new string[]
                        {
                            "الرقم",
                            "اسم السلعة المطلوبة",
                            "الباركود",
                            "الكمية المطلوبة",
                            "السعر التقديري (د.م.)",
                            "اسم الزبون",
                            "هاتف الزبون",
                            "الممون المقترح",
                            "درجة الأولوية",
                            "حالة الطلب",
                            "تاريخ الطلب",
                            "ملاحظات"
                        };

                        for (int col = 0; col < headers.Length; col++)
                        {
                            worksheet.Cell(3, col + 1).Value = headers[col];
                        }

                        var headerRange = worksheet.Range(3, 1, 3, headers.Length);
                        headerRange.Style.Font.Bold = true;
                        headerRange.Style.Font.FontSize = 11;
                        headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#2980B9");
                        headerRange.Style.Font.FontColor = XLColor.White;
                        headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        worksheet.Row(3).Height = 26;

                        // الصفوف
                        int row = 4;
                        for (int i = 0; i < itemsToExport.Count; i++)
                        {
                            var item = itemsToExport[i];
                            worksheet.Cell(row, 1).Value = i + 1;
                            worksheet.Cell(row, 2).Value = item.ProductName;
                            worksheet.Cell(row, 3).Value = item.Barcode;
                            worksheet.Cell(row, 4).Value = item.Quantity;
                            worksheet.Cell(row, 5).Value = item.EstimatedPrice;
                            worksheet.Cell(row, 6).Value = item.CustomerName;
                            worksheet.Cell(row, 7).Value = item.CustomerPhone;
                            worksheet.Cell(row, 8).Value = item.SupplierName;
                            worksheet.Cell(row, 9).Value = item.Priority;
                            worksheet.Cell(row, 10).Value = item.Status;
                            worksheet.Cell(row, 11).Value = item.FormattedDate;
                            worksheet.Cell(row, 12).Value = item.Notes;

                            // تنسيق الألوان الخفيفة حسب الحالة
                            if (item.Status == "تم التوفير بالمخزن")
                                worksheet.Row(row).Style.Fill.BackgroundColor = XLColor.FromHtml("#F0FDF4");
                            else if (item.Status == "تم الطلب من الممون")
                                worksheet.Row(row).Style.Fill.BackgroundColor = XLColor.FromHtml("#EFF6FF");
                            else if (item.Priority == "عاجل")
                                worksheet.Row(row).Style.Fill.BackgroundColor = XLColor.FromHtml("#FEF2F2");

                            worksheet.Row(row).Height = 22;
                            row++;
                        }

                        // تذييل وإطارات
                        var tableRange = worksheet.Range(3, 1, row - 1, headers.Length);
                        tableRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                        tableRange.Style.Border.InsideBorderColor = XLColor.FromHtml("#CBD5E1");
                        tableRange.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
                        tableRange.Style.Border.OutsideBorderColor = XLColor.FromHtml("#94A3B8");

                        // محاذاة الأعمدة
                        worksheet.Column(1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        worksheet.Column(4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        worksheet.Column(5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                        worksheet.Column(7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        worksheet.Column(9).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        worksheet.Column(10).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        worksheet.Column(11).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                        worksheet.Columns().AdjustToContents();

                        workbook.SaveAs(sfd.FileName);

                        var askOpen = MessageBox.Show("تم تصدير ملف الإكسيل بنجاح!\nهل تود فتح الملف الآن؟", "تم التصدير", MessageBoxButton.YesNo, MessageBoxImage.Information);
                        if (askOpen == MessageBoxResult.Yes)
                        {
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = sfd.FileName,
                                UseShellExecute = true
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("خطأ أثناء تصدير ملف الإكسيل: " + ex.Message, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ================= طباعة اللائحة / وثيقة طلبيات =================
        private void BtnPrintList_Click(object sender, RoutedEventArgs e)
        {
            var itemsToPrint = (dgRequestedProducts.ItemsSource as IEnumerable<RequestedProduct>)?.ToList() ?? allRequests;
            if (itemsToPrint.Count == 0)
            {
                MessageBox.Show("لا توجد بيانات لطباعتها!", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                string html = GeneratePrintHtml(itemsToPrint);
                string tempFile = Path.Combine(Path.GetTempPath(), $"Bon_Commande_Nawaqis_{DateTime.Now:yyyyMMdd_HHmmss}.html");
                File.WriteAllText(tempFile, html, Encoding.UTF8);

                Process.Start(new ProcessStartInfo
                {
                    FileName = tempFile,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("خطأ أثناء تجهيز الطباعة: " + ex.Message, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string GeneratePrintHtml(List<RequestedProduct> items)
        {
            // قراءة اسم المتجر وعنوانه
            string storeName = "متجر LMIMA SHOP";
            string storePhone = "";
            string storeAddress = "";

            try
            {
                string settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
                if (File.Exists(settingsPath))
                {
                    string json = File.ReadAllText(settingsPath);
                    using (JsonDocument doc = JsonDocument.Parse(json))
                    {
                        var root = doc.RootElement;
                        if (root.TryGetProperty("StoreName", out var sn) && !string.IsNullOrWhiteSpace(sn.GetString()))
                            storeName = sn.GetString()!;
                        if (root.TryGetProperty("StorePhone", out var sp))
                            storePhone = sp.GetString() ?? "";
                        if (root.TryGetProperty("StoreAddress", out var sa))
                            storeAddress = sa.GetString() ?? "";
                    }
                }
            }
            catch { }

            string selectedSupplier = (cmbSupplierFilter?.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "كل الممونين";
            string selectedStatus = (cmbStatusFilter?.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "كل الحالات";

            StringBuilder rowsBuilder = new StringBuilder();
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                string priorityBadge = it.Priority == "عاجل" 
                    ? "<span style='background:#fee2e2; color:#dc2626; padding:2px 6px; border-radius:4px; font-weight:bold;'>عاجل</span>"
                    : (it.Priority == "مهم" ? "<span style='background:#ffedd5; color:#c2410c; padding:2px 6px; border-radius:4px;'>مهم</span>" : "عادي");

                string statusBadge = it.Status == "قيد الانتظار"
                    ? "<span style='color:#b45309; font-weight:bold;'>⏳ قيد الانتظار</span>"
                    : (it.Status == "تم الطلب من الممون" ? "<span style='color:#1d4ed8; font-weight:bold;'>🚚 تم الطلب</span>" : "<span style='color:#047857; font-weight:bold;'>✅ متوفر</span>");

                rowsBuilder.Append($@"
                <tr>
                    <td style='text-align:center;'>{i + 1}</td>
                    <td style='font-weight:bold; font-size:13px;'>{System.Net.WebUtility.HtmlEncode(it.ProductName)}</td>
                    <td style='text-align:center; font-family:monospace; color:#475569;'>{System.Net.WebUtility.HtmlEncode(it.Barcode)}</td>
                    <td style='text-align:center; font-weight:bold; font-size:14px; color:#1e3a8a;'>{it.Quantity:0.###}</td>
                    <td style='text-align:center;'>{System.Net.WebUtility.HtmlEncode(it.SupplierName)}</td>
                    <td style='text-align:center;'>{priorityBadge}</td>
                    <td style='text-align:center;'>{statusBadge}</td>
                    <td>{System.Net.WebUtility.HtmlEncode(it.CustomerName)} {(!string.IsNullOrEmpty(it.CustomerPhone) ? $"<br><small dir='ltr' style='color:#0284c7;'>{System.Net.WebUtility.HtmlEncode(it.CustomerPhone)}</small>" : "")}</td>
                    <td style='font-size:11px; color:#475569;'>{System.Net.WebUtility.HtmlEncode(it.Notes)}</td>
                </tr>");
            }

            return $@"<!DOCTYPE html>
<html lang='ar' dir='rtl'>
<head>
  <meta charset='UTF-8'>
  <title>لائحة السلع والطلبيات المطلوبة من الزبائن للممونين</title>
  <style>
    * {{ box-sizing: border-box; margin: 0; padding: 0; }}
    body {{ font-family: 'Segoe UI', Tahoma, Arial, sans-serif; background: #f8fafc; color: #1e293b; padding: 25px; direction: rtl; }}
    .container {{ max-width: 950px; margin: 0 auto; background: white; border: 1px solid #cbd5e1; border-radius: 10px; padding: 25px 30px; box-shadow: 0 4px 15px rgba(0,0,0,0.05); }}
    .header {{ display: flex; justify-content: space-between; align-items: flex-start; margin-bottom: 12px; }}
    .store-name {{ font-size: 22px; font-weight: 800; color: #1e3a8a; }}
    .store-sub {{ font-size: 12px; color: #64748b; margin-top: 4px; }}
    .badge {{ background: #1e3a8a; color: white; padding: 5px 12px; border-radius: 6px; font-size: 13px; font-weight: bold; text-align: center; }}
    .date-text {{ font-size: 11px; color: #64748b; margin-top: 5px; text-align: left; }}
    .divider {{ height: 2px; background: linear-gradient(90deg, #1e3a8a, #3b82f6); margin: 12px 0 16px 0; border-radius: 2px; }}
    .info-card {{ display: flex; justify-content: space-between; background: #f1f5f9; border: 1px dashed #cbd5e1; border-radius: 8px; padding: 10px 16px; margin-bottom: 16px; font-size: 13px; }}
    table {{ width: 100%; border-collapse: collapse; font-size: 12px; margin-bottom: 20px; }}
    th {{ background: #f1f5f9; color: #334155; font-weight: bold; padding: 9px 8px; border: 1px solid #cbd5e1; text-align: right; }}
    td {{ padding: 8px 8px; border: 1px solid #e2e8f0; }}
    tr:nth-child(even) {{ background: #fcfcfc; }}
    tfoot td {{ background: #f8fafc; font-weight: bold; border-top: 2px solid #cbd5e1; }}
    .signatures {{ display: flex; justify-content: space-between; margin-top: 35px; padding-top: 15px; border-top: 1px dashed #cbd5e1; }}
    .sign-box {{ width: 45%; text-align: center; font-size: 13px; color: #475569; }}
    .sign-line {{ height: 50px; border-bottom: 1px solid #94a3b8; margin-top: 10px; }}
    .print-actions {{ text-align: center; margin-bottom: 18px; }}
    .btn-print {{ background: #1e3a8a; color: white; border: none; padding: 10px 24px; border-radius: 6px; font-size: 14px; font-weight: bold; cursor: pointer; }}
    @media print {{
      .print-actions {{ display: none; }}
      body {{ padding: 0; background: white; }}
      .container {{ border: none; box-shadow: none; padding: 0; max-width: 100%; }}
    }}
  </style>
</head>
<body>
  <div class='print-actions'>
    <button class='btn-print' onclick='window.print()'>🖨️ طباعة اللائحة أو حفظ كـ PDF</button>
  </div>
  <div class='container'>
    <div class='header'>
      <div>
        <div class='store-name'>{System.Net.WebUtility.HtmlEncode(storeName)}</div>
        <div class='store-sub'>{System.Net.WebUtility.HtmlEncode(storeAddress)} {(!string.IsNullOrEmpty(storePhone) ? " • هاتف: " + storePhone : "")}</div>
      </div>
      <div>
        <div class='badge'>بون طلبيات ونواقص السلع</div>
        <div class='date-text'>التاريخ: {DateTime.Now:yyyy-MM-dd HH:mm}</div>
      </div>
    </div>
    <div class='divider'></div>

    <div class='info-card'>
      <div>الممون المستهدف: <strong>{System.Net.WebUtility.HtmlEncode(selectedSupplier)}</strong></div>
      <div>الحالة: <strong>{System.Net.WebUtility.HtmlEncode(selectedStatus)}</strong></div>
      <div>إجمالي السلع المطلوبة: <strong>{items.Count} صنف</strong></div>
    </div>

    <table>
      <thead>
        <tr>
          <th style='width:35px; text-align:center;'>#</th>
          <th>اسم السلعة المطلوبة</th>
          <th style='width:90px; text-align:center;'>الباركود</th>
          <th style='width:60px; text-align:center;'>الكمية</th>
          <th style='width:120px; text-align:center;'>الممون المقترح</th>
          <th style='width:65px; text-align:center;'>الأولوية</th>
          <th style='width:95px; text-align:center;'>الحالة</th>
          <th style='width:130px;'>الزبون الطالب</th>
          <th style='width:130px;'>ملاحظات</th>
        </tr>
      </thead>
      <tbody>
        {rowsBuilder}
      </tbody>
      <tfoot>
        <tr>
          <td colspan='3' style='text-align:right;'>مجموع الأصناف:</td>
          <td style='text-align:center; color:#1e3a8a;'>{items.Sum(x => x.Quantity):0.###}</td>
          <td colspan='5' style='text-align:left; color:#64748b;'>تاريخ إعداد الوثيقة: {DateTime.Now:yyyy-MM-dd HH:mm}</td>
        </tr>
      </tfoot>
    </table>

    <div class='signatures'>
      <div class='sign-box'>
        <span>توقيع وختم المتجر:</span>
        <div class='sign-line'></div>
      </div>
      <div class='sign-box'>
        <span>استلام وتوقيع الممون:</span>
        <div class='sign-line'></div>
      </div>
    </div>
  </div>
</body>
</html>";
        }
    }
}
