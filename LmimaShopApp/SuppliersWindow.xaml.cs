using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MySqlConnector;

namespace LmimaShopApp
{
    public partial class SuppliersWindow : UserControl
    {
        private readonly DatabaseHelper dbHelper = new DatabaseHelper();
        private List<SupplierModel> allSuppliers = new List<SupplierModel>();
        private bool isEditMode = false;
        private int editingSupplierId = 0;

        public SuppliersWindow()
        {
            InitializeComponent();
            LoadSuppliers();
            KeyboardLayoutHelper.BindToTextBox(txtSearch);
            KeyboardLayoutHelper.BindToTextBox(txtSupplierName);
            KeyboardLayoutHelper.BindToTextBox(txtCompany);
            KeyboardLayoutHelper.BindToTextBox(txtAddress);

            txtSupplierName.GotFocus += (s, e) => activeSupplierModalTextBox = txtSupplierName;
            txtCompany.GotFocus += (s, e) => activeSupplierModalTextBox = txtCompany;
            txtPhone.GotFocus += (s, e) => activeSupplierModalTextBox = txtPhone;
            txtEmail.GotFocus += (s, e) => activeSupplierModalTextBox = txtEmail;
            txtAddress.GotFocus += (s, e) => activeSupplierModalTextBox = txtAddress;
            txtDueBalance.GotFocus += (s, e) => activeSupplierModalTextBox = txtDueBalance;
        }

