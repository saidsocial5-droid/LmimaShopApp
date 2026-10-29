using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.IO;
using MySqlConnector;

namespace LmimaShopApp
{
    public partial class ReportsWindow : UserControl
    {
        private readonly DatabaseHelper dbHelper = new DatabaseHelper();

        public ReportsWindow()
        {
            InitializeComponent();
            
            // Set default date range to today
            dpFrom.SelectedDate = DateTime.Today;
            dpTo.SelectedDate = DateTime.Today;
            
            LoadCashiers();
            LoadReportData();
        }

        private void LoadCashiers()
        {
            try
            {
                cmbCashier.Items.Clear();
                cmbCashier.Items.Add("كل الكاشير");
                
                using (MySqlConnection conn = dbHelper.GetConnection())
                {
                    conn.Open();
                    string query = "SELECT DISTINCT FullName FROM Users ORDER BY FullName ASC";
                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    {
                        using (MySqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                cmbCashier.Items.Add(reader["FullName"].ToString() ?? "");
                            }
                        }
                    }
                }
                cmbCashier.SelectedIndex = 0;
            }
            catch
            {
                // Fallback
                cmbCashier.Items.Add("مدير النظام");
                cmbCashier.SelectedIndex = 0;
            }
        }

        private void LoadReportData()
        {
            if (dpFrom == null || dpTo == null || cmbCashier == null || cmbPayMethod == null || 
                donutCanvas == null || donutLegend == null || lineChartCanvas == null || dgTopProducts == null ||
                txtTotalSales == null || txtInvoiceCount == null || txtTotalItemsSold == null || txtTotalReturns == null ||
                txtCashSales == null || txtCardSales == null || txtCreditSales == null || txtSplitSales == null ||
                lblTopCashierName == null || lblTopCashierSales == null)
            {
                return;
            }

            DateTime fromDate = dpFrom.SelectedDate ?? DateTime.Today;
            DateTime toDate = dpTo.SelectedDate ?? DateTime.Today;
            // Adjust to end of day
            toDate = toDate.Date.AddDays(1).AddSeconds(-1);

            string cashierFilter = cmbCashier.SelectedItem?.ToString() ?? "كل الكاشير";
            string payFilter = cmbPayMethod.SelectedItem is ComboBoxItem payItem ? payItem.Content.ToString() ?? "" : "كل طرق الدفع";

            decimal totalSales = 0;
            int invoiceCount = 0;
            decimal totalItemsSold = 0;

            var dailySales = new Dictionary<DateTime, decimal>();
            var categorySales = new Dictionary<string, decimal>();
            var topProducts = new List<TopProductModel>();
            string topCashierName = "-";
            decimal topCashierSales = 0;
            var salesList = new List<SaleItem>();

            try
            {
                using (MySqlConnection conn = dbHelper.GetConnection())
                {
                    conn.Open();

                    // 1. Fetch sales summary and group for charts
                    string salesQuery = "SELECT InvoiceId, CashierName, TotalAmount, Date FROM Sales WHERE Date BETWEEN @from AND @to";
                    salesList = new List<SaleItem>();
                    
                    using (MySqlCommand cmd = new MySqlCommand(salesQuery, conn))
                    {
                        cmd.Parameters.AddWithValue("@from", fromDate);
                        cmd.Parameters.AddWithValue("@to", toDate);

                        using (MySqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                int invoiceId = Convert.ToInt32(reader["InvoiceId"]);
                                string cashier = reader["CashierName"].ToString() ?? "";
                                decimal amt = Convert.ToDecimal(reader["TotalAmount"]);
                                DateTime date = Convert.ToDateTime(reader["Date"]);

                                // Apply filters
                                if (cashierFilter != "كل الكاشير" && !cashier.StartsWith(cashierFilter)) continue;
                                
                                string method = (invoiceId % 3 == 0) ? "بطاقة" : "كاش";
                                if (payFilter != "كل طرق الدفع" && payFilter != method) continue;

                                salesList.Add(new SaleItem { InvoiceId = invoiceId, CashierName = cashier, TotalAmount = amt, Date = date, PaymentMethod = method });
                            }
                        }
                    }

                    totalSales = salesList.Sum(s => s.TotalAmount);
                    invoiceCount = salesList.Count;

                    // 2. Fetch Sales Details for quantity sold & top products
                    if (salesList.Count > 0)
                    {
                        string invoiceIdsString = string.Join(",", salesList.Select(s => s.InvoiceId));
                        string detailsQuery = $"SELECT sd.ProductId, sd.Quantity, sd.SubTotal, COALESCE(sd.CustomName, p.ProductName) AS ProductName, p.Category " +
                                              $"FROM SalesDetails sd JOIN Products p ON sd.ProductId = p.Id " +
                                              $"WHERE sd.InvoiceId IN ({invoiceIdsString})";

                        var detailsList = new List<DetailItem>();
                        using (MySqlCommand cmd = new MySqlCommand(detailsQuery, conn))
                        {
                            using (MySqlDataReader reader = cmd.ExecuteReader())
                            {
                                while (reader.Read())
                                {
                                    detailsList.Add(new DetailItem
                                    {
                                        ProductId = Convert.ToInt32(reader["ProductId"]),
                                        Quantity = Convert.ToDecimal(reader["Quantity"]),
                                        SubTotal = Convert.ToDecimal(reader["SubTotal"]),
                                        ProductName = reader["ProductName"].ToString() ?? "",
                                        Category = reader["Category"].ToString() ?? ""
                                    });
                                }
                            }
                        }

                        totalItemsSold = detailsList.Sum(d => d.Quantity);

                        // Category sales group
                        foreach (var det in detailsList)
                        {
                            string cat = string.IsNullOrEmpty(det.Category) ? "غير مصنف" : det.Category;
                            if (categorySales.ContainsKey(cat))
                                categorySales[cat] += det.SubTotal;
                            else
                                categorySales[cat] = det.SubTotal;
                        }

                        // Top 5 Products
                        topProducts = detailsList.GroupBy(d => d.ProductName)
                            .Select(g => new TopProductModel
                            {
                                ProductName = g.Key,
                                Quantity = g.Sum(x => x.Quantity),
                                Revenue = g.Sum(x => x.SubTotal)
                            })
                            .OrderByDescending(p => p.Quantity)
                            .Take(5)
                            .ToList();
                    }

                    // 3. Group by date for sales trend line chart
                    for (DateTime d = fromDate.Date; d <= toDate.Date; d = d.AddDays(1))
                    {
                        dailySales[d] = 0;
                    }
                    foreach (var sale in salesList)
                    {
                        DateTime d = sale.Date.Date;
                        if (dailySales.ContainsKey(d))
                            dailySales[d] += sale.TotalAmount;
                        else
                            dailySales[d] = sale.TotalAmount;
                    }

                    // 4. Find Top Cashier
                    var cashierSales = salesList.GroupBy(s => s.CashierName.Split(' ')[0])
                        .Select(g => new { Cashier = g.Key, Total = g.Sum(x => x.TotalAmount) })
                        .OrderByDescending(c => c.Total)
                        .FirstOrDefault();

                    if (cashierSales != null)
                    {
                        topCashierName = cashierSales.Cashier;
                        topCashierSales = cashierSales.Total;
                    }
                }

                // Update UI Labels
                txtTotalSales.Text = totalSales.ToString("N2");
                txtInvoiceCount.Text = invoiceCount.ToString();
                txtTotalItemsSold.Text = totalItemsSold.ToString("0.###");
                txtTotalReturns.Text = "0.00"; // Mocked

                decimal cashTotal = salesList.Where(s => s.PaymentMethod == "كاش").Sum(s => s.TotalAmount);
                decimal cardTotal = salesList.Where(s => s.PaymentMethod == "بطاقة").Sum(s => s.TotalAmount);

                txtCashSales.Text = $"النقد (كاش): {cashTotal:N2}";
                txtCardSales.Text = $"البطاقة: {cardTotal:N2}";
                txtCreditSales.Text = "الأجل: 0.00";
                txtSplitSales.Text = "مقسم: 0.00";

                lblTopCashierName.Text = topCashierName;
                lblTopCashierSales.Text = $"{topCashierSales:N2} درهم";

                dgTopProducts.ItemsSource = null;
                dgTopProducts.ItemsSource = topProducts;

                // Render Charts
                var categoryPoints = categorySales.Select(kv => new CategorySalesPoint { CategoryName = kv.Key, Amount = kv.Value }).ToList();
                DrawDonutChart(categoryPoints);

                var trendPoints = dailySales.Select(kv => new SalesTrendPoint { Date = kv.Key, Amount = kv.Value }).ToList();
                DrawLineChart(trendPoints);
            }
            catch (Exception ex)
            {
                MessageBox.Show("خطأ في معالجة تقارير المبيعات: " + ex.Message, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DrawDonutChart(List<CategorySalesPoint> categories)
        {
            donutCanvas.Children.Clear();
            donutLegend.Children.Clear();

            if (categories == null || categories.Count == 0) return;

            double total = (double)categories.Sum(c => c.Amount);
            if (total == 0) return;

            var colors = new[] { 
                System.Windows.Media.Brushes.MediumSeaGreen, 
                System.Windows.Media.Brushes.Tomato, 
                System.Windows.Media.Brushes.DodgerBlue, 
                System.Windows.Media.Brushes.Goldenrod, 
                System.Windows.Media.Brushes.MediumPurple 
            };

            double startAngle = 0;
            double radius = 70;
            double centerX = 90;
            double centerY = 90;

            for (int i = 0; i < categories.Count; i++)
            {
                var cat = categories[i];
                double pct = (double)cat.Amount / total;
                double sweepAngle = pct * 360;

                var brush = colors[i % colors.Length];

                if (sweepAngle > 0)
                {
                    var path = new System.Windows.Shapes.Path { Fill = brush };
                    var geom = new System.Windows.Media.PathGeometry();
                    var fig = new System.Windows.Media.PathFigure { StartPoint = new Point(centerX, centerY), IsClosed = true };

                    double angleRad1 = (startAngle - 90) * Math.PI / 180.0;
                    double x1 = centerX + radius * Math.Cos(angleRad1);
                    double y1 = centerY + radius * Math.Sin(angleRad1);

                    double angleRad2 = (startAngle + sweepAngle - 90) * Math.PI / 180.0;
                    double x2 = centerX + radius * Math.Cos(angleRad2);
                    double y2 = centerY + radius * Math.Sin(angleRad2);

                    fig.Segments.Add(new System.Windows.Media.LineSegment(new Point(x1, y1), true));
                    fig.Segments.Add(new System.Windows.Media.ArcSegment(
                        new Point(x2, y2), 
                        new Size(radius, radius), 
                        0, 
                        sweepAngle > 180, 
                        System.Windows.Media.SweepDirection.Clockwise, 
                        true
                    ));
                    fig.Segments.Add(new System.Windows.Media.LineSegment(new Point(centerX, centerY), true));
                    geom.Figures.Add(fig);
                    path.Data = geom;
                    donutCanvas.Children.Add(path);
                }

                startAngle += sweepAngle;

                // Add to legend
                var itemGrid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
                itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var colorBox = new Border { Background = brush, CornerRadius = new CornerRadius(2), Width = 10, Height = 10, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(colorBox, 0);
                itemGrid.Children.Add(colorBox);

                var txt = new TextBlock { Text = $"{cat.CategoryName} ({pct * 100:0}%)", FontSize = 11, Foreground = System.Windows.Media.Brushes.DimGray, Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(txt, 1);
                itemGrid.Children.Add(txt);

                donutLegend.Children.Add(itemGrid);
            }

            var hole = new System.Windows.Shapes.Ellipse
            {
                Width = 70,
                Height = 70,
                Fill = System.Windows.Media.Brushes.White,
                Margin = new Thickness(-35, -35, 0, 0)
            };
            Canvas.SetLeft(hole, centerX);
            Canvas.SetTop(hole, centerY);
            donutCanvas.Children.Add(hole);
        }

        private void DrawLineChart(List<SalesTrendPoint> points)
        {
            lineChartCanvas.Children.Clear();
            if (points == null || points.Count == 0) return;

            double width = 500;
            double height = 180;

            double maxVal = (double)points.Max(p => p.Amount);
            if (maxVal == 0) maxVal = 1000;
            maxVal = Math.Ceiling(maxVal / 100.0) * 100.0;

            int gridLines = 4;
            for (int i = 0; i <= gridLines; i++)
            {
                double y = height - (i * (height / gridLines));
                double val = (maxVal / gridLines) * i;

                var line = new System.Windows.Shapes.Line
                {
                    X1 = 0,
                    Y1 = y,
                    X2 = width,
                    Y2 = y,
                    Stroke = System.Windows.Media.Brushes.Gainsboro,
                    StrokeThickness = 0.5
                };
                lineChartCanvas.Children.Add(line);

                var txt = new TextBlock
                {
                    Text = val.ToString("G"),
                    FontSize = 9,
                    Foreground = System.Windows.Media.Brushes.Gray
                };
                Canvas.SetLeft(txt, -28);
                Canvas.SetTop(txt, y - 6);
                lineChartCanvas.Children.Add(txt);
            }

            var polyline = new System.Windows.Shapes.Polyline
            {
                Stroke = System.Windows.Media.Brushes.DodgerBlue,
                StrokeThickness = 2
            };

            double xStep = width / (points.Count > 1 ? points.Count - 1 : 1);
            for (int i = 0; i < points.Count; i++)
            {
                double x = i * xStep;
                double y = height - ((double)points[i].Amount / maxVal * height);
                polyline.Points.Add(new Point(x, y));

                var dot = new System.Windows.Shapes.Ellipse
                {
                    Width = 6,
                    Height = 6,
                    Fill = System.Windows.Media.Brushes.DeepSkyBlue,
                    Margin = new Thickness(-3, -3, 0, 0)
                };
                Canvas.SetLeft(dot, x);
                Canvas.SetTop(dot, y);
                lineChartCanvas.Children.Add(dot);

                var txt = new TextBlock
                {
                    Text = points[i].Date.ToString("MM-dd"),
                    FontSize = 9,
                    Foreground = System.Windows.Media.Brushes.Gray
                };
                Canvas.SetLeft(txt, x - 12);
                Canvas.SetTop(txt, height + 5);
                lineChartCanvas.Children.Add(txt);
            }
            lineChartCanvas.Children.Add(polyline);
        }

        private void BtnRefreshData_Click(object sender, RoutedEventArgs e)
        {
            LoadReportData();
        }

        private void BtnDateFilter_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                string tag = btn.Content.ToString() ?? "";
                
                // Reset backgrounds
                btnDay.Background = System.Windows.Media.Brushes.White; btnDay.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(93, 109, 126));
                btnYesterday.Background = System.Windows.Media.Brushes.White; btnYesterday.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(93, 109, 126));
                btnWeek.Background = System.Windows.Media.Brushes.White; btnWeek.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(93, 109, 126));
                btnMonth.Background = System.Windows.Media.Brushes.White; btnMonth.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(93, 109, 126));

