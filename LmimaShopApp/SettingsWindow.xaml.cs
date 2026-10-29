using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Collections.Generic;
using System.Windows.Media;
using MySqlConnector;

namespace LmimaShopApp
{
    public partial class SettingsWindow : UserControl
    {
        private readonly string settingsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
        private readonly DatabaseHelper dbHelper = new DatabaseHelper();
        private List<Product> barcodeProductsList = new List<Product>();
        private List<Product> filteredBarcodeProductsList = new List<Product>();

        private static readonly string[] Code128Patterns = new string[]
        {
            "212222", "222122", "222221", "121223", "121322", "131222", 
            "122213", "122312", "132212", "221213", "221312", "231212", 
            "112232", "122132", "122231", "113222", "123122", "123221", 
            "223211", "221132", "221231", "213212", "223112", "312131", 
            "311222", "321122", "321221", "312212", "322112", "322211", 
            "212123", "212321", "232121", "111323", "131123", "131321", 
            "112313", "132113", "132311", "211313", "231113", "231311", 
            "112133", "112331", "132131", "113123", "113321", "133121", 
            "313121", "211331", "231131", "213113", "213311", "213131", 
            "311123", "311321", "331121", "312113", "312311", "332111", 
            "314111", "221411", "431111", "111224", "111422", "121124", 
            "121421", "141122", "141221", "112214", "112412", "122114", 
            "122411", "142112", "142211", "241211", "221114", "413111",
            "241112", "134111", "111242", "121142", "121241", "114212", 
            "124112", "124211", "411212", "421112", "421211", "212141", 
            "214121", "412121", "111143", "111341", "131141", "114113", 
            "114311", "411113", "411311", "113141", "114131", "311141", 
            "411131", "211412", "211214", "211232", "2331112"
        };

        private static List<int> EncodeCode128(string text)
        {
            List<int> symbols = new List<int>();
            if (string.IsNullOrEmpty(text)) return symbols;

            int i = 0;
            char currentMode = '\0';

            while (i < text.Length)
            {
                int digitsCount = 0;
                while (i + digitsCount < text.Length && char.IsDigit(text[i + digitsCount]))
                {
                    digitsCount++;
                }

                bool chooseC = (currentMode == 'C' && digitsCount >= 2) || (digitsCount >= 4);

                if (chooseC)
                {
                    if (currentMode != 'C')
                    {
                        if (currentMode == '\0')
                        {
                            symbols.Add(105); // Start C
                        }
                        else
                        {
                            symbols.Add(99); // Switch to C
                        }
                        currentMode = 'C';
                    }

                    string pair = text.Substring(i, 2);
                    symbols.Add(int.Parse(pair));
                    i += 2;
                }
                else
                {
                    if (currentMode != 'B')
                    {
                        if (currentMode == '\0')
                        {
                            symbols.Add(104); // Start B
                        }
                        else
                        {
                            symbols.Add(100); // Switch to B
                        }
                        currentMode = 'B';
                    }

                    char c = text[i];
                    int code = c - 32;
                    if (code < 0 || code > 95)
                    {
                        code = 0; // Replace invalid characters with Space
                    }
                    symbols.Add(code);
                    i++;
                }
            }

            int checksum = symbols[0];
            for (int pos = 1; pos < symbols.Count; pos++)
            {
                checksum += symbols[pos] * pos;
            }
            symbols.Add(checksum % 103);
            symbols.Add(106); // Stop

            return symbols;
        }

        public SettingsWindow()
        {
            InitializeComponent();
            LoadSettings();
            Loaded += UserControl_Loaded;

            // ربط حقل الباركود والبحث بمحول اللغة الفرنسي للباركود
            KeyboardLayoutHelper.BindToTextBox(txtBarcodeProductSearch);
            KeyboardLayoutHelper.BindToTextBox(txtBarcodeCode);
        }

