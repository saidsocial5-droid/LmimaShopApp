#define DEBUG

using System;

using System.CodeDom.Compiler;

using System.Collections.Generic;

using System.Collections.ObjectModel;

using System.ComponentModel;

using System.Diagnostics;

using System.Drawing.Printing;

using System.Globalization;

using System.IO;

using System.Linq;

using System.Printing;

using System.Runtime.InteropServices;

using System.Text.Json;

using System.Threading.Tasks;

using System.Windows;

using System.Windows.Controls;

using System.Windows.Controls.Primitives;

using System.Windows.Input;

using System.Windows.Markup;

using System.Windows.Media;

using System.Windows.Media.Imaging;

using System.Windows.Threading;

using MySqlConnector;



namespace LmimaShopApp;



public partial class MainWindow : Window

{

	public static class RawPrinterHelper

	{

		[StructLayout(LayoutKind.Sequential)]

		public class DOCINFOA

		{

			[MarshalAs(UnmanagedType.LPStr)]

			public string? pDocName;



			[MarshalAs(UnmanagedType.LPStr)]

			public string? pOutputFile;



			[MarshalAs(UnmanagedType.LPStr)]

			public string? pDataType;

		}



		[DllImport("winspool.Drv", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, EntryPoint = "OpenPrinterA", ExactSpelling = true, SetLastError = true)]

		public static extern bool OpenPrinter([MarshalAs(UnmanagedType.LPStr)] string szPrinter, out nint hPrinter, nint pd);



		[DllImport("winspool.Drv", CallingConvention = CallingConvention.StdCall, ExactSpelling = true, SetLastError = true)]

		public static extern bool ClosePrinter(nint hPrinter);



		[DllImport("winspool.Drv", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, EntryPoint = "StartDocPrinterA", ExactSpelling = true, SetLastError = true)]

		public static extern bool StartDocPrinter(nint hPrinter, int level, [MarshalAs(UnmanagedType.LPStruct)] DOCINFOA di);



		[DllImport("winspool.Drv", CallingConvention = CallingConvention.StdCall, ExactSpelling = true, SetLastError = true)]

		public static extern bool EndDocPrinter(nint hPrinter);



		[DllImport("winspool.Drv", CallingConvention = CallingConvention.StdCall, ExactSpelling = true, SetLastError = true)]

		public static extern bool StartPagePrinter(nint hPrinter);



		[DllImport("winspool.Drv", CallingConvention = CallingConvention.StdCall, ExactSpelling = true, SetLastError = true)]

		public static extern bool EndPagePrinter(nint hPrinter);



		[DllImport("winspool.Drv", CallingConvention = CallingConvention.StdCall, ExactSpelling = true, SetLastError = true)]

		public static extern bool WritePrinter(nint hPrinter, nint pBytes, int dwCount, out int dwWritten);



		public static bool SendBytesToPrinter(string szPrinterName, byte[] bytes)

		{

			nint hPrinter = new IntPtr(0);

			DOCINFOA dOCINFOA = new DOCINFOA();

			int dwWritten = 0;

			bool result = false;

			dOCINFOA.pDocName = "ESC/POS Raw Command";

			dOCINFOA.pDataType = "RAW";

			if (OpenPrinter(szPrinterName.Normalize(), out hPrinter, IntPtr.Zero))

			{

				if (StartDocPrinter(hPrinter, 1, dOCINFOA))

				{

					if (StartPagePrinter(hPrinter))

					{

						nint num = Marshal.AllocCoTaskMem(bytes.Length);

						Marshal.Copy(bytes, 0, num, bytes.Length);

						result = WritePrinter(hPrinter, num, bytes.Length, out dwWritten);

						Marshal.FreeCoTaskMem(num);

						EndPagePrinter(hPrinter);

					}

					EndDocPrinter(hPrinter);

				}

				ClosePrinter(hPrinter);

			}

			return result;

		}

	}



	private readonly DatabaseHelper dbHelper = new DatabaseHelper();



	private List<Product> allProducts = new List<Product>();



	private ObservableCollection<CartItem> cartItems => (this.DataContext as LmimaShopApp.Presentation.ViewModels.MainViewModel)?.Cart ?? new ObservableCollection<CartItem>();



	private readonly string cashierName = "demo account";



	private string currentCustomer = "زبون نقدي افتراضي";



	private int currentInvoiceId = 0;



	private decimal currentInvoiceTotal = 0m;



	private decimal currentInvoicePaid = 0m;



	private decimal currentInvoiceChange = 0m;



	private string keypadMode = "QTY";



	private string currentInput = "";



	private DispatcherTimer? searchDebounceTimer;



	private int manualProductId = 0;



	private TextBox? activeOverlayTextBox = null;



	private TextBox? activeTextTextBox = null;



	private string currentKeyboardLang = "AR";



	private readonly string[] arRow0 = new string[12]

	{

		"أ", "إ", "ء", "ا", "ب", "ت", "ث", "ج", "ح", "خ",

		"د", "ذ"

	};



	private readonly string[] arRow1 = new string[12]

	{

		"ر", "ز", "س", "ش", "ص", "ض", "ط", "ظ", "ع", "غ",

		"ف", "ق"

	};



	private readonly string[] arRow2 = new string[12]

	{

		"ك", "ل", "لا", "م", "ن", "ه", "ة", "و", "ؤ", "ي",

		"ى", "ئ"

	};



	private readonly string[] frRow0 = new string[12]

	{

		"A", "B", "C", "D", "E", "F", "G", "H", "I", "J",

		"K", "L"

	};



	private readonly string[] frRow1 = new string[12]

	{

		"M", "N", "O", "P", "Q", "R", "S", "T", "U", "V",

		"W", "X"

	};



	private readonly string[] frRow2 = new string[12]

	{

		"Y", "Z", "-", "_", "'", ",", ".", "?", "!", "@",

		"(", ")"

	};



	public static MainWindow? Instance { get; private set; }



	public MainWindow()

	{

		InitializeComponent();

		Instance = this;

        

        // MVVM Transition

        try

        {

            var vm = AppHost.GetService<LmimaShopApp.Presentation.ViewModels.MainViewModel>();

            this.DataContext = vm;

        }

        catch { /* Ignore in designer or if not initialized */ }



		EnsureManualPlaceholderProduct();

		LoadProducts();

		Task.Run(delegate

		{

			dbHelper.MigrateProductImages();

		});

		LoadPOSCategories();

		LoadCustomers();

		StartClock();

		txtManualItemPrice.GotFocus += NumericTextBox_GotFocus;

		txtManualItemQty.GotFocus += NumericTextBox_GotFocus;

		txtPaidAmount.GotFocus += NumericTextBox_GotFocus;

		KeyboardLayoutHelper.BindToTextBox(txtSearch);

		base.Loaded += delegate

		{

			txtSearch.Focus();

		};

		base.PreviewKeyDown += MainWindow_PreviewKeyDown;

		base.Closing += MainWindow_Closing;

		StartCloudSyncTimer();

		SignalRServer.OnBarcodeReceived += (barcode) =>

		{
			if (string.IsNullOrWhiteSpace(barcode) || barcode.StartsWith("SYNC_") || barcode.Equals("REFRESH", StringComparison.OrdinalIgnoreCase)) return;

			base.Dispatcher.Invoke(() =>

			{

				txtSearch.Text = barcode;

				TxtSearch_KeyDown(txtSearch, new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(this), 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });

			});

		};

		if (lblMobileSyncIp != null)

