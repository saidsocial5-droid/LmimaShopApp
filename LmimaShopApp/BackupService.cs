using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using MySqlConnector;

namespace LmimaShopApp
{
    public static class BackupService
    {
        private static readonly string SettingsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
        private static readonly string AppDataSettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LmimaShop", "settings.json");

        public static DateTime LastAutoBackupTime => _lastAutoBackupTime;

        /// <summary>
        /// جلب مسار مجلد النسخ الاحتياطي المحفوظ في الإعدادات مع فحص المسارات الاحتياطية
        /// </summary>
        public static string GetConfiguredBackupPath()
        {
            try
            {
                // 1. فحص ملف الإعدادات المحلي
                if (File.Exists(SettingsFilePath))
                {
                    string json = File.ReadAllText(SettingsFilePath);
                    var s = JsonSerializer.Deserialize<AppSettings>(json);
                    if (!string.IsNullOrWhiteSpace(s?.BackupFolderPath) && !s.BackupFolderPath.Contains("لم يتم تحديد"))
                    {
                        return s.BackupFolderPath.Trim();
                    }
                }

                // 2. فحص ملف الإعدادات المستمر في AppData
                if (File.Exists(AppDataSettingsPath))
                {
                    string json = File.ReadAllText(AppDataSettingsPath);
                    var s = JsonSerializer.Deserialize<AppSettings>(json);
                    if (!string.IsNullOrWhiteSpace(s?.BackupFolderPath) && !s.BackupFolderPath.Contains("لم يتم تحديد"))
                    {
                        return s.BackupFolderPath.Trim();
                    }
                }

                // 3. مسار E:\Drive\Lmima Shop المفضل للمستخدم
                if (Directory.Exists(@"E:\Drive\Lmima Shop"))
                {
                    return @"E:\Drive\Lmima Shop";
                }
                if (Directory.Exists(@"E:\Drive"))
                {
                    string p = @"E:\Drive\Lmima Shop";
                    try { Directory.CreateDirectory(p); } catch {}
                    return p;
                }

                // 4. كشف تلقائي عن Google Drive
                string? detected = DetectGoogleDriveFolder();
                if (!string.IsNullOrEmpty(detected)) return detected;
            }
            catch {}

            return @"E:\Drive\Lmima Shop";
        }

        /// <summary>
        /// حفظ إعدادات النسخ الاحتياطي في ملف الإعدادات المحلي والمستمر
        /// </summary>
        public static void SaveBackupConfiguration(string folderPath, bool autoBackupOnExit = true, bool periodic = true, int intervalMins = 30, bool syncMobileToCloud = true, bool updateLastBackupDate = false)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(folderPath) || folderPath.Contains("لم يتم تحديد")) return;