        private void LoadSettings()
        {
            try
            {
                if (File.Exists(settingsFilePath))
                {
                    string json = File.ReadAllText(settingsFilePath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    if (settings != null)
                    {
                        // App Settings
                        cmbDateFormat.SelectedIndex = settings.DateFormatIndex;
                        cmbTimeFormat.SelectedIndex = settings.TimeFormatIndex;
                        cmbCurrency.SelectedIndex = settings.CurrencyIndex;
                        cmbDecimals.SelectedIndex = settings.DecimalIndex;
                        cmbLanguage.SelectedIndex = settings.LanguageIndex;

                        // Load Font Setting
                        string savedFont = settings.SelectedFont ?? "Cairo";
                        bool fontSelected = false;
                        foreach (ComboBoxItem item in cmbFontFamily.Items)
                        {
                            if (item.Tag?.ToString() == savedFont)
                            {
                                cmbFontFamily.SelectedItem = item;
                                fontSelected = true;
                                break;
                            }
                        }
                        if (!fontSelected && cmbFontFamily.Items.Count > 0)
                        {
                            cmbFontFamily.SelectedIndex = 0;
                        }

                        // Load Login Background Image Settings
                        txtLoginBgPath.Text = settings.LoginBgImagePath ?? "";
                        if (!string.IsNullOrEmpty(settings.LoginBgImagePath) && File.Exists(settings.LoginBgImagePath))
                        {
                            try
                            {
                                imgLoginBgPreview.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(settings.LoginBgImagePath));
                                txtNoLoginBgPlaceholder.Visibility = Visibility.Collapsed;
                                imgLoginBgPreview.Visibility = Visibility.Visible;
                                lblLoginBgFileName.Text = Path.GetFileName(settings.LoginBgImagePath);
                            }
                            catch
                            {
                                txtNoLoginBgPlaceholder.Visibility = Visibility.Visible;
                                imgLoginBgPreview.Visibility = Visibility.Collapsed;
                                lblLoginBgFileName.Text = "لم يتم اختيار ملف (سيتم استخدام الخلفية الافتراضية)";
                            }
                        }
                        else
                        {
                            txtNoLoginBgPlaceholder.Visibility = Visibility.Visible;
                            imgLoginBgPreview.Visibility = Visibility.Collapsed;
                            lblLoginBgFileName.Text = "لم يتم اختيار ملف (سيتم استخدام الخلفية الافتراضية)";
                        }

                        // Store Settings
                        txtStoreName.Text = string.IsNullOrEmpty(settings.StoreName) ? "متجر LMIMA SHOP" : settings.StoreName;
                        txtStorePhone.Text = string.IsNullOrEmpty(settings.StorePhone) ? "0614714949" : settings.StorePhone;
                        txtStoreAddress.Text = string.IsNullOrEmpty(settings.StoreAddress) ? "SIDI ASSOU" : settings.StoreAddress;
                        txtInvoiceFooter.Text = string.IsNullOrEmpty(settings.InvoiceFooter) ? "شكراً لزيارتكم" : settings.InvoiceFooter;
                        txtLogoPath.Text = settings.StoreLogoPath ?? "";
                        
                        txtICE.Text = settings.ICE ?? "";
                        txtIF.Text = settings.IF ?? "";
                        txtRC.Text = settings.RC ?? "";
                        txtPatente.Text = settings.Patente ?? "";
                        txtDefaultTva.Text = settings.DefaultTva.ToString("0.00");

                        // Load Installed Printers
                        cmbInvoicePrinters.Items.Clear();
                        cmbBarcodePrinter.Items.Clear();
                        cmbInvoicePrinters.Items.Add("طابعة النظام الافتراضية");
                        cmbBarcodePrinter.Items.Add("طابعة النظام الافتراضية");
                        try
                        {
                            var printersList = new System.Collections.Generic.HashSet<string>();
                            
                            try 
                            {
                                foreach (string printer in System.Drawing.Printing.PrinterSettings.InstalledPrinters)
                                {
                                    printersList.Add(printer);
                                }
                            } 
                            catch {}

                            try 
                            {
                                using (var searcher = new System.Management.ManagementObjectSearcher("SELECT * FROM Win32_Printer"))
                                {
                                    foreach (System.Management.ManagementObject printer in searcher.Get())
                                    {
                                        string pName = printer["Name"]?.ToString();
                                        if (!string.IsNullOrWhiteSpace(pName))
                                        {
                                            printersList.Add(pName);
                                        }
                                    }
                                }
                            } 
                            catch {}

                            foreach (string printer in printersList)
                            {
                                cmbInvoicePrinters.Items.Add(printer);
                                cmbBarcodePrinter.Items.Add(printer);
                            }
                        }
                        catch {}

                        // Invoice & Drawer Settings
                        string savedPrinter = string.IsNullOrEmpty(settings.InvoicePrinter) ? "طابعة النظام الافتراضية" : settings.InvoicePrinter;
                        int pIdx = cmbInvoicePrinters.Items.IndexOf(savedPrinter);
                        cmbInvoicePrinters.SelectedIndex = pIdx >= 0 ? pIdx : 0;

                        // Load Paper Size Setting
                        string savedPaperSize = settings.PaperSize ?? "80";
                        bool paperSizeSelected = false;
                        foreach (ComboBoxItem item in cmbPaperSize.Items)
                        {
                            if (item.Tag?.ToString() == savedPaperSize)
                            {
                                cmbPaperSize.SelectedItem = item;
                                paperSizeSelected = true;
                                break;
                            }
                        }
                        if (!paperSizeSelected && cmbPaperSize.Items.Count > 0)
                        {
                            cmbPaperSize.SelectedIndex = 0;
                        }
                        
                        chkOpenDrawerOnCheckout.IsChecked = settings.OpenDrawerOnCheckout;
                        txtDrawerKickCode.Text = string.IsNullOrEmpty(settings.DrawerKickCode) ? "27,112,0,25,250" : settings.DrawerKickCode;

                        chkPrintStoreLogo.IsChecked = settings.PrintStoreLogo;
                        txtInvoiceLogoPath.Text = settings.StoreLogoPath ?? "";
                        gridInvoiceLogoContainer.Visibility = settings.PrintStoreLogo ? Visibility.Visible : Visibility.Collapsed;

                        // تحميل معاينة الشعار في تبويب الفاتورة
                        if (!string.IsNullOrEmpty(settings.StoreLogoPath) && File.Exists(settings.StoreLogoPath))
                        {
                            try
                            {
                                imgInvoiceLogoPreview.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(settings.StoreLogoPath));
                                txtNoInvoiceLogoPlaceholder.Visibility = Visibility.Collapsed;
                                imgInvoiceLogoPreview.Visibility = Visibility.Visible;
                                lblInvoiceLogoFileName.Text = Path.GetFileName(settings.StoreLogoPath);
                            }
                            catch 
                            {
                                txtNoInvoiceLogoPlaceholder.Visibility = Visibility.Visible;
                                imgInvoiceLogoPreview.Visibility = Visibility.Collapsed;
                                lblInvoiceLogoFileName.Text = "لم يتم اختيار ملف";
                            }
                        }
                        else
                        {
                            txtNoInvoiceLogoPlaceholder.Visibility = Visibility.Visible;
                            imgInvoiceLogoPreview.Visibility = Visibility.Collapsed;
                            lblInvoiceLogoFileName.Text = "لم يتم اختيار ملف";
                        }

                        if (!string.IsNullOrEmpty(settings.StoreLogoPath) && File.Exists(settings.StoreLogoPath))
                        {
                            try
                            {
                                imgLogoPreview.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(settings.StoreLogoPath));
                                txtNoLogoPlaceholder.Visibility = Visibility.Collapsed;
                                imgLogoPreview.Visibility = Visibility.Visible;
                                lblLogoFileName.Text = Path.GetFileName(settings.StoreLogoPath);
                            }
                            catch 
                            {
                                txtNoLogoPlaceholder.Visibility = Visibility.Visible;
                                imgLogoPreview.Visibility = Visibility.Collapsed;
                                lblLogoFileName.Text = "Aucun fichier choisi";
                            }
                        }
                        else
                        {
                            txtNoLogoPlaceholder.Visibility = Visibility.Visible;
                            imgLogoPreview.Visibility = Visibility.Collapsed;
                            lblLogoFileName.Text = "Aucun fichier choisi";
                        }

                        // Load Backup Settings
                        string configuredPath = BackupService.GetConfiguredBackupPath();
                        if (!string.IsNullOrWhiteSpace(configuredPath))
                        {
                            txtBackupFolder.Text = configuredPath;
                        }
                        else
                        {
                            txtBackupFolder.Text = @"E:\Drive\Lmima Shop";
                        }

                        chkAutoBackupOnExit.IsChecked = settings.AutoBackupOnExit;
                        chkPeriodicAutoBackup.IsChecked = settings.PeriodicAutoBackup;
                        chkAutoSyncMobileToCloud.IsChecked = settings.AutoSyncMobileToCloud;

                        int interval = settings.AutoBackupIntervalMinutes > 0 ? settings.AutoBackupIntervalMinutes : 30;
                        if (cmbAutoBackupInterval != null)
                        {
                            foreach (ComboBoxItem item in cmbAutoBackupInterval.Items)
                            {
                                if (item.Tag?.ToString() == interval.ToString())
                                {
                                    cmbAutoBackupInterval.SelectedItem = item;
                                    break;
                                }
                            }
                        }

                        UpdateBackupStatusUi(settings.LastBackupDate);
                        UpdateMobileServerInfoUi();
                    }
                }
            }
            catch
            {
                // Fallback silently if any error reading settings
            }
        }

        private void BtnSaveSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AppSettings settings = null;
                if (File.Exists(settingsFilePath))
                {
                    try 
                    {
                        string json = File.ReadAllText(settingsFilePath).Trim('\0');
                        if (!string.IsNullOrWhiteSpace(json))
                        {
                            settings = JsonSerializer.Deserialize<AppSettings>(json);
                        }
                    } 
                    catch { }
                }
                
                if (settings == null)
                {
                    settings = new AppSettings();
                }

                // Update app settings
                settings.DateFormatIndex = cmbDateFormat.SelectedIndex;
                settings.TimeFormatIndex = cmbTimeFormat.SelectedIndex;
                settings.CurrencyIndex = cmbCurrency.SelectedIndex;
                settings.DecimalIndex = cmbDecimals.SelectedIndex;
                settings.LanguageIndex = cmbLanguage.SelectedIndex;
                settings.LoginBgImagePath = txtLoginBgPath.Text;

                // Save selected font
                if (cmbFontFamily.SelectedItem is ComboBoxItem selectedFontItem)
                {
                    settings.SelectedFont = selectedFontItem.Tag?.ToString() ?? "Cairo";
                }
                else
                {
                    settings.SelectedFont = "Cairo";
                }

                // Apply selected font immediately
                FontHelper.ApplyFont(settings.SelectedFont);

                // Preserve and sync backup settings
                if (txtBackupFolder != null && !string.IsNullOrWhiteSpace(txtBackupFolder.Text) && !txtBackupFolder.Text.Contains("لم يتم تحديد"))
                {
                    settings.BackupFolderPath = txtBackupFolder.Text.Trim();
                }
                if (chkAutoBackupOnExit != null) settings.AutoBackupOnExit = chkAutoBackupOnExit.IsChecked == true;
                if (chkPeriodicAutoBackup != null) settings.PeriodicAutoBackup = chkPeriodicAutoBackup.IsChecked == true;
                if (cmbAutoBackupInterval?.SelectedItem is ComboBoxItem intervalItem && int.TryParse(intervalItem.Tag?.ToString(), out int parsedInterval))
                {
                    settings.AutoBackupIntervalMinutes = parsedInterval;
                }
                if (chkAutoSyncMobileToCloud != null) settings.AutoSyncMobileToCloud = chkAutoSyncMobileToCloud.IsChecked == true;

                string jsonOut = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(settingsFilePath, jsonOut);
                BackupService.SaveBackupConfiguration(settings.BackupFolderPath, settings.AutoBackupOnExit, settings.PeriodicAutoBackup, settings.AutoBackupIntervalMinutes, settings.AutoSyncMobileToCloud);

                MessageBox.Show("تم حفظ إعدادات النظام وتحديث التفضيلات بنجاح!", "تمت العملية", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("فشل حفظ الإعدادات: " + ex.Message, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnHealImagePaths_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                int healedCount = 0;
                string appBaseDir = AppDomain.CurrentDomain.BaseDirectory;

                using (MySqlConnection conn = dbHelper.GetConnection())
                {
                    conn.Open();
                    string selectQuery = "SELECT Id, Barcode, Category, ImagePath FROM Products WHERE ImagePath IS NOT NULL AND ImagePath <> ''";
                    
                    var productsToHeal = new System.Collections.Generic.List<(int Id, string Barcode, string Category, string ImagePath)>();
                    using (MySqlCommand cmd = new MySqlCommand(selectQuery, conn))
                    {
                        using (MySqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                int id = Convert.ToInt32(reader["Id"]);
                                string barcode = reader["Barcode"]?.ToString() ?? "";
                                string category = reader["Category"]?.ToString() ?? "";
                                string imagePath = reader["ImagePath"]?.ToString() ?? "";

                                if (System.IO.Path.IsPathRooted(imagePath))
                                {
                                    productsToHeal.Add((id, barcode, category, imagePath));
                                }
                            }
                        }
                    }

                    foreach (var product in productsToHeal)
                    {
                        string safeCategory = product.Category;
                        foreach (char c in System.IO.Path.GetInvalidFileNameChars())
                        {
                            safeCategory = safeCategory.Replace(c, '_');
                        }
                        if (string.IsNullOrEmpty(safeCategory)) safeCategory = "Uncategorized";

                        string ext = System.IO.Path.GetExtension(product.ImagePath);
                        if (string.IsNullOrEmpty(ext)) ext = ".jpg";

                        string safeBarcode = product.Barcode;
                        foreach (char c in System.IO.Path.GetInvalidFileNameChars())
                        {
                            safeBarcode = safeBarcode.Replace(c, '_');
                        }

                        string targetFileName = safeBarcode + ext;
                        string relativePath = System.IO.Path.Combine("ProductImages", safeCategory, targetFileName);
                        string currentAbsolutePath = System.IO.Path.Combine(appBaseDir, relativePath);

                        bool isHealed = false;

                        // 1. Check if the file exists at the new relative path (already moved)
                        if (System.IO.File.Exists(currentAbsolutePath))
                        {
                            isHealed = true;
                        }
                        // 2. Extract relative path structure starting with "ProductImages" if present
                        else
                        {
                            int index = product.ImagePath.IndexOf("ProductImages", StringComparison.OrdinalIgnoreCase);
                            if (index >= 0)
                            {
                                string extractedRelative = product.ImagePath.Substring(index);
                                string newAbsolutePath = System.IO.Path.Combine(appBaseDir, extractedRelative);
                                if (System.IO.File.Exists(newAbsolutePath))
                                {
                                    relativePath = extractedRelative;
                                    isHealed = true;
                                }
                            }
                        }

                        if (isHealed)
                        {
                            string updateQuery = "UPDATE Products SET ImagePath = @imagePath WHERE Id = @id";
                            using (MySqlCommand updateCmd = new MySqlCommand(updateQuery, conn))
                            {
                                string formattedRelativePath = relativePath.Replace('\\', '/');
                                updateCmd.Parameters.AddWithValue("@imagePath", formattedRelativePath);
                                updateCmd.Parameters.AddWithValue("@id", product.Id);
                                updateCmd.ExecuteNonQuery();
                            }
                            healedCount++;
                        }
                    }
                }

                MessageBox.Show($"تمت عملية تصحيح مسارات الصور بنجاح!\n\nعدد الروابط التي تم تصحيحها في قاعدة البيانات: {healedCount}", "نجاح العملية", MessageBoxButton.OK, MessageBoxImage.Information);
                
                // Refresh main products if loaded
                MainWindow.Instance?.LoadProducts();
            }
            catch (Exception ex)
            {
                MessageBox.Show("حدث خطأ أثناء تصحيح روابط الصور: " + ex.Message, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private void BtnSaveStoreSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AppSettings settings = null;
                if (File.Exists(settingsFilePath))
                {
                    try 
                    {
                        string json = File.ReadAllText(settingsFilePath).Trim('\0');
                        if (!string.IsNullOrWhiteSpace(json))
                        {
                            settings = JsonSerializer.Deserialize<AppSettings>(json);
                        }
                    } 
                    catch { }
                }
                
                if (settings == null)
                {
                    settings = new AppSettings();
                }

                // Update store settings
                settings.StoreName = txtStoreName.Text;
                settings.StorePhone = txtStorePhone.Text;
                settings.StoreAddress = txtStoreAddress.Text;
                settings.InvoiceFooter = txtInvoiceFooter.Text;
                settings.StoreLogoPath = txtLogoPath.Text;

                // Save Fiscal Settings
                settings.ICE = txtICE.Text;
                settings.IF = txtIF.Text;
                settings.RC = txtRC.Text;
                settings.Patente = txtPatente.Text;
                if (decimal.TryParse(txtDefaultTva.Text, out decimal tva))
                {
                    settings.DefaultTva = tva;
                }

                // مزامنة مسار الشعار مع تبويب الفاتورة ومعاينتها
                txtInvoiceLogoPath.Text = txtLogoPath.Text;
                if (!string.IsNullOrEmpty(settings.StoreLogoPath) && File.Exists(settings.StoreLogoPath))
                {
                    try
                    {
                        imgInvoiceLogoPreview.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(settings.StoreLogoPath));
                        txtNoInvoiceLogoPlaceholder.Visibility = Visibility.Collapsed;
                        imgInvoiceLogoPreview.Visibility = Visibility.Visible;
                        lblInvoiceLogoFileName.Text = Path.GetFileName(settings.StoreLogoPath);
                    }
                    catch {}
                }
                else
                {
                    txtNoInvoiceLogoPlaceholder.Visibility = Visibility.Visible;
                    imgInvoiceLogoPreview.Visibility = Visibility.Collapsed;
                    lblInvoiceLogoFileName.Text = "لم يتم اختيار ملف";
                }

                string jsonOut = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(settingsFilePath, jsonOut);

                MessageBox.Show("تم حفظ بيانات المتجر بنجاح!", "تمت العملية", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("فشل حفظ بيانات المتجر: " + ex.Message, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnSaveInvoiceSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AppSettings settings = null;
                if (File.Exists(settingsFilePath))
                {
                    try 
                    {
                        string json = File.ReadAllText(settingsFilePath).Trim('\0');
                        if (!string.IsNullOrWhiteSpace(json))
                        {
                            settings = JsonSerializer.Deserialize<AppSettings>(json);
                        }
                    } 
                    catch { }
                }
                
                if (settings == null)
                {
                    settings = new AppSettings();
                }

                // Update invoice / drawer settings
                settings.InvoicePrinter = cmbInvoicePrinters.SelectedItem?.ToString() ?? "طابعة النظام الافتراضية";
                settings.OpenDrawerOnCheckout = chkOpenDrawerOnCheckout.IsChecked == true;
                settings.DrawerKickCode = txtDrawerKickCode.Text.Trim();
                settings.PrintStoreLogo = chkPrintStoreLogo.IsChecked == true;
                settings.StoreLogoPath = txtInvoiceLogoPath.Text;

                if (cmbPaperSize.SelectedItem is ComboBoxItem selectedPaperSizeItem)
                {
                    settings.PaperSize = selectedPaperSizeItem.Tag?.ToString() ?? "80";
                }
                else
                {
                    settings.PaperSize = "80";
                }

                // مزامنة مسار الشعار مع تبويب بيانات المتجر ومعاينتها
                txtLogoPath.Text = txtInvoiceLogoPath.Text;
                if (!string.IsNullOrEmpty(settings.StoreLogoPath) && File.Exists(settings.StoreLogoPath))
                {
                    try
                    {
                        imgLogoPreview.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(settings.StoreLogoPath));
                        txtNoLogoPlaceholder.Visibility = Visibility.Collapsed;
                        imgLogoPreview.Visibility = Visibility.Visible;
                        lblLogoFileName.Text = Path.GetFileName(settings.StoreLogoPath);
                    }
                    catch {}
                }
                else
                {
                    txtNoLogoPlaceholder.Visibility = Visibility.Visible;
                    imgLogoPreview.Visibility = Visibility.Collapsed;
                    lblLogoFileName.Text = "Aucun fichier choisi";
                }

                string jsonOut = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(settingsFilePath, jsonOut);

                MessageBox.Show("تم حفظ إعدادات طابعة الفواتير وصندوق النقود بنجاح!", "تمت العملية", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("فشل حفظ إعدادات الفاتورة: " + ex.Message, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnBrowseLogo_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Image files (*.png;*.jpeg;*.jpg;*.gif)|*.png;*.jpeg;*.jpg;*.gif|All files (*.*)|*.*",
                Title = "اختر شعار المتجر"
            };
            if (openFileDialog.ShowDialog() == true)
            {
                txtLogoPath.Text = openFileDialog.FileName;
                lblLogoFileName.Text = Path.GetFileName(openFileDialog.FileName);
                try
                {
                    imgLogoPreview.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(openFileDialog.FileName));
                    txtNoLogoPlaceholder.Visibility = Visibility.Collapsed;
                    imgLogoPreview.Visibility = Visibility.Visible;
                }
                catch (Exception ex)
                {
                    MessageBox.Show("فشل تحميل الصورة المحددة: " + ex.Message, "خطأ في تحميل الصورة", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnBrowseLoginBg_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Image files (*.png;*.jpeg;*.jpg;*.gif)|*.png;*.jpeg;*.jpg;*.gif|All files (*.*)|*.*",
                Title = "اختر صورة خلفية شاشة تسجيل الدخول"
            };
            if (openFileDialog.ShowDialog() == true)
            {
                txtLoginBgPath.Text = openFileDialog.FileName;
                lblLoginBgFileName.Text = Path.GetFileName(openFileDialog.FileName);
                try
                {
                    imgLoginBgPreview.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(openFileDialog.FileName));
                    txtNoLoginBgPlaceholder.Visibility = Visibility.Collapsed;
                    imgLoginBgPreview.Visibility = Visibility.Visible;
                }
                catch (Exception ex)
                {
                    MessageBox.Show("فشل تحميل الصورة المحددة: " + ex.Message, "خطأ في تحميل الصورة", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ChkPrintStoreLogo_Checked(object sender, RoutedEventArgs e)
        {
            if (gridInvoiceLogoContainer != null)
                gridInvoiceLogoContainer.Visibility = Visibility.Visible;
        }

        private void ChkPrintStoreLogo_Unchecked(object sender, RoutedEventArgs e)
        {
            if (gridInvoiceLogoContainer != null)
                gridInvoiceLogoContainer.Visibility = Visibility.Collapsed;
        }

        private void BtnBrowseInvoiceLogo_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Image files (*.png;*.jpeg;*.jpg;*.gif)|*.png;*.jpeg;*.jpg;*.gif|All files (*.*)|*.*",
                Title = "اختر شعار الفاتورة"
            };
            if (openFileDialog.ShowDialog() == true)
            {
                txtInvoiceLogoPath.Text = openFileDialog.FileName;
                lblInvoiceLogoFileName.Text = Path.GetFileName(openFileDialog.FileName);
                try
                {
                    imgInvoiceLogoPreview.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(openFileDialog.FileName));
                    txtNoInvoiceLogoPlaceholder.Visibility = Visibility.Collapsed;
                    imgInvoiceLogoPreview.Visibility = Visibility.Visible;
                }
                catch (Exception ex)
                {
                    MessageBox.Show("فشل تحميل الصورة المحددة: " + ex.Message, "خطأ في تحميل الصورة", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void Tab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                // Reset all tab button styles to normal
                tabApp.Style = (Style)FindResource("TopTabButtonStyle");
                tabStore.Style = (Style)FindResource("TopTabButtonStyle");
                tabInvoice.Style = (Style)FindResource("TopTabButtonStyle");
                tabBarcode.Style = (Style)FindResource("TopTabButtonStyle");
                tabBackup.Style = (Style)FindResource("TopTabButtonStyle");
                tabAbout.Style = (Style)FindResource("TopTabButtonStyle");
                tabMobileSync.Style = (Style)FindResource("TopTabButtonStyle");

                // Set clicked tab button style to active
                btn.Style = (Style)FindResource("ActiveTopTabButtonStyle");

                // Hide all grids
                gridAppSettings.Visibility = Visibility.Collapsed;
                gridStoreSettings.Visibility = Visibility.Collapsed;
                gridInvoiceSettings.Visibility = Visibility.Collapsed;
                gridBarcodeSettings.Visibility = Visibility.Collapsed;
                gridBackupSettings.Visibility = Visibility.Collapsed;
                gridAboutSettings.Visibility = Visibility.Collapsed;
                gridMobileSyncSettings.Visibility = Visibility.Collapsed;

                string content = btn.Content.ToString() ?? "";
                if (content.Contains("إعدادات البرنامج"))
                {
                    gridAppSettings.Visibility = Visibility.Visible;
                }
                else if (content.Contains("بيانات المتجر"))
                {
                    gridStoreSettings.Visibility = Visibility.Visible;
                }
                else if (content.Contains("طباعة الفواتير"))
                {
                    gridInvoiceSettings.Visibility = Visibility.Visible;
                }
                else if (content.Contains("طباعة الباركود"))
                {
                    gridBarcodeSettings.Visibility = Visibility.Visible;
                }
                else if (content.Contains("النسخ الاحتياطي"))
                {
                    gridBackupSettings.Visibility = Visibility.Visible;
                }
                else if (content.Contains("عن البرنامج"))
                {
                    gridAboutSettings.Visibility = Visibility.Visible;
                }
                else if (content.Contains("ربط الهاتف") || content.Contains("المزامنة"))
                {
                    gridMobileSyncSettings.Visibility = Visibility.Visible;
                    LoadMobileSyncSettings();
                }
            }
        }

        // Redundant sidebar methods removed since managed by MainWindow container

        #region Barcode Printing Feature Implementation
        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            LoadBarcodeProducts();
            cmbBarcodePrinter.SelectedIndex = 0;
            if (cmbBarcodeProduct.Items.Count > 0)
            {
                cmbBarcodeProduct.SelectedIndex = 0;
            }
            UpdateBarcodePreview();
        }

        private void LoadBarcodeProducts()
        {
            try
            {
                cmbBarcodeProduct.Items.Clear();
                barcodeProductsList.Clear();
                filteredBarcodeProductsList.Clear();
                using (MySqlConnection conn = dbHelper.GetConnection())
                {
                    conn.Open();
                    string query = "SELECT Id, Barcode, ProductName, Category, PurchasePrice, SellPrice, StockQuantity, ImagePath FROM Products WHERE deleted_at IS NULL ORDER BY ProductName";
                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    {
                        using (MySqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var product = new Product
                                {
                                    Id = Convert.ToInt32(reader["Id"]),
                                    Barcode = reader["Barcode"].ToString() ?? "",
                                    ProductName = reader["ProductName"].ToString() ?? "",
                                    Category = reader["Category"].ToString() ?? "",
                                    PurchasePrice = Convert.ToDecimal(reader["PurchasePrice"]),
                                    SellPrice = Convert.ToDecimal(reader["SellPrice"]),
                                    StockQuantity = Convert.ToDecimal(reader["StockQuantity"]),
                                    ImagePath = reader["ImagePath"]?.ToString()
                                };
                                barcodeProductsList.Add(product);
                                filteredBarcodeProductsList.Add(product);
                                cmbBarcodeProduct.Items.Add(product.ProductName);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("خطأ في تحميل المنتجات: " + ex.Message, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CmbBarcodeProduct_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbBarcodeProduct.SelectedIndex >= 0 && cmbBarcodeProduct.SelectedIndex < filteredBarcodeProductsList.Count)
            {
                var product = filteredBarcodeProductsList[cmbBarcodeProduct.SelectedIndex];
                txtBarcodeCode.Text = product.Barcode;
                txtBarcodeProductName.Text = product.ProductName;
                txtBarcodePrice.Text = product.SellPrice.ToString("0.00");
                UpdateBarcodePreview();
            }
        }

        private void BarcodeInputs_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateBarcodePreview();
        }

        private void CmbBarcodeSize_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbBarcodeSize == null || borderBarcodeLabelPreview == null || canvasBarcodeBars == null) return;
            if (cmbBarcodeSize.SelectedItem is ComboBoxItem item)
            {
                string tag = item.Tag?.ToString() ?? "38x25";
                var parts = tag.Split('x');
                if (parts.Length == 2 && double.TryParse(parts[0], out double widthMm) && double.TryParse(parts[1], out double heightMm))
                {
                    // Calculate preview dimensions maintaining aspect ratio, max width 200, max height 130
                    double scale = Math.Min(200.0 / widthMm, 130.0 / heightMm);
                    borderBarcodeLabelPreview.Width = widthMm * scale;
                    borderBarcodeLabelPreview.Height = heightMm * scale;

                    // Update canvas dimensions dynamically to fit inside the preview border
                    canvasBarcodeBars.Width = Math.Max(50, (widthMm * scale) - 20);
                    canvasBarcodeBars.Height = Math.Max(20, (heightMm * scale) - 80);
                }
                UpdateBarcodePreview();
            }
        }

        private void UpdateBarcodePreview()
        {
            if (lblPreviewStore == null || lblPreviewName == null || lblPreviewBarcode == null || lblPreviewPrice == null || canvasBarcodeBars == null)
                return;

            lblPreviewStore.Text = string.IsNullOrEmpty(txtBarcodeStoreName.Text) ? "LMIMA SHOP" : txtBarcodeStoreName.Text.ToUpper();
            lblPreviewName.Text = txtBarcodeProductName.Text;
            lblPreviewBarcode.Text = txtBarcodeCode.Text;

            if (TryParseDecimal(txtBarcodePrice.Text, out decimal price))
                lblPreviewPrice.Text = $"{price:N2} درهم";
            else
                lblPreviewPrice.Text = "0.00 درهم";

            RenderBarcodeCode128(canvasBarcodeBars, txtBarcodeCode.Text);
        }

        private void RenderBarcodeCode128(Canvas canvas, string text)
        {
            canvas.Children.Clear();
            if (string.IsNullOrEmpty(text)) return;

            var symbols = EncodeCode128(text);
            if (symbols.Count == 0) return;

            double canvasWidth = canvas.Width;
            double canvasHeight = canvas.Height;
            if (double.IsNaN(canvasWidth) || canvasWidth <= 0) canvasWidth = canvas.ActualWidth > 0 ? canvas.ActualWidth : 160;
            if (double.IsNaN(canvasHeight) || canvasHeight <= 0) canvasHeight = canvas.ActualHeight > 0 ? canvas.ActualHeight : 40;

            int totalUnits = 11 * (symbols.Count - 1) + 13;

            int moduleWidth = Math.Max(1, (int)Math.Floor(canvasWidth / totalUnits));
            double actualBarcodeWidth = totalUnits * moduleWidth;
            double startX = Math.Max(0, (int)((canvasWidth - actualBarcodeWidth) / 2));
            double x = startX;

            foreach (int symbol in symbols)
            {
                if (symbol < 0 || symbol >= Code128Patterns.Length) continue;
                string pattern = Code128Patterns[symbol];
                bool isBlack = true;

                foreach (char p in pattern)
                {
                    int widthVal = p - '0';
                    double barWidth = widthVal * moduleWidth;
                    if (isBlack)
                    {
                        System.Windows.Shapes.Rectangle rect = new System.Windows.Shapes.Rectangle
                        {
                            Width = barWidth,
                            Height = canvasHeight,
                            Fill = System.Windows.Media.Brushes.Black
                        };
                        Canvas.SetLeft(rect, (int)x);
                        Canvas.SetTop(rect, 0);
                        canvas.Children.Add(rect);
                    }
                    x += barWidth;
                    isBlack = !isBlack;
                }
            }
        }

        private void BtnPrintBarcode_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string barcode = txtBarcodeCode.Text.Trim();
                string productName = txtBarcodeProductName.Text.Trim();
                string priceText = txtBarcodePrice.Text.Trim();
                string storeName = txtBarcodeStoreName.Text.Trim();
                
                if (string.IsNullOrEmpty(barcode))
                {
                    MessageBox.Show("يرجى إدخال رقم الباركود أولاً!", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!int.TryParse(txtBarcodeCopies.Text, out int copies) || copies <= 0)
                {
                    MessageBox.Show("يرجى إدخال عدد نسخ صحيح أكبر من الصفر!", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string selectedPrinter = cmbBarcodePrinter.SelectedItem?.ToString() ?? "طابعة النظام الافتراضية";

                double widthMm = 38;
                double heightMm = 25;
                if (cmbBarcodeSize.SelectedItem is ComboBoxItem item)
                {
                    string tag = item.Tag?.ToString() ?? "38x25";
                    var parts = tag.Split('x');
                    if (parts.Length == 2 && double.TryParse(parts[0], out double w) && double.TryParse(parts[1], out double h))
                    {
                        widthMm = w;
                        heightMm = h;
                    }
                }

                double widthDpi = (widthMm / 25.4) * 96;
                double heightDpi = (heightMm / 25.4) * 96;

                PrintDialog printDialog = new PrintDialog();
                if (selectedPrinter != "طابعة النظام الافتراضية")
                {
                    try
                    {
                        printDialog.PrintQueue = new System.Printing.PrintQueue(new System.Printing.PrintServer(), selectedPrinter);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"تعذر العثور على الطابعة المحددة: {ex.Message}. سيتم الطباعة بالطابعة الافتراضية.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }

                // ضبط حجم الورق في تذكرة الطباعة لمنع مشاكل المحاذاة أو تغذية الورق الزائد
                try
                {
                    if (printDialog.PrintTicket != null)
                    {
                        printDialog.PrintTicket.PageMediaSize = new System.Printing.PageMediaSize(widthDpi, heightDpi);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"تعذر ضبط حجم الورق في تذكرة الطباعة: {ex.Message}");
                }

                for (int i = 0; i < copies; i++)
                {
                    DrawingVisual drawingVisual = new DrawingVisual();
                    using (DrawingContext drawingContext = drawingVisual.RenderOpen())
                    {
                        drawingContext.DrawRectangle(System.Windows.Media.Brushes.White, null, new Rect(0, 0, widthDpi, heightDpi));

                        FormattedText storeText = new FormattedText(
                            storeName.ToUpper(),
                            System.Globalization.CultureInfo.CurrentCulture,
                            FlowDirection.LeftToRight,
                            new Typeface("Segoe UI"),
                            10,
                            System.Windows.Media.Brushes.Black,
                            96
                        );
                        storeText.TextAlignment = TextAlignment.Center;
                        drawingContext.DrawText(storeText, new Point(widthDpi / 2, 4));

                        FormattedText nameText = new FormattedText(
                            productName,
                            System.Globalization.CultureInfo.CurrentCulture,
                            FlowDirection.RightToLeft,
                            new Typeface("Segoe UI"),
                            11,
                            System.Windows.Media.Brushes.Black,
                            96
                        );
                        nameText.MaxTextWidth = widthDpi - 10;
                        nameText.MaxTextHeight = 30;
                        nameText.TextAlignment = TextAlignment.Center;
                        drawingContext.DrawText(nameText, new Point(widthDpi / 2, 16));

                        double barcodeHeight = heightDpi - 55;
                        double barcodeY = 32;
                        DrawCode128OnDC(drawingContext, barcode, 5, barcodeY, widthDpi - 10, barcodeHeight);

                        double bottomY = barcodeY + barcodeHeight + 2;
                        FormattedText barcodeDigitsText = new FormattedText(
                            barcode,
                            System.Globalization.CultureInfo.CurrentCulture,
                            FlowDirection.LeftToRight,
                            new Typeface("Segoe UI"),
                            9,
                            System.Windows.Media.Brushes.DimGray,
                            96
                        );
                        drawingContext.DrawText(barcodeDigitsText, new Point(10, bottomY));

                        FormattedText priceFormattedText = new FormattedText(
                            $"{priceText} درهم",
                            System.Globalization.CultureInfo.CurrentCulture,
                            FlowDirection.RightToLeft,
                            new Typeface("Segoe UI"),
                            10,
                            System.Windows.Media.Brushes.Black,
                            96
                        );
                        drawingContext.DrawText(priceFormattedText, new Point(widthDpi - 10, bottomY));
                    }

                    printDialog.PrintVisual(drawingVisual, "Barcode Label");
                }

                MessageBox.Show("تم إرسال أمر طباعة الملصقات بنجاح!", "نجاح", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("فشل إرسال أمر الطباعة: " + ex.Message, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnExportBarcodeJpeg_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string barcode = txtBarcodeCode.Text.Trim();
                string productName = txtBarcodeProductName.Text.Trim();
                string priceText = txtBarcodePrice.Text.Trim();
                string storeName = txtBarcodeStoreName.Text.Trim();
                
                if (string.IsNullOrEmpty(barcode))
                {
                    MessageBox.Show("يرجى إدخال رقم الباركود أولاً!", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                double widthMm = 38;
                double heightMm = 25;
                if (cmbBarcodeSize.SelectedItem is ComboBoxItem item)
                {
                    string tag = item.Tag?.ToString() ?? "38x25";
                    var parts = tag.Split('x');
                    if (parts.Length == 2 && double.TryParse(parts[0], out double w) && double.TryParse(parts[1], out double h))
                    {
                        widthMm = w;
                        heightMm = h;
                    }
                }

                double widthDpi = (widthMm / 25.4) * 96;
                double heightDpi = (heightMm / 25.4) * 96;

                DrawingVisual drawingVisual = new DrawingVisual();
                using (DrawingContext drawingContext = drawingVisual.RenderOpen())
                {
                    drawingContext.DrawRectangle(System.Windows.Media.Brushes.White, null, new Rect(0, 0, widthDpi, heightDpi));

                    FormattedText storeText = new FormattedText(
                        storeName.ToUpper(),
                        System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        new Typeface("Segoe UI"),
                        10,
                        System.Windows.Media.Brushes.Black,
                        96
                    );
                    storeText.TextAlignment = TextAlignment.Center;
                    drawingContext.DrawText(storeText, new Point(widthDpi / 2, 4));

                    FormattedText nameText = new FormattedText(
                        productName,
                        System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.RightToLeft,
                        new Typeface("Segoe UI"),
                        11,
                        System.Windows.Media.Brushes.Black,
                        96
                    );
                    nameText.MaxTextWidth = widthDpi - 10;
                    nameText.MaxTextHeight = 30;
                    nameText.TextAlignment = TextAlignment.Center;
                    drawingContext.DrawText(nameText, new Point(widthDpi / 2, 16));

                    double barcodeHeight = heightDpi - 55;
                    double barcodeY = 32;
                    DrawCode128OnDC(drawingContext, barcode, 5, barcodeY, widthDpi - 10, barcodeHeight);

                    double bottomY = barcodeY + barcodeHeight + 2;
                    FormattedText barcodeDigitsText = new FormattedText(
                        barcode,
                        System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        new Typeface("Segoe UI"),
                        9,
                        System.Windows.Media.Brushes.DimGray,
                        96
                    );
                    drawingContext.DrawText(barcodeDigitsText, new Point(10, bottomY));

                    FormattedText priceFormattedText = new FormattedText(
                        $"{priceText} درهم",
                        System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.RightToLeft,
                        new Typeface("Segoe UI"),
                        10,
                        System.Windows.Media.Brushes.Black,
                        96
                    );
                    drawingContext.DrawText(priceFormattedText, new Point(widthDpi - 10, bottomY));
                }

                System.Windows.Media.Imaging.RenderTargetBitmap renderTargetBitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    (int)Math.Ceiling(widthDpi), 
                    (int)Math.Ceiling(heightDpi), 
                    96, 
                    96, 
                    System.Windows.Media.PixelFormats.Pbgra32
                );
                renderTargetBitmap.Render(drawingVisual);

                Microsoft.Win32.SaveFileDialog saveFileDialog = new Microsoft.Win32.SaveFileDialog();
                saveFileDialog.Filter = "JPEG Image (*.jpg)|*.jpg";
                saveFileDialog.FileName = $"Barcode_{barcode}.jpg";
                if (saveFileDialog.ShowDialog() == true)
                {
                    using (var fileStream = new System.IO.FileStream(saveFileDialog.FileName, System.IO.FileMode.Create))
                    {
                        System.Windows.Media.Imaging.JpegBitmapEncoder encoder = new System.Windows.Media.Imaging.JpegBitmapEncoder();
                        encoder.QualityLevel = 90;
                        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(renderTargetBitmap));
                        encoder.Save(fileStream);
                    }
                    MessageBox.Show("تم تصدير الباركود كـ JPEG بنجاح!", "نجاح", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("فشل تصدير الباركود: " + ex.Message, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DrawCode128OnDC(DrawingContext dc, string text, double startX, double y, double totalWidth, double height)
        {
            if (string.IsNullOrEmpty(text)) return;
            var symbols = EncodeCode128(text);
            if (symbols.Count == 0) return;

            int totalUnits = 11 * (symbols.Count - 1) + 13;

            int moduleWidth = Math.Max(1, (int)Math.Floor(totalWidth / totalUnits));
            double actualBarcodeWidth = totalUnits * moduleWidth;
            double adjustedStartX = startX + Math.Max(0, (int)((totalWidth - actualBarcodeWidth) / 2));
            double x = adjustedStartX;

            foreach (int symbol in symbols)
            {
                if (symbol < 0 || symbol >= Code128Patterns.Length) continue;
                string pattern = Code128Patterns[symbol];
                bool isBlack = true;

                foreach (char p in pattern)
                {
                    int widthVal = p - '0';
                    double barWidth = widthVal * moduleWidth;
                    if (isBlack)
                    {
                        dc.DrawRectangle(System.Windows.Media.Brushes.Black, null, new Rect((int)x, (int)y, (int)barWidth, (int)height));
                    }
                    x += barWidth;
                    isBlack = !isBlack;
                }
            }
        }
        #endregion

        #region Database Backup and Restore Implementation (Google Drive & Local)
        private void BtnBrowseBackupFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFolderDialog
                {
                    Title = "اختر مجلد Google Drive أو مجلد حفظ النسخ الاحتياطية"
                };

                if (!string.IsNullOrEmpty(txtBackupFolder.Text) && Directory.Exists(txtBackupFolder.Text))
                {
                    dialog.InitialDirectory = txtBackupFolder.Text;
                }

                if (dialog.ShowDialog() == true)
                {
                    txtBackupFolder.Text = dialog.FolderName;
                    SaveBackupConfiguration();
                    MessageBox.Show($"تم ربط مجلد النسخ الاحتياطي بنجاح:\n{dialog.FolderName}", "تم التحديد", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("خطأ أثناء فتح مستعرض المجلدات: " + ex.Message, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnDetectGoogleDrive_Click(object sender, RoutedEventArgs e)
        {
            string? detected = BackupService.DetectGoogleDriveFolder();
            if (!string.IsNullOrEmpty(detected))
            {
                txtBackupFolder.Text = detected;
                SaveBackupConfiguration();
                MessageBox.Show($"تم العثور على مجلد Google Drive وربطه بنجاح:\n{detected}", "تم الكشف التلقائي", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("لم يتم العثور على مجلد Google Drive تلقائياً في المسارات الشائعة.\nيرجى الضغط على زر 'استعراض...' واختيار مجلد Google Drive يدوياً.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnOpenBackupFolder_Click(object sender, RoutedEventArgs e)
        {
            string folder = txtBackupFolder.Text.Trim();
            if (Directory.Exists(folder))
            {
                BackupService.OpenFolderInExplorer(folder);
            }
            else
            {
                MessageBox.Show("المجلد المحدد غير موجود حالياً أو لم يتم اختياره بعد!", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BackupOption_Changed(object sender, RoutedEventArgs e)
        {
            SaveBackupConfiguration();
        }

        private void TxtBackupFolder_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (txtBackupFolder != null && !string.IsNullOrWhiteSpace(txtBackupFolder.Text) && !txtBackupFolder.Text.Contains("لم يتم تحديد"))
            {
                SaveBackupConfiguration();
            }
        }

        private void BtnSaveBackupFolder_Click(object sender, RoutedEventArgs e)
        {
            SaveBackupConfiguration();
            MessageBox.Show($"تم حفظ مسار مجلد النسخ الاحتياطي وتثبيته بنجاح:\n{txtBackupFolder.Text}", "تم الحفظ", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void CmbAutoBackupInterval_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SaveBackupConfiguration();
        }

        private void SaveBackupConfiguration()
        {
            try
            {
                string folder = txtBackupFolder?.Text.Trim() ?? "";
                if (folder.Contains("لم يتم تحديد")) folder = "";

                int interval = 30;
                if (cmbAutoBackupInterval?.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out int parsed))
                {
                    interval = parsed;
                }

                BackupService.SaveBackupConfiguration(
                    folder,
                    autoBackupOnExit: chkAutoBackupOnExit?.IsChecked == true,
                    periodic: chkPeriodicAutoBackup?.IsChecked == true,
                    intervalMins: interval,
                    syncMobileToCloud: chkAutoSyncMobileToCloud?.IsChecked == true
                );

                UpdateBackupStatusUi(null);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error saving backup settings: " + ex.Message);
            }
        }

        private void UpdateBackupStatusUi(string? lastDate)
        {
            try
            {
                bool periodic = chkPeriodicAutoBackup?.IsChecked == true;
                
                if (lblAutoSyncStatusBadge != null)
                {
                    lblAutoSyncStatusBadge.Text = periodic ? "🟢 المزامنة التلقائية نشطة" : "⚪ المزامنة التلقائية متوقفة";
                }

                string displayDate = lastDate;
                if (string.IsNullOrWhiteSpace(displayDate) && File.Exists(settingsFilePath))
                {
                    try {
                        var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(settingsFilePath));
                        displayDate = s?.LastBackupDate;
                    } catch {}
                }

                if (lblLastBackupStatus != null)
                {
                    lblLastBackupStatus.Text = !string.IsNullOrWhiteSpace(displayDate) ? $"تاريخ آخر نسخة احتياطية: {displayDate}" : "تاريخ آخر نسخة احتياطية: لم يتم أخذ نسخة بعد";
                }

                if (lblNextBackupStatus != null)
                {
                    if (periodic)
                    {
                        int interval = 30;
                        if (cmbAutoBackupInterval?.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out int parsed))
                        {
                            interval = parsed;
                        }
                        lblNextBackupStatus.Text = $"المزامنة التلقائية مفعلة (كل {interval} دقيقة)";
                    }
                    else
                    {
                        lblNextBackupStatus.Text = "المزامنة التلقائية متوقفة (النسخ عند الإغلاق فقط)";
                    }
                }
            }
            catch {}
        }

        private void UpdateMobileServerInfoUi()
        {
            try
            {
                bool running = MobileSyncHttpServer.IsRunning;
                if (badgeMobileServerInBackup != null)
                {
                    badgeMobileServerInBackup.Background = running ? 
                        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 252, 231)) : 
                        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(254, 226, 226));
                }
                if (lblMobileServerInBackup != null)
                {
                    lblMobileServerInBackup.Text = running ? $"🟢 الخادم يعمل بنشاط (:{MobileSyncHttpServer.Port})" : "🔴 الخادم متوقف";
                    lblMobileServerInBackup.Foreground = running ? 
                        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(22, 101, 52)) : 
                        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(153, 27, 27));
                }
                if (lblMobileServerIpInBackup != null)
                {
                    lblMobileServerIpInBackup.Text = $"العنوان: http://{MobileSyncHttpServer.LocalIp}:{MobileSyncHttpServer.Port}";
                }
            }
            catch {}
        }

        private void BtnOpenMobileQrWindow_Click(object sender, RoutedEventArgs e)
        {
            var win = new MobileScannerPairingWindow();
            var parentWin = Window.GetWindow(this);
            if (parentWin != null) win.Owner = parentWin;
            win.ShowDialog();
            UpdateMobileServerInfoUi();
        }

        private void BtnBroadcastSyncNow_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _ = LmimaShopApp.Core.Sync.SseManager.BroadcastEventAsync("changed", DateTime.UtcNow.ToString("O"));
                _ = SignalRServer.BroadcastSyncToClientsAsync();
                CloudSyncService.SyncMobileData(force: true);
                MessageBox.Show("تم بث إشارة تحيين وتحديث فوري لجميع الهواتف المتصلة بالمتجر بنجاح! ⚡", "تحيين فوري", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"حدث خطأ أثناء بث التحيين: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnRestartMobileServerQuick_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                MobileSyncHttpServer.Restart();
                _ = SignalRServer.StartAsync(8081);
                UpdateMobileServerInfoUi();
                MessageBox.Show("تمت إعادة تشغيل خادم الاتصال بالهاتف بنجاح!", "إعادة التشغيل", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"حدث خطأ أثناء إعادة تشغيل الخادم: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnOpenFirewallQuick_Click(object sender, RoutedEventArgs e)
        {
            bool ok = MobileSyncHttpServer.OpenWindowsFirewall();
            if (ok)
            {
                MessageBox.Show("تم فتح جدار حماية ويندوز (Windows Firewall) للمنافذ 8080 و 8081 بنجاح!", "جدار الحماية", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("تعذر فتح جدار الحماية تلقائياً. يرجى تشغيل البرنامج كمسؤول (Run as administrator).", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnCreateCloudBackup_Click(object sender, RoutedEventArgs e)
        {
            string folder = txtBackupFolder.Text.Trim();
            if (string.IsNullOrEmpty(folder) || folder.Contains("لم يتم تحديد") || !Directory.Exists(folder))
            {
                MessageBox.Show("يرجى تحديد مجلد Google Drive أولاً عبر الضغط على 'استعراض...'!", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                this.Cursor = System.Windows.Input.Cursors.Wait;
                string fileName = $"LmimaShop_Backup_{DateTime.Now:yyyy_MM_dd_HHmmss}.sql";
                string targetFile = Path.Combine(folder, fileName);

                BackupService.GenerateBackup(targetFile, dbHelper);

                string lastDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                lblLastBackupStatus.Text = $"تاريخ آخر نسخة احتياطية: {lastDate}";

                // Save last backup date to settings
                if (File.Exists(settingsFilePath))
                {
                    string json = File.ReadAllText(settingsFilePath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                    settings.LastBackupDate = lastDate;
                    settings.BackupFolderPath = folder;
                    File.WriteAllText(settingsFilePath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
                }

                // أيضاً مزامنة بيانات تطبيق الهاتف السحابية
                CloudSyncService.SyncMobileData(force: true);

                this.Cursor = System.Windows.Input.Cursors.Arrow;
                MessageBox.Show($"تم إنشاء النسخة الاحتياطية السحابية وتحديث بيانات الهاتف بنجاح في:\n{targetFile}\n\nسيقوم تطبيق Google Drive بمزامنتها فوراً إلى السحابة.", "تم النسخ بنجاح", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                this.Cursor = System.Windows.Input.Cursors.Arrow;
                MessageBox.Show("فشل إنشاء النسخة الاحتياطية السحابية: " + ex.Message, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

                private void BtnManageDevices_Click(object sender, RoutedEventArgs e)
        {
            var deviceManager = new DeviceManagerWindow();
            deviceManager.ShowDialog();
        }

        private void BtnSyncMobileNow_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                this.Cursor = System.Windows.Input.Cursors.Wait;
                bool ok = CloudSyncService.SyncMobileData(force: true);
                this.Cursor = System.Windows.Input.Cursors.Arrow;

                if (ok)
                {
                    MessageBox.Show("تمت مزامنة بيانات تطبيق الهاتف بنجاح!\nتم تحديث ملفات (lmima_mobile_sync.json / lmima_mobile_sync.js) في مجلد المزامنة.", "مزامنة الهاتف", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("فشلت عملية مزامنة الهاتف. يرجى التحقق من مسار المجلد واتصال قاعدة البيانات.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                this.Cursor = System.Windows.Input.Cursors.Arrow;
                MessageBox.Show("خطأ أثناء مزامنة الهاتف: " + ex.Message, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCreateBackup_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.SaveFileDialog saveFileDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "SQL Files (*.sql)|*.sql",
                FileName = $"LmimaShop_Backup_{DateTime.Now:yyyy_MM_dd_HHmmss}.sql",
                Title = "حفظ النسخة الاحتياطية (تصدير يدوي)"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    this.Cursor = System.Windows.Input.Cursors.Wait;
                    BackupService.GenerateBackup(saveFileDialog.FileName, dbHelper);
                    this.Cursor = System.Windows.Input.Cursors.Arrow;
                    MessageBox.Show("تم إنشاء وتصدير النسخة الاحتياطية بنجاح!", "نجاح العملية", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    this.Cursor = System.Windows.Input.Cursors.Arrow;
                    MessageBox.Show("فشل إنشاء النسخة الاحتياطية: " + ex.Message, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnRestoreBackup_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "تحذير هام جداً!\n\nاستعادة نسخة احتياطية سيقوم بحذف جميع البيانات الحالية (المبيعات، المنتجات، الزبناء والديون، المستخدمين، المرتجعات) واستبدالها بالكامل.\n\nهل أنت متأكد من المتابعة والاستعادة؟",
                "تنبيه استعادة البيانات",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            );

            if (result == MessageBoxResult.Yes)
            {
                Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "SQL Files (*.sql)|*.sql",
                    Title = "اختر ملف النسخة الاحتياطية لاستعادتها"
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    try
                    {
                        this.Cursor = System.Windows.Input.Cursors.Wait;
                        BackupService.RestoreBackup(openFileDialog.FileName, dbHelper);
                        this.Cursor = System.Windows.Input.Cursors.Arrow;
                        MessageBox.Show("تم استعادة قاعدة البيانات بنجاح! سيتم إغلاق التطبيق لتطبيق التغييرات.", "نجاح العملية", MessageBoxButton.OK, MessageBoxImage.Information);
                        
                        Application.Current.Shutdown();
                    }
                    catch (Exception ex)
                    {
                        this.Cursor = System.Windows.Input.Cursors.Arrow;
                        MessageBox.Show("فشل استعادة قاعدة البيانات: " + ex.Message, "خطأ في الاستعادة", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }
        #endregion

        private void TxtBarcodeProductSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            string filterText = txtBarcodeProductSearch.Text.Trim();
            cmbBarcodeProduct.Items.Clear();
            filteredBarcodeProductsList.Clear();

            System.Collections.Generic.IEnumerable<Product> query = barcodeProductsList;
            if (!string.IsNullOrEmpty(filterText))
            {
                query = System.Linq.Enumerable.Where(barcodeProductsList, p => p.ProductName.Contains(filterText, StringComparison.OrdinalIgnoreCase) || p.Barcode.Contains(filterText, StringComparison.OrdinalIgnoreCase));
            }

            foreach (var product in query)
            {
                filteredBarcodeProductsList.Add(product);
                cmbBarcodeProduct.Items.Add(product.ProductName);
            }

            if (cmbBarcodeProduct.Items.Count > 0)
            {
                cmbBarcodeProduct.SelectedIndex = 0;
            }
        }

        private bool TryParseDecimal(string input, out decimal result)
        {
            result = 0;
            if (string.IsNullOrEmpty(input)) return false;
            string normalized = input.Replace(',', '.').Trim();
            return decimal.TryParse(normalized, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out result);
        }

        private void BarcodeKeypad_Click(object sender, RoutedEventArgs e)
        {
            var activeBox = System.Windows.Input.FocusManager.GetFocusedElement(System.Windows.Window.GetWindow(this)) as TextBox;
            if (activeBox == null) return;

            if (activeBox != txtBarcodePrice && activeBox != txtBarcodeCode && activeBox != txtBarcodeProductSearch) return;

            if (sender is Button btn && btn.Content != null)
            {
                string key = btn.Content.ToString() ?? "";
                if (key == "C")
                {
                    activeBox.Text = "";
                }
                else if (key == "⌫")
                {
                    if (activeBox.Text.Length > 0)
                    {
                        activeBox.Text = activeBox.Text.Substring(0, activeBox.Text.Length - 1);
                    }
                }
                else
                {
                    activeBox.Text += key;
                }
                activeBox.SelectionStart = activeBox.Text.Length;
            }
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
        private void BtnRefreshPrinters_Click(object sender, RoutedEventArgs e)
        {
            string savedInvoicePrinter = cmbInvoicePrinters.SelectedItem?.ToString();
            string savedBarcodePrinter = cmbBarcodePrinter.SelectedItem?.ToString();

            cmbInvoicePrinters.Items.Clear();
            cmbBarcodePrinter.Items.Clear();
            cmbInvoicePrinters.Items.Add("طابعة النظام الافتراضية");
            cmbBarcodePrinter.Items.Add("طابعة النظام الافتراضية");

            try
            {
                var printersList = new System.Collections.Generic.HashSet<string>();
                
                try 
                {
                    foreach (string printer in System.Drawing.Printing.PrinterSettings.InstalledPrinters)
                    {
                        printersList.Add(printer);
                    }
                } 
                catch {}

                try 
                {
                    using (var searcher = new System.Management.ManagementObjectSearcher("SELECT * FROM Win32_Printer"))
                    {
                        foreach (System.Management.ManagementObject printer in searcher.Get())
                        {
                            string pName = printer["Name"]?.ToString();
                            if (!string.IsNullOrWhiteSpace(pName))
                            {
                                printersList.Add(pName);
                            }
                        }
                    }
                } 
                catch {}

                foreach (string printer in printersList)
                {
                    cmbInvoicePrinters.Items.Add(printer);
                    cmbBarcodePrinter.Items.Add(printer);
                }
            }
            catch {}

            if (!string.IsNullOrEmpty(savedInvoicePrinter) && cmbInvoicePrinters.Items.Contains(savedInvoicePrinter))
                cmbInvoicePrinters.SelectedItem = savedInvoicePrinter;
            else
                cmbInvoicePrinters.SelectedIndex = 0;

            if (!string.IsNullOrEmpty(savedBarcodePrinter) && cmbBarcodePrinter.Items.Contains(savedBarcodePrinter))
                cmbBarcodePrinter.SelectedItem = savedBarcodePrinter;
            else
                cmbBarcodePrinter.SelectedIndex = 0;
                
            MessageBox.Show("تم تحديث قائمة الطابعات بنجاح.", "تحديث", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    
                #region Mobile Sync and Connectivity Settings

        private void LoadMobileSyncSettings()
        {
            try
            {
                string ip = MobileSyncHttpServer.LocalIp;
                int port = MobileSyncHttpServer.Port;

                txtSettingsServerIp.Text = ip + ":" + port;
                txtSettingsPairingCode.Text = LmimaShopApp.Core.Sync.MobileAuth.EnsureFreshPairingCode();

                bool isRunning = MobileSyncHttpServer.IsRunning;
                lblSettingsServerStatus.Text = isRunning ? (" الخادم يعمل بنشاط على المنفذ " + port) : " الخادم متوقف";
                borderSettingsServerBadge.Background = isRunning ? 
                    new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(222, 247, 236)) : 
                    new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(254, 226, 226));
                lblSettingsServerStatus.Foreground = isRunning ? 
                    new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(3, 84, 63)) : 
                    new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(153, 27, 27));

                cmbSettingsNetworkIps.Items.Clear();
                var ips = MobileSyncHttpServer.GetAllLocalIps();
                foreach (var netIp in ips)
                {
                    cmbSettingsNetworkIps.Items.Add(new ComboBoxItem { Content = netIp + ":" + port, Tag = netIp });
                }
                if (cmbSettingsNetworkIps.Items.Count > 0) cmbSettingsNetworkIps.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex.Message);
            }
        }

        private void CmbSettingsNetworkIps_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbSettingsNetworkIps.SelectedItem is ComboBoxItem item && item.Tag is string selectedIp)
            {
                txtSettingsServerIp.Text = selectedIp + ":" + MobileSyncHttpServer.Port;
            }
        }

        private void BtnCopyServerIp_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(txtSettingsServerIp.Text);
            MessageBox.Show("تم نسخ عنوان الخادم إلى الحافظة بنجاح: " + txtSettingsServerIp.Text, "تم النسخ", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnSettingsRestartServer_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                MobileSyncHttpServer.Restart();
                _ = SignalRServer.StartAsync(8081);
                LoadMobileSyncSettings();
                MessageBox.Show("تمت إعادة تشغيل خادم الاتصال بالهاتف بنجاح!", "إعادة التشغيل", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("حدث خطأ أثناء إعادة تشغيل الخادم: " + ex.Message, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnSettingsOpenFirewall_Click(object sender, RoutedEventArgs e)
        {
            bool ok = MobileSyncHttpServer.OpenWindowsFirewall();
            if (ok)
            {
                MessageBox.Show("تم فتح جدار حماية ويندوز للمنافذ 8080 و 8081 بنجاح!", "جدار الحماية", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("تعذر فتح جدار الحماية تلقائياً. يرجى تشغيل Install_Service_And_Firewall.bat كمسؤول.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnSettingsNewPairingCode_Click(object sender, RoutedEventArgs e)
        {
            txtSettingsPairingCode.Text = LmimaShopApp.Core.Sync.MobileAuth.RegeneratePairingCode();
        }

        private void BtnSettingsSavePairingCode_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                LmimaShopApp.Core.Sync.MobileAuth.SetPairingCode(txtSettingsPairingCode.Text);
                MessageBox.Show($"تم حفظ رمز الاقتران: {txtSettingsPairingCode.Text.Trim()}\nالرمز صالح لمدة {LmimaShopApp.Core.Sync.MobileAuth.PairingCodeLifetime.TotalMinutes:0} دقيقة لاقتران هاتف جديد. الهواتف المقترنة سابقاً تبقى متصلة.", "تم الحفظ", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnSettingsShowQr_Click(object sender, RoutedEventArgs e)
        {
            var win = new MobileScannerPairingWindow();
            win.ShowDialog();
        }

        private void BtnSettingsBroadcastSync_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _ = LmimaShopApp.Core.Sync.SseManager.BroadcastEventAsync("changed", DateTime.UtcNow.ToString("O"));
                _ = SignalRServer.BroadcastSyncToClientsAsync();
                CloudSyncService.SyncMobileData(force: true);

                MessageBox.Show("تم بث إشارة تحيين وتحديث فوري لجميع الهواتف المتصلة بالمتجر بنجاح!", "تحيين فوري", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("حدث خطأ أثناء بث التحيين: " + ex.Message, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion
    }

    public class AppSettings
    {
        // App Settings
        public int DateFormatIndex { get; set; }
        public int TimeFormatIndex { get; set; }
        public int CurrencyIndex { get; set; }
        public int DecimalIndex { get; set; }
        public int LanguageIndex { get; set; }
        public string LoginBgImagePath { get; set; } = "";
        public string SelectedFont { get; set; } = "Cairo";

        // Store Settings
        public string StoreName { get; set; } = "متجر LMIMA SHOP";
        public string StorePhone { get; set; } = "0614714949";
        public string StoreAddress { get; set; } = "SIDI ASSOU";
        public string InvoiceFooter { get; set; } = "شكراً لزيارتكم";
        public string StoreLogoPath { get; set; } = "";
        
        // Moroccan Fiscal Settings
        public string ICE { get; set; } = "";
        public string IF { get; set; } = "";
        public string RC { get; set; } = "";
        public string Patente { get; set; } = "";
        public decimal DefaultTva { get; set; } = 20.0m;

        // Invoice/Drawer Settings
        public string InvoicePrinter { get; set; } = "طابعة النظام الافتراضية";
        public bool OpenDrawerOnCheckout { get; set; } = true;
        public string DrawerKickCode { get; set; } = "27,112,0,25,250";
        public bool PrintStoreLogo { get; set; } = false;
        public string PaperSize { get; set; } = "80";

        // Backup / Google Drive Settings
        public string BackupFolderPath { get; set; } = "";
        public bool AutoBackupOnExit { get; set; } = true;
        public string LastBackupDate { get; set; } = "";
        public bool PeriodicAutoBackup { get; set; } = true;
        public int AutoBackupIntervalMinutes { get; set; } = 30;
        public bool AutoSyncMobileToCloud { get; set; } = true;
}

}