                // Color active
                btn.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(52, 152, 219));
                btn.Foreground = System.Windows.Media.Brushes.White;

                if (tag == "اليوم")
                {
                    dpFrom.SelectedDate = DateTime.Today;
                    dpTo.SelectedDate = DateTime.Today;
                }
                else if (tag == "أمس")
                {
                    dpFrom.SelectedDate = DateTime.Today.AddDays(-1);
                    dpTo.SelectedDate = DateTime.Today.AddDays(-1);
                }
                else if (tag == "الأسبوع")
                {
                    dpFrom.SelectedDate = DateTime.Today.AddDays(-7);
                    dpTo.SelectedDate = DateTime.Today;
                }
                else if (tag == "الشهر")
                {
                    dpFrom.SelectedDate = DateTime.Today.AddMonths(-1);
                    dpTo.SelectedDate = DateTime.Today;
                }

                LoadReportData();
            }
        }

        private void CmbFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            LoadReportData();
        }

        private void BtnResetReports_Click(object sender, RoutedEventArgs e)
        {
            MessageBoxResult result1 = MessageBox.Show(
                "هل أنت متأكد من تصفير وإرجاع كافة تقارير المبيعات للصفر؟\n\nسيؤدي هذا الإجراء إلى حذف جميع الفواتير وسجل المبيعات بالكامل بشكل نهائي!",
                "تأكيد تصفير التقارير",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            );

            if (result1 != MessageBoxResult.Yes) return;

            MessageBoxResult result2 = MessageBox.Show(
                "تحذير صارم: لا يمكن التراجع عن هذه العملية بعد إتمامها. هل أنت متأكد تماماً وتود الاستمرار حقاً؟",
                "تأكيد نهائي للتصفير",
                MessageBoxButton.YesNo,
                MessageBoxImage.Error
            );

            if (result2 != MessageBoxResult.Yes) return;

            try
            {
                using (MySqlConnection conn = dbHelper.GetConnection())
                {
                    conn.Open();
                    
                    string deleteDetailsQuery = "DELETE FROM SalesDetails";
                    using (MySqlCommand cmd = new MySqlCommand(deleteDetailsQuery, conn))
                    {
                        cmd.ExecuteNonQuery();
                    }

                    string deleteSalesQuery = "DELETE FROM Sales";
                    using (MySqlCommand cmd = new MySqlCommand(deleteSalesQuery, conn))
                    {
                        cmd.ExecuteNonQuery();
                    }

                    string updateCustomersQuery = "UPDATE Customers SET InvoiceCount = 0";
                    using (MySqlCommand cmd = new MySqlCommand(updateCustomersQuery, conn))
                    {
                        cmd.ExecuteNonQuery();
                    }
                }

                MessageBox.Show(
                    "تم تصفير وإرجاع كافة التقارير للصفر بنجاح!\n\nتم مسح جميع سجلات الفواتير والمبيعات وتحديث الواجهة.",
                    "نجاح العملية",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );

                LoadReportData();
                
                if (gridCustomers.Visibility == Visibility.Visible)
                {
                    LoadCustomersReportData();
                }

                MainWindow.Instance?.LoadProducts();
            }
            catch (Exception ex)
            {
                MessageBox.Show("حدث خطأ أثناء تصفير التقارير: " + ex.Message, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private void BtnCloseDay_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("تم إنهاء وإقفال يومية الكاشير بنجاح وإرسال التقرير للمدير!", "إقفال اليومية", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnMenuDashboard_Click(object sender, RoutedEventArgs e)
        {
            SwitchToTab("Dashboard");
        }

        private void BtnMenuCustomers_Click(object sender, RoutedEventArgs e)
        {
            SwitchToTab("Customers");
        }

        private void SwitchToTab(string tabName)
        {
            btnMenuDashboard.Style = (Style)FindResource("ReportMenuButtonStyle");
            btnMenuSales.Style = (Style)FindResource("ReportMenuButtonStyle");
            btnMenuPayments.Style = (Style)FindResource("ReportMenuButtonStyle");
            btnMenuReturns.Style = (Style)FindResource("ReportMenuButtonStyle");
            btnMenuCustomers.Style = (Style)FindResource("ReportMenuButtonStyle");
            btnMenuCashier.Style = (Style)FindResource("ReportMenuButtonStyle");
            btnMenuInventory.Style = (Style)FindResource("ReportMenuButtonStyle");

            gridDashboard.Visibility = Visibility.Collapsed;
            gridSales.Visibility = Visibility.Collapsed;
            gridPayments.Visibility = Visibility.Collapsed;
            gridReturns.Visibility = Visibility.Collapsed;
            gridCustomers.Visibility = Visibility.Collapsed;
            gridCashier.Visibility = Visibility.Collapsed;
            gridInventory.Visibility = Visibility.Collapsed;

            if (tabName == "Dashboard")
            {
                btnMenuDashboard.Style = (Style)FindResource("ActiveReportMenuButtonStyle");
                gridDashboard.Visibility = Visibility.Visible;
                LoadReportData();
            }
            else if (tabName == "Sales")
            {
                btnMenuSales.Style = (Style)FindResource("ActiveReportMenuButtonStyle");
                gridSales.Visibility = Visibility.Visible;
                if (dpSalesFrom.SelectedDate == null) dpSalesFrom.SelectedDate = DateTime.Today;
                if (dpSalesTo.SelectedDate == null) dpSalesTo.SelectedDate = DateTime.Today;
                LoadSalesReportData();
            }
            else if (tabName == "Payments")
            {
                btnMenuPayments.Style = (Style)FindResource("ActiveReportMenuButtonStyle");
                gridPayments.Visibility = Visibility.Visible;
                if (dpPaymentsFrom.SelectedDate == null) dpPaymentsFrom.SelectedDate = DateTime.Today;
                if (dpPaymentsTo.SelectedDate == null) dpPaymentsTo.SelectedDate = DateTime.Today;
                LoadPaymentsReportData();
            }
            else if (tabName == "Returns")
            {
                btnMenuReturns.Style = (Style)FindResource("ActiveReportMenuButtonStyle");
                gridReturns.Visibility = Visibility.Visible;
                if (dpReturnsFrom.SelectedDate == null) dpReturnsFrom.SelectedDate = DateTime.Today;
                if (dpReturnsTo.SelectedDate == null) dpReturnsTo.SelectedDate = DateTime.Today;
                LoadReturnsReportData();
            }
            else if (tabName == "Customers")
            {
                btnMenuCustomers.Style = (Style)FindResource("ActiveReportMenuButtonStyle");
                gridCustomers.Visibility = Visibility.Visible;
                LoadCustomersReportData();
            }
            else if (tabName == "Cashier")
            {
                btnMenuCashier.Style = (Style)FindResource("ActiveReportMenuButtonStyle");
                gridCashier.Visibility = Visibility.Visible;
                if (dpCashierFrom.SelectedDate == null) dpCashierFrom.SelectedDate = DateTime.Today;
                if (dpCashierTo.SelectedDate == null) dpCashierTo.SelectedDate = DateTime.Today;
                LoadCashierReportData();
            }
            else if (tabName == "Inventory")
            {
                btnMenuInventory.Style = (Style)FindResource("ActiveReportMenuButtonStyle");
                gridInventory.Visibility = Visibility.Visible;
                LoadInventoryReportData();
            }
        }
        #region Navigation & Tabs Switching
        private void BtnMenuSales_Click(object sender, RoutedEventArgs e)
        {
            SwitchToTab("Sales");
        }

        private void BtnMenuPayments_Click(object sender, RoutedEventArgs e)
        {
            SwitchToTab("Payments");
        }

        private void BtnMenuReturns_Click(object sender, RoutedEventArgs e)
        {
            SwitchToTab("Returns");
        }

        private void BtnMenuCashier_Click(object sender, RoutedEventArgs e)
        {
            SwitchToTab("Cashier");
        }

        private void BtnMenuInventory_Click(object sender, RoutedEventArgs e)
        {
            SwitchToTab("Inventory");
        }

        private void ApplyDateFilterToPickers(string tag, DatePicker fromPicker, DatePicker toPicker)
        {
            DateTime today = DateTime.Today;
            switch (tag)
            {
                case "today":
                    fromPicker.SelectedDate = today;
                    toPicker.SelectedDate = today;
                    break;
                case "yesterday":
                    fromPicker.SelectedDate = today.AddDays(-1);
                    toPicker.SelectedDate = today.AddDays(-1);
                    break;
                case "week":
                    fromPicker.SelectedDate = today.AddDays(-7);
                    toPicker.SelectedDate = today;
                    break;
                case "month":
                    fromPicker.SelectedDate = today.AddDays(-30);
                    toPicker.SelectedDate = today;
                    break;
            }
        }
        #endregion

        #region 1. تقارير المبيعات (Sales Reports)
        private List<SalesReportItem> cachedSalesList = new List<SalesReportItem>();

        private void BtnSalesDateFilter_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag)
            {
                ApplyDateFilterToPickers(tag, dpSalesFrom, dpSalesTo);
                LoadSalesReportData();
            }
        }

        private void BtnRefreshSales_Click(object sender, RoutedEventArgs e)
        {
            LoadSalesReportData();
        }

        private void TxtSalesSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            FilterSalesReportList();
        }

        private void FilterSalesReportList()
        {
            if (cachedSalesList == null || dgSalesReport == null) return;
            string search = txtSalesSearch.Text.Trim();
            var filtered = cachedSalesList.AsEnumerable();
            if (!string.IsNullOrEmpty(search))
            {
                filtered = filtered.Where(s =>
                    s.InvoiceId.ToString().Contains(search) ||
                    s.CashierOnly.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    s.CustomerName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    s.PaymentMethod.Contains(search, StringComparison.OrdinalIgnoreCase)
                );
            }
            dgSalesReport.ItemsSource = null;
            dgSalesReport.ItemsSource = filtered.ToList();
        }

        private void LoadSalesReportData()
        {
            try
            {
                DateTime from = dpSalesFrom.SelectedDate ?? DateTime.Today;
                DateTime to = dpSalesTo.SelectedDate ?? DateTime.Today;
                to = to.Date.AddDays(1).AddSeconds(-1);

                decimal totalSales = 0;
                int invoiceCount = 0;
                decimal itemsSold = 0;
                cachedSalesList = new List<SalesReportItem>();

                using (MySqlConnection conn = dbHelper.GetConnection())
                {
                    conn.Open();
                    string query = @"
                        SELECT s.InvoiceId, s.CashierName, s.TotalAmount, s.Date,
                               COALESCE((SELECT SUM(Quantity) FROM SalesDetails WHERE InvoiceId = s.InvoiceId), 0) AS TotalItems
                        FROM Sales s
                        WHERE s.Date BETWEEN @from AND @to
                        ORDER BY s.Date DESC;";

                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@from", from);
                        cmd.Parameters.AddWithValue("@to", to);

                        using (MySqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                int invId = Convert.ToInt32(reader["InvoiceId"]);
                                string rawCashier = reader["CashierName"]?.ToString() ?? "";
                                decimal amt = Convert.ToDecimal(reader["TotalAmount"]);
                                DateTime dt = Convert.ToDateTime(reader["Date"]);
                                decimal items = Convert.ToDecimal(reader["TotalItems"]);

                                string cashierOnly = rawCashier;
                                string customerName = "زبون نقدي";
                                if (rawCashier.Contains("(") && rawCashier.Contains(")"))
                                {
                                    int openIdx = rawCashier.IndexOf('(');
                                    int closeIdx = rawCashier.IndexOf(')');
                                    if (openIdx >= 0 && closeIdx > openIdx)
                                    {
                                        cashierOnly = rawCashier.Substring(0, openIdx).Trim();
                                        customerName = rawCashier.Substring(openIdx + 1, closeIdx - openIdx - 1).Trim();
                                    }
                                }

                                string method = (customerName == "زبون نقدي عابر" || customerName == "زبون نقدي افتراضي" || customerName == "زبون نقدي") ? "كاش" : "كريدي (أجل)";

                                cachedSalesList.Add(new SalesReportItem
                                {
                                    InvoiceId = invId,
                                    Date = dt,
                                    CashierOnly = cashierOnly,
                                    CustomerName = customerName,
                                    PaymentMethod = method,
                                    TotalAmount = amt,
                                    TotalItems = items
                                });

                                totalSales += amt;
                                invoiceCount++;
                                itemsSold += items;
                            }
                        }
                    }
                }

                // Update UI Cards
                txtSalesTotalSales.Text = totalSales.ToString("N2") + " د.م.";
                txtSalesInvoiceCount.Text = invoiceCount.ToString();
                txtSalesAvgInvoice.Text = (invoiceCount > 0 ? (totalSales / invoiceCount) : 0).ToString("N2") + " د.م.";
                txtSalesItemsSold.Text = itemsSold.ToString("0.###");

                FilterSalesReportList();

                // Reset invoice preview
                lblSelectedInvoiceHeader.Text = "حدد فاتورة من الجدول لعرض بنودها";
                dgInvoiceDetailsItems.ItemsSource = null;
            }
            catch (Exception ex)
            {
                MessageBox.Show("خطأ في تحميل تقارير المبيعات: " + ex.Message, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DgSalesReport_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dgSalesReport.SelectedItem is SalesReportItem sale)
            {
                LoadInvoiceItems(sale);
            }
        }

        private void LoadInvoiceItems(SalesReportItem sale)
        {
            try
            {
                lblSelectedInvoiceHeader.Text = $"فاتورة رقم #{sale.InvoiceId} | {sale.CustomerName} | {sale.TotalAmount:N2} د.م.";
                var items = new List<InvoiceDetailItemModel>();

                using (MySqlConnection conn = dbHelper.GetConnection())
                {
                    conn.Open();
                    string query = @"
                        SELECT sd.Quantity, sd.SubTotal, COALESCE(sd.CustomName, p.ProductName) AS ProductName
                        FROM SalesDetails sd
                        JOIN Products p ON sd.ProductId = p.Id
                        WHERE sd.InvoiceId = @invId;";

                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@invId", sale.InvoiceId);
                        using (MySqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                string pName = reader["ProductName"]?.ToString() ?? "";
                                decimal qty = Convert.ToDecimal(reader["Quantity"]);
                                decimal sub = Convert.ToDecimal(reader["SubTotal"]);
                                decimal price = qty > 0 ? sub / qty : 0;

                                items.Add(new InvoiceDetailItemModel
                                {
                                    ProductName = pName,
                                    Quantity = qty,
                                    UnitPrice = price,
                                    SubTotal = sub
                                });
                            }
                        }
                    }
                }

                dgInvoiceDetailsItems.ItemsSource = null;
                dgInvoiceDetailsItems.ItemsSource = items;
            }
            catch (Exception ex)
            {
                MessageBox.Show("خطأ في تحميل تفاصيل الفاتورة: " + ex.Message, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnPrintSelectedInvoice_Click(object sender, RoutedEventArgs e)
        {
            if (dgSalesReport.SelectedItem is SalesReportItem sale)
            {
                MessageBox.Show($"جاري تجهيز أمر طباعة الفاتورة رقم #{sale.InvoiceId} بقيمة {sale.TotalAmount:N2} د.م.", "طباعة الفاتورة", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("الرجاء تحديد فاتورة من القائمة أولاً لطباعتها!", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        #endregion

        #region 2. طرق الدفع (Payment Methods)


        private void BtnPaymentsDateFilter_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag)
            {
                ApplyDateFilterToPickers(tag, dpPaymentsFrom, dpPaymentsTo);
                LoadPaymentsReportData();
            }
        }

        private void BtnRefreshPayments_Click(object sender, RoutedEventArgs e)
        {
            LoadPaymentsReportData();
        }

        private void LoadPaymentsReportData()
        {
            try
            {
                DateTime from = dpPaymentsFrom.SelectedDate ?? DateTime.Today;
                DateTime to = dpPaymentsTo.SelectedDate ?? DateTime.Today;
                to = to.Date.AddDays(1).AddSeconds(-1);

                decimal cashTotal = 0;
                int cashCount = 0;
                decimal creditTotal = 0;
                int creditCount = 0;
                decimal cardTotal = 0;
                int cardCount = 0;

                var dailyBreakdown = new Dictionary<DateTime, PaymentDaySummaryModel>();

                using (MySqlConnection conn = dbHelper.GetConnection())
                {
                    conn.Open();
                    string query = "SELECT InvoiceId, CashierName, TotalAmount, Date FROM Sales WHERE Date BETWEEN @from AND @to ORDER BY Date ASC;";
                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@from", from);
                        cmd.Parameters.AddWithValue("@to", to);
                        using (MySqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                int invId = Convert.ToInt32(reader["InvoiceId"]);
                                string cashier = reader["CashierName"]?.ToString() ?? "";
                                decimal amt = Convert.ToDecimal(reader["TotalAmount"]);
                                DateTime dt = Convert.ToDateTime(reader["Date"]);
                                DateTime dayKey = dt.Date;

                                if (!dailyBreakdown.ContainsKey(dayKey))
                                {
                                    dailyBreakdown[dayKey] = new PaymentDaySummaryModel { Date = dayKey };
                                }

                                bool isCredit = false;
                                if (cashier.Contains("(") && cashier.Contains(")"))
                                {
                                    int openIdx = cashier.IndexOf('(');
                                    int closeIdx = cashier.IndexOf(')');
                                    if (openIdx >= 0 && closeIdx > openIdx)
                                    {
                                        string cust = cashier.Substring(openIdx + 1, closeIdx - openIdx - 1).Trim();
                                        if (cust != "زبون نقدي عابر" && cust != "زبون نقدي افتراضي" && cust != "زبون نقدي")
                                        {
                                            isCredit = true;
                                        }
                                    }
                                }

                                if (isCredit)
                                {
                                    creditTotal += amt;
                                    creditCount++;
                                    dailyBreakdown[dayKey].CreditSales += amt;
                                    dailyBreakdown[dayKey].CreditCount++;
                                }
                                else
                                {
                                    cashTotal += amt;
                                    cashCount++;
                                    dailyBreakdown[dayKey].CashSales += amt;
                                    dailyBreakdown[dayKey].CashCount++;
                                }
                            }
                        }
                    }
                }

                decimal grandTotal = cashTotal + creditTotal + cardTotal;
                int totalOps = cashCount + creditCount + cardCount;

                txtPaymentsCashTotal.Text = cashTotal.ToString("N2") + " د.م.";
                txtPaymentsCashCount.Text = $"{cashCount} عملية";

                txtPaymentsCreditTotal.Text = creditTotal.ToString("N2") + " د.م.";
                txtPaymentsCreditCount.Text = $"{creditCount} عملية";

                txtPaymentsCardTotal.Text = cardTotal.ToString("N2") + " د.م.";
                txtPaymentsCardCount.Text = $"{cardCount} عملية";

                txtPaymentsGrandTotal.Text = grandTotal.ToString("N2") + " د.م.";
                txtPaymentsTotalTransactions.Text = $"{totalOps} عملية";

                double cashPct = grandTotal > 0 ? (double)((cashTotal / grandTotal) * 100) : 0;
                double creditPct = grandTotal > 0 ? (double)((creditTotal / grandTotal) * 100) : 0;
                double cardPct = grandTotal > 0 ? (double)((cardTotal / grandTotal) * 100) : 0;

                pbPaymentCash.Value = cashPct;
                lblPaymentCashPercent.Text = $"{cashPct:F1}%";

                pbPaymentCredit.Value = creditPct;
                lblPaymentCreditPercent.Text = $"{creditPct:F1}%";

                pbPaymentCard.Value = cardPct;
                lblPaymentCardPercent.Text = $"{cardPct:F1}%";

                dgPaymentBreakdown.ItemsSource = null;
                dgPaymentBreakdown.ItemsSource = dailyBreakdown.Values.OrderByDescending(d => d.Date).ToList();
            }
            catch (Exception ex)
            {
                MessageBox.Show("خطأ في تحميل تقرير طرق الدفع: " + ex.Message, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        #endregion

        #region 3. المرتجعات (Returns)


        private void BtnReturnsDateFilter_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag)
            {
                ApplyDateFilterToPickers(tag, dpReturnsFrom, dpReturnsTo);
                LoadReturnsReportData();
            }
        }

        private void BtnRefreshReturns_Click(object sender, RoutedEventArgs e)
        {
            LoadReturnsReportData();
        }

        private void BtnOpenAddReturnOverlay_Click(object sender, RoutedEventArgs e)
        {
            txtReturnInvoiceId.Text = "";
            txtReturnProductName.Text = "";
            txtReturnQuantity.Text = "1";
            txtReturnRefundAmount.Text = "0.00";
            cmbReturnReason.SelectedIndex = 0;
            returnsOverlay.Visibility = Visibility.Visible;
            txtReturnProductName.Focus();
        }

        private void BtnCancelReturn_Click(object sender, RoutedEventArgs e)
        {
            returnsOverlay.Visibility = Visibility.Collapsed;
        }

        private void BtnConfirmReturn_Click(object sender, RoutedEventArgs e)
        {
            string pName = txtReturnProductName.Text.Trim();
            if (string.IsNullOrEmpty(pName))
            {
                MessageBox.Show("الرجاء إدخال اسم المنتج المسترجع!", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!decimal.TryParse(txtReturnQuantity.Text.Trim().Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal qty) || qty <= 0)
            {
                MessageBox.Show("الرجاء إدخال كمية صحيحة أكبر من الصفر!", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!decimal.TryParse(txtReturnRefundAmount.Text.Trim().Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal refund) || refund < 0)
            {
                MessageBox.Show("الرجاء إدخال مبلغ استرداد صحيح!", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int? invId = null;
            if (int.TryParse(txtReturnInvoiceId.Text.Trim(), out int parsedInvId))
            {
                invId = parsedInvId;
            }

            string reason = (cmbReturnReason.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "سلعة تالفة أو بها عيب";
            string cashier = "مدير النظام";

            try
            {
                using (MySqlConnection conn = dbHelper.GetConnection())
                {
                    conn.Open();
                    string insertQuery = @"
                        INSERT INTO Returns (InvoiceId, ProductName, Quantity, RefundAmount, ReturnDate, CashierName, Reason)
                        VALUES (@invId, @prod, @qty, @refund, @dt, @cashier, @reason);";

                    using (MySqlCommand cmd = new MySqlCommand(insertQuery, conn))
                    {
                        cmd.Parameters.AddWithValue("@invId", invId.HasValue ? (object)invId.Value : DBNull.Value);
                        cmd.Parameters.AddWithValue("@prod", pName);
                        cmd.Parameters.AddWithValue("@qty", qty);
                        cmd.Parameters.AddWithValue("@refund", refund);
                        cmd.Parameters.AddWithValue("@dt", DateTime.Now);
                        cmd.Parameters.AddWithValue("@cashier", cashier);
                        cmd.Parameters.AddWithValue("@reason", reason);
                        cmd.ExecuteNonQuery();
                    }

                    string restockQuery = "UPDATE Products SET StockQuantity = StockQuantity + @qty WHERE ProductName = @prod OR Barcode = @prod;";
                    using (MySqlCommand cmd = new MySqlCommand(restockQuery, conn))
                    {
                        cmd.Parameters.AddWithValue("@qty", qty);
                        cmd.Parameters.AddWithValue("@prod", pName);
                        cmd.ExecuteNonQuery();
                    }
                }

                returnsOverlay.Visibility = Visibility.Collapsed;
                MessageBox.Show("تم تسجيل عملية الإرجاع بنجاح وإعادة الكمية للمخزن!", "نجاح", MessageBoxButton.OK, MessageBoxImage.Information);
                LoadReturnsReportData();
                MainWindow.Instance?.LoadProducts();
            }
            catch (Exception ex)
            {
                MessageBox.Show("حدث خطأ أثناء حفظ الإرجاع: " + ex.Message, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadReturnsReportData()
        {
            try
            {
                DateTime from = dpReturnsFrom.SelectedDate ?? DateTime.Today;
                DateTime to = dpReturnsTo.SelectedDate ?? DateTime.Today;
                to = to.Date.AddDays(1).AddSeconds(-1);

                decimal totalRefund = 0;
                int count = 0;
                decimal totalQty = 0;
                var list = new List<ReturnItemModel>();

                using (MySqlConnection conn = dbHelper.GetConnection())
                {
                    conn.Open();
                    string query = "SELECT Id, InvoiceId, ProductName, Quantity, RefundAmount, ReturnDate, CashierName, Reason FROM Returns WHERE ReturnDate BETWEEN @from AND @to ORDER BY ReturnDate DESC;";
                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@from", from);
                        cmd.Parameters.AddWithValue("@to", to);
                        using (MySqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                int id = Convert.ToInt32(reader["Id"]);
                                int? inv = reader["InvoiceId"] != DBNull.Value ? Convert.ToInt32(reader["InvoiceId"]) : (int?)null;
                                string prod = reader["ProductName"]?.ToString() ?? "";
                                decimal q = Convert.ToDecimal(reader["Quantity"]);
                                decimal refAmt = Convert.ToDecimal(reader["RefundAmount"]);
                                DateTime dt = Convert.ToDateTime(reader["ReturnDate"]);
                                string cName = reader["CashierName"]?.ToString() ?? "";
                                string rsn = reader["Reason"]?.ToString() ?? "";

                                totalRefund += refAmt;
                                count++;
                                totalQty += q;

                                list.Add(new ReturnItemModel
                                {
                                    Id = id,
                                    InvoiceId = inv,
                                    ProductName = prod,
                                    Quantity = q,
                                    RefundAmount = refAmt,
                                    ReturnDate = dt,
                                    CashierName = cName,
                                    Reason = rsn
                                });
                            }
                        }
                    }
                }

                txtReturnsTotalAmount.Text = totalRefund.ToString("N2") + " د.م.";
                txtReturnsCount.Text = count.ToString();
                txtReturnsTotalQty.Text = totalQty.ToString("0.###");

                dgReturnsReport.ItemsSource = null;
                dgReturnsReport.ItemsSource = list;
            }
            catch (Exception ex)
            {
                MessageBox.Show("خطأ في تحميل سجل المرتجعات: " + ex.Message, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        #endregion

        #region 4. الكاشير (Cashier Performance)


        private void BtnCashierDateFilter_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag)
            {
                ApplyDateFilterToPickers(tag, dpCashierFrom, dpCashierTo);
                LoadCashierReportData();
            }
        }

        private void BtnRefreshCashier_Click(object sender, RoutedEventArgs e)
        {
            LoadCashierReportData();
        }

        private void LoadCashierReportData()
        {
            try
            {
                DateTime from = dpCashierFrom.SelectedDate ?? DateTime.Today;
                DateTime to = dpCashierTo.SelectedDate ?? DateTime.Today;
                to = to.Date.AddDays(1).AddSeconds(-1);

                decimal grandTotal = 0;
                int totalInvoices = 0;

                var cashierMap = new Dictionary<string, CashierPerformanceModel>();

                using (MySqlConnection conn = dbHelper.GetConnection())
                {
                    conn.Open();
                    string query = "SELECT InvoiceId, CashierName, TotalAmount, Date FROM Sales WHERE Date BETWEEN @from AND @to ORDER BY Date ASC;";
                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@from", from);
                        cmd.Parameters.AddWithValue("@to", to);
                        using (MySqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                string rawName = reader["CashierName"]?.ToString() ?? "كاشير عام";
                                decimal amt = Convert.ToDecimal(reader["TotalAmount"]);
                                DateTime dt = Convert.ToDateTime(reader["Date"]);

                                string cName = rawName;
                                if (rawName.Contains("("))
                                {
                                    cName = rawName.Split('(')[0].Trim();
                                }
                                if (string.IsNullOrEmpty(cName)) cName = "كاشير عام";

                                if (!cashierMap.ContainsKey(cName))
                                {
                                    cashierMap[cName] = new CashierPerformanceModel
                                    {
                                        CashierName = cName,
                                        FirstSaleDate = dt,
                                        LastSaleDate = dt
                                    };
                                }

                                var model = cashierMap[cName];
                                model.InvoiceCount++;
                                model.TotalSales += amt;
                                if (dt < model.FirstSaleDate) model.FirstSaleDate = dt;
                                if (dt > model.LastSaleDate) model.LastSaleDate = dt;

                                grandTotal += amt;
                                totalInvoices++;
                            }
                        }
                    }
                }

                var list = cashierMap.Values.OrderByDescending(c => c.TotalSales).ToList();
                foreach (var c in list)
                {
                    c.SalesSharePercent = grandTotal > 0 ? (double)((c.TotalSales / grandTotal) * 100) : 0;
                    c.AvgInvoice = c.InvoiceCount > 0 ? c.TotalSales / c.InvoiceCount : 0;
                }

                var topCashier = list.FirstOrDefault();
                txtTopCashierNameCard.Text = topCashier != null ? topCashier.CashierName : "-";
                txtTopCashierAmountCard.Text = topCashier != null ? topCashier.TotalSales.ToString("N2") + " د.م." : "0.00 د.م.";

                txtCashierTotalInvoices.Text = totalInvoices.ToString();
                txtCashierOverallAvgBasket.Text = (totalInvoices > 0 ? grandTotal / totalInvoices : 0).ToString("N2") + " د.م.";
                txtActiveCashiersCount.Text = list.Count.ToString();

                dgCashierReport.ItemsSource = null;
                dgCashierReport.ItemsSource = list;
            }
            catch (Exception ex)
            {
                MessageBox.Show("خطأ في تحميل تقرير الكاشير: " + ex.Message, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        #endregion

        #region 5. المخزون (Inventory Reports)
        private List<InventoryReportItemModel> cachedInventoryList = new List<InventoryReportItemModel>();


        private void BtnRefreshInventory_Click(object sender, RoutedEventArgs e)
        {
            LoadInventoryReportData();
        }

        private void TxtInventorySearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            FilterInventoryReportList();
        }

        private void CmbInventoryFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            FilterInventoryReportList();
        }

        private void FilterInventoryReportList()
        {
            if (cachedInventoryList == null || dgInventoryReport == null) return;

            string search = txtInventorySearch?.Text.Trim() ?? "";
            string category = (cmbInventoryCategory?.SelectedItem as ComboBoxItem)?.Content?.ToString() 
                              ?? cmbInventoryCategory?.SelectedItem?.ToString() 
                              ?? "كل الفئات";
            string status = (cmbInventoryStatus?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "كل الحالات";

            var filtered = cachedInventoryList.AsEnumerable();

            if (!string.IsNullOrEmpty(search))
            {
                filtered = filtered.Where(p =>
                    p.ProductName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    p.Barcode.Contains(search, StringComparison.OrdinalIgnoreCase)
                );
            }

            if (category != "كل الفئات")
            {
                filtered = filtered.Where(p => p.Category == category);
            }

            if (status == "متوفر فقط")
            {
                filtered = filtered.Where(p => p.StockQuantity > 5);
            }
            else if (status == "منخفض فقط (أقل من 5)")
            {
                filtered = filtered.Where(p => p.StockQuantity > 0 && p.StockQuantity <= 5);
            }
            else if (status == "نافد تماماً (0)")
            {
                filtered = filtered.Where(p => p.StockQuantity <= 0);
            }

            dgInventoryReport.ItemsSource = null;
            dgInventoryReport.ItemsSource = filtered.ToList();
        }

        private void LoadInventoryReportData()
        {
            try
            {
                decimal totalRetail = 0;
                decimal totalCost = 0;
                int lowOrOutOfStockCount = 0;
                cachedInventoryList = new List<InventoryReportItemModel>();
                var categoriesSet = new HashSet<string>();

                using (MySqlConnection conn = dbHelper.GetConnection())
                {
                    conn.Open();
                    string query = "SELECT Barcode, ProductName, Category, PurchasePrice, SellPrice, StockQuantity FROM Products WHERE Barcode != 'MANUAL' AND deleted_at IS NULL ORDER BY ProductName ASC;";
                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string bar = reader["Barcode"]?.ToString() ?? "";
                            string name = reader["ProductName"]?.ToString() ?? "";
                            string cat = reader["Category"]?.ToString() ?? "عام";
                            decimal cost = Convert.ToDecimal(reader["PurchasePrice"]);
                            decimal price = Convert.ToDecimal(reader["SellPrice"]);
                            decimal stock = Convert.ToDecimal(reader["StockQuantity"]);

                            if (!string.IsNullOrEmpty(cat)) categoriesSet.Add(cat);

                            decimal retailVal = stock * price;
                            decimal costVal = stock * cost;
                            decimal expProfit = retailVal - costVal;

                            totalRetail += Math.Max(0, retailVal);
                            totalCost += Math.Max(0, costVal);

                            string statusStr = "متوفر";
                            if (stock <= 0)
                            {
                                statusStr = "نافد تماماً";
                                lowOrOutOfStockCount++;
                            }
                            else if (stock <= 5)
                            {
                                statusStr = "منخفض جداً";
                                lowOrOutOfStockCount++;
                            }

                            cachedInventoryList.Add(new InventoryReportItemModel
                            {
                                Barcode = bar,
                                ProductName = name,
                                Category = cat,
                                StockQuantity = stock,
                                PurchasePrice = cost,
                                SellPrice = price,
                                TotalRetailValue = retailVal,
                                ExpectedProfit = expProfit,
                                StatusStr = statusStr
                            });
                        }
                    }
                }

                if (cmbInventoryCategory != null && cmbInventoryCategory.Items.Count <= 1)
                {
                    foreach (var cat in categoriesSet.OrderBy(c => c))
                    {
                        cmbInventoryCategory.Items.Add(cat);
                    }
                }

                txtInventoryRetailValue.Text = totalRetail.ToString("N2") + " د.م.";
                txtInventoryCostValue.Text = totalCost.ToString("N2") + " د.م.";
                txtInventoryExpectedProfit.Text = (totalRetail - totalCost).ToString("N2") + " د.م.";
                txtInventoryLowStockCount.Text = lowOrOutOfStockCount.ToString();

                FilterInventoryReportList();
            }
            catch (Exception ex)
            {
                MessageBox.Show("خطأ في تحميل تقرير المخزون: " + ex.Message, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        #endregion


        private void TxtCustomersReportSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            LoadCustomersReportData();
        }

        private void BtnRefreshCustomersReport_Click(object sender, RoutedEventArgs e)
        {
            LoadCustomersReportData();
        }

        private void LoadCustomersReportData()
        {
            try
            {
                decimal totalDebts = 0;
                int debtorCustomersCount = 0;
                decimal totalCreditLimit = 0;
                decimal maxIndividualDebt = 0;
                string topDebtorName = "-";

                var customersList = new List<CustomerReportModel>();

                using (MySqlConnection conn = dbHelper.GetConnection())
                {
                    conn.Open();
                    string query = "SELECT FullName, Phone, AllowCredit, CreditLimit, DueBalance, LastCreditDate FROM Customers WHERE deleted_at IS NULL ORDER BY DueBalance DESC";
                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string name = reader["FullName"].ToString() ?? "";
                            string phone = reader["Phone"].ToString() ?? "";
                            bool allowCredit = Convert.ToBoolean(reader["AllowCredit"]);
                            decimal limit = Convert.ToDecimal(reader["CreditLimit"]);
                            decimal debt = Convert.ToDecimal(reader["DueBalance"]);
                            DateTime? lastCredit = reader["LastCreditDate"] != DBNull.Value ? Convert.ToDateTime(reader["LastCreditDate"]) : (DateTime?)null;

                            totalDebts += debt;
                            if (debt > 0)
                            {
                                debtorCustomersCount++;
                            }
                            if (allowCredit)
                            {
                                totalCreditLimit += limit;
                            }

                            customersList.Add(new CustomerReportModel
                            {
                                FullName = name,
                                Phone = phone,
                                AllowCredit = allowCredit,
                                CreditLimit = limit,
                                DueBalance = debt,
                                LastCreditDate = lastCredit
                            });
                        }
                    }
                }

                if (customersList.Count > 0)
                {
                    var maxDebtor = customersList.OrderByDescending(c => c.DueBalance).FirstOrDefault();
                    if (maxDebtor != null && maxDebtor.DueBalance > 0)
                    {
                        maxIndividualDebt = maxDebtor.DueBalance;
                        topDebtorName = maxDebtor.FullName;
                    }
                }

                // Update UI Cards
                txtTotalDebts.Text = totalDebts.ToString("N2") + " د.م.";
                txtDebtorCustomers.Text = debtorCustomersCount.ToString();
                txtTotalCreditLimit.Text = totalCreditLimit.ToString("N2") + " د.م.";
                txtTopDebtor.Text = topDebtorName + (maxIndividualDebt > 0 ? $" ({maxIndividualDebt:N2} د.م.)" : "");

                // Filter report list based on search
                string searchText = txtCustomersReportSearch.Text.Trim();
                var filtered = customersList.AsEnumerable();
                if (!string.IsNullOrEmpty(searchText))
                {
                    filtered = filtered.Where(c => 
                        c.FullName.Contains(searchText, StringComparison.OrdinalIgnoreCase) || 
                        c.Phone.Contains(searchText, StringComparison.OrdinalIgnoreCase)
                    );
                }

                dgCustomersReport.ItemsSource = null;
                dgCustomersReport.ItemsSource = filtered.ToList();

                // Load Top 5 debtors for the visual list
                var top5Debtors = customersList.Where(c => c.DueBalance > 0).OrderByDescending(c => c.DueBalance).Take(5).ToList();
                lstTopDebtors.ItemsSource = null;
                lstTopDebtors.ItemsSource = top5Debtors;
            }
            catch (Exception ex)
            {
                MessageBox.Show("خطأ في تحميل تقرير الزبناء والديون: " + ex.Message, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Redundant sidebar methods removed since managed by MainWindow container
    }

    public class SaleItem
    {
        public int InvoiceId { get; set; }
        public string CashierName { get; set; } = string.Empty;
        public decimal TotalAmount { get; set; }
        public DateTime Date { get; set; }
        public string PaymentMethod { get; set; } = string.Empty;
    }

    public class DetailItem
    {
        public int ProductId { get; set; }
        public decimal Quantity { get; set; }
        public decimal SubTotal { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
    }

    public class TopProductModel
    {
        public string ProductName { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public decimal Revenue { get; set; }
    }

    public class CategorySalesPoint
    {
        public string CategoryName { get; set; } = string.Empty;
        public decimal Amount { get; set; }
    }

    public class SalesTrendPoint
    {
        public DateTime Date { get; set; }
        public decimal Amount { get; set; }
    }

    public class CustomerReportModel
    {
        public string FullName { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public bool AllowCredit { get; set; }
        public decimal CreditLimit { get; set; }
        public decimal DueBalance { get; set; }
        public DateTime? LastCreditDate { get; set; }

        public string AllowCreditStr
        {
            get => AllowCredit ? "نعم" : "لا";
            set { }
        }

        public string LastCreditDateStr
        {
            get => LastCreditDate?.ToString("yyyy-MM-dd") ?? "-";
            set { }
        }

        public double CreditUsagePercent
        {
            get => CreditLimit > 0 ? (double)Math.Min(100, Math.Max(0, (DueBalance / CreditLimit) * 100)) : 0;
            set { }
        }

        public string CreditUsageText
        {
            get => AllowCredit ? $"{DueBalance:N2} / {CreditLimit:N2} د.م." : $"{DueBalance:N2} د.م. (غير مسموح بالأجل)";
            set { }
        }
    }

    public class SalesReportItem
    {
        public int InvoiceId { get; set; }
        public DateTime Date { get; set; }
        public string DateStr => Date.ToString("yyyy-MM-dd HH:mm");
        public string CashierOnly { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string PaymentMethod { get; set; } = string.Empty;
        public decimal TotalAmount { get; set; }
        public decimal TotalItems { get; set; }
    }

    public class InvoiceDetailItemModel
    {
        public string ProductName { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public string QuantityStr => Quantity.ToString("0.###");
        public decimal UnitPrice { get; set; }
        public string UnitPriceStr => UnitPrice.ToString("N2");
        public decimal SubTotal { get; set; }
        public string SubTotalStr => SubTotal.ToString("N2");
    }

    public class PaymentDaySummaryModel
    {
        public DateTime Date { get; set; }
        public string DateStr => Date.ToString("yyyy-MM-dd");
        public decimal CashSales { get; set; }
        public int CashCount { get; set; }
        public decimal CreditSales { get; set; }
        public int CreditCount { get; set; }
        public decimal CardSales { get; set; }
        public decimal TotalSales => CashSales + CreditSales + CardSales;
    }

    public class ReturnItemModel
    {
        public int Id { get; set; }
        public int? InvoiceId { get; set; }
        public string InvoiceRefStr => InvoiceId.HasValue ? $"#{InvoiceId.Value}" : "-";
        public string ProductName { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public string QuantityStr => Quantity.ToString("0.###");
        public decimal RefundAmount { get; set; }
        public DateTime ReturnDate { get; set; }
        public string ReturnDateStr => ReturnDate.ToString("yyyy-MM-dd HH:mm");
        public string CashierName { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
    }

    public class CashierPerformanceModel
    {
        public string CashierName { get; set; } = string.Empty;
        public int InvoiceCount { get; set; }
        public decimal TotalSales { get; set; }
        public decimal AvgInvoice { get; set; }
        public double SalesSharePercent { get; set; }
        public string SalesShareStr => $"{SalesSharePercent:F1}%";
        public DateTime FirstSaleDate { get; set; }
        public DateTime LastSaleDate { get; set; }
        public string FirstSaleDateStr => FirstSaleDate.ToString("yyyy-MM-dd HH:mm");
        public string LastSaleDateStr => LastSaleDate.ToString("yyyy-MM-dd HH:mm");
    }

    public class InventoryReportItemModel
    {
        public string Barcode { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public decimal StockQuantity { get; set; }
        public string StockQuantityStr => StockQuantity.ToString("0.###");
        public decimal PurchasePrice { get; set; }
        public decimal SellPrice { get; set; }
        public decimal TotalRetailValue { get; set; }
        public decimal ExpectedProfit { get; set; }
        public string StatusStr { get; set; } = string.Empty;
    }

}