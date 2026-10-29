using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MySqlConnector;

namespace LmimaShopApp
{
    public partial class UsersWindow : UserControl
    {
        private readonly DatabaseHelper dbHelper = new DatabaseHelper();
        private List<UserModel> allUsers = new List<UserModel>();
        private bool isEditMode = false;
        private int editingUserId = 0;

        public UsersWindow()
        {
            InitializeComponent();
            LoadUsers();
        }

        private void LoadUsers()
        {
            try
            {
                allUsers.Clear();
                using (MySqlConnection conn = dbHelper.GetConnection())
                {
                    conn.Open();
                    string query = "SELECT UserID, FullName, Username, Password, Role FROM Users ORDER BY UserID DESC";
                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    {
                        using (MySqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                allUsers.Add(new UserModel
                                {
                                    UserID = Convert.ToInt32(reader["UserID"]),
                                    FullName = reader["FullName"].ToString() ?? "",
                                    Username = reader["Username"].ToString() ?? "",
                                    Password = reader["Password"].ToString() ?? "",
                                    Role = reader["Role"].ToString() ?? ""
                                });
                            }
                        }
                    }
                }

                dgUsers.ItemsSource = null;
                dgUsers.ItemsSource = allUsers;
            }
            catch (Exception ex)
            {
                MessageBox.Show("فشل تحميل المستخدمين: " + ex.Message, "خطأ قاعدة بيانات", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnAddUser_Click(object sender, RoutedEventArgs e)
        {
            isEditMode = false;
            editingUserId = 0;
            lblModalTitle.Text = "إضافة مستخدم جديد";

            txtFullName.Clear();
            txtUsername.Clear();
            txtPassword.Clear();
            cmbRole.SelectedIndex = 0;

            modalOverlay.Visibility = Visibility.Visible;
        }

        private void BtnEditUser_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is UserModel user)
            {
                isEditMode = true;
                editingUserId = user.UserID;
                lblModalTitle.Text = "تعديل بيانات المستخدم";

                txtFullName.Text = user.FullName;
                txtUsername.Text = user.Username;
                txtPassword.Text = ""; // لا نعرض كلمة المرور المشفرة
                txtPassword.Tag = user.Password; // نحفظ الهاش القديم هنا مؤقتاً
                
                // Role selection
                if (user.Role == "مدير" || user.Role == "Admin") cmbRole.SelectedIndex = 0;
                else cmbRole.SelectedIndex = 1;

                modalOverlay.Visibility = Visibility.Visible;
            }
        }