        private void LoadSuppliers()
        {
            try
            {
                allSuppliers.Clear();
                using (MySqlConnection conn = dbHelper.GetConnection())
                {
                    conn.Open();
                    string query = "SELECT Id, FullName, Phone, Email, Address, Company, DueBalance FROM Suppliers ORDER BY Id DESC";
                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    {
                        using (MySqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                allSuppliers.Add(new SupplierModel
                                {
                                    Id = Convert.ToInt32(reader["Id"]),
                                    FullName = reader["FullName"].ToString() ?? "",
                                    Phone = reader["Phone"].ToString() ?? "",
                                    Email = reader["Email"].ToString() ?? "",
                                    Address = reader["Address"].ToString() ?? "",
                                    Company = reader["Company"].ToString() ?? "",
                                    DueBalance = Convert.ToDecimal(reader["DueBalance"])
                                });
                            }
                        }
                    }
                }

                ApplyFilter();
            }
            catch (Exception ex)
            {
                MessageBox.Show("فشل تحميل الممونين: " + ex.Message, "خطأ قاعدة بيانات", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplyFilter()
        {
            if (allSuppliers == null || txtSearch == null || cmbBalance == null || dgSuppliers == null) return;

            string searchText = txtSearch.Text.Trim();
            var filtered = allSuppliers.AsEnumerable();

            // فلترة الرصيد/الديون
            if (cmbBalance.SelectedItem is ComboBoxItem balanceItem)
            {
                string bal = balanceItem.Content.ToString() ?? "";
                if (bal == "علينا ديون لهم")
                {
                    filtered = filtered.Where(s => s.DueBalance > 0);
                }
                else if (bal == "بلا ديون")
                {
                    filtered = filtered.Where(s => s.DueBalance == 0);
                }
            }

            // فلترة نص البحث
            if (!string.IsNullOrEmpty(searchText))
            {
                filtered = filtered.Where(s =>
                    s.FullName.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                    s.Phone.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                    s.Company.Contains(searchText, StringComparison.OrdinalIgnoreCase)
                );
            }

            dgSuppliers.ItemsSource = null;
            dgSuppliers.ItemsSource = filtered.ToList();
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void Filter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void BtnAddSupplier_Click(object sender, RoutedEventArgs e)
        {
            isEditMode = false;
            editingSupplierId = 0;
            lblModalTitle.Text = "إضافة ممون جديد";
            
            txtSupplierName.Clear();
            txtCompany.Clear();
            txtPhone.Text = "-";
            txtEmail.Clear();
            txtAddress.Clear();
            txtDueBalance.Text = "0.00";

            modalOverlay.Visibility = Visibility.Visible;
            activeSupplierModalTextBox = txtSupplierName;
            txtSupplierName.Focus();
        }

        private void BtnEditSupplier_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is SupplierModel supplier)
            {
                isEditMode = true;
                editingSupplierId = supplier.Id;
                lblModalTitle.Text = "تعديل بيانات الممون";

                txtSupplierName.Text = supplier.FullName;
                txtCompany.Text = supplier.Company;
                txtPhone.Text = supplier.Phone;
                txtEmail.Text = supplier.Email;
                txtAddress.Text = supplier.Address;
                txtDueBalance.Text = supplier.DueBalance.ToString("0.00");

                modalOverlay.Visibility = Visibility.Visible;
                activeSupplierModalTextBox = txtSupplierName;
                txtSupplierName.Focus();
                txtSupplierName.SelectAll();
            }
        }

        private void BtnDeleteSupplier_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is SupplierModel supplier)
            {
                MessageBoxResult result = MessageBox.Show($"هل تود حذف الممون '{supplier.FullName}' نهائياً؟", "تأكيد الحذف", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes) return;

                using (MySqlConnection conn = dbHelper.GetConnection())
                {
                    try
                    {
                        conn.Open();
                        string query = "DELETE FROM Suppliers WHERE Id = @id";
                        using (MySqlCommand cmd = new MySqlCommand(query, conn))
                        {
                            cmd.Parameters.AddWithValue("@id", supplier.Id);
                            cmd.ExecuteNonQuery();
                        }

                        MessageBox.Show("تم حذف الممون بنجاح!", "تم العملية", MessageBoxButton.OK, MessageBoxImage.Information);
                        LoadSuppliers();
                        _ = CloudSyncService.SyncMobileDataAsync(true);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("فشل حذف الممون: " + ex.Message, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void BtnDetailsSupplier_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is SupplierModel supplier)
            {
                string info = $"سجل الممون:\n\n" +
                              $"الاسم الكامل: {supplier.FullName}\n" +
                              $"الشركة / العلامة: {supplier.Company}\n" +
                              $"الهاتف: {supplier.Phone}\n" +
                              $"البريد الإلكتروني: {supplier.Email}\n" +
                              $"العنوان: {supplier.Address}\n" +
                              $"الديون المستحقة له: {supplier.DueBalance:N2} د.م.";
                
                MessageBox.Show(info, "بيانات وتفاصيل الممون", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnSaveSupplier_Click(object sender, RoutedEventArgs e)
        {
            string name = txtSupplierName.Text.Trim();
            string company = txtCompany.Text.Trim();
            string phone = txtPhone.Text.Trim();
            string email = txtEmail.Text.Trim();
            string address = txtAddress.Text.Trim();
            
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("الرجاء إدخال اسم الممون!", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!decimal.TryParse(txtDueBalance.Text, out decimal dueBalance) || dueBalance < 0)
            {
                MessageBox.Show("الرجاء إدخال ديون مستحقة صحيحة وموجبة!", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            using (MySqlConnection conn = dbHelper.GetConnection())
            {
                try
                {
                    conn.Open();
                    string query;
                    if (isEditMode)
                    {
                        query = "UPDATE Suppliers SET FullName = @name, Company = @company, Phone = @phone, Email = @email, Address = @address, DueBalance = @balance WHERE Id = @id";
                    }
                    else
                    {
                        query = "INSERT INTO Suppliers (FullName, Company, Phone, Email, Address, DueBalance) VALUES (@name, @company, @phone, @email, @address, @balance)";
                    }

                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@name", name);
                        cmd.Parameters.AddWithValue("@company", company);
                        cmd.Parameters.AddWithValue("@phone", phone);
                        cmd.Parameters.AddWithValue("@email", email);
                        cmd.Parameters.AddWithValue("@address", address);
                        cmd.Parameters.AddWithValue("@balance", dueBalance);
                        if (isEditMode)
                        {
                            cmd.Parameters.AddWithValue("@id", editingSupplierId);
                        }

                        cmd.ExecuteNonQuery();
                    }

                    MessageBox.Show("تم حفظ بيانات الممون بنجاح!", "تم العملية", MessageBoxButton.OK, MessageBoxImage.Information);
                    modalOverlay.Visibility = Visibility.Collapsed;
                    LoadSuppliers();
                    _ = CloudSyncService.SyncMobileDataAsync(true);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("فشل حفظ الممون (ربما الاسم مسجل مسبقاً): " + ex.Message, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private TextBox? activeSupplierModalTextBox = null;

        private TextBox GetActiveSupplierModalTextBox()
        {
            var focused = System.Windows.Input.FocusManager.GetFocusedElement(Window.GetWindow(this)) as TextBox;
            if (focused != null && (focused == txtSupplierName || focused == txtCompany || focused == txtPhone || focused == txtEmail || focused == txtAddress || focused == txtDueBalance))
            {
                activeSupplierModalTextBox = focused;
            }
            return activeSupplierModalTextBox ?? txtPhone;
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

        private void SupplierModalKeypad_Click(object sender, RoutedEventArgs e)
        {
            var targetBox = GetActiveSupplierModalTextBox();
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
                    else if (targetBox == txtDueBalance) targetBox.Text = "0.00";
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
                    if (targetBox == txtDueBalance || targetBox == txtSupplierName)
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
                    else if (targetBox == txtDueBalance && (targetBox.Text == "0.00" || targetBox.Text == "0"))
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

        private void BtnCancelSupplier_Click(object sender, RoutedEventArgs e)
        {
            modalOverlay.Visibility = Visibility.Collapsed;
        }
    }

    public class SupplierModel
    {
        public int Id { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Company { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public decimal DueBalance { get; set; }
    }
}
