using System;
using System.Globalization;
using System.Windows;

namespace LmimaShopApp
{
    /// <summary>
    /// نافذة حوار لتحديد كمية المخزون الجديدة لجميع المنتجات دفعة واحدة
    /// </summary>
    public partial class BulkStockUpdateDialog : Window
    {
        public decimal TargetQuantity { get; private set; } = 100;

        public BulkStockUpdateDialog(int productCount)
        {
            InitializeComponent();
            lblProductCount.Text = $"سيتم تطبيق التعديل على جميع المنتجات المسجلة ({productCount} منتج)";

            Loaded += (s, e) =>
            {
                txtQuantity.Focus();
                txtQuantity.SelectAll();
            };

            txtQuantity.GotFocus += (s, e) =>
            {
                KeyboardHelper.ShowTouchKeyboard();
            };
        }

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            string input = txtQuantity.Text.Trim();
            if (string.IsNullOrEmpty(input))
            {
                MessageBox.Show("يرجى إدخال كمية المخزون المطلوبة.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtQuantity.Focus();
                return;
            }

            if (!decimal.TryParse(input, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal qty) &&
                !decimal.TryParse(input, NumberStyles.Any, CultureInfo.CurrentCulture, out qty))
            {
                MessageBox.Show("الرجاء إدخال رقم صحيح أو عشري صالح للكمية.", "خطأ في الإدخال", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtQuantity.Focus();
                txtQuantity.SelectAll();
                return;
            }

            if (qty < 0)
            {
                MessageBox.Show("لا يمكن أن تكون كمية المخزون سالبة (أدخل 0 أو قيمة موجبة).", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtQuantity.Focus();
                txtQuantity.SelectAll();
                return;
            }

            TargetQuantity = qty;
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
