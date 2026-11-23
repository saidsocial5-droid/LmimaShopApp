using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ExcelDataReader;
using MySqlConnector;

namespace LmimaShopApp
{
    /// <summary>
    /// المنطق البرمجي لشاشة إدارة المنتجات وتصميم لوحة التحكم المتقدمة
    /// </summary>
    public partial class ProductsWindow : UserControl
    {
        // استدعاء فئة الاتصال بقاعدة البيانات
        private readonly DatabaseHelper dbHelper = new DatabaseHelper();

        // قائمة الفئات
        private readonly List<CategoryItem> categoriesList = new List<CategoryItem>();

        // قائمة المنتجات المعروضة
        private List<Product> allProducts = new List<Product>();

        // متغير لتحديد ما إذا كنا نقوم بإضافة أو تعديل فئة/منتج
        private bool isEditModeProduct = false;
        private int editingProductId = 0;

        private bool isEditModeCategory = false;
        private int editingCategoryId = 0;
        private string editingCategoryOldName = string.Empty;
        private System.Windows.Threading.DispatcherTimer? searchDebounceTimer;

        public ProductsWindow()
        {
            InitializeComponent();
            ReloadAll();

            // ربط حقول الأرقام والأسعار بلوحة المفاتيح الافتراضية
            txtPurchasePrice.GotFocus += NumericTextBox_GotFocus;
            txtSellPrice.GotFocus += NumericTextBox_GotFocus;
            txtStockQuantity.GotFocus += NumericTextBox_GotFocus;
            KeyboardLayoutHelper.BindToTextBox(txtSearch);
        }

        private void NumericTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            KeyboardHelper.ShowTouchKeyboard();
        }

        /// <summary>
        /// إعادة تحميل الفئات والمنتجات بالكامل وتحديث الشاشة
        /// </summary>
        private void ReloadAll()
        {
            LoadCategories();
            LoadProducts();
        }

        #region 1. إدارة الفئات (Categories Management)

        /// <summary>
        /// تحميل الفئات من قاعدة البيانات وعرضها في القائمة الجانبية وكومبوبوكس المنتجات
        /// </summary>
        private void LoadCategories()
        {
            try
            {
                categoriesList.Clear();
                // الفئة الافتراضية الأولى
                categoriesList.Add(new CategoryItem { Id = -1, CategoryName = "كل المنتجات" });

                using (MySqlConnection conn = dbHelper.GetConnection())
                {
                    conn.Open();
                    string query = "SELECT Id, CategoryName FROM Categories ORDER BY CategoryName ASC";
                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    {
                        using (MySqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                categoriesList.Add(new CategoryItem
                                {
                                    Id = Convert.ToInt32(reader["Id"]),
                                    CategoryName = reader["CategoryName"].ToString() ?? ""
                                });
                            }
                        }
                    }
                }

                // ربط القائمة الجانبية بالفئات
                lstCategories.ItemsSource = null;
                lstCategories.ItemsSource = categoriesList;
                lstCategories.SelectedIndex = 0; // تحديد "كل المنتجات" افتراضياً

                // ربط كومبوبوكس إضافة/تعديل المنتج بالفئات الفعلية (بدون الفئة الافتراضية)
                var comboList = categoriesList.Where(c => c.Id != -1).Select(c => c.CategoryName).ToList();
                cmbCategory.ItemsSource = comboList;
            }
            catch (Exception ex)
            {
                MessageBox.Show("فشل تحميل الفئات: " + ex.Message, "خطأ قاعدة بيانات", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// تصفية جدول المنتجات عند تحديد فئة معينة من القائمة الجانبية
        /// </summary>
        private void LstCategories_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyFilter();
        }

        /// <summary>
        /// زر فتح نافذة إضافة فئة جديدة
        /// </summary>
        private void BtnAddCategory_Click(object sender, RoutedEventArgs e)
        {
            isEditModeCategory = false;
            txtCategoryName.Clear();
            lblCategoryModalTitle.Text = "إضافة فئة جديدة";
            categoryOverlay.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// زر فتح نافذة تعديل فئة موجودة
        /// </summary>
        private void BtnEditCategory_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is CategoryItem cat)
            {
                if (cat.Id == -1) return; // لا يمكن تعديل الفئة الافتراضية
                
                isEditModeCategory = true;
                editingCategoryId = cat.Id;
                editingCategoryOldName = cat.CategoryName;
                txtCategoryName.Text = cat.CategoryName;
                lblCategoryModalTitle.Text = "تعديل اسم الفئة";
                categoryOverlay.Visibility = Visibility.Visible;
            }
        }