        private void BtnDeleteUser_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is UserModel user)
            {
                if (user.Username.Equals("admin", StringComparison.OrdinalIgnoreCase)) return;

                MessageBoxResult result = MessageBox.Show($"هل تود حذف المستخدم '{user.FullName}' نهائياً؟", "تأكيد الحذف", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes) return;

                using (MySqlConnection conn = dbHelper.GetConnection())
                {
                    try
                    {
                        conn.Open();
                        string query = "DELETE FROM Users WHERE UserID = @id";
                        using (MySqlCommand cmd = new MySqlCommand(query, conn))
                        {
                            cmd.Parameters.AddWithValue("@id", user.UserID);
                            cmd.ExecuteNonQuery();
                        }

                        DatabaseHelper.LogAudit("حذف مستخدم", "Users", user.UserID.ToString(), user.Username, "");

                        MessageBox.Show("تم حذف المستخدم بنجاح!", "تم العملية", MessageBoxButton.OK, MessageBoxImage.Information);
                        LoadUsers();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("فشل حذف المستخدم: " + ex.Message, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void BtnUserStats_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is UserModel user)
            {
                int invoicesCount = 0;
                decimal totalSales = 0;

                try
                {
                    using (MySqlConnection conn = dbHelper.GetConnection())
                    {
                        conn.Open();
                        string query = "SELECT COUNT(*), IFNULL(SUM(TotalAmount), 0) FROM Sales WHERE CashierName LIKE @name";
                        using (MySqlCommand cmd = new MySqlCommand(query, conn))
                        {
                            cmd.Parameters.AddWithValue("@name", $"{user.FullName}%");
                            using (MySqlDataReader reader = cmd.ExecuteReader())
                            {
                                if (reader.Read())
                                {
                                    invoicesCount = Convert.ToInt32(reader[0]);
                                    totalSales = Convert.ToDecimal(reader[1]);
                                }
                            }
                        }
                    }
                }
                catch { }

                string info = $"إحصائيات المستخدم الكاشير:\n\nالاسم الكامل: {user.FullName}\nاسم الدخول: {user.Username}\nالدور: {user.Role}\n\nعدد الفواتير المنشأة: {invoicesCount}\nإجمالي قيمة المبيعات: {totalSales:N2} د.م.";
                MessageBox.Show(info, "إحصائيات الموظف", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnSaveUser_Click(object sender, RoutedEventArgs e)
        {
            string fullName = txtFullName.Text.Trim();
            string username = txtUsername.Text.Trim();
            string passwordInput = txtPassword.Text.Trim();
            string role = cmbRole.SelectedItem is ComboBoxItem item ? item.Content.ToString() ?? "كاشير" : "كاشير";

            if (string.IsNullOrEmpty(fullName) || string.IsNullOrEmpty(username))
            {
                MessageBox.Show("الرجاء إدخال اسم المستخدم والاسم الكامل!", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!isEditMode && string.IsNullOrEmpty(passwordInput))
            {
                MessageBox.Show("كلمة المرور مطلوبة للمستخدم الجديد!", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            using (MySqlConnection conn = dbHelper.GetConnection())
            {
                try
                {
                    conn.Open();
                    string query;
                    string finalPasswordHash = passwordInput;
                    
                    if (isEditMode)
                    {
                        if (!string.IsNullOrEmpty(passwordInput))
                        {
                            finalPasswordHash = BCrypt.Net.BCrypt.HashPassword(passwordInput);
                        }
                        else
                        {
                            finalPasswordHash = txtPassword.Tag?.ToString() ?? ""; // Use old hash if no new password
                        }
                        
                        query = "UPDATE Users SET FullName = @fullname, Username = @username, Password = @password, Role = @role WHERE UserID = @id";
                    }
                    else
                    {
                        finalPasswordHash = BCrypt.Net.BCrypt.HashPassword(passwordInput);
                        query = "INSERT INTO Users (FullName, Username, Password, Role) VALUES (@fullname, @username, @password, @role)";
                    }

                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@fullname", fullName);
                        cmd.Parameters.AddWithValue("@username", username);
                        cmd.Parameters.AddWithValue("@password", finalPasswordHash);
                        cmd.Parameters.AddWithValue("@role", role);
                        if (isEditMode)
                        {
                            cmd.Parameters.AddWithValue("@id", editingUserId);
                        }

                        cmd.ExecuteNonQuery();
                        
                        string action = isEditMode ? "تعديل مستخدم" : "إضافة مستخدم";
                        string entityId = isEditMode ? editingUserId.ToString() : "جديد";
                        DatabaseHelper.LogAudit(action, "Users", entityId, "", $"Username: {username}, Role: {role}");
                    }

                    MessageBox.Show("تم حفظ بيانات المستخدم بنجاح!", "تم العملية", MessageBoxButton.OK, MessageBoxImage.Information);
                    modalOverlay.Visibility = Visibility.Collapsed;
                    LoadUsers();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("فشل حفظ المستخدم (ربما اسم المستخدم مسجل مسبقاً): " + ex.Message, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnCancelUser_Click(object sender, RoutedEventArgs e)
        {
            modalOverlay.Visibility = Visibility.Collapsed;
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

        // Redundant sidebar methods removed since managed by MainWindow container
    }

    public class UserModel
    {
        public int UserID { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
    }
}