                AppSettings? settings = null;
                if (File.Exists(SettingsFilePath))
                {
                    try {
                        settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsFilePath));
                    } catch {}
                }
                if (settings == null && File.Exists(AppDataSettingsPath))
                {
                    try {
                        settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppDataSettingsPath));
                    } catch {}
                }
                if (settings == null) settings = new AppSettings();

                settings.BackupFolderPath = folderPath.Trim();
                settings.AutoBackupOnExit = autoBackupOnExit;
                settings.PeriodicAutoBackup = periodic;
                settings.AutoBackupIntervalMinutes = intervalMins > 0 ? intervalMins : 30;
                settings.AutoSyncMobileToCloud = syncMobileToCloud;
                if (updateLastBackupDate)
                {
                    settings.LastBackupDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                }

                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                
                // حفظ محلي في مجلد البرنامج
                try {
                    File.WriteAllText(SettingsFilePath, json);
                } catch {}

                // حفظ مستمر في AppData
                try {
                    string? appDataDir = Path.GetDirectoryName(AppDataSettingsPath);
                    if (!string.IsNullOrEmpty(appDataDir) && !Directory.Exists(appDataDir))
                    {
                        Directory.CreateDirectory(appDataDir);
                    }
                    File.WriteAllText(AppDataSettingsPath, json);
                } catch {}
            }
            catch (Exception ex)
            {
                Debug.WriteLine("SaveBackupConfiguration error: " + ex.Message);
            }
        }

        /// <summary>
        /// تنفيذ النسخ الاحتياطي التلقائي الدوري أثناء تشغيل البرنامج
        /// </summary>
        public static bool PerformPeriodicBackupIfDue()
        {
            try
            {
                string path = GetConfiguredBackupPath();
                if (string.IsNullOrWhiteSpace(path) || path.Contains("لم يتم تحديد")) return false;

                int interval = 30;
                bool periodic = true;
                bool syncMobile = true;

                if (File.Exists(SettingsFilePath))
                {
                    try {
                        var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsFilePath));
                        if (s != null)
                        {
                            periodic = s.PeriodicAutoBackup;
                            interval = s.AutoBackupIntervalMinutes > 0 ? s.AutoBackupIntervalMinutes : 30;
                            syncMobile = s.AutoSyncMobileToCloud;
                        }
                    } catch {}
                }

                if (!periodic) return false;

                // فحص هل حان موعد النسخ الدوري
                if (_lastAutoBackupTime != DateTime.MinValue && (DateTime.Now - _lastAutoBackupTime).TotalMinutes < interval)
                {
                    return false;
                }

                if (!Directory.Exists(path)) Directory.CreateDirectory(path);

                string fileName = $"LmimaShop_AutoBackup_{DateTime.Now:yyyy_MM_dd_HHmmss}.sql";
                string fullPath = Path.Combine(path, fileName);

                GenerateBackup(fullPath);

                // مزامنة ملفات الهاتف إلى Google Drive
                if (syncMobile)
                {
                    CloudSyncService.SyncMobileData(force: true);
                }

                SaveBackupConfiguration(path, autoBackupOnExit: true, periodic: periodic, intervalMins: interval, syncMobileToCloud: syncMobile, updateLastBackupDate: true);
                
                CleanupOldBackups(path, 30);
                _lastAutoBackupTime = DateTime.Now;
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("PerformPeriodicBackupIfDue error: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// فحص أجهزة التخزين للبحث التلقائي عن مجلد Google Drive المثبت على الحاسوب
        /// </summary>
        public static string? DetectGoogleDriveFolder()
        {
            try
            {
                // 1. فحص الأقراص الافتراضية (Virtual Drives مثل G:\ أو H:\)
                foreach (var drive in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (!drive.IsReady) continue;

                        string root = drive.RootDirectory.FullName;
                        string[] candidateSubfolders = { "Mon Drive", "My Drive", "مساحتي في Drive", "Mon disque", "Google Drive" };

                        foreach (var sub in candidateSubfolders)
                        {
                            string candidate = Path.Combine(root, sub);
                            if (Directory.Exists(candidate))
                            {
                                string targetDir = Path.Combine(candidate, "LmimaShop_Backups");
                                return targetDir;
                            }
                        }

                        // إذا كان اسم القرص يحتوي على Google Drive
                        if (drive.VolumeLabel.Contains("Google Drive", StringComparison.OrdinalIgnoreCase) ||
                            drive.VolumeLabel.Contains("Drive", StringComparison.OrdinalIgnoreCase))
                        {
                            string targetDir = Path.Combine(root, "LmimaShop_Backups");
                            return targetDir;
                        }
                    }
                    catch
                    {
                        // تجاهل أي قرص غير قابل للوصول
                    }
                }

                // 2. فحص مسار مجلد المستخدم الافتراضي
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string[] userCandidates = { "Google Drive", "Google Drive\\Mon Drive", "Google Drive\\My Drive" };
                foreach (var sub in userCandidates)
                {
                    string candidate = Path.Combine(userProfile, sub);
                    if (Directory.Exists(candidate))
                    {
                        return Path.Combine(candidate, "LmimaShop_Backups");
                    }
                }
            }
            catch
            {
                // تجاهل الأخطاء
            }

            return null;
        }

        /// <summary>
        /// إنشاء نسخة احتياطية شاملة لكافة جداول قاعدة بيانات المتجر
        /// </summary>
        public static void GenerateBackup(string filePath, DatabaseHelper? dbHelper = null)
        {
            string? dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var helper = dbHelper ?? new DatabaseHelper();

            using (var conn = helper.GetConnection())
            {
                conn.Open();

                // 1. جلب كافة جداول قاعدة البيانات ديناميكياً
                List<string> tables = new List<string>();
                try
                {
                    using (var cmd = new MySqlCommand("SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_TYPE = 'BASE TABLE' ORDER BY TABLE_NAME;", conn))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            tables.Add(reader.GetString(0));
                        }
                    }
                }
                catch
                {
                    // Fallback
                    tables.AddRange(new[] { "categories", "customers", "products", "returns", "sales", "salesdetails", "suppliers", "users" });
                }

                if (tables.Count == 0)
                {
                    tables.AddRange(new[] { "categories", "customers", "products", "returns", "sales", "salesdetails", "suppliers", "users" });
                }

                using (var stream = new StreamWriter(filePath, false, Encoding.UTF8))
                {
                    stream.WriteLine("-- =============================================");
                    stream.WriteLine("-- LMIMA SHOP COMPLETE DATABASE BACKUP");
                    stream.WriteLine($"-- Date & Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    stream.WriteLine($"-- Total Tables: {tables.Count}");
                    stream.WriteLine("-- =============================================");
                    stream.WriteLine("SET FOREIGN_KEY_CHECKS = 0;");
                    stream.WriteLine("SET SQL_MODE = 'NO_AUTO_VALUE_ON_ZERO';");
                    stream.WriteLine();

                    foreach (string table in tables)
                    {
                        stream.WriteLine($"-- ---------------------------------------------");
                        stream.WriteLine($"-- Table: `{table}`");
                        stream.WriteLine($"-- ---------------------------------------------");

                        // 1. بنية الجدول (Structure)
                        try
                        {
                            using (var createCmd = new MySqlCommand($"SHOW CREATE TABLE `{table}`;", conn))
                            using (var createReader = createCmd.ExecuteReader())
                            {
                                if (createReader.Read())
                                {
                                    string createTableSql = createReader.GetString(1);
                                    stream.WriteLine($"DROP TABLE IF EXISTS `{table}`;");
                                    stream.WriteLine(createTableSql + ";");
                                    stream.WriteLine();
                                }
                            }
                        }
                        catch
                        {
                            stream.WriteLine($"TRUNCATE TABLE `{table}`;");
                        }

                        // 2. بيانات الجدول (Rows)
                        using (var selectCmd = new MySqlCommand($"SELECT * FROM `{table}`;", conn))
                        using (var reader = selectCmd.ExecuteReader())
                        {
                            int fieldCount = reader.FieldCount;
                            List<string> columns = new List<string>();
                            for (int i = 0; i < fieldCount; i++)
                            {
                                columns.Add($"`{reader.GetName(i)}`");
                            }
                            string columnsList = string.Join(", ", columns);

                            while (reader.Read())
                            {
                                List<string> values = new List<string>();
                                for (int i = 0; i < fieldCount; i++)
                                {
                                    object val = reader.GetValue(i);
                                    if (val == null || val == DBNull.Value)
                                    {
                                        values.Add("NULL");
                                    }
                                    else if (val is string str)
                                    {
                                        values.Add($"'{EscapeSqlString(str)}'");
                                    }
                                    else if (val is DateTime dt)
                                    {
                                        values.Add($"'{dt:yyyy-MM-dd HH:mm:ss}'");
                                    }
                                    else if (val is bool b)
                                    {
                                        values.Add(b ? "1" : "0");
                                    }
                                    else if (val is decimal dec)
                                    {
                                        values.Add(dec.ToString(CultureInfo.InvariantCulture));
                                    }
                                    else if (val is double dbl)
                                    {
                                        values.Add(dbl.ToString(CultureInfo.InvariantCulture));
                                    }
                                    else if (val is float flt)
                                    {
                                        values.Add(flt.ToString(CultureInfo.InvariantCulture));
                                    }
                                    else if (val is byte[] bytes)
                                    {
                                        values.Add($"0x{Convert.ToHexString(bytes)}");
                                    }
                                    else
                                    {
                                        values.Add($"'{EscapeSqlString(val.ToString() ?? "")}'");
                                    }
                                }

                                stream.WriteLine($"INSERT INTO `{table}` ({columnsList}) VALUES ({string.Join(", ", values)});");
                            }
                        }
                        stream.WriteLine();
                    }

                    stream.WriteLine("SET FOREIGN_KEY_CHECKS = 1;");
                    stream.WriteLine("-- Backup completed successfully.");
                }
            }
        }

        /// <summary>
        /// استرجاع قاعدة البيانات من ملف نسخة احتياطية
        /// </summary>
        public static void RestoreBackup(string filePath, DatabaseHelper? dbHelper = null)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("ملف النسخة الاحتياطية غير موجود!", filePath);
            }

            var helper = dbHelper ?? new DatabaseHelper();

            using (var conn = helper.GetConnection())
            {
                conn.Open();

                using (var disableFkCmd = new MySqlCommand("SET FOREIGN_KEY_CHECKS = 0;", conn))
                {
                    disableFkCmd.ExecuteNonQuery();
                }

                var lines = File.ReadLines(filePath, Encoding.UTF8);
                StringBuilder currentStatement = new StringBuilder();

                foreach (var line in lines)
                {
                    string trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("--") || trimmed.StartsWith("#") || trimmed.StartsWith("/*"))
                        continue;

                    currentStatement.AppendLine(line);

                    if (trimmed.EndsWith(";"))
                    {
                        string sql = currentStatement.ToString().Trim();
                        if (!string.IsNullOrEmpty(sql))
                        {
                            using (var cmd = new MySqlCommand(sql, conn))
                            {
                                cmd.CommandTimeout = 120;
                                cmd.ExecuteNonQuery();
                            }
                        }
                        currentStatement.Clear();
                    }
                }

                using (var enableFkCmd = new MySqlCommand("SET FOREIGN_KEY_CHECKS = 1;", conn))
                {
                    enableFkCmd.ExecuteNonQuery();
                }
            }
        }

        private static DateTime _lastAutoBackupTime = DateTime.MinValue;

        /// <summary>
        /// تنفيذ النسخ الاحتياطي التلقائي عند إغلاق البرنامج إذا كان مفعلاً في الإعدادات
        /// </summary>
        public static bool PerformAutoBackupIfConfigured()
        {
            try
            {
                // إذا تم النسخ الاحتياطي خلال آخر 30 ثانية فلا داعي لتكراره
                if ((DateTime.Now - _lastAutoBackupTime).TotalSeconds < 30)
                {
                    return true;
                }

                string backupDir = GetConfiguredBackupPath();
                if (string.IsNullOrWhiteSpace(backupDir) || backupDir.Contains("لم يتم تحديد")) return false;

                bool autoOnExit = true;
                if (File.Exists(SettingsFilePath))
                {
                    try {
                        var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsFilePath));
                        if (s != null) autoOnExit = s.AutoBackupOnExit;
                    } catch {}
                }

                if (!autoOnExit) return false;
                if (!Directory.Exists(backupDir))
                {
                    Directory.CreateDirectory(backupDir);
                }

                string fileName = $"LmimaShop_AutoBackup_{DateTime.Now:yyyy_MM_dd_HHmmss}.sql";
                string fullPath = Path.Combine(backupDir, fileName);

                GenerateBackup(fullPath);

                // تحديث تاريخ آخر عملية نسخ احتياطي وحفظها
                SaveBackupConfiguration(backupDir, autoBackupOnExit: autoOnExit, updateLastBackupDate: true);

                // تطبيق سياسة الاحتفاظ (Retention Policy): الاحتفاظ بآخر 30 نسخة وحذف الأقدم
                CleanupOldBackups(backupDir, 30);

                _lastAutoBackupTime = DateTime.Now;
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Auto backup error: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// الاحتفاظ بعدد محدد من النسخ الاحتياطية لتوفير مساحة القرص
        /// </summary>
        private static void CleanupOldBackups(string directoryPath, int keepCount)
        {
            try
            {
                var dirInfo = new DirectoryInfo(directoryPath);
                var backupFiles = dirInfo.GetFiles("LmimaShop_AutoBackup_*.sql");

                if (backupFiles.Length > keepCount)
                {
                    Array.Sort(backupFiles, (a, b) => b.CreationTime.CompareTo(a.CreationTime)); // الأحدث أولاً
                    for (int i = keepCount; i < backupFiles.Length; i++)
                    {
                        try
                        {
                            backupFiles[i].Delete();
                        }
                        catch
                        {
                            // تجاوز أي ملف مقفل
                        }
                    }
                }
            }
            catch
            {
                // تجاهل أخطاء الحذف
            }
        }

        /// <summary>
        /// فتح المجلد في مستكشف الملفات
        /// </summary>
        public static void OpenFolderInExplorer(string folderPath)
        {
            if (Directory.Exists(folderPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = folderPath,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
        }

        private static string EscapeSqlString(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            return input
                .Replace("\\", "\\\\")
                .Replace("'", "\\'")
                .Replace("\0", "\\0")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n");
        }
    }
}