		{

			lblMobileSyncIp.Text = $"متصل";

		}

	}



	public MainWindow(string cashier)

		: this()

	{

		cashierName = cashier;

		lblCashier.Text = cashier;

	}



	private void StartClock()

	{

		//IL_0001: Unknown result type (might be due to invalid IL or missing references)

		//IL_0007: Expected O, but got Unknown

		DispatcherTimer val = new DispatcherTimer();

		val.Interval = TimeSpan.FromSeconds(1L);

		val.Tick += delegate

		{

			txtClock.Text = DateTime.Now.ToString("HH:mm");

		};

		val.Start();

	}



	public void LoadProducts()

	{

		try

		{

			allProducts.Clear();

			using (MySqlConnection mySqlConnection = dbHelper.GetConnection())

			{

				mySqlConnection.Open();

				string commandText = "SELECT Id, Barcode, ProductName, Category, SubCategory, PurchasePrice, SellPrice, StockQuantity, ImagePath FROM Products WHERE deleted_at IS NULL";

				using MySqlCommand mySqlCommand = new MySqlCommand(commandText, mySqlConnection);

				using MySqlDataReader mySqlDataReader = mySqlCommand.ExecuteReader();

				while (mySqlDataReader.Read())

				{

					string text = mySqlDataReader["Barcode"].ToString() ?? "";

					if (text == "MANUAL")

					{

						continue;

					}

					string text2 = mySqlDataReader["ImagePath"]?.ToString() ?? "";

					string imagePath = null;

					if (!string.IsNullOrEmpty(text2))

					{

						if (Path.IsPathRooted(text2))

						{

							if (File.Exists(text2))

							{

								imagePath = text2;

							}

							else

							{

								int num = text2.IndexOf("ProductImages", StringComparison.OrdinalIgnoreCase);

								if (num >= 0)

								{

									string text3 = text2.Substring(num);

									string text4 = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, text3);

									if (File.Exists(text4))

									{

										imagePath = text4;

										try

										{

											string value = text3.Replace('\\', '/');

											using MySqlConnection mySqlConnection2 = dbHelper.GetConnection();

											mySqlConnection2.Open();

											string commandText2 = "UPDATE Products SET ImagePath = @imagePath WHERE Id = @id";

											using MySqlCommand mySqlCommand2 = new MySqlCommand(commandText2, mySqlConnection2);

											mySqlCommand2.Parameters.AddWithValue("@imagePath", value);

											mySqlCommand2.Parameters.AddWithValue("@id", Convert.ToInt32(mySqlDataReader["Id"]));

											mySqlCommand2.ExecuteNonQuery();

										}

										catch (Exception ex)

										{

											Debug.WriteLine("Failed to auto-heal path in DB: " + ex.Message);

										}

									}

								}

							}

						}

						else

						{

							imagePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, text2);

						}

					}

					allProducts.Add(new Product

					{

						Id = Convert.ToInt32(mySqlDataReader["Id"]),

						Barcode = text,

						ProductName = (mySqlDataReader["ProductName"].ToString() ?? ""),

						Category = (mySqlDataReader["Category"].ToString() ?? ""),

						SubCategory = ((mySqlDataReader["SubCategory"] != DBNull.Value) ? (mySqlDataReader["SubCategory"].ToString() ?? "") : ""),

						PurchasePrice = Convert.ToDecimal(mySqlDataReader["PurchasePrice"]),

						SellPrice = Convert.ToDecimal(mySqlDataReader["SellPrice"]),

						StockQuantity = Convert.ToDecimal(mySqlDataReader["StockQuantity"]),

						ImagePath = imagePath

					});

				}

			}

			ApplyPOSFilter();

		}

		catch (Exception ex2)

		{

			MessageBox.Show("فشل تحميل المنتجات: " + ex2.Message, "خطأ قاعدة بيانات", MessageBoxButton.OK, MessageBoxImage.Hand);

		}

	}



	private void LoadPOSCategories()

	{

		try

		{

			List<string> list = new List<string> { "الكل" };

			using (MySqlConnection mySqlConnection = dbHelper.GetConnection())

			{

				mySqlConnection.Open();

				string commandText = "SELECT DISTINCT CategoryName FROM Categories ORDER BY CategoryName ASC";

				using MySqlCommand mySqlCommand = new MySqlCommand(commandText, mySqlConnection);

				using MySqlDataReader mySqlDataReader = mySqlCommand.ExecuteReader();

				while (mySqlDataReader.Read())

				{

					string text = mySqlDataReader["CategoryName"]?.ToString() ?? "";

					if (!string.IsNullOrEmpty(text))

					{

						list.Add(text);

					}

				}

			}

			lstPOSCategories.ItemsSource = list;

			lstPOSCategories.SelectedIndex = 0;

		}

		catch (Exception ex)

		{

			Console.WriteLine("Error loading POS categories: " + ex.Message);

		}

	}



	private void ApplyPOSFilter()

	{

		if (lstPOSCategories == null || txtSearch == null || lstProducts == null)

		{

			return;

		}

		string searchText = txtSearch.Text.Trim();

		string selectedCategory = (lstPOSCategories.SelectedItem as string) ?? "الكل";

		IEnumerable<Product> source = allProducts.AsEnumerable();

		if (selectedCategory != "الكل")

		{

			source = source.Where((Product p) => p.Category.Equals(selectedCategory, StringComparison.OrdinalIgnoreCase) || ((selectedCategory == "منتوجات بالعبار" || selectedCategory == "منتوجات العبار") && (p.SubCategory == "منتوجات بالعبار" || p.SubCategory == "منتوجات العبار")));

		}

		if (!string.IsNullOrEmpty(searchText))

		{

			source = source.Where((Product p) => p.ProductName.Contains(searchText, StringComparison.OrdinalIgnoreCase) || p.Barcode.Contains(searchText, StringComparison.OrdinalIgnoreCase));

		}

        

        lstProducts.ItemsSource = source.ToList();

	}



	private void LstPOSCategories_SelectionChanged(object sender, SelectionChangedEventArgs e)

	{

		ApplyPOSFilter();

		txtSearch.Focus();

	}



	private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)

	{

		//IL_0010: Unknown result type (might be due to invalid IL or missing references)

		//IL_001a: Expected O, but got Unknown

		if (searchDebounceTimer == null)

		{

			searchDebounceTimer = new DispatcherTimer();

			searchDebounceTimer.Interval = TimeSpan.FromMilliseconds(150L);

			searchDebounceTimer.Tick += delegate

			{

				searchDebounceTimer.Stop();

				ApplyPOSFilter();

			};

		}

		searchDebounceTimer.Stop();

		searchDebounceTimer.Start();

	}



	private void TxtSearch_KeyDown(object sender, KeyEventArgs e)

	{

		//IL_0002: Unknown result type (might be due to invalid IL or missing references)

		//IL_0008: Invalid comparison between Unknown and I4

		if ((int)e.Key != 6)

		{

			return;

		}

		string searchText = txtSearch.Text.Trim();

		if (string.IsNullOrEmpty(searchText))

		{

			return;

		}

		decimal parsedQty = 0m;

		Product product = null;



		// Check for scale barcode (e.g. 20xxxxxYYYyyC)

		if (searchText.Length == 13 && (searchText.StartsWith("20") || searchText.StartsWith("21") || searchText.StartsWith("28") || searchText.StartsWith("29")))

		{

			string itemCode = searchText.Substring(2, 5);

			string weightPricePart = searchText.Substring(7, 5);

			if (decimal.TryParse(weightPricePart, out decimal val))

			{

				parsedQty = val / 1000m; // usually grams, so divide by 1000 to get KG

				product = allProducts.FirstOrDefault(p => p.Barcode.Equals(itemCode, StringComparison.OrdinalIgnoreCase) || p.Barcode.EndsWith(itemCode));

			}

		}



		if (product == null)

		{

			product = allProducts.FirstOrDefault((Product p) => p.Barcode.Equals(searchText, StringComparison.OrdinalIgnoreCase));

		}

		if (product == null)

		{

			product = allProducts.FirstOrDefault((Product p) => p.ProductName.Contains(searchText, StringComparison.OrdinalIgnoreCase));

		}

		DispatcherTimer? obj = searchDebounceTimer;

		if (obj != null)

		{

			obj.Stop();

		}

		if (product != null)

		{

			AddToCart(product, parsedQty);

			txtSearch.Clear();

			ApplyPOSFilter();

			e.Handled = true;

		}

		else

		{

			var askRegister = MessageBox.Show($"المنتج \"{searchText}\" غير متوفر في المخزن!\nهل تود تسجيله في لائحة السلع المطلوبة من الزبائن لطلبه من الممونين؟", "منتج غير متوفر", MessageBoxButton.YesNo, MessageBoxImage.Question);

			if (askRegister == MessageBoxResult.Yes)

			{

				txtSearch.Clear();

				ApplyPOSFilter();

				NavigateTo("RequestedProducts");

				if (mainContentControl.Content is RequestedProductsWindow reqWin)

				{

					bool isBarcode = searchText.Length >= 6 && searchText.All(char.IsDigit);

					reqWin.OpenAddModalWithPreset(isBarcode ? "" : searchText, isBarcode ? searchText : "");

				}

			}

			else

			{

				txtSearch.Clear();

				ApplyPOSFilter();

				txtSearch.Focus();

			}

			e.Handled = true;

		}

	}



	private void NumericTextBox_GotFocus(object sender, RoutedEventArgs e)

	{

		KeyboardHelper.ShowTouchKeyboard();

	}



	private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)

	{

		//IL_0052: Unknown result type (might be due to invalid IL or missing references)

		//IL_0059: Invalid comparison between Unknown and I4

		//IL_0066: Unknown result type (might be due to invalid IL or missing references)

		//IL_006d: Invalid comparison between Unknown and I4

		//IL_005c: Unknown result type (might be due to invalid IL or missing references)

		//IL_0063: Invalid comparison between Unknown and I4

		//IL_007a: Unknown result type (might be due to invalid IL or missing references)

		//IL_0081: Invalid comparison between Unknown and I4

		//IL_0070: Unknown result type (might be due to invalid IL or missing references)

		//IL_0077: Invalid comparison between Unknown and I4

		//IL_008e: Unknown result type (might be due to invalid IL or missing references)

		//IL_0098: Invalid comparison between Unknown and I4

		//IL_0084: Unknown result type (might be due to invalid IL or missing references)

		//IL_008b: Invalid comparison between Unknown and I4

		//IL_009b: Unknown result type (might be due to invalid IL or missing references)

		//IL_00a5: Invalid comparison between Unknown and I4

		//IL_00a8: Unknown result type (might be due to invalid IL or missing references)

		//IL_00af: Invalid comparison between Unknown and I4

		//IL_00b2: Unknown result type (might be due to invalid IL or missing references)

		//IL_00b9: Invalid comparison between Unknown and I4

		//IL_00bc: Unknown result type (might be due to invalid IL or missing references)

		if (gridPOS == null || gridPOS.Visibility != Visibility.Visible) return;

		IInputElement focusedElement = FocusManager.GetFocusedElement((DependencyObject)(object)this);

		DependencyObject val = (DependencyObject)((focusedElement is DependencyObject) ? focusedElement : null);

		if ((object)val != txtPaidAmount && (object)val != txtManualItemName && (object)val != txtManualItemPrice && (object)val != txtManualItemQty && (object)val != txtSearch && (((int)e.Key >= 44 && (int)e.Key <= 69) || ((int)e.Key >= 34 && (int)e.Key <= 43) || ((int)e.Key >= 74 && (int)e.Key <= 83) || (int)e.Key == 143 || (int)e.Key == 141 || (int)e.Key == 87 || (int)e.Key == 85 || (int)e.Key == 88))

		{

			txtSearch.Focus();

		}

	}



	private void LstProducts_SelectionChanged(object sender, SelectionChangedEventArgs e)

	{

		if (lstProducts.SelectedItem is Product product)

		{

			AddToCart(product);

			lstProducts.SelectedItem = null;

		}

	}



	private void AddToCart(Product product, decimal parsedQty = 0m)

	{

		if (product.StockQuantity <= 0m)

		{

			MessageBox.Show("المنتج '" + product.ProductName + "' غير متوفر بالمخزن!", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Exclamation);

			return;

		}

		

		decimal qtyToAdd = parsedQty > 0m ? parsedQty : 1m;



		CartItem cartItem = cartItems.FirstOrDefault((CartItem item) => item.ProductId == product.Id);

		if (cartItem != null)

		{

			if (cartItem.Quantity + (parsedQty > 0 ? 0 : 1) > product.StockQuantity && product.StockQuantity > 0)

			{

				MessageBox.Show("تجاوزت الكمية المتوفرة بالمخزن من '" + product.ProductName + "'!", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Exclamation);

				return;

			}

			cartItem.Quantity = parsedQty > 0m ? parsedQty : cartItem.Quantity + 1;

			lstCart.Items.Refresh();

		}

		else

		{

			CartItem cartItem2 = new CartItem

			{

				ProductId = product.Id,

				Barcode = product.Barcode,

				ProductName = product.ProductName,

				SellPrice = product.SellPrice,

				Quantity = qtyToAdd,

				DiscountPercent = 0m

			};

			cartItems.Add(cartItem2);

			cartItem = cartItem2;

		}

		lstCart.SelectedItem = cartItem;

		UpdateTotalAmount();

		txtSearch.Focus();

	}



	private void UpdateTotalAmount()

	{

		decimal num = cartItems.Sum((CartItem item) => item.Total);

		txtTotalAmount.Text = $"{num:N2} درهم";

		txtTotalAmountRial.Text = $"({num * 20m:N2} ريال)";

		txtCartBadge.Text = cartItems.Sum((CartItem item) => item.Quantity).ToString();

	}



	private void BtnMode_Click(object sender, RoutedEventArgs e)

	{

		if (sender is Button button)

		{

			string activeMode = "QTY";

			if (button == btnModeGram)

			{

				activeMode = "GRAM";

			}

			else if (button == btnModeDiscount)

			{

				activeMode = "DISC";

			}

			else if (button == btnModePrice)

			{

				activeMode = "PRICE";

			}

			SetActiveMode(activeMode);

		}

	}



	private void SetActiveMode(string mode)

	{

		keypadMode = mode;

		currentInput = "";

		btnModeQty.Style = (Style)FindResource((mode == "QTY") ? "ActiveKeypadButtonStyle" : "KeypadButtonStyle");

		btnModeGram.Style = (Style)FindResource((mode == "GRAM") ? "ActiveKeypadButtonStyle" : "KeypadButtonStyle");

		btnModeDiscount.Style = (Style)FindResource((mode == "DISC") ? "ActiveKeypadButtonStyle" : "KeypadButtonStyle");

		btnModePrice.Style = (Style)FindResource((mode == "PRICE") ? "ActiveKeypadButtonStyle" : "KeypadButtonStyle");

	}



	private void BtnNumber_Click(object sender, RoutedEventArgs e)

	{

		if (sender is Button button && lstCart.SelectedItem is CartItem item)

		{

			string text = button.Content.ToString() ?? "";

			currentInput += text;

			ApplyKeypadValue(item);

		}

	}



	private void BtnDecimal_Click(object sender, RoutedEventArgs e)

	{

		if (lstCart.SelectedItem is CartItem && !currentInput.Contains("."))

		{

			currentInput += ((currentInput.Length == 0) ? "0." : ".");

		}

	}



	private void ApplyKeypadValue(CartItem item)

	{

		if (string.IsNullOrEmpty(currentInput))

		{

			return;

		}

		decimal result4;

		if (keypadMode == "QTY")

		{

			if (TryParseDecimal(currentInput, out var result))

			{

				Product product = allProducts.FirstOrDefault((Product p) => p.Id == item.ProductId);

				if (product != null && result > product.StockQuantity)

				{

					MessageBox.Show("الكمية المطلوبة تتجاوز المتاح في المخزن! المتاح: " + product.StockQuantity.ToString("0.###"), "تنبيه", MessageBoxButton.OK, MessageBoxImage.Exclamation);

					currentInput = product.StockQuantity.ToString("0.###");

					result = product.StockQuantity;

				}

				item.Quantity = ((result > 0m) ? result : 1m);

			}

		}

		else if (keypadMode == "GRAM")

		{

			if (TryParseDecimal(currentInput, out var result2))

			{

				decimal num = result2 / 1000m;

				Product product2 = allProducts.FirstOrDefault((Product p) => p.Id == item.ProductId);

				if (product2 != null && num > product2.StockQuantity)

				{

					MessageBox.Show("الوزن المطلوب يتجاوز المتاح في المخزن! المتاح: " + product2.StockQuantity.ToString("0.###"), "تنبيه", MessageBoxButton.OK, MessageBoxImage.Exclamation);

					num = product2.StockQuantity;

				}

				item.Quantity = ((num > 0m) ? num : 0.001m);

			}

		}

		else if (keypadMode == "DISC")

		{

			if (TryParseDecimal(currentInput, out var result3))

			{

				if (result3 > 100m)

				{

					result3 = 100m;

				}

				item.DiscountPercent = result3;

			}

		}

		else if (keypadMode == "PRICE" && TryParseDecimal(currentInput, out result4))

		{

			item.SellPrice = result4;

		}

		lstCart.Items.Refresh();

		UpdateTotalAmount();

	}



	private void BtnBackspace_Click(object sender, RoutedEventArgs e)

	{

		if (!(lstCart.SelectedItem is CartItem cartItem))

		{

			return;

		}

		if (currentInput.Length > 0)

		{

			currentInput = currentInput.Substring(0, currentInput.Length - 1);

			if (currentInput.Length == 0)

			{

				if (keypadMode == "QTY")

				{

					cartItem.Quantity = 1m;

				}

				else if (keypadMode == "DISC")

				{

					cartItem.DiscountPercent = 0m;

				}

			}

			else

			{

				ApplyKeypadValue(cartItem);

			}

			lstCart.Items.Refresh();

			UpdateTotalAmount();

		}

		else

		{

			MessageBoxResult messageBoxResult = MessageBox.Show("هل أنت متأكد من إزالة المنتج '" + cartItem.ProductName + "' من السلة؟", "حذف المنتج", MessageBoxButton.YesNo, MessageBoxImage.Question);

			if (messageBoxResult == MessageBoxResult.Yes)

			{

				cartItems.Remove(cartItem);

				UpdateTotalAmount();

			}

		}

	}



	private void BtnToggleSign_Click(object sender, RoutedEventArgs e)

	{

		if (lstCart.SelectedItem is CartItem cartItem)

		{

			currentInput = "";

			if (keypadMode == "QTY")

			{

				cartItem.Quantity = 1m;

			}

			else if (keypadMode == "DISC")

			{

				cartItem.DiscountPercent = 0m;

			}

			lstCart.Items.Refresh();

			UpdateTotalAmount();

		}

	}



	private void BtnCustomer_Click(object sender, RoutedEventArgs e)

	{

		customerOverlay.Visibility = Visibility.Visible;

	}



	private void BtnSelectCustomer_Click(object sender, RoutedEventArgs e)

	{

		if (cmbCustomers.SelectedItem is ComboBoxItem comboBoxItem)

		{

			currentCustomer = comboBoxItem.Content.ToString() ?? "زبون نقدي افتراضي";

		}

		else if (cmbCustomers.SelectedItem is string text)

		{

			currentCustomer = text;

		}

		lblCashier.Text = cashierName + " (" + currentCustomer + ")";

		if (lblSelectedCustomerName != null)

		{

			lblSelectedCustomerName.Text = currentCustomer;

		}

		customerOverlay.Visibility = Visibility.Collapsed;

		if (txtPaidAmount != null)

		{

			TxtPaidAmount_TextChanged(this, null);

		}

	}



	private void BtnCancelCustomer_Click(object sender, RoutedEventArgs e)

	{

		customerOverlay.Visibility = Visibility.Collapsed;

	}



	private void BtnIncrementSelected_Click(object sender, RoutedEventArgs e)

	{

		object selectedItem = lstCart.SelectedItem;

		CartItem item = selectedItem as CartItem;

		if (item == null)

		{

			return;

		}

		Product product = allProducts.FirstOrDefault((Product p) => p.Id == item.ProductId);

		if (product != null)

		{

			if (item.Quantity >= product.StockQuantity)

			{

				MessageBox.Show("الكمية غير متوفرة في المخزن!", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Exclamation);

				return;

			}

			item.Quantity++;

			lstCart.Items.Refresh();

			UpdateTotalAmount();

		}

	}



	private void BtnDecrementSelected_Click(object sender, RoutedEventArgs e)

	{

		if (lstCart.SelectedItem is CartItem cartItem)

		{

			if (cartItem.Quantity > 1m)

			{

				cartItem.Quantity--;

			}

			else

			{

				cartItems.Remove(cartItem);

			}

			lstCart.Items.Refresh();

			UpdateTotalAmount();

		}

	}



	private void BtnReload_Click(object sender, RoutedEventArgs e)

	{

		LoadProducts();

	}



	private void StartCloudSyncTimer()

	{

		try

		{

			Task.Run(() => CloudSyncService.SyncMobileDataAsync());

                _ = LmimaShopApp.Core.Sync.SseManager.BroadcastEventAsync("changed", DateTime.UtcNow.ToString("O"));

			DispatcherTimer syncTimer = new DispatcherTimer

			{

				Interval = TimeSpan.FromMinutes(5.0)

			};

			syncTimer.Tick += (s, e) => Task.Run(() => CloudSyncService.SyncMobileDataAsync());

                _ = LmimaShopApp.Core.Sync.SseManager.BroadcastEventAsync("changed", DateTime.UtcNow.ToString("O"));

			syncTimer.Start();

		}

		catch

		{

		}

	}



	private void MainWindow_Closing(object? sender, CancelEventArgs e)

	{

		try

		{

			BackupService.PerformAutoBackupIfConfigured();

			CloudSyncService.SyncMobileData(force: true);

		}

		catch (Exception ex)

		{

			Debug.WriteLine("Auto-backup on closing failed: " + ex.Message);

		}

	}



	private void BtnShutdown_Click(object sender, RoutedEventArgs e)

	{

		MessageBoxResult messageBoxResult = MessageBox.Show("هل تود إغلاق تطبيق نقطة البيع؟", "خروج", MessageBoxButton.YesNo, MessageBoxImage.Question);

		if (messageBoxResult == MessageBoxResult.Yes)

		{

			try

			{

				BackupService.PerformAutoBackupIfConfigured();

				CloudSyncService.SyncMobileData(force: true);

			}

			catch

			{

			}

			Application.Current.Shutdown();

		}

	}



	private void BtnCheckout_Click(object sender, RoutedEventArgs e)

	{

		if (cartItems.Count == 0)

		{

			MessageBox.Show("سلة المشتريات فارغة! يرجى إضافة منتجات أولا\u064b.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Exclamation);

			return;

		}

		decimal num = cartItems.Sum((CartItem item) => item.Total);

		txtCheckoutTotal.Text = $"{num:N2} درهم";

		txtCheckoutTotalRial.Text = $"({num * 20m:N2} ريال)";

		txtPaidAmount.Text = "";

		txtChangeDue.Text = "0.00 درهم";

		txtChangeDueRial.Text = " (0.00 ريال)";

		txtChangeDue.Foreground = Brushes.Gray;

		txtChangeDueRial.Foreground = Brushes.Gray;

		btnConfirmCheckout.IsEnabled = false;

		checkoutOverlay.Visibility = Visibility.Visible;

		txtPaidAmount.Focus();

	}



	private void TxtPaidAmount_TextChanged(object sender, TextChangedEventArgs e)

	{

		decimal num = cartItems.Sum((CartItem item) => item.Total);

		if (TryParseDecimal(txtPaidAmount.Text, out var result))

		{

			decimal num2 = result - num;

			if (num2 >= 0m)

			{

				txtChangeDue.Text = $"{num2:N2} درهم";

				txtChangeDueRial.Text = $"({num2 * 20m:N2} ريال)";

				txtChangeDue.Foreground = Brushes.Green;

				txtChangeDueRial.Foreground = Brushes.Green;

				btnConfirmCheckout.IsEnabled = true;

			}

			else if (currentCustomer != "زبون نقدي عابر" && currentCustomer != "زبون نقدي افتراضي" && !string.IsNullOrEmpty(currentCustomer))

			{

				txtChangeDue.Text = $"المتبقي دين (كريدي): {Math.Abs(num2):N2} درهم";

				txtChangeDueRial.Text = $"({Math.Abs(num2) * 20m:N2} ريال)";

				txtChangeDue.Foreground = Brushes.Orange;

				txtChangeDueRial.Foreground = Brushes.Orange;

				btnConfirmCheckout.IsEnabled = true;

			}

			else

			{

				txtChangeDue.Text = $"المبلغ غير كاف\u064d ({Math.Abs(num2):N2} درهم)";

				txtChangeDueRial.Text = $"({Math.Abs(num2) * 20m:N2} ريال)";

				txtChangeDue.Foreground = Brushes.Red;

				txtChangeDueRial.Foreground = Brushes.Red;

				btnConfirmCheckout.IsEnabled = false;

			}

		}

		else if (currentCustomer != "زبون نقدي عابر" && currentCustomer != "زبون نقدي افتراضي" && !string.IsNullOrEmpty(currentCustomer))

		{

			txtChangeDue.Text = $"المتبقي دين (كريدي): {num:N2} درهم";

			txtChangeDueRial.Text = $"({num * 20m:N2} ريال)";

			txtChangeDue.Foreground = Brushes.Orange;

			txtChangeDueRial.Foreground = Brushes.Orange;

			btnConfirmCheckout.IsEnabled = true;

		}

		else

		{

			txtChangeDue.Text = "0.00 درهم";

			txtChangeDueRial.Text = " (0.00 ريال)";

			txtChangeDue.Foreground = Brushes.Gray;

			txtChangeDueRial.Foreground = Brushes.Gray;

			btnConfirmCheckout.IsEnabled = false;

		}

	}



	private void BtnQuickCash_Click(object sender, RoutedEventArgs e)

	{

		if (sender is Button { Tag: var tag })

		{

			string text = tag?.ToString() ?? "";

			decimal num = cartItems.Sum((CartItem item) => item.Total);

			if (text == "exact")

			{

				txtPaidAmount.Text = num.ToString("0.00");

			}

			else if (text == "clear" || text == "reset")

			{

				txtPaidAmount.Text = "";

			}

			else if (TryParseDecimal(text, out var result))

			{

				decimal current = 0m;

				if (!string.IsNullOrWhiteSpace(txtPaidAmount.Text))

				{

					TryParseDecimal(txtPaidAmount.Text, out current);

				}

				decimal newTotal = current + result;

				txtPaidAmount.Text = newTotal.ToString("0.00");

			}

			txtPaidAmount.SelectionStart = txtPaidAmount.Text.Length;

		}

	}



	private void BtnClearCart_Click(object sender, RoutedEventArgs e)

	{

		if (cartItems.Count == 0)

		{

			MessageBox.Show("سلة المشتريات فارغة بالفعل!", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Asterisk);

			return;

		}

		MessageBoxResult messageBoxResult = MessageBox.Show("هل أنت متأكد من رغبتك في تفريغ سلة المشتريات بالكامل؟", "تأكيد تفريغ السلة", MessageBoxButton.YesNo, MessageBoxImage.Question);

		if (messageBoxResult == MessageBoxResult.Yes)

		{

			cartItems.Clear();

			currentInput = "";

			UpdateTotalAmount();

			txtSearch.Text = "";

			txtSearch.Focus();

		}

	}



	private void BtnCancelCheckout_Click(object sender, RoutedEventArgs e)

	{

		checkoutOverlay.Visibility = Visibility.Collapsed;

		txtSearch.Focus();

	}



	private void BtnConfirmCheckout_Click(object sender, RoutedEventArgs e)

	{

		decimal num = cartItems.Sum((CartItem item) => item.Total);

		decimal result = 0m;

		if (!TryParseDecimal(txtPaidAmount.Text.Trim(), out result))

		{

			result = 0m;

		}

		decimal num2 = 0m;

		if (result < num)

		{

			num2 = num - result;

		}

		try

		{

			File.AppendAllText("C:\\Users\\Admin\\.gemini\\antigravity\\brain\\2c504fd4-21e3-4cba-9f43-2689b8394199\\checkout_debug.log", $"[{DateTime.Now}] Checkout: total={num}, paid={result}, debt={num2}, customer={currentCustomer}, txtPaidAmount='{txtPaidAmount.Text}'\n");

		}

		catch

		{

		}

		if (num2 > 0m)

		{

			if (currentCustomer == "زبون نقدي عابر" || currentCustomer == "زبون نقدي افتراضي" || string.IsNullOrEmpty(currentCustomer))

			{

				MessageBox.Show("الزبون النقدي العابر لا يمكنه الشراء بالكريدي! الرجاء إدخال المبلغ كاملا\u064b أو اختيار زبون مسجل.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Exclamation);

				return;

			}

			bool flag = false;

			decimal num3 = 0m;

			decimal num4 = 0m;

			using (MySqlConnection mySqlConnection = dbHelper.GetConnection())

			{

				try

				{

					mySqlConnection.Open();

					string commandText = "SELECT AllowCredit, CreditLimit, DueBalance FROM Customers WHERE FullName = @name AND deleted_at IS NULL";

					using MySqlCommand mySqlCommand = new MySqlCommand(commandText, mySqlConnection);

					mySqlCommand.Parameters.AddWithValue("@name", currentCustomer);

					using MySqlDataReader mySqlDataReader = mySqlCommand.ExecuteReader();

					if (!mySqlDataReader.Read())

					{

						MessageBox.Show("الزبون المحدد غير موجود في قاعدة البيانات!", "خطأ", MessageBoxButton.OK, MessageBoxImage.Hand);

						return;

					}

					flag = Convert.ToBoolean(mySqlDataReader["AllowCredit"]);

					num3 = Convert.ToDecimal(mySqlDataReader["CreditLimit"]);

					num4 = Convert.ToDecimal(mySqlDataReader["DueBalance"]);

				}

				catch (Exception ex)

				{

					MessageBox.Show("فشل التحقق من بيانات الزبون: " + ex.Message, "خطأ", MessageBoxButton.OK, MessageBoxImage.Hand);

					return;

				}

			}

			if (!flag)

			{

				MessageBox.Show("الزبون '" + currentCustomer + "' غير مسموح له بالشراء بالكريدي (بالأجل)!", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Exclamation);

				return;

			}

			if (num4 + num2 > num3)

			{

				MessageBox.Show($"لا يمكن إتمام العملية! إجمالي ديون الزبون بعد هذه العملية ({num4 + num2:N2} د.م.) سيتجاوز حد الائتمان المسموح به وهو ({num3:N2} د.م.).", "تجاوز حد الائتمان", MessageBoxButton.OK, MessageBoxImage.Exclamation);

				return;

			}

			MessageBoxResult messageBoxResult = MessageBox.Show($"سيتم تسجيل مبلغ {num2:N2} د.م. كدين (كريدي) على الزبون '{currentCustomer}'. هل تود الاستمرار؟", "تأكيد البيع بالكريدي", MessageBoxButton.YesNo, MessageBoxImage.Question);

			if (messageBoxResult != MessageBoxResult.Yes)

			{

				return;

			}

		}

		decimal num5 = Math.Max(0m, result - num);

		using MySqlConnection mySqlConnection2 = dbHelper.GetConnection();

		try

		{

			mySqlConnection2.Open();

			using MySqlTransaction mySqlTransaction = mySqlConnection2.BeginTransaction();

			try

			{

				string paymentMethod = (cmbPaymentMethod.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "نقداً (Cash)";

				string commandText2 = "INSERT INTO Sales (CashierName, TotalAmount, Date, PaymentMethod) VALUES (@cashier, @total, @date, @pmethod); SELECT LAST_INSERT_ID();";

				int num6;

				using (MySqlCommand mySqlCommand2 = new MySqlCommand(commandText2, mySqlConnection2, mySqlTransaction))

				{

					mySqlCommand2.Parameters.AddWithValue("@cashier", cashierName + " (" + currentCustomer + ")");

					mySqlCommand2.Parameters.AddWithValue("@total", num);

					mySqlCommand2.Parameters.AddWithValue("@date", DateTime.Now);

					mySqlCommand2.Parameters.AddWithValue("@pmethod", paymentMethod);

					num6 = Convert.ToInt32(mySqlCommand2.ExecuteScalar());

				}

				if (currentCustomer != "زبون نقدي عابر" && currentCustomer != "زبون نقدي افتراضي" && !string.IsNullOrEmpty(currentCustomer))

				{

					int pointsEarned = (int)(num / 10m); // 1 point for every 10 DH

					string commandText3 = ((!(num2 > 0m)) ? "UPDATE Customers SET InvoiceCount = InvoiceCount + 1, LoyaltyPoints = LoyaltyPoints + @pts WHERE FullName = @custName" : "UPDATE Customers SET DueBalance = DueBalance + @debt, InvoiceCount = InvoiceCount + 1, LastCreditDate = @now, LoyaltyPoints = LoyaltyPoints + @pts WHERE FullName = @custName");

					using MySqlCommand mySqlCommand3 = new MySqlCommand(commandText3, mySqlConnection2, mySqlTransaction);

					mySqlCommand3.Parameters.AddWithValue("@debt", num2);

					mySqlCommand3.Parameters.AddWithValue("@now", DateTime.Now);

					mySqlCommand3.Parameters.AddWithValue("@pts", pointsEarned);

					mySqlCommand3.Parameters.AddWithValue("@custName", currentCustomer);

					mySqlCommand3.ExecuteNonQuery();

				}

				foreach (CartItem cartItem in cartItems)

				{

					bool flag2 = cartItem.Barcode == "MANUAL" || cartItem.ProductId == manualProductId;

					if (!flag2)

					{

						string commandText4 = "SELECT StockQuantity FROM Products WHERE Id = @id FOR UPDATE";

						decimal num7;

						using (MySqlCommand mySqlCommand4 = new MySqlCommand(commandText4, mySqlConnection2, mySqlTransaction))

						{

							mySqlCommand4.Parameters.AddWithValue("@id", cartItem.ProductId);

							num7 = Convert.ToDecimal(mySqlCommand4.ExecuteScalar());

						}

						if (num7 < cartItem.Quantity)

						{

							throw new Exception($"المنتج '{cartItem.ProductName}' لا يحتوي على كمية كافية في المخزن. المتاح: {num7}");

						}

					}

					string commandText5 = "INSERT INTO SalesDetails (InvoiceId, ProductId, Quantity, SubTotal, CustomName) VALUES (@invId, @prodId, @qty, @subTotal, @customName)";

					using (MySqlCommand mySqlCommand5 = new MySqlCommand(commandText5, mySqlConnection2, mySqlTransaction))

					{

						mySqlCommand5.Parameters.AddWithValue("@invId", num6);

						mySqlCommand5.Parameters.AddWithValue("@prodId", cartItem.ProductId);

						mySqlCommand5.Parameters.AddWithValue("@qty", cartItem.Quantity);

						mySqlCommand5.Parameters.AddWithValue("@subTotal", cartItem.Total);

						mySqlCommand5.Parameters.AddWithValue("@customName", flag2 ? ((IConvertible)cartItem.ProductName) : ((IConvertible)DBNull.Value));

						mySqlCommand5.ExecuteNonQuery();

					}

					if (!flag2)

					{

						string commandText6 = "UPDATE Products SET StockQuantity = StockQuantity - @qty WHERE Id = @prodId";

						using MySqlCommand mySqlCommand6 = new MySqlCommand(commandText6, mySqlConnection2, mySqlTransaction);

						mySqlCommand6.Parameters.AddWithValue("@qty", cartItem.Quantity);

						mySqlCommand6.Parameters.AddWithValue("@prodId", cartItem.ProductId);

						mySqlCommand6.ExecuteNonQuery();

					}

				}

				mySqlTransaction.Commit();

				Task.Run(() => { CloudSyncService.SyncMobileDataAsync(); BackupService.PerformPeriodicBackupIfDue(); });

                _ = LmimaShopApp.Core.Sync.SseManager.BroadcastEventAsync("changed", DateTime.UtcNow.ToString("O"));

				if (num2 <= 0m)

				{

					AutoOpenDrawerIfConfigured();

				}

				currentInvoiceId = num6;

				currentInvoiceTotal = num;

				currentInvoicePaid = result;

				currentInvoiceChange = num5;

				ShowReceiptPreview(num6, num, result, num5);

				MessageBox.Show($"تم إنهاء البيع بنجاح!\n\nرقم الفاتورة: {num6}\nالمبلغ الإجمالي: {num:N2} درهم ({num * 20m:N0} ريال)\nالمبلغ المدفوع: {result:N2} درهم ({result * 20m:N0} ريال)\nالباقي للزبون: {num5:N2} درهم ({num5 * 20m:N0} ريال)", "نجاح العملية", MessageBoxButton.OK, MessageBoxImage.Asterisk);

				checkoutOverlay.Visibility = Visibility.Collapsed;

				cartItems.Clear();

				currentInput = "";

				UpdateTotalAmount();

				LoadProducts();

				txtSearch.Focus();

			}

			catch (Exception ex2)

			{

				mySqlTransaction.Rollback();

				throw new Exception("خطأ أثناء كتابة تفاصيل الفاتورة، تم التراجع لضمان سلامة المخازن: " + ex2.Message);

			}

		}

		catch (Exception ex3)

		{

			MessageBox.Show("فشل إتمام الدفع: " + ex3.Message, "خطأ في المعاملة", MessageBoxButton.OK, MessageBoxImage.Hand);

		}

	}



	private void PrintReceipt(int invoiceId, decimal totalAmount, decimal paidAmount, decimal changeDue, List<CartItem> itemsToPrint)

	{

		//IL_033d: Unknown result type (might be due to invalid IL or missing references)

		//IL_044e: Unknown result type (might be due to invalid IL or missing references)

		//IL_045d: Unknown result type (might be due to invalid IL or missing references)

		//IL_0568: Unknown result type (might be due to invalid IL or missing references)

		//IL_05a5: Unknown result type (might be due to invalid IL or missing references)

		//IL_05b4: Unknown result type (might be due to invalid IL or missing references)

		//IL_06b7: Unknown result type (might be due to invalid IL or missing references)

		//IL_06d7: Unknown result type (might be due to invalid IL or missing references)

		//IL_06ed: Unknown result type (might be due to invalid IL or missing references)

		//IL_072e: Unknown result type (might be due to invalid IL or missing references)

		//IL_073d: Unknown result type (might be due to invalid IL or missing references)

		//IL_0404: Unknown result type (might be due to invalid IL or missing references)

		//IL_087b: Unknown result type (might be due to invalid IL or missing references)

		//IL_089b: Unknown result type (might be due to invalid IL or missing references)

		//IL_08b1: Unknown result type (might be due to invalid IL or missing references)

		//IL_08fd: Unknown result type (might be due to invalid IL or missing references)

		//IL_090c: Unknown result type (might be due to invalid IL or missing references)

		//IL_09e9: Unknown result type (might be due to invalid IL or missing references)

		//IL_09ff: Unknown result type (might be due to invalid IL or missing references)

		//IL_0aaa: Unknown result type (might be due to invalid IL or missing references)

		//IL_0ac0: Unknown result type (might be due to invalid IL or missing references)

		//IL_0b6c: Unknown result type (might be due to invalid IL or missing references)

		//IL_0b82: Unknown result type (might be due to invalid IL or missing references)

		//IL_0bc3: Unknown result type (might be due to invalid IL or missing references)

		//IL_0bd2: Unknown result type (might be due to invalid IL or missing references)

		//IL_0c45: Unknown result type (might be due to invalid IL or missing references)

		//IL_0239: Unknown result type (might be due to invalid IL or missing references)

		//IL_029a: Unknown result type (might be due to invalid IL or missing references)

		try

		{

			string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

			AppSettings appSettings = new AppSettings();

			if (File.Exists(path))

			{

				try

				{

					string json = File.ReadAllText(path);

					appSettings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();

				}

				catch

				{

				}

			}

			string text = appSettings.InvoicePrinter;

			if (string.IsNullOrEmpty(text) || text == "طابعة النظام الافتراضية")

			{

				PrinterSettings printerSettings = new PrinterSettings();

				text = printerSettings.PrinterName;

			}

			if (string.IsNullOrEmpty(text))

			{

				MessageBox.Show("لا توجد طابعة افتراضية مهيئة في النظام لطباعة الفواتير!", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Exclamation);

				return;

			}

			PrintDialog printDialog = new PrintDialog();

			try

			{

				printDialog.PrintQueue = new PrintQueue(new PrintServer(), text);

			}

			catch

			{

			}

			double num = printDialog.PrintableAreaWidth;

			if (num <= 0.0)

			{

				num = ((appSettings.PaperSize == "57") ? 180 : 280);

			}

			double num2 = ((appSettings.PaperSize == "57") ? 180 : 280);

			if (num < num2)

			{

				num2 = num;

			}

			double num3 = (num - num2) / 2.0;

			double num4 = ((appSettings.PaperSize == "57") ? 8 : 15);

			int num5 = ((appSettings.PaperSize == "57") ? 12 : 20);

			double num6 = 22.0;

			double num7 = 150.0;

			double num8 = 90.0;

			double num9 = 60.0;

			bool flag = appSettings.PrintStoreLogo && !string.IsNullOrEmpty(appSettings.StoreLogoPath) && File.Exists(appSettings.StoreLogoPath);

			double num10 = num7 + (double)itemsToPrint.Count * num6 + num8 + num9 + 30.0;

			if (flag)

			{

				num10 += 70.0;

			}

			DrawingVisual drawingVisual = new DrawingVisual();

			using (DrawingContext drawingContext = drawingVisual.RenderOpen())

			{

				drawingContext.DrawRectangle(Brushes.White, null, new Rect(0.0, 0.0, num, num10));

				double num11 = 10.0;

				if (flag)

				{

					try

					{

						Uri uriSource = new Uri(appSettings.StoreLogoPath);

						BitmapImage imageSource = new BitmapImage(uriSource);

						double num12 = 60.0;

						double num13 = num3 + (num2 - num12) / 2.0;

						drawingContext.DrawImage(imageSource, new Rect(num13, num11, num12, num12));

						num11 += num12 + 10.0;

					}

					catch

					{

					}

				}

				Typeface typeface = new Typeface(new FontFamily("Segoe UI, Cairo, Tahoma"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

				FormattedText formattedText = new FormattedText(appSettings.StoreName, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(typeface.FontFamily, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), 14.0, Brushes.Black, 96.0)

				{

					TextAlignment = TextAlignment.Center,

					MaxTextWidth = num2

				};

				drawingContext.DrawText(formattedText, new Point(num3, num11));

				num11 += 24.0;

				string text2 = "";

				if (!string.IsNullOrEmpty(appSettings.StoreAddress))

				{

					text2 = text2 + appSettings.StoreAddress + "\n";

				}

				if (!string.IsNullOrEmpty(appSettings.StorePhone))

				{

					text2 = text2 + "الهاتف: " + appSettings.StorePhone + "\n";

				}

				if (!string.IsNullOrEmpty(appSettings.ICE))

				{

					text2 = text2 + "ICE: " + appSettings.ICE;

					if (!string.IsNullOrEmpty(appSettings.RC)) text2 += " | RC: " + appSettings.RC;

					if (!string.IsNullOrEmpty(appSettings.IF)) text2 += " | IF: " + appSettings.IF;

					text2 += "\n";

				}

				if (!string.IsNullOrEmpty(text2))

				{

					FormattedText formattedText2 = new FormattedText(text2, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 9.0, Brushes.Black, 96.0)

					{

						TextAlignment = TextAlignment.Center,

						MaxTextWidth = num2

					};

					drawingContext.DrawText(formattedText2, new Point(num3, num11));

					num11 += formattedText2.Height + 5.0;

				}

				drawingContext.DrawLine(new Pen(Brushes.Black, 1.0)

				{

					DashStyle = DashStyles.Dash

				}, new Point(num3 + num4, num11), new Point(num3 + num2 - num4, num11));

				num11 += 5.0;

				string textToFormat = $"فاتورة رقم: {invoiceId}\nالتاريخ: {DateTime.Now:yyyy-MM-dd HH:mm}\nالكاشير: {cashierName}\nالزبون: {currentCustomer}";

				FormattedText formattedText3 = new FormattedText(textToFormat, CultureInfo.CurrentCulture, FlowDirection.RightToLeft, typeface, 9.0, Brushes.Black, 96.0);

				formattedText3.MaxTextWidth = num2 - 2.0 * num4;

				drawingContext.DrawText(formattedText3, new Point(num3 + num2 - num4, num11));

				num11 += formattedText3.Height + 5.0;

				drawingContext.DrawLine(new Pen(Brushes.Black, 1.0), new Point(num3 + num4, num11), new Point(num3 + num2 - num4, num11));

				num11 += 5.0;

				FormattedText formattedText4 = new FormattedText("المنتج", CultureInfo.CurrentCulture, FlowDirection.RightToLeft, new Typeface(typeface.FontFamily, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), 9.0, Brushes.Black, 96.0);

				FormattedText formattedText5 = new FormattedText("الكمية", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(typeface.FontFamily, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), 9.0, Brushes.Black, 96.0)

				{

					TextAlignment = TextAlignment.Center

				};

				FormattedText formattedText6 = new FormattedText("المجموع", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(typeface.FontFamily, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), 9.0, Brushes.Black, 96.0)

				{

					TextAlignment = TextAlignment.Left

				};

				drawingContext.DrawText(formattedText4, new Point(num3 + num2 - num4, num11));

				drawingContext.DrawText(formattedText5, new Point(num3 + num2 / 2.0, num11));

				drawingContext.DrawText(formattedText6, new Point(num3 + num4, num11));

				num11 += 18.0;

				drawingContext.DrawLine(new Pen(Brushes.Black, 1.0)

				{

					DashStyle = DashStyles.Dash

				}, new Point(num3 + num4, num11), new Point(num3 + num2 - num4, num11));

				num11 += 5.0;

				foreach (CartItem item in itemsToPrint)

				{

					string text3 = item.ProductName;

					if (text3.Length > num5)

					{

						text3 = text3.Substring(0, num5 - 2) + "..";

					}

					FormattedText formattedText7 = new FormattedText(text3, CultureInfo.CurrentCulture, FlowDirection.RightToLeft, typeface, 9.0, Brushes.Black, 96.0);

					FormattedText formattedText8 = new FormattedText($"{item.Quantity:0.###}", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 9.0, Brushes.Black, 96.0)

					{

						TextAlignment = TextAlignment.Center

					};

					FormattedText formattedText9 = new FormattedText($"{item.Total:N2}", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 9.0, Brushes.Black, 96.0)

					{

						TextAlignment = TextAlignment.Left

					};

					drawingContext.DrawText(formattedText7, new Point(num3 + num2 - num4, num11));

					drawingContext.DrawText(formattedText8, new Point(num3 + num2 / 2.0, num11));

					drawingContext.DrawText(formattedText9, new Point(num3 + num4, num11));

					num11 += num6;

				}

				drawingContext.DrawLine(new Pen(Brushes.Black, 1.0), new Point(num3 + num4, num11), new Point(num3 + num2 - num4, num11));

				num11 += 5.0;



				// TVA section removed per user request.



				FormattedText formattedText10 = new FormattedText("الإجمالي (TTC):", CultureInfo.CurrentCulture, FlowDirection.RightToLeft, new Typeface(typeface.FontFamily, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), 10.0, Brushes.Black, 96.0);

				FormattedText formattedText11 = new FormattedText($"{totalAmount:N2} درهم", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(typeface.FontFamily, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), 10.0, Brushes.Black, 96.0)

				{

					TextAlignment = TextAlignment.Left

				};

				drawingContext.DrawText(formattedText10, new Point(num3 + num2 - num4, num11));

				drawingContext.DrawText(formattedText11, new Point(num3 + num4, num11));

				num11 += 18.0;

				FormattedText formattedText12 = new FormattedText("المدفوع:", CultureInfo.CurrentCulture, FlowDirection.RightToLeft, typeface, 9.0, Brushes.Black, 96.0);

				FormattedText formattedText13 = new FormattedText($"{paidAmount:N2} درهم", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 9.0, Brushes.Black, 96.0)

				{

					TextAlignment = TextAlignment.Left

				};

				drawingContext.DrawText(formattedText12, new Point(num3 + num2 - num4, num11));

				drawingContext.DrawText(formattedText13, new Point(num3 + num4, num11));

				num11 += 16.0;

				FormattedText formattedText14 = new FormattedText("الباقي:", CultureInfo.CurrentCulture, FlowDirection.RightToLeft, typeface, 9.0, Brushes.Black, 96.0);

				FormattedText formattedText15 = new FormattedText($"{changeDue:N2} درهم", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 9.0, Brushes.Black, 96.0)

				{

					TextAlignment = TextAlignment.Left

				};

				drawingContext.DrawText(formattedText14, new Point(num3 + num2 - num4, num11));

				drawingContext.DrawText(formattedText15, new Point(num3 + num4, num11));

				num11 += 20.0;

				drawingContext.DrawLine(new Pen(Brushes.Black, 1.0)

				{

					DashStyle = DashStyles.Dash

				}, new Point(num3 + num4, num11), new Point(num3 + num2 - num4, num11));

				num11 += 8.0;

				if (!string.IsNullOrEmpty(appSettings.InvoiceFooter))

				{

					FormattedText formattedText16 = new FormattedText(appSettings.InvoiceFooter, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 10.0, Brushes.Black, 96.0)

					{

						TextAlignment = TextAlignment.Center,

						MaxTextWidth = num2

					};

					drawingContext.DrawText(formattedText16, new Point(num3, num11));

				}

			}

			printDialog.PrintVisual(drawingVisual, $"Invoice #{invoiceId}");

		}

		catch (Exception ex)

		{

			MessageBox.Show("فشل طباعة الفاتورة: " + ex.Message, "خطأ في الطباعة", MessageBoxButton.OK, MessageBoxImage.Hand);

		}

	}



	private void ShowReceiptPreview(int invoiceId, decimal totalAmount, decimal paidAmount, decimal changeDue)

	{

		try

		{

			string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

			AppSettings appSettings = new AppSettings();

			if (File.Exists(path))

			{

				try

				{

					string json = File.ReadAllText(path);

					appSettings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();

				}

				catch

				{

				}

			}

			if (appSettings.PrintStoreLogo && !string.IsNullOrEmpty(appSettings.StoreLogoPath) && File.Exists(appSettings.StoreLogoPath))

			{

				try

				{

					imgPreviewLogo.Source = new BitmapImage(new Uri(appSettings.StoreLogoPath));

					imgPreviewLogo.Visibility = Visibility.Visible;

				}

				catch

				{

					imgPreviewLogo.Visibility = Visibility.Collapsed;

				}

			}

			else

			{

				imgPreviewLogo.Visibility = Visibility.Collapsed;

			}

			lblPreviewStoreName.Text = appSettings.StoreName;

			string text = "";

			if (!string.IsNullOrEmpty(appSettings.StoreAddress))

			{

				text = text + appSettings.StoreAddress + "\n";

			}

			if (!string.IsNullOrEmpty(appSettings.StorePhone))

			{

				text = text + "الهاتف: " + appSettings.StorePhone;

			}

			lblPreviewStoreDetails.Text = text;

			lblPreviewInvoiceMeta.Text = $"فاتورة رقم: {invoiceId}\nالتاريخ: {DateTime.Now:yyyy-MM-dd HH:mm}\nالكاشير: {cashierName}\nالزبون: {currentCustomer}";

			itemsReceiptPreview.ItemsSource = cartItems.ToList();

			lblPreviewTotalAmount.Text = $"{totalAmount:N2} درهم";

			lblPreviewPaidAmount.Text = $"{paidAmount:N2} درهم";

			lblPreviewChangeDue.Text = $"{changeDue:N2} درهم";

			lblPreviewInvoiceFooter.Text = appSettings.InvoiceFooter;

			if (borderReceiptPreview != null)

			{

				borderReceiptPreview.Width = ((appSettings.PaperSize == "57") ? 210 : 290);

			}

			receiptPreviewOverlay.Visibility = Visibility.Visible;

		}

		catch (Exception ex)

		{

			MessageBox.Show("فشل تحميل معاينة الفاتورة: " + ex.Message, "خطأ", MessageBoxButton.OK, MessageBoxImage.Hand);

		}

	}



	private void BtnPrintPreviewInvoice_Click(object sender, RoutedEventArgs e)

	{

		if (itemsReceiptPreview.ItemsSource is List<CartItem> itemsToPrint && currentInvoiceId > 0)

		{

			PrintReceipt(currentInvoiceId, currentInvoiceTotal, currentInvoicePaid, currentInvoiceChange, itemsToPrint);

		}

		receiptPreviewOverlay.Visibility = Visibility.Collapsed;

		txtSearch.Focus();

	}



		private void BtnSavePdfPreviewInvoice_Click(object sender, RoutedEventArgs e)

	{

		try

		{

			System.Windows.Controls.PrintDialog printDialog = new System.Windows.Controls.PrintDialog();

			var pdfPrinter = new System.Printing.LocalPrintServer().GetPrintQueues().FirstOrDefault(q => q.Name.IndexOf("PDF", StringComparison.OrdinalIgnoreCase) >= 0);

			if (pdfPrinter != null)

			{

				printDialog.PrintQueue = pdfPrinter;

				printDialog.PrintVisual(borderReceiptPreview, "Facture PDF");

				MessageBox.Show("?? ????? ???????? ?????. ???? ?????? ???? ????? ?? ??????? ????????.", "??? PDF", MessageBoxButton.OK, MessageBoxImage.Information);

			}

			else

			{

				MessageBox.Show("????? PDF ??? ??????. ???? ??????? ?? ??????? ?????? ??????? ????? PDF ??????.", "???", MessageBoxButton.OK, MessageBoxImage.Warning);

			}

		}

		catch (Exception ex)

		{

			MessageBox.Show("??? ????? ??? PDF: " + ex.Message, "???", MessageBoxButton.OK, MessageBoxImage.Error);

		}

	}

private void BtnClosePreviewInvoice_Click(object sender, RoutedEventArgs e)

	{

		receiptPreviewOverlay.Visibility = Visibility.Collapsed;

		txtSearch.Focus();

	}



	private void LoadCustomers()

	{

		try

		{

			cmbCustomers.Items.Clear();

			using (MySqlConnection mySqlConnection = dbHelper.GetConnection())

			{

				mySqlConnection.Open();

				string commandText = "SELECT FullName FROM Customers WHERE deleted_at IS NULL ORDER BY Id ASC";

				using MySqlCommand mySqlCommand = new MySqlCommand(commandText, mySqlConnection);

				using MySqlDataReader mySqlDataReader = mySqlCommand.ExecuteReader();

				while (mySqlDataReader.Read())

				{

					cmbCustomers.Items.Add(mySqlDataReader["FullName"].ToString() ?? "");

				}

			}

			if (cmbCustomers.Items.Count > 0)

			{

				cmbCustomers.SelectedIndex = 0;

				currentCustomer = cmbCustomers.SelectedItem?.ToString() ?? "زبون نقدي افتراضي";

				lblCashier.Text = cashierName + " (" + currentCustomer + ")";

				if (lblSelectedCustomerName != null)

				{

					lblSelectedCustomerName.Text = currentCustomer;

				}

			}

		}

		catch

		{

			cmbCustomers.Items.Add("زبون نقدي افتراضي");

			cmbCustomers.Items.Add("أحمد السعيدي");

			cmbCustomers.Items.Add("فاطمة الزهراء");

			cmbCustomers.Items.Add("ياسين المعمري");

			cmbCustomers.SelectedIndex = 0;

			currentCustomer = "زبون نقدي افتراضي";

			if (lblSelectedCustomerName != null)

			{

				lblSelectedCustomerName.Text = currentCustomer;

			}

		}

	}



	public void NavigateTo(string viewName)

	{

		if (btnMenuPOS == null || btnMenuProducts == null || btnMenuRequestedProducts == null || btnMenuCustomers == null || btnMenuSuppliers == null || btnMenuReports == null || btnMenuUsers == null || btnMenuSettings == null || mainContentControl == null || gridPOS == null)

		{

			return;

		}

		btnMenuPOS.Style = (Style)FindResource("SidebarButtonStyle");

		btnMenuProducts.Style = (Style)FindResource("SidebarButtonStyle");

		btnMenuRequestedProducts.Style = (Style)FindResource("SidebarButtonStyle");

		btnMenuCustomers.Style = (Style)FindResource("SidebarButtonStyle");

		btnMenuSuppliers.Style = (Style)FindResource("SidebarButtonStyle");

		btnMenuReports.Style = (Style)FindResource("SidebarButtonStyle");

		btnMenuUsers.Style = (Style)FindResource("SidebarButtonStyle");

		btnMenuSettings.Style = (Style)FindResource("SidebarButtonStyle");

		if (viewName == "POS")

		{

			btnMenuPOS.Style = (Style)FindResource("ActiveSidebarButtonStyle");

			mainContentControl.Visibility = Visibility.Collapsed;

			gridPOS.Visibility = Visibility.Visible;

			LoadProducts();

			LoadCustomers();

			return;

		}

		gridPOS.Visibility = Visibility.Collapsed;

		mainContentControl.Visibility = Visibility.Visible;

		switch (viewName)

		{

		case "Products":

			btnMenuProducts.Style = (Style)FindResource("ActiveSidebarButtonStyle");

			mainContentControl.Content = new ProductsWindow();

			break;

		case "RequestedProducts":

			btnMenuRequestedProducts.Style = (Style)FindResource("ActiveSidebarButtonStyle");

			mainContentControl.Content = new RequestedProductsWindow();

			break;

		case "Customers":

			btnMenuCustomers.Style = (Style)FindResource("ActiveSidebarButtonStyle");

			mainContentControl.Content = new CustomersWindow();

			break;

		case "Suppliers":

			btnMenuSuppliers.Style = (Style)FindResource("ActiveSidebarButtonStyle");

			mainContentControl.Content = new SuppliersWindow();

			break;

		case "Reports":

			btnMenuReports.Style = (Style)FindResource("ActiveSidebarButtonStyle");

			mainContentControl.Content = new ReportsWindow();

			break;

		case "Users":

			btnMenuUsers.Style = (Style)FindResource("ActiveSidebarButtonStyle");

			mainContentControl.Content = new UsersWindow();

			break;

		case "Settings":

			btnMenuSettings.Style = (Style)FindResource("ActiveSidebarButtonStyle");

			mainContentControl.Content = new SettingsWindow();

			break;

		}

	}



	private void BtnSidebar_Click(object sender, RoutedEventArgs e)

	{

		if (sender is Button { Content: not null } button)

		{

			string text = button.Content.ToString() ?? "";

			if (text.Contains("نقطة البيع"))

			{

				NavigateTo("POS");

			}

			else if (text.Contains("المنتجات"))

			{

				NavigateTo("Products");

			}

			else if (text.Contains("السلع المطلوبة"))

			{

				NavigateTo("RequestedProducts");

			}

			else if (text.Contains("الزبناء"))

			{

				NavigateTo("Customers");

			}

			else if (text.Contains("الممونين"))

			{

				NavigateTo("Suppliers");

			}

			else if (text.Contains("التقارير"))

			{

				NavigateTo("Reports");

			}

			else if (text.Contains("إغلاق الصندوق") || button.Name == "btnMenuZReport")

			{

				new ZReportWindow { Owner = this }.ShowDialog();

			}

			else if (text.Contains("المستخدمين"))

			{

				NavigateTo("Users");

			}

			else if (button.Name == "btnMenuMobileScanner")

			{

				new MobileScannerPairingWindow { Owner = this }.ShowDialog();

			}

			else if (text.Contains("الإعدادات"))

			{

				NavigateTo("Settings");

			}

		}

	}



	private void BtnQuickRequestProduct_Click(object sender, RoutedEventArgs e)

	{

		string currentSearch = txtSearch.Text.Trim();

		NavigateTo("RequestedProducts");

		if (mainContentControl.Content is RequestedProductsWindow reqWin)

		{

			reqWin.OpenAddModalWithPreset(currentSearch);

		}

	}



	private void BtnMobileSyncInfo_Click(object sender, RoutedEventArgs e)

	{

		new MobileScannerPairingWindow { Owner = this }.ShowDialog();

	}



	private void BtnSidebarLogout_Click(object sender, RoutedEventArgs e)

	{

		MessageBoxResult messageBoxResult = MessageBox.Show("هل تود تسجيل الخروج من النظام بالكامل؟", "تسجيل خروج", MessageBoxButton.YesNo, MessageBoxImage.Question);

		if (messageBoxResult == MessageBoxResult.Yes)

		{

			LoginWindow loginWindow = new LoginWindow();

			loginWindow.Show();

			Close();

		}

	}



	private void LstCart_SelectionChanged(object sender, SelectionChangedEventArgs e)

	{

		currentInput = "";

	}



	private void BtnOpenDrawer_Click(object sender, RoutedEventArgs e)

	{

		TriggerCashDrawerOpen(silent: false);

	}



	private void AutoOpenDrawerIfConfigured()

	{

		try

		{

			string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

			bool flag = true;

			if (File.Exists(path))

			{

				string json = File.ReadAllText(path);

				using JsonDocument jsonDocument = JsonDocument.Parse(json);

				if (jsonDocument.RootElement.TryGetProperty("OpenDrawerOnCheckout", out var value))

				{

					flag = value.GetBoolean();

				}

			}

			if (flag)

			{

				TriggerCashDrawerOpen(silent: true);

			}

		}

		catch

		{

		}

	}



	private void TriggerCashDrawerOpen(bool silent)

	{

		try

		{

			string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

			string text = "";

			string text2 = "27,112,0,25,250";

			if (File.Exists(path))

			{

				string json = File.ReadAllText(path);

				using JsonDocument jsonDocument = JsonDocument.Parse(json);

				JsonElement rootElement = jsonDocument.RootElement;

				if (rootElement.TryGetProperty("InvoicePrinter", out var value))

				{

					text = value.GetString() ?? "";

				}

				if (rootElement.TryGetProperty("DrawerKickCode", out var value2))

				{

					text2 = value2.GetString() ?? "27,112,0,25,250";

				}

			}

			if (string.IsNullOrEmpty(text) || text == "طابعة النظام الافتراضية")

			{

				PrinterSettings printerSettings = new PrinterSettings();

				text = printerSettings.PrinterName;

			}

			if (string.IsNullOrEmpty(text))

			{

				if (!silent)

				{

					MessageBox.Show("لا توجد طابعة افتراضية مهيئة في النظام! يرجى تثبيت طابعة فواتير أو تهيئتها من الإعدادات.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Exclamation);

				}

				return;

			}

			string[] array = text2.Split(',');

			byte[] array2 = new byte[array.Length];

			for (int i = 0; i < array.Length; i++)

			{

				array2[i] = byte.Parse(array[i].Trim());

			}

			bool flag = RawPrinterHelper.SendBytesToPrinter(text, array2);

			if (!silent)

			{

				if (flag)

				{

					MessageBox.Show("تم إرسال أمر فتح درج النقود (صندوق الكاشير) إلى الطابعة بنجاح!", "فتح الدرج", MessageBoxButton.OK, MessageBoxImage.Asterisk);

				}

				else

				{

					MessageBox.Show("تعذر إرسال أمر فتح الدرج للطابعة '" + text + "'. يرجى التأكد من توصيل الطابعة والدرج.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Exclamation);

				}

			}

		}

		catch (Exception ex)

		{

			if (!silent)

			{

				MessageBox.Show("فشل فتح درج النقود: " + ex.Message + "\nتنبيه: تأكد من تهيئة الطابعة الافتراضية في الإعدادات.", "خطأ في فتح الدرج", MessageBoxButton.OK, MessageBoxImage.Hand);

			}

		}

	}



	private void EnsureManualPlaceholderProduct()

	{

		using MySqlConnection mySqlConnection = dbHelper.GetConnection();

		try

		{

			mySqlConnection.Open();

			string commandText = "SELECT Id FROM Products WHERE Barcode = 'MANUAL' LIMIT 1;";

			using (MySqlCommand mySqlCommand = new MySqlCommand(commandText, mySqlConnection))

			{

				object obj = mySqlCommand.ExecuteScalar();

				if (obj != null)

				{

					manualProductId = Convert.ToInt32(obj);

				}

			}

			if (manualProductId == 0)

			{

				string commandText2 = "\r\n                            INSERT INTO Products (Barcode, ProductName, Category, PurchasePrice, SellPrice, StockQuantity) \r\n                            VALUES ('MANUAL', 'منتج يدوي غير مدرج', 'عام', 0.00, 0.00, 999999);\r\n                            SELECT LAST_INSERT_ID();";

				using MySqlCommand mySqlCommand2 = new MySqlCommand(commandText2, mySqlConnection);

				manualProductId = Convert.ToInt32(mySqlCommand2.ExecuteScalar());

				return;

			}

		}

		catch (Exception ex)

		{

			Console.WriteLine("Error ensuring manual placeholder product: " + ex.Message);

		}

	}



	private void BtnAddManualItem_Click(object sender, RoutedEventArgs e)

	{

		txtManualItemName.Text = "";

		txtManualItemPrice.Text = "";

		txtManualItemQty.Text = "1";

		manualItemOverlay.Visibility = Visibility.Visible;

		txtManualItemName.Focus();

	}



	private void BtnCancelAddManualItem_Click(object sender, RoutedEventArgs e)

	{

		manualItemOverlay.Visibility = Visibility.Collapsed;

		txtSearch.Focus();

	}



	private void BtnConfirmAddManualItem_Click(object sender, RoutedEventArgs e)

	{

		string text = txtManualItemName.Text.Trim();

		string input = txtManualItemPrice.Text.Trim();

		string s = txtManualItemQty.Text.Trim();

		if (string.IsNullOrEmpty(text))

		{

			MessageBox.Show("يرجى إدخال اسم المنتج!", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Exclamation);

			return;

		}

		if (!TryParseDecimal(input, out var result) || result < 0m)

		{

			MessageBox.Show("يرجى إدخال سعر صحيح!", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Exclamation);

			return;

		}

		if (!int.TryParse(s, out var result2) || result2 <= 0)

		{

			MessageBox.Show("يرجى إدخال كمية صحيحة!", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Exclamation);

			return;

		}

		if (manualProductId == 0)

		{

			EnsureManualPlaceholderProduct();

		}

		CartItem item = new CartItem

		{

			ProductId = manualProductId,

			Barcode = "MANUAL",

			ProductName = text,

			SellPrice = result,

			Quantity = result2,

			DiscountPercent = 0m

		};

		cartItems.Add(item);

		lstCart.Items.Refresh();

		UpdateTotalAmount();

		manualItemOverlay.Visibility = Visibility.Collapsed;

		txtSearch.Focus();

	}



	private void BtnRemoveCartItem_Click(object sender, RoutedEventArgs e)

	{

		if (sender is Button { DataContext: CartItem dataContext })

		{

			cartItems.Remove(dataContext);

			UpdateTotalAmount();

			txtSearch.Focus();

		}

	}



	private void BtnDiscountCartItem_Click(object sender, RoutedEventArgs e)

	{

		if (sender is Button { DataContext: CartItem dataContext })

		{

            // Simple logic: Toggle discount 0 -> 10 -> 20 -> 50 -> 0

            if (dataContext.DiscountPercent == 0) dataContext.DiscountPercent = 10;

            else if (dataContext.DiscountPercent == 10) dataContext.DiscountPercent = 20;

            else if (dataContext.DiscountPercent == 20) dataContext.DiscountPercent = 50;

            else dataContext.DiscountPercent = 0;

            

            // Force UI update

            var idx = cartItems.IndexOf(dataContext);

            if (idx >= 0)

            {

                cartItems.RemoveAt(idx);

                cartItems.Insert(idx, dataContext);

            }

			UpdateTotalAmount();

			txtSearch.Focus();

		}

	}



	private void BtnDecrementCartItem_Qty_Click(object sender, RoutedEventArgs e)

	{

		if (sender is Button { DataContext: CartItem dataContext })

		{

			if (dataContext.Quantity > 1m)

			{

				dataContext.Quantity--;

			}

			else

			{

				cartItems.Remove(dataContext);

			}

			lstCart.Items.Refresh();

			UpdateTotalAmount();

			txtSearch.Focus();

		}

	}



	private void BtnIncrementCartItem_Qty_Click(object sender, RoutedEventArgs e)

	{

		if (!(sender is Button { DataContext: var dataContext }))

		{

			return;

		}

		CartItem item = dataContext as CartItem;

		if (item == null)

		{

			return;

		}

		if (!(item.Barcode == "MANUAL") && item.ProductId != manualProductId)

		{

			Product product = allProducts.FirstOrDefault((Product p) => p.Id == item.ProductId);

			if (product != null && item.Quantity >= product.StockQuantity)

			{

				MessageBox.Show("الكمية المطلوبة تتجاوز الكمية المتاحة في المخزن (" + product.StockQuantity.ToString("0.###") + ")!", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Exclamation);

				txtSearch.Focus();

				return;

			}

		}

		item.Quantity++;

		lstCart.Items.Refresh();

		UpdateTotalAmount();

		txtSearch.Focus();

	}



	private void OverlayTextBox_GotFocus(object sender, RoutedEventArgs e)

	{

		activeOverlayTextBox = sender as TextBox;

	}



	private void OverlayKeypad_Click(object sender, RoutedEventArgs e)

	{

		if (activeOverlayTextBox == null || !(sender is Button { Content: not null } button))

		{

			return;

		}

		string text = button.Content.ToString() ?? "";

		if (text == "C")

		{

			activeOverlayTextBox.Text = "";

		}

		else if (text == "⌫" || text == "⌫" || text == "âŒ«" || text.Contains("Back"))

		{

			if (activeOverlayTextBox.Text.Length > 0)

			{

				activeOverlayTextBox.Text = activeOverlayTextBox.Text.Substring(0, activeOverlayTextBox.Text.Length - 1);

			}

		}

		else

		{

			activeOverlayTextBox.Text += text;

		}

		activeOverlayTextBox.SelectionStart = activeOverlayTextBox.Text.Length;

	}



	private bool TryParseDecimal(string input, out decimal result)

	{

		result = 0m;

		if (string.IsNullOrEmpty(input))

		{

			return false;

		}

		string s = input.Replace(',', '.').Trim();

		return decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out result);

	}



	private void BtnQtyMinus_Click(object sender, RoutedEventArgs e)

	{

		if (int.TryParse(txtManualItemQty.Text, out var result))

		{

			if (result > 1)

			{

				txtManualItemQty.Text = (result - 1).ToString();

			}

		}

		else

		{

			txtManualItemQty.Text = "1";

		}

	}



	private void BtnQtyPlus_Click(object sender, RoutedEventArgs e)

	{

		if (int.TryParse(txtManualItemQty.Text, out var result))

		{

			txtManualItemQty.Text = (result + 1).ToString();

		}

		else

		{

			txtManualItemQty.Text = "1";

		}

	}



	private void InitializeVirtualKeyboard()

	{

		if (keyboardRow0 != null && keyboardRow1 != null && keyboardRow2 != null)

		{

			keyboardRow0.Children.Clear();

			keyboardRow1.Children.Clear();

			keyboardRow2.Children.Clear();

			string[] array = ((currentKeyboardLang == "AR") ? arRow0 : frRow0);

			string[] array2 = ((currentKeyboardLang == "AR") ? arRow1 : frRow1);

			string[] array3 = ((currentKeyboardLang == "AR") ? arRow2 : frRow2);

			string[] array4 = array;

			foreach (string content in array4)

			{

				keyboardRow0.Children.Add(CreateKeyButton(content));

			}

			string[] array5 = array2;

			foreach (string content2 in array5)

			{

				keyboardRow1.Children.Add(CreateKeyButton(content2));

			}

			string[] array6 = array3;

			foreach (string content3 in array6)

			{

				keyboardRow2.Children.Add(CreateKeyButton(content3));

			}

			btnSwitchLang.Content = ((currentKeyboardLang == "AR") ? "Français" : "العربية");

		}

	}



	private int keyboardSizeMode = 0; // 0: كبير وواضح (افتراضي 260px), 1: شاشة كاملة (290px), 2: متوسط (190px)



	private Button CreateKeyButton(string content)

	{

		Button button = new Button();

		button.Content = content;

		button.Style = FindResource("KeypadButtonStyle") as Style;

		button.Focusable = false;

		button.FontSize = keyboardSizeMode switch

		{

			0 => 21.0, // كبير وواضح جداً (افتراضي)

			1 => 23.0, // شاشة كاملة

			_ => 16.0  // متوسط

		};

		button.FontWeight = FontWeights.Bold;

		button.Margin = new Thickness(1.5, 1.0, 1.5, 1.0);

		button.Padding = new Thickness(0);

		Button button2 = button;

		button2.Click += VirtualKeyboardKey_Click;

		return button2;

	}



	private void VirtualKeyboardKey_Click(object sender, RoutedEventArgs e)

	{

		if (sender is Button { Content: not null } button)

		{

			string text = button.Content.ToString() ?? "";

			InsertTextIntoActiveTextBox(text);

		}

	}



	private TextBox? GetActiveTextBox()

	{

		if (Keyboard.FocusedElement is TextBox textBox)

		{

			activeTextTextBox = textBox;

		}

		return activeTextTextBox;

	}



	private void InsertTextIntoActiveTextBox(string text)

	{

		TextBox activeTextBox = GetActiveTextBox();

		if (activeTextBox == null)

		{

			activeTextBox = txtSearch;

		}

		int selectionStart = activeTextBox.SelectionStart;

		int selectionLength = activeTextBox.SelectionLength;

		string text2 = activeTextBox.Text;

		activeTextBox.Text = text2.Remove(selectionStart, selectionLength).Insert(selectionStart, text);

		activeTextBox.SelectionStart = selectionStart + text.Length;

		activeTextBox.Focus();

	}



	private void BtnSwitchLang_Click(object sender, RoutedEventArgs e)

	{

		currentKeyboardLang = ((currentKeyboardLang == "AR") ? "FR" : "AR");

		InitializeVirtualKeyboard();

	}



	private void BtnKeyboardSpace_Click(object sender, RoutedEventArgs e)

	{

		InsertTextIntoActiveTextBox(" ");

	}



	private void BtnKeyboardBackspace_Click(object sender, RoutedEventArgs e)

	{

		TextBox activeTextBox = GetActiveTextBox();

		if (activeTextBox == null)

		{

			activeTextBox = txtSearch;

		}

		int selectionStart = activeTextBox.SelectionStart;

		int selectionLength = activeTextBox.SelectionLength;

		if (selectionLength > 0)

		{

			activeTextBox.Text = activeTextBox.Text.Remove(selectionStart, selectionLength);

			activeTextBox.SelectionStart = selectionStart;

		}

		else if (selectionStart > 0)

		{

			activeTextBox.Text = activeTextBox.Text.Remove(selectionStart - 1, 1);

			activeTextBox.SelectionStart = selectionStart - 1;

		}

		activeTextBox.Focus();

	}



	private void BtnHideKeyboard_Click(object sender, RoutedEventArgs e)

	{

		virtualKeyboardPanel.Visibility = Visibility.Collapsed;

	}



	private void BtnToggleKeyboardSize_Click(object sender, RoutedEventArgs e)

	{

		keyboardSizeMode = (keyboardSizeMode + 1) % 3;

		ApplyKeyboardSize();

	}



	private void ApplyKeyboardSize()

	{

		if (virtualKeyboardPanel == null) return;



		switch (keyboardSizeMode)

		{

			case 0: // كبير وواضح جداً (افتراضي)

				virtualKeyboardPanel.Height = 260;

				virtualKeyboardPanel.MaxWidth = 1350;

				if (btnToggleKeyboardSize != null) btnToggleKeyboardSize.Content = "📐 كبير";

				break;

			case 1: // كامل الشاشة

				virtualKeyboardPanel.Height = 290;

				virtualKeyboardPanel.MaxWidth = double.PositiveInfinity;

				if (btnToggleKeyboardSize != null) btnToggleKeyboardSize.Content = "📐 كامل";

				break;

			case 2: // متوسط

				virtualKeyboardPanel.Height = 190;

				virtualKeyboardPanel.MaxWidth = 1050;

				if (btnToggleKeyboardSize != null) btnToggleKeyboardSize.Content = "📐 متوسط";

				break;

		}



		if (btnSwitchLang != null)

			btnSwitchLang.FontSize = (keyboardSizeMode == 2) ? 13.5 : (keyboardSizeMode == 0 ? 15.0 : 16.5);

		if (btnSpace != null)

			btnSpace.FontSize = (keyboardSizeMode == 2) ? 13.5 : (keyboardSizeMode == 0 ? 15.0 : 16.5);

		if (btnBackspace != null)

			btnBackspace.FontSize = (keyboardSizeMode == 2) ? 16.0 : (keyboardSizeMode == 0 ? 20.0 : 22.0);



		InitializeVirtualKeyboard();

	}



	public void ToggleVirtualKeyboard()

	{

		if (virtualKeyboardPanel.Visibility == Visibility.Visible)

		{

			virtualKeyboardPanel.Visibility = Visibility.Collapsed;

			return;

		}

		virtualKeyboardPanel.Visibility = Visibility.Visible;

		InitializeVirtualKeyboard();

	}



	private void BtnToggleVirtualKeyboard_Click(object sender, RoutedEventArgs e)

	{

		ToggleVirtualKeyboard();

	}



}









