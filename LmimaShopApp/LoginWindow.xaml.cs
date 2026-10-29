using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using MySqlConnector;

namespace LmimaShopApp
{
    public partial class LoginWindow : Window
    {
        // استدعاء فئة الاتصال التي أنشأناها سابقاً
        DatabaseHelper dbHelper = new DatabaseHelper();
        private readonly string settingsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

        public LoginWindow()
        {
            InitializeComponent();
            LoadRememberMe();
            LoadBackgroundImage();
            Task.Run(() =>
            {
                try
                {
                    dbHelper.UpdateDatabaseConstraints();
                }
                catch {}
            });
        }

        private void LoadBackgroundImage()
        {
            try
            {
                if (File.Exists(settingsFilePath))
                {
                    string json = File.ReadAllText(settingsFilePath);
                    var node = JsonNode.Parse(json);
                    if (node != null)
                    {
                        var bgImageNode = node["LoginBgImagePath"];
                        if (bgImageNode != null)
                        {
                            string bgPath = bgImageNode.ToString();
                            if (!string.IsNullOrEmpty(bgPath) && File.Exists(bgPath))
                            {
                                imgBg.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(bgPath));
                            }
                        }
                    }
                }
            }
            catch
            {
                // Fallback silently
            }
        }

        private void LoadRememberMe()
        {
            try
            {
                if (File.Exists(settingsFilePath))
                {
                    string json = File.ReadAllText(settingsFilePath);
                    var node = JsonNode.Parse(json);
                    if (node != null)
                    {
                        var rememberMeNode = node["IsRememberMeChecked"];
                        var usernameNode = node["RememberedUsername"];
                        
                        if (rememberMeNode != null && (bool)rememberMeNode)
                        {
                            chkRememberMe.IsChecked = true;
                            if (usernameNode != null)
                            {
                                txtUsername.Text = usernameNode.ToString();
                            }
                        }
                    }
                }
            }
            catch
            {
                // Fallback silently
            }
        }