        /// <summary>
        /// حفظ الفئة (إضافة أو تعديل)
        /// </summary>
        private void BtnSaveCategory_Click(object sender, RoutedEventArgs e)
        {
            string categoryName = txtCategoryName.Text.Trim();
            if (string.IsNullOrEmpty(categoryName))
            {
                MessageBox.Show("الرجاء إدخال اسم الفئة!", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            using (MySqlConnection conn = dbHelper.GetConnection())
            {
                try
                {
                    conn.Open();
                    if (isEditModeCategory)
                    {
                        // تعديل الفئة وتحديث المنتجات المرتبطة بها
                        using (MySqlTransaction trans = conn.BeginTransaction())
                        {
                            try
                            {
                                // 1. تحديث جدول الفئات
                                string updateCatQuery = "UPDATE Categories SET CategoryName = @newName WHERE Id = @id";
                                using (MySqlCommand cmd = new MySqlCommand(updateCatQuery, conn, trans))
                                {
                                    cmd.Parameters.AddWithValue("@newName", categoryName);
                                    cmd.Parameters.AddWithValue("@id", editingCategoryId);
                                    cmd.ExecuteNonQuery();
                                }

                                // 2. تحديث جدول المنتجات التي تحتوي الفئة القديمة
                                string updateProdQuery = "UPDATE Products SET Category = @newName WHERE Category = @oldName";
                                using (MySqlCommand cmd = new MySqlCommand(updateProdQuery, conn, trans))
                                {
                                    cmd.Parameters.AddWithValue("@newName", categoryName);
                                    cmd.Parameters.AddWithValue("@oldName", editingCategoryOldName);
                                    cmd.ExecuteNonQuery();
                                }

                                trans.Commit();
                                MessageBox.Show("تم تعديل الفئة وتحديث المنتجات بنجاح!", "نجاح العملية", MessageBoxButton.OK, MessageBoxImage.Information);
                            }
                            catch
                            {
                                trans.Rollback();
                                throw;
                            }
                        }
                    }
                    else
                    {
                        // إضافة فئة جديدة
                        string query = "INSERT INTO Categories (CategoryName) VALUES (@name)";
                        using (MySqlCommand cmd = new MySqlCommand(query, conn))
                        {
                            cmd.Parameters.AddWithValue("@name", categoryName);
                            cmd.ExecuteNonQuery();
                        }
                        MessageBox.Show("تم إضافة الفئة بنجاح!", "نجاح العملية", MessageBoxButton.OK, MessageBoxImage.Information);
                    }

                    categoryOverlay.Visibility = Visibility.Collapsed;
                    ReloadAll();
                    _ = CloudSyncService.SyncMobileDataAsync(true);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("فشل حفظ الفئة (ربما الاسم مكرر): " + ex.Message, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// إلغاء إغلاق نافذة الفئة
        /// </summary>
        private void BtnCancelCategory_Click(object sender, RoutedEventArgs e)
        {
            categoryOverlay.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// حذف فئة
        /// </summary>
        private void BtnDeleteCategory_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is CategoryItem cat)
            {
                if (cat.Id == -1) return; // الفئة الافتراضية لا تحذف

                MessageBoxResult result = MessageBox.Show($"هل أنت متأكد من حذف الفئة '{cat.CategoryName}'؟\nملاحظة: لن يتم حذف المنتجات ولكن ستصبح بدون فئة.", "تأكيد حذف الفئة", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes) return;

                using (MySqlConnection conn = dbHelper.GetConnection())
                {
                    try
                    {
                        conn.Open();
                        using (MySqlTransaction trans = conn.BeginTransaction())
                        {
                            try
                            {
                                // 1. مسح الفئة من جدول الفئات
                                string deleteQuery = "DELETE FROM Categories WHERE Id = @id";
                                using (MySqlCommand cmd = new MySqlCommand(deleteQuery, conn, trans))
                                {
                                    cmd.Parameters.AddWithValue("@id", cat.Id);
                                    cmd.ExecuteNonQuery();
                                }

                                // 2. تعيين قيمة الفئة للمنتجات المرتبطة إلى فارغ
                                string updateProductsQuery = "UPDATE Products SET Category = '' WHERE Category = @catName";
                                using (MySqlCommand cmd = new MySqlCommand(updateProductsQuery, conn, trans))
                                {
                                    cmd.Parameters.AddWithValue("@catName", cat.CategoryName);
                                    cmd.ExecuteNonQuery();
                                }

                                trans.Commit();
                                MessageBox.Show("تم حذف الفئة بنجاح!", "نجاح العملية", MessageBoxButton.OK, MessageBoxImage.Information);
                            }
                            catch
                            {
                                trans.Rollback();
                                throw;
                            }
                        }

                        ReloadAll();
                        _ = CloudSyncService.SyncMobileDataAsync(true);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("فشل حذف الفئة: " + ex.Message, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        #endregion

        #region 2. إدارة المنتجات (Products Management)

        /// <summary>
        /// جلب جميع المنتجات من قاعدة البيانات وتخزينها في الذاكرة
        /// </summary>
        private void LoadProducts()
        {
            try
            {
                allProducts.Clear();
                using (MySqlConnection conn = dbHelper.GetConnection())
                {
                    conn.Open();
                    string query = "SELECT Id, Barcode, ProductName, Category, SubCategory, PurchasePrice, SellPrice, StockQuantity, ImagePath FROM Products WHERE deleted_at IS NULL ORDER BY Id DESC";
                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    {
                        using (MySqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                string rawPath = reader["ImagePath"]?.ToString() ?? "";
                                string? resolvedPath = null;
                                if (!string.IsNullOrEmpty(rawPath))
                                {
                                    if (System.IO.Path.IsPathRooted(rawPath))
                                    {
                                        if (System.IO.File.Exists(rawPath))
                                        {
                                            resolvedPath = rawPath;
                                        }
                                        else
                                        {
                                            int index = rawPath.IndexOf("ProductImages", StringComparison.OrdinalIgnoreCase);
                                            if (index >= 0)
                                            {
                                                string relativeSub = rawPath.Substring(index);
                                                string checkPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativeSub);
                                                if (System.IO.File.Exists(checkPath))
                                                {
                                                    resolvedPath = checkPath;
                                                    try
                                                    {
                                                        string cleanRelative = relativeSub.Replace('\\', '/');
                                                        using (MySqlConnection conn2 = dbHelper.GetConnection())
                                                        {
                                                            conn2.Open();
                                                            string updateQuery = "UPDATE Products SET ImagePath = @imagePath WHERE Id = @id";
                                                            using (MySqlCommand updateCmd = new MySqlCommand(updateQuery, conn2))
                                                            {
                                                                updateCmd.Parameters.AddWithValue("@imagePath", cleanRelative);
                                                                updateCmd.Parameters.AddWithValue("@id", Convert.ToInt32(reader["Id"]));
                                                                updateCmd.ExecuteNonQuery();
                                                            }
                                                        }
                                                    }
                                                    catch (Exception ex)
                                                    {
                                                        System.Diagnostics.Debug.WriteLine($"Failed to auto-heal path in DB: {ex.Message}");
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    else
                                    {
                                        resolvedPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, rawPath);
                                    }
                                }

                                allProducts.Add(new Product
                                {
                                    Id = Convert.ToInt32(reader["Id"]),
                                    Barcode = reader["Barcode"].ToString() ?? "",
                                    ProductName = reader["ProductName"].ToString() ?? "",
                                    Category = reader["Category"].ToString() ?? "",
                                    SubCategory = reader["SubCategory"] != DBNull.Value ? reader["SubCategory"].ToString() ?? "" : "",
                                    PurchasePrice = Convert.ToDecimal(reader["PurchasePrice"]),
                                    SellPrice = Convert.ToDecimal(reader["SellPrice"]),
                                    StockQuantity = Convert.ToDecimal(reader["StockQuantity"]),
                                    ImagePath = resolvedPath
                                });
                            }
                        }
                    }
                }

                ApplyFilter();
            }
            catch (Exception ex)
            {
                MessageBox.Show("فشل تحميل المنتجات: " + ex.Message, "خطأ قاعدة بيانات", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        /// تطبيق فلترة وتصفية المنتجات بناءً على البحث والفئة المحددة
        /// </summary>
        private void ApplyFilter()
        {
            if (allProducts == null) return;

            string searchText = txtSearch.Text.Trim();
            CategoryItem? selectedCategory = lstCategories.SelectedItem as CategoryItem;

            var filtered = allProducts.AsEnumerable();

            // 1. فلترة الفئة
            if (selectedCategory != null && selectedCategory.Id != -1)
            {
                filtered = filtered.Where(p => 
                    string.Equals(p.Category, selectedCategory.CategoryName, StringComparison.OrdinalIgnoreCase) ||
                    ((selectedCategory.CategoryName == "منتوجات بالعبار" || selectedCategory.CategoryName == "منتوجات العبار") &&
                     (string.Equals(p.SubCategory, "منتوجات بالعبار", StringComparison.OrdinalIgnoreCase) || string.Equals(p.SubCategory, "منتوجات العبار", StringComparison.OrdinalIgnoreCase)))
                );
            }

            // 2. فلترة نص البحث
            if (!string.IsNullOrEmpty(searchText))
            {
                filtered = filtered.Where(p =>
                    (p.ProductName != null && p.ProductName.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (p.Barcode != null && p.Barcode.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
                );
            }

            var finalResult = filtered.ToList();

            // تحديث الجدول
            dgProducts.ItemsSource = null;
            dgProducts.ItemsSource = finalResult;

            // تحديث عدد النتائج
            lblResultsCount.Text = $"النتائج: {finalResult.Count} منتج";
        }

        /// <summary>
        /// فلترة فورية أثناء الكتابة
        /// </summary>
        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (searchDebounceTimer == null)
            {
                searchDebounceTimer = new System.Windows.Threading.DispatcherTimer();
                searchDebounceTimer.Interval = TimeSpan.FromMilliseconds(200); // 200ms for admin search is very comfortable
                searchDebounceTimer.Tick += (s, args) =>
                {
                    searchDebounceTimer.Stop();
                    ApplyFilter();
                };
            }
            searchDebounceTimer.Stop();
            searchDebounceTimer.Start();
        }

        /// <summary>
        /// زر فتح نموذج إضافة منتج جديد
        /// </summary>
        private void BtnAddProduct_Click(object sender, RoutedEventArgs e)
        {
            isEditModeProduct = false;
            editingProductId = 0;
            lblModalTitle.Text = "إضافة منتج جديد";
            
            // تفريغ المدخلات
            txtBarcode.Clear();
            txtProductName.Clear();
            txtPurchasePrice.Clear();
            txtSellPrice.Clear();
            txtStockQuantity.Clear();
            txtImagePath.Clear();
            if (cmbCategory.Items.Count > 0) cmbCategory.SelectedIndex = 0;
            cmbSubCategory.SelectedIndex = 0;

            modalOverlay.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// زر فتح نموذج تعديل بيانات المنتج المختار
        /// </summary>
        private void BtnEditProduct_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is Product product)
            {
                isEditModeProduct = true;
                editingProductId = product.Id;
                lblModalTitle.Text = "تعديل بيانات المنتج";

                // تعبئة البيانات في النموذج المنبثق
                txtBarcode.Text = product.Barcode;
                txtProductName.Text = product.ProductName;
                txtPurchasePrice.Text = product.PurchasePrice.ToString("0.00");
                txtSellPrice.Text = product.SellPrice.ToString("0.00");
                txtStockQuantity.Text = product.StockQuantity.ToString("0.###");
                txtImagePath.Text = product.ImagePath ?? "";
                
                // تحديد الفئة في الكومبوبوكس
                cmbCategory.SelectedItem = product.Category;

                // تحديد الفئة الثانوية
                if (product.SubCategory == "منتوجات العبار")
                    cmbSubCategory.SelectedIndex = 1;
                else
                    cmbSubCategory.SelectedIndex = 0;

                modalOverlay.Visibility = Visibility.Visible;
            }
        }

        /// <summary>
        /// زر نسخ المنتج المحدد مع مسح الباركود وفتح نموذج الإضافة
        /// </summary>
        private void BtnDuplicateProduct_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is Product product)
            {
                isEditModeProduct = false; // إضافة منتج جديد
                editingProductId = 0;
                lblModalTitle.Text = "نسخ المنتج (إضافة كمنتج جديد)";

                // تعبئة البيانات من المنتج الأصلي ما عدا الباركود
                txtBarcode.Text = ""; // مسح الباركود للنسخة المنسوخة
                txtProductName.Text = product.ProductName + " - نسخة";
                txtPurchasePrice.Text = product.PurchasePrice.ToString("0.00");
                txtSellPrice.Text = product.SellPrice.ToString("0.00");
                txtStockQuantity.Text = product.StockQuantity.ToString("0.###");
                txtImagePath.Text = product.ImagePath ?? "";

                // تحديد الفئة
                cmbCategory.SelectedItem = product.Category;

                // تحديد الفئة الثانوية
                if (product.SubCategory == "منتوجات العبار")
                    cmbSubCategory.SelectedIndex = 1;
                else
                    cmbSubCategory.SelectedIndex = 0;

                modalOverlay.Visibility = Visibility.Visible;
                txtBarcode.Focus();
            }
        }

        /// <summary>
        /// التحقق من الحقول وحفظ المنتج (إضافة أو تعديل)
        /// </summary>
        private void BtnSaveProduct_Click(object sender, RoutedEventArgs e)
        {
            string barcode = txtBarcode.Text.Trim();
            string productName = txtProductName.Text.Trim();
            string category = cmbCategory.SelectedItem?.ToString() ?? "";
            string subCategory = "";
            if (cmbSubCategory.SelectedIndex == 1)
            {
                subCategory = "منتوجات العبار";
            }
            
            if (string.IsNullOrEmpty(barcode) || string.IsNullOrEmpty(productName))
            {
                MessageBox.Show("الباركود واسم المنتج حقول إلزامية!", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!TryParseDecimal(txtPurchasePrice.Text, out decimal purchasePrice) || purchasePrice < 0)
            {
                MessageBox.Show("سعر الشراء يجب أن يكون قيمة رقمية موجبة!", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!TryParseDecimal(txtSellPrice.Text, out decimal sellPrice) || sellPrice < 0)
            {
                MessageBox.Show("سعر البيع يجب أن يكون قيمة رقمية موجبة!", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!TryParseDecimal(txtStockQuantity.Text, out decimal stockQuantity) || stockQuantity < 0)
            {
                MessageBox.Show("الكمية المتاحة يجب أن تكون قيمة رقمية موجبة!", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // التحقق من تكرار الباركود
            if (allProducts.Any(p => p.Barcode.Equals(barcode, StringComparison.OrdinalIgnoreCase) && p.Id != editingProductId))
            {
                MessageBox.Show("الباركود المدخل مستخدم بالفعل لمنتج آخر!", "خطأ الباركود", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            using (MySqlConnection conn = dbHelper.GetConnection())
            {
                try
                {
                    conn.Open();

                    // التأكد من وجود الفئة الرئيسية "منتوجات بالعبار" تلقائياً إذا تم تحديد الفئة الثانوية "منتوجات العبار"
                    if (subCategory == "منتوجات العبار")
                    {
                        EnsureCategoryExists(conn, "منتوجات بالعبار");
                    }

                    string query;
                    if (isEditModeProduct)
                    {
                        query = "UPDATE Products SET Barcode = @barcode, ProductName = @name, Category = @category, SubCategory = @subcategory, " +
                                "PurchasePrice = @purchase, SellPrice = @sell, StockQuantity = @stock, ImagePath = @imagePath WHERE Id = @id";
                    }
                    else
                    {
                        // الباركود فريد في القاعدة: إن وُجد منتج محذوف (حذف ناعم) بنفس الباركود نعيد تفعيله بدل الإدراج
                        query = "UPDATE Products SET ProductName = @name, Category = @category, SubCategory = @subcategory, " +
                                "PurchasePrice = @purchase, SellPrice = @sell, StockQuantity = @stock, ImagePath = @imagePath, deleted_at = NULL " +
                                "WHERE Barcode = @barcode AND deleted_at IS NOT NULL";
                    }

                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@barcode", barcode);
                        cmd.Parameters.AddWithValue("@name", productName);
                        cmd.Parameters.AddWithValue("@category", category);
                        cmd.Parameters.AddWithValue("@subcategory", string.IsNullOrEmpty(subCategory) ? (object)DBNull.Value : subCategory);
                        cmd.Parameters.AddWithValue("@purchase", purchasePrice);
                        cmd.Parameters.AddWithValue("@sell", sellPrice);
                        cmd.Parameters.AddWithValue("@stock", stockQuantity);
                        string processedImagePath = ProcessProductImage(txtImagePath.Text.Trim(), category, barcode);
                        cmd.Parameters.AddWithValue("@imagePath", string.IsNullOrEmpty(processedImagePath) ? (object)DBNull.Value : processedImagePath);
                        if (isEditModeProduct)
                        {
                            cmd.Parameters.AddWithValue("@id", editingProductId);
                        }

                        int affected = cmd.ExecuteNonQuery();
                        if (!isEditModeProduct && affected == 0)
                        {
                            cmd.CommandText = "INSERT INTO Products (Barcode, ProductName, Category, SubCategory, PurchasePrice, SellPrice, StockQuantity, ImagePath) " +
                                              "VALUES (@barcode, @name, @category, @subcategory, @purchase, @sell, @stock, @imagePath)";
                            cmd.ExecuteNonQuery();
                        }
                    }

                    MessageBox.Show("تم حفظ بيانات المنتج بنجاح!", "تم العملية", MessageBoxButton.OK, MessageBoxImage.Information);
                    modalOverlay.Visibility = Visibility.Collapsed;
                    LoadProducts();
                    _ = CloudSyncService.SyncMobileDataAsync(true);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("فشل حفظ المنتج: " + ex.Message, "خطأ قاعدة بيانات", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// إلغاء إغلاق نافذة المنتج المنبثقة
        /// </summary>
        private void BtnCancelProduct_Click(object sender, RoutedEventArgs e)
        {
            modalOverlay.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// معالجة نقرات لوحة الأرقام الجانبية للنافذة المنبثقة
        /// </summary>
        private void ProductModalKeypad_Click(object sender, RoutedEventArgs e)
        {
            var activeBox = System.Windows.Input.FocusManager.GetFocusedElement(System.Windows.Window.GetWindow(this)) as TextBox;
            if (activeBox == null) return;

            // السماح فقط بإدخال الأرقام في الحقول الرقمية أو حقل الباركود
            if (activeBox != txtBarcode && activeBox != txtPurchasePrice && activeBox != txtSellPrice && activeBox != txtStockQuantity) return;

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
                activeBox.Focus();
                activeBox.SelectionStart = activeBox.Text.Length;
            }
        }

        /// <summary>
        /// حذف منتج بالكامل
        /// </summary>
        private void BtnDeleteProduct_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is Product product)
            {
                MessageBoxResult result = MessageBox.Show($"هل تود حذف المنتج '{product.ProductName}' من المخازن؟", "تأكيد الحذف", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes) return;

                using (MySqlConnection conn = dbHelper.GetConnection())
                {
                    try
                    {
                        conn.Open();
                        // حذف ناعم: يبقى السجل لتاريخ المبيعات ولكي تعلم الهواتف بالحذف عند المزامنة
                        string query = "UPDATE Products SET deleted_at = UTC_TIMESTAMP(3) WHERE Id = @id";
                        using (MySqlCommand cmd = new MySqlCommand(query, conn))
                        {
                            cmd.Parameters.AddWithValue("@id", product.Id);
                            cmd.ExecuteNonQuery();
                        }

                        MessageBox.Show("تم حذف المنتج بنجاح!", "نجاح العملية", MessageBoxButton.OK, MessageBoxImage.Information);
                        LoadProducts();
                        _ = CloudSyncService.SyncMobileDataAsync(true);
                    }
                    catch (MySqlException ex) when (ex.Number == 1451)
                    {
                        MessageBox.Show("لا يمكن حذف هذا المنتج لوجود مبيعات مرتبطة به! يرجى تصفير كميته بدلاً من حذفه.", "خطأ في الحذف", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("فشل حذف المنتج: " + ex.Message, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        #endregion

        #region 3. أزرار المنيو والتنقل الجانبي والعمليات الفرعية

        private void BtnReload_Click(object sender, RoutedEventArgs e)
        {
            ReloadAll();
        }

        // Redundant sidebar methods removed since hosted in MainWindow container

        private void BtnFilterPlaceholder_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("تصفية متقدمة للمنتجات حسب السعر ونفاد الكميات.", "ميزة الفلترة", MessageBoxButton.OK, MessageBoxImage.Information);
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

        /// <summary>
        /// تعديل كمية المخزون لجميع المنتجات دفعة واحدة (الافتراضي 100)
        /// </summary>
        private void BtnSetAllStockTo100_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                int totalProducts = allProducts.Count(p => p.Barcode != "MANUAL");
                if (totalProducts == 0)
                {
                    MessageBox.Show("لا توجد منتجات مسجلة في المخزون لتعديلها.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var dialog = new BulkStockUpdateDialog(totalProducts)
                {
                    Owner = Window.GetWindow(this)
                };

                if (dialog.ShowDialog() == true)
                {
                    decimal newQty = dialog.TargetQuantity;

                    // تنفيذ التعديل في قاعدة البيانات دفعة واحدة لجميع المنتجات (باستثناء المنتج اليدوي)
                    using (MySqlConnection conn = dbHelper.GetConnection())
                    {
                        conn.Open();
                        string updateQuery = "UPDATE Products SET StockQuantity = @newQty WHERE Barcode != 'MANUAL' OR Barcode IS NULL;";
                        using (MySqlCommand cmd = new MySqlCommand(updateQuery, conn))
                        {
                            cmd.Parameters.AddWithValue("@newQty", newQty);
                            int affectedRows = cmd.ExecuteNonQuery();

                            // إعادة تحميل المنتجات في جدول العرض والذاكرة
                            LoadProducts();

                            // تحديث المزامنة السحابية لتطبيق الهاتف تلقائياً
                            _ = CloudSyncService.SyncMobileDataAsync(true);

                            MessageBox.Show(
                                $"تم بنجاح تحديث كمية المخزون لجميع المنتجات ({affectedRows} منتج) لتصبح: {newQty:0.###} قطعة!",
                                "نجاح التعديل",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("حدث خطأ أثناء تعديل كميات المخزون: " + ex.Message, "خطأ قاعدة بيانات", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnExcelImportPlaceholder_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Excel Files (*.xlsx; *.xls)|*.xlsx;*.xls|All Files (*.*)|*.*",
                Title = "اختر ملف إكسل لاستيراد المنتجات"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                string filePath = openFileDialog.FileName;
                ImportProductsFromExcel(filePath);
            }
        }

        private void BtnAutoLinkImages_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFolderDialog
                {
                    Title = "اختر المجلد المحتوي على صور المنتجات",
                    Multiselect = false
                };

                if (dialog.ShowDialog() == true)
                {
                    string folderPath = dialog.FolderName;
                    AutoLinkImagesFromFolder(folderPath);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("حدث خطأ أثناء فتح نافذة اختيار المجلد: " + ex.Message, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AutoLinkImagesFromFolder(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath) || !System.IO.Directory.Exists(folderPath))
            {
                MessageBox.Show("المجلد المحدد غير موجود!", "خطأ", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".webp", ".gif" };
                var imageFiles = System.IO.Directory.GetFiles(folderPath, "*.*", System.IO.SearchOption.AllDirectories)
                    .Where(file => allowedExtensions.Contains(System.IO.Path.GetExtension(file).ToLower()))
                    .ToList();

                if (imageFiles.Count == 0)
                {
                    MessageBox.Show("لم يتم العثور على أي صور في المجلد المحدد!", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                int successfullyLinked = 0;
                var dbProducts = new System.Collections.Generic.List<Product>();

                using (MySqlConnection conn = dbHelper.GetConnection())
                {
                    conn.Open();
                    string query = "SELECT Id, Barcode, ProductName, Category FROM Products WHERE deleted_at IS NULL";
                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            dbProducts.Add(new Product
                            {
                                Id = Convert.ToInt32(reader["Id"]),
                                Barcode = reader["Barcode"]?.ToString() ?? "",
                                ProductName = reader["ProductName"]?.ToString() ?? "",
                                Category = reader["Category"]?.ToString() ?? ""
                            });
                        }
                    }
                }

                string appBaseDir = AppDomain.CurrentDomain.BaseDirectory;
                
                using (MySqlConnection conn = dbHelper.GetConnection())
                {
                    conn.Open();

                    foreach (var imgPath in imageFiles)
                    {
                        string fileNameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(imgPath).Trim();
                        string ext = System.IO.Path.GetExtension(imgPath).ToLower();

                        var product = dbProducts.FirstOrDefault(p => 
                            p.Barcode.Equals(fileNameWithoutExt, StringComparison.OrdinalIgnoreCase) && 
                            p.Barcode != "MANUAL");

                        if (product == null)
                        {
                            string normalizedFileName = NormalizeString(fileNameWithoutExt);
                            
                            product = dbProducts.FirstOrDefault(p => 
                                p.Barcode != "MANUAL" && 
                                (NormalizeString(p.ProductName).Equals(normalizedFileName, StringComparison.OrdinalIgnoreCase) || 
                                 NormalizeString(p.ProductName).Contains(normalizedFileName) || 
                                 normalizedFileName.Contains(NormalizeString(p.ProductName))));
                        }

                        if (product != null)
                        {
                            try
                            {
                                string safeCategory = product.Category;
                                foreach (char c in System.IO.Path.GetInvalidFileNameChars())
                                {
                                    safeCategory = safeCategory.Replace(c, '_');
                                }
                                if (string.IsNullOrEmpty(safeCategory)) safeCategory = "Uncategorized";

                                string targetDir = System.IO.Path.Combine(appBaseDir, "ProductImages", safeCategory);
                                if (!System.IO.Directory.Exists(targetDir))
                                {
                                    System.IO.Directory.CreateDirectory(targetDir);
                                }

                                string safeBarcode = product.Barcode;
                                foreach (char c in System.IO.Path.GetInvalidFileNameChars())
                                {
                                    safeBarcode = safeBarcode.Replace(c, '_');
                                }
                                string targetFileName = safeBarcode + ext;
                                string targetPath = System.IO.Path.Combine(targetDir, targetFileName);

                                System.IO.File.Copy(imgPath, targetPath, true);

                                string relativePath = System.IO.Path.Combine("ProductImages", safeCategory, targetFileName).Replace('\\', '/');
                                string updateQuery = "UPDATE Products SET ImagePath = @imagePath WHERE Id = @id";
                                using (MySqlCommand updateCmd = new MySqlCommand(updateQuery, conn))
                                {
                                    updateCmd.Parameters.AddWithValue("@imagePath", relativePath);
                                    updateCmd.Parameters.AddWithValue("@id", product.Id);
                                    updateCmd.ExecuteNonQuery();
                                }

                                successfullyLinked++;
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Failed to link image {imgPath} to product {product.Barcode}: {ex.Message}");
                            }
                        }
                    }
                }

                MessageBox.Show($"تمت عملية الربط التلقائي بنجاح!\n\n" +
                                $"إجمالي الصور المكتشفة: {imageFiles.Count}\n" +
                                $"المنتجات التي تم ربط صورها بنجاح: {successfullyLinked}", 
                                "اكتمال العملية", MessageBoxButton.OK, MessageBoxImage.Information);

                LoadProducts();
            }
            catch (Exception ex)
            {
                MessageBox.Show("حدث خطأ أثناء عملية الربط التلقائي للصور: " + ex.Message, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string NormalizeString(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            var chars = input.ToCharArray();
            var sb = new System.Text.StringBuilder();
            foreach (var c in chars)
            {
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(c);
                }
            }
            return sb.ToString().ToLower();
        }


        private void BtnExcelExport_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var saveFileDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "Excel Files (*.xlsx)|*.xlsx",
                    FileName = "المنتجات.xlsx",
                    DefaultExt = ".xlsx",
                    Title = "حفظ ملف تصدير المنتجات"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    string filePath = saveFileDialog.FileName;

                    using (var workbook = new ClosedXML.Excel.XLWorkbook())
                    {
                        var worksheet = workbook.Worksheets.Add("المنتجات");
                        worksheet.RightToLeft = true;

                        // Headers
                        worksheet.Cell(1, 1).Value = "صورة المنتج";
                        worksheet.Cell(1, 2).Value = "الباركود";
                        worksheet.Cell(1, 3).Value = "اسم المنتج";
                        worksheet.Cell(1, 4).Value = "الفئة";
                        worksheet.Cell(1, 5).Value = "سعر الشراء";
                        worksheet.Cell(1, 6).Value = "سعر البيع";
                        worksheet.Cell(1, 7).Value = "الكمية المتاحة";

                        // Format Headers
                        var headerRange = worksheet.Range("A1:G1");
                        headerRange.Style.Font.Bold = true;
                        headerRange.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#27AE60");
                        headerRange.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;

                        int row = 2;
                        var sortedProducts = allProducts.OrderBy(p => p.Category).ToList();
                        foreach (var product in sortedProducts)
                        {
                            if (product.Barcode == "MANUAL")
                                continue;

                            worksheet.Cell(row, 2).Value = product.Barcode;
                            worksheet.Cell(row, 3).Value = product.ProductName;
                            worksheet.Cell(row, 4).Value = product.Category;
                            worksheet.Cell(row, 5).Value = product.PurchasePrice;
                            worksheet.Cell(row, 6).Value = product.SellPrice;
                            worksheet.Cell(row, 7).Value = product.StockQuantity;

                            if (!string.IsNullOrEmpty(product.ImagePath) && System.IO.File.Exists(product.ImagePath))
                            {
                                try
                                {
                                    worksheet.Row(row).Height = 45;
                                    var pic = worksheet.AddPicture(product.ImagePath)
                                                       .MoveTo(worksheet.Cell(row, 1))
                                                       .WithSize(50, 50);
                                }
                                catch
                                {
                                    // Fallback to text in case image could not be loaded
                                    worksheet.Cell(row, 1).Value = product.ImagePath;
                                }
                            }
                            else
                            {
                                worksheet.Cell(row, 1).Value = "";
                            }
                            row++;
                        }

                        worksheet.Columns().AdjustToContents();
                        worksheet.Column(1).Width = 12; // Maintain width for image column after adjustment
                        workbook.SaveAs(filePath);
                    }

                    MessageBox.Show("تم تصدير المنتجات بنجاح!", "تصدير البيانات", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("فشل تصدير المنتجات: " + ex.Message, "خطأ تصدير", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ImportProductsFromExcel(string filePath)
        {
            try
            {
                // Register code pages encoding provider for .NET Core
                System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

                int importedCount = 0;
                int updatedCount = 0;
                int skippedCount = 0;

                using (var workbook = new ClosedXML.Excel.XLWorkbook(filePath))
                {
                    var worksheet = workbook.Worksheet(1);
                    var range = worksheet.RangeUsed();
                    if (range == null)
                    {
                        MessageBox.Show("الملف المختار فارغ أو غير صالح!", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    var rows = range.RowsUsed().ToList();
                    if (rows.Count <= 1)
                    {
                        MessageBox.Show("لا توجد بيانات كافية في الملف لاستيرادها!", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    // Let's identify column headers and map their indexes (1-based)
                    int imageIdx = -1;
                    int barcodeIdx = -1;
                    int nameIdx = -1;
                    int catIdx = -1;
                    int purchaseIdx = -1;
                    int sellIdx = -1;
                    int qtyIdx = -1;

                    var headerRow = rows[0];
                    int lastCellNum = headerRow.LastCellUsed()?.Address.ColumnNumber ?? 1;

                    for (int i = 1; i <= lastCellNum; i++)
                    {
                        string colVal = headerRow.Cell(i).GetString().Trim();
                        if (colVal.Contains("صورة المنتج") || colVal.Contains("الصورة") || colVal.Contains("صورة") || colVal.Equals("ImagePath", StringComparison.OrdinalIgnoreCase) || colVal.Equals("Image", StringComparison.OrdinalIgnoreCase)) imageIdx = i;
                        else if (colVal.Contains("الباركود") || colVal.Equals("Barcode", StringComparison.OrdinalIgnoreCase)) barcodeIdx = i;
                        else if (colVal.Contains("اسم المنتج") || colVal.Contains("المنتج") || colVal.Equals("ProductName", StringComparison.OrdinalIgnoreCase) || colVal.Equals("Name", StringComparison.OrdinalIgnoreCase)) nameIdx = i;
                        else if (colVal.Contains("الفئة") || colVal.Equals("Category", StringComparison.OrdinalIgnoreCase)) catIdx = i;
                        else if (colVal.Contains("سعر الشراء") || colVal.Equals("PurchasePrice", StringComparison.OrdinalIgnoreCase)) purchaseIdx = i;
                        else if (colVal.Contains("سعر البيع") || colVal.Equals("SellPrice", StringComparison.OrdinalIgnoreCase) || colVal.Equals("Price", StringComparison.OrdinalIgnoreCase)) sellIdx = i;
                        else if (colVal.Contains("الكمية المتاحة") || colVal.Contains("الكمية") || colVal.Equals("StockQuantity", StringComparison.OrdinalIgnoreCase) || colVal.Equals("Qty", StringComparison.OrdinalIgnoreCase) || colVal.Equals("Quantity", StringComparison.OrdinalIgnoreCase)) qtyIdx = i;
                    }

                    // Fallback mapping if headers are not found by name
                    if (lastCellNum >= 7)
                    {
                        if (imageIdx == -1) imageIdx = 1;
                        if (barcodeIdx == -1) barcodeIdx = 2;
                        if (nameIdx == -1) nameIdx = 3;
                        if (catIdx == -1) catIdx = 4;
                        if (purchaseIdx == -1) purchaseIdx = 5;
                        if (sellIdx == -1) sellIdx = 6;
                        if (qtyIdx == -1) qtyIdx = 7;
                    }
                    else
                    {
                        if (barcodeIdx == -1) barcodeIdx = 1;
                        if (nameIdx == -1) nameIdx = 2;
                        if (catIdx == -1) catIdx = 3;
                        if (purchaseIdx == -1) purchaseIdx = 4;
                        if (sellIdx == -1) sellIdx = 5;
                        if (qtyIdx == -1) qtyIdx = 6;
                        imageIdx = -1;
                    }

                    // Extract pictures and map them to their rows
                    var rowPictures = new Dictionary<int, byte[]>();
                    if (imageIdx != -1)
                    {
                        foreach (var picture in worksheet.Pictures)
                        {
                            int picRow = picture.TopLeftCell.Address.RowNumber;
                            int picCol = picture.TopLeftCell.Address.ColumnNumber;
                            if (picCol == imageIdx)
                            {
                                try
                                {
                                    rowPictures[picRow] = picture.ImageStream.ToArray();
                                }
                                catch
                                {
                                    // Ignore individual picture loading error
                                }
                            }
                        }
                    }

                    // Start reading data from row 1 (row 0 is header)
                    using (var conn = dbHelper.GetConnection())
                    {
                        conn.Open();

                        for (int r = 1; r < rows.Count; r++)
                        {
                            var row = rows[r];

                            // Skip empty rows
                            if (string.IsNullOrEmpty(row.Cell(barcodeIdx).GetString()) && string.IsNullOrEmpty(row.Cell(nameIdx).GetString()))
                            {
                                skippedCount++;
                                continue;
                            }

                            string barcode = row.Cell(barcodeIdx).GetString().Trim();
                            string name = row.Cell(nameIdx).GetString().Trim();
                            string category = catIdx != -1 ? row.Cell(catIdx).GetString().Trim() : "عام";
                            if (string.IsNullOrEmpty(category)) category = "عام";
                            
                            if (string.IsNullOrEmpty(barcode) || string.IsNullOrEmpty(name))
                            {
                                skippedCount++;
                                continue;
                            }

                            // Parse numbers safely
                            decimal purchasePrice = 0;
                            decimal sellPrice = 0;
                            decimal stockQty = 0;

                            if (purchaseIdx != -1)
                                decimal.TryParse(row.Cell(purchaseIdx).GetString(), out purchasePrice);
                            if (sellIdx != -1)
                                decimal.TryParse(row.Cell(sellIdx).GetString(), out sellPrice);
                            if (qtyIdx != -1)
                                decimal.TryParse(row.Cell(qtyIdx).GetString(), out stockQty);

                            // Extract and save the embedded image if it exists
                            string? imagePath = null;
                            bool hasImage = false;
                            if (imageIdx != -1 && rowPictures.TryGetValue(row.RowNumber(), out byte[]? imgBytes) && imgBytes != null && imgBytes.Length > 0)
                            {
                                try
                                {
                                    string safeCategory = category;
                                    foreach (char c in System.IO.Path.GetInvalidFileNameChars())
                                    {
                                        safeCategory = safeCategory.Replace(c, '_');
                                    }
                                    if (string.IsNullOrEmpty(safeCategory)) safeCategory = "Uncategorized";

                                    string imagesDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ProductImages", safeCategory);
                                    if (!System.IO.Directory.Exists(imagesDir))
                                    {
                                        System.IO.Directory.CreateDirectory(imagesDir);
                                    }
                                    string imageFileName = $"{barcode}.png";
                                    string destPath = System.IO.Path.Combine(imagesDir, imageFileName);
                                    System.IO.File.WriteAllBytes(destPath, imgBytes);
                                    imagePath = System.IO.Path.Combine("ProductImages", safeCategory, imageFileName);
                                    hasImage = true;
                                }
                                catch
                                {
                                    // Ignore image saving error
                                }
                            }

                            // 1. Ensure Category exists in Categories table
                            EnsureCategoryExists(conn, category);

                            // 2. Check if product already exists
                            bool exists = CheckProductExists(conn, barcode);

                            string query;
                            if (exists)
                            {
                                if (imageIdx != -1 && hasImage)
                                {
                                    // deleted_at = NULL: استيراد منتج محذوف سابقاً يعيد تفعيله
                                    query = "UPDATE Products SET ProductName = @name, Category = @category, " +
                                            "PurchasePrice = @purchase, SellPrice = @sell, StockQuantity = @stock, ImagePath = @imagePath, deleted_at = NULL WHERE Barcode = @barcode";
                                }
                                else
                                {
                                    query = "UPDATE Products SET ProductName = @name, Category = @category, " +
                                            "PurchasePrice = @purchase, SellPrice = @sell, StockQuantity = @stock, deleted_at = NULL WHERE Barcode = @barcode";
                                }
                                updatedCount++;
                            }
                            else
                            {
                                query = "INSERT INTO Products (Barcode, ProductName, Category, PurchasePrice, SellPrice, StockQuantity, ImagePath) " +
                                        "VALUES (@barcode, @name, @category, @purchase, @sell, @stock, @imagePath)";
                                importedCount++;
                            }

                            using (var cmd = new MySqlCommand(query, conn))
                            {
                                cmd.Parameters.AddWithValue("@barcode", barcode);
                                cmd.Parameters.AddWithValue("@name", name);
                                cmd.Parameters.AddWithValue("@category", category);
                                cmd.Parameters.AddWithValue("@purchase", purchasePrice);
                                cmd.Parameters.AddWithValue("@sell", sellPrice);
                                cmd.Parameters.AddWithValue("@stock", stockQty);
                                if (imageIdx != -1 && hasImage)
                                {
                                    cmd.Parameters.AddWithValue("@imagePath", imagePath);
                                }
                                else if (!exists)
                                {
                                    cmd.Parameters.AddWithValue("@imagePath", DBNull.Value);
                                }
                                cmd.ExecuteNonQuery();
                            }
                        }
                    }
                }

                MessageBox.Show($"تم الاستيراد بنجاح!\n\nالمنتجات الجديدة: {importedCount}\nالمنتجات المحدثة: {updatedCount}\nصفوف تم تخطيها: {skippedCount}", 
                                "نجاح الاستيراد", MessageBoxButton.OK, MessageBoxImage.Information);

                // Reload data
                ReloadAll();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"حدث خطأ أثناء قراءة ملف إكسل:\n{ex.Message}", "خطأ في الاستيراد", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void EnsureCategoryExists(MySqlConnection conn, string categoryName)
        {
            if (string.IsNullOrEmpty(categoryName)) return;

            string checkQuery = "SELECT COUNT(*) FROM Categories WHERE CategoryName = @name";
            using (var checkCmd = new MySqlCommand(checkQuery, conn))
            {
                checkCmd.Parameters.AddWithValue("@name", categoryName);
                long count = Convert.ToInt64(checkCmd.ExecuteScalar());
                if (count == 0)
                {
                    string insertQuery = "INSERT INTO Categories (CategoryName) VALUES (@name)";
                    using (var insertCmd = new MySqlCommand(insertQuery, conn))
                    {
                        insertCmd.Parameters.AddWithValue("@name", categoryName);
                        insertCmd.ExecuteNonQuery();
                    }
                }
            }
        }

        private bool CheckProductExists(MySqlConnection conn, string barcode)
        {
            if (string.IsNullOrEmpty(barcode)) return false;

            string checkQuery = "SELECT COUNT(*) FROM Products WHERE Barcode = @barcode";
            using (var checkCmd = new MySqlCommand(checkQuery, conn))
            {
                checkCmd.Parameters.AddWithValue("@barcode", barcode);
                long count = Convert.ToInt64(checkCmd.ExecuteScalar());
                return count > 0;
            }
        }

        private bool TryParseDecimal(string input, out decimal result)
        {
            result = 0;
            if (string.IsNullOrEmpty(input)) return false;
            string normalized = input.Replace(',', '.').Trim();
            return decimal.TryParse(normalized, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out result);
        }

        private void BtnGenerateBarcode_Click(object sender, RoutedEventArgs e)
        {
            string newBarcode = GenerateUniqueBarcode();
            txtBarcode.Text = newBarcode;
        }

        private string GenerateUniqueBarcode()
        {
            Random rand = new Random();
            string barcode = "";
            bool isUnique = false;

            using (MySqlConnection conn = dbHelper.GetConnection())
            {
                try
                {
                    conn.Open();
                    while (!isUnique)
                    {
                        // Generate a 12-digit barcode starting with 200 (standard for internal use)
                        string randomPart = "";
                        for (int i = 0; i < 9; i++)
                        {
                            randomPart += rand.Next(0, 10).ToString();
                        }
                        barcode = "200" + randomPart;

                        // Check in memory list first
                        bool existsInMemory = allProducts.Any(p => p.Barcode.Equals(barcode, StringComparison.OrdinalIgnoreCase));
                        if (existsInMemory) continue;

                        // Check in database
                        using (MySqlCommand cmd = new MySqlCommand("SELECT COUNT(*) FROM Products WHERE Barcode = @barcode", conn))
                        {
                            cmd.Parameters.AddWithValue("@barcode", barcode);
                            long count = Convert.ToInt64(cmd.ExecuteScalar());
                            if (count == 0)
                            {
                                isUnique = true;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Fallback to memory-only check if database connection fails
                    System.Diagnostics.Debug.WriteLine("Database check failed in GenerateUniqueBarcode: " + ex.Message);
                    while (!isUnique)
                    {
                        string randomPart = "";
                        for (int i = 0; i < 9; i++)
                        {
                            randomPart += rand.Next(0, 10).ToString();
                        }
                        barcode = "200" + randomPart;
                        isUnique = !allProducts.Any(p => p.Barcode.Equals(barcode, StringComparison.OrdinalIgnoreCase));
                    }
                }
            }

            return barcode;
        }

        private void BtnActionPlaceholder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                MessageBox.Show($"تم النقر على إجراء: {btn.Content} للمنتج المحدد.", "عمليات فرعية", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnBrowseImage_Click(object sender, RoutedEventArgs e)
        {
            var fileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "صور المنتجات (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp|كل الملفات (*.*)|*.*",
                Title = "اختر صورة المنتج"
            };

            if (fileDialog.ShowDialog() == true)
            {
                txtImagePath.Text = fileDialog.FileName;
            }
        }

        private void TxtImagePath_TextChanged(object sender, TextChangedEventArgs e)
        {
            string path = txtImagePath.Text.Trim();
            if (string.IsNullOrEmpty(path))
            {
                imgPreview.Source = null;
                txtNoPreview.Visibility = Visibility.Visible;
            }
            else
            {
                try
                {
                    if (System.IO.File.Exists(path))
                    {
                        imgPreview.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(path));
                        txtNoPreview.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        imgPreview.Source = null;
                        txtNoPreview.Visibility = Visibility.Visible;
                    }
                }
                catch
                {
                    imgPreview.Source = null;
                    txtNoPreview.Visibility = Visibility.Visible;
                }
            }
        }

        private string ProcessProductImage(string sourcePath, string category, string barcode)
        {
            if (string.IsNullOrEmpty(sourcePath)) return null;

            string safeCategory = category;
            foreach (char c in System.IO.Path.GetInvalidFileNameChars())
            {
                safeCategory = safeCategory.Replace(c, '_');
            }
            if (string.IsNullOrEmpty(safeCategory)) safeCategory = "Uncategorized";

            string appBaseDir = AppDomain.CurrentDomain.BaseDirectory;
            string targetDir = System.IO.Path.Combine(appBaseDir, "ProductImages", safeCategory);
            
            string ext = System.IO.Path.GetExtension(sourcePath);
            if (string.IsNullOrEmpty(ext)) ext = ".jpg";
            
            string safeBarcode = barcode;
            foreach (char c in System.IO.Path.GetInvalidFileNameChars())
            {
                safeBarcode = safeBarcode.Replace(c, '_');
            }
            string targetFileName = safeBarcode + ext;
            string targetPath = System.IO.Path.Combine(targetDir, targetFileName);

            if (string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
            {
                return System.IO.Path.Combine("ProductImages", safeCategory, targetFileName);
            }

            string absoluteSourcePath = sourcePath;
            if (!System.IO.Path.IsPathRooted(sourcePath))
            {
                absoluteSourcePath = System.IO.Path.Combine(appBaseDir, sourcePath);
            }

            if (!System.IO.File.Exists(absoluteSourcePath))
            {
                return null;
            }

            if (!System.IO.Directory.Exists(targetDir))
            {
                System.IO.Directory.CreateDirectory(targetDir);
            }

            try
            {
                System.IO.File.Copy(absoluteSourcePath, targetPath, true);

                if (!string.Equals(absoluteSourcePath, targetPath, StringComparison.OrdinalIgnoreCase) && 
                    absoluteSourcePath.StartsWith(System.IO.Path.Combine(appBaseDir, "ProductImages"), StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        System.IO.File.Delete(absoluteSourcePath);
                    }
                    catch { }
                }

                return System.IO.Path.Combine("ProductImages", safeCategory, targetFileName);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error processing product image: {ex.Message}");
                if (absoluteSourcePath.StartsWith(appBaseDir, StringComparison.OrdinalIgnoreCase))
                {
                    return System.IO.Path.GetRelativePath(appBaseDir, absoluteSourcePath);
                }
                return sourcePath;
            }
        }

        #endregion
    }

    /// <summary>
    /// كائن الفئة بقاعدة البيانات
    /// </summary>
    public class CategoryItem
    {
        public int Id { get; set; }
        public string CategoryName { get; set; } = string.Empty;
    }
}