        private void SaveRememberMe(string username, bool isChecked)
        {
            try
            {
                JsonNode node;
                if (File.Exists(settingsFilePath))
                {
                    string json = File.ReadAllText(settingsFilePath);
                    node = JsonNode.Parse(json) ?? new JsonObject();
                }
                else
                {
                    node = new JsonObject();
                }

                node["IsRememberMeChecked"] = isChecked;
                node["RememberedUsername"] = isChecked ? username : "";

                string outputJson = JsonSerializer.Serialize(node, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(settingsFilePath, outputJson);
            }
            catch
            {
                // Fallback silently
            }
        }

        private static readonly System.Collections.Generic.Dictionary<string, (int Attempts, DateTime LockoutEnd)> _failedAttempts = new();

        private void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            string username = txtUsername.Text.Trim();
            string password = txtPassword.Password;

            // التحقق من أن الخانات ليست فارغة
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                MessageBox.Show("الرجاء إدخال اسم المستخدم وكلمة المرور", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // منع القفل الدائم وتأخير تصاعدي (Rate Limiting)
            if (_failedAttempts.TryGetValue(username, out var lockInfo))
            {
                if (DateTime.Now < lockInfo.LockoutEnd)
                {
                    var remaining = (lockInfo.LockoutEnd - DateTime.Now).TotalSeconds;
                    MessageBox.Show($"تم قفل الحساب مؤقتاً بسبب كثرة المحاولات الخاطئة. الرجاء المحاولة بعد {Math.Ceiling(remaining)} ثانية.", "حماية النظام", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            try
            {
                using (MySqlConnection conn = dbHelper.GetConnection())
                {
                    conn.Open();
                    // Query user by Username only
                    string query = "SELECT UserID, Role, FullName, Password FROM Users WHERE Username = @user LIMIT 1";
                    MySqlCommand cmd = new MySqlCommand(query, conn);
                    cmd.Parameters.AddWithValue("@user", username);

                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            string? storedPassword = reader["Password"]?.ToString();
                            string? role = reader["Role"]?.ToString();
                            string? fullName = reader["FullName"]?.ToString();
                            int userId = Convert.ToInt32(reader["UserID"]);
                            
                            bool passwordMatches = false;
                            bool needsMigration = false;

                            // Check if the stored password is a BCrypt hash (starts with $2a$, $2b$, $2x$, $2y$)
                            if (!string.IsNullOrEmpty(storedPassword) && storedPassword.StartsWith("$2"))
                            {
                                passwordMatches = BCrypt.Net.BCrypt.Verify(password, storedPassword);
                            }
                            else
                            {
                                // Legacy plain-text password check
                                if (storedPassword == password)
                                {
                                    passwordMatches = true;
                                    needsMigration = true; // Flag for silent migration
                                }
                            }

                            if (passwordMatches)
                            {
                                // Reset failed attempts on success
                                if (_failedAttempts.ContainsKey(username))
                                {
                                    _failedAttempts.Remove(username);
                                }

                                reader.Close(); // Close reader before executing another command
                                
                                // Silent Migration: Hash the password and save it
                                if (needsMigration)
                                {
                                    string newHash = BCrypt.Net.BCrypt.HashPassword(password);
                                    string updateQuery = "UPDATE Users SET Password = @newHash WHERE UserID = @userId";
                                    using (MySqlCommand updateCmd = new MySqlCommand(updateQuery, conn))
                                    {
                                        updateCmd.Parameters.AddWithValue("@newHash", newHash);
                                        updateCmd.Parameters.AddWithValue("@userId", userId);
                                        updateCmd.ExecuteNonQuery();
                                    }
                                }

                                // إعداد الجلسة الحالية والصلاحيات
                                AppSession.CurrentUserId = userId;
                                AppSession.CurrentUserName = username;
                                AppSession.CurrentUserRole = role ?? "كاشير";
                                // سنضيف لاحقاً استرجاع الصلاحيات الدقيقة من قاعدة البيانات
                                AppSession.Permissions = UserPermissions.GetDefault(role ?? "كاشير");

                                // حفظ حالة تذكرني قبل إغلاق النافذة
                                SaveRememberMe(username, chkRememberMe.IsChecked == true);

                                // تسجيل حدث الدخول في سجل التدقيق
                                DatabaseHelper.LogAudit("تسجيل دخول", "النظام", userId.ToString(), "", "تم تسجيل الدخول بنجاح");

                                // فتح الشاشة الرئيسية للنظام بعد نجاح الدخول
                                MainWindow mainWindow = new MainWindow(fullName ?? "مدير النظام");
                                Application.Current.MainWindow = mainWindow;
                                mainWindow.Show();

                                // إغلاق شاشة تسجيل الدخول
                                this.Close();
                            }
                            else
                            {
                                HandleFailedLogin(username);
                            }
                        }
                        else
                        {
                            HandleFailedLogin(username, true);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("حدث خطأ أثناء الاتصال بالنظام: " + ex.Message, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void HandleFailedLogin(string username, bool isUserNotFound = false)
        {
            int attempts = 1;
            int delaySeconds = 5;

            if (_failedAttempts.TryGetValue(username, out var currentLock))
            {
                attempts = currentLock.Attempts + 1;
                // تأخير تصاعدي: 5 ثواني ثم 15 ثم 30 ثم 60...
                delaySeconds = attempts switch
                {
                    1 => 5,
                    2 => 15,
                    3 => 30,
                    _ => 60
                };
            }

            _failedAttempts[username] = (attempts, DateTime.Now.AddSeconds(delaySeconds));

            string msg = isUserNotFound ? "اسم المستخدم غير موجود!" : "كلمة المرور غير صحيحة!";
            MessageBox.Show($"{msg}\nتم تسجيل محاولة خاطئة. يرجى الحذر لتجنب قفل الحساب.", "خطأ في الدخول", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}