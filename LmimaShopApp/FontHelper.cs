using System;
using System.Windows;
using System.Windows.Media;

namespace LmimaShopApp
{
    public static class FontHelper
    {
        public static FontFamily GetFontFamily(string fontName)
        {
            switch (fontName)
            {
                case "Cairo":
                    return new FontFamily(new Uri("pack://application:,,,/"), "./Assets/Fonts/#Cairo");
                case "Tajawal":
                    return new FontFamily(new Uri("pack://application:,,,/"), "./Assets/Fonts/#Tajawal");
                case "Amiri":
                    return new FontFamily(new Uri("pack://application:,,,/"), "./Assets/Fonts/#Amiri");
                case "Almarai":
                    return new FontFamily(new Uri("pack://application:,,,/"), "./Assets/Fonts/#Almarai");
                case "Segoe UI":
                    return new FontFamily("Segoe UI");
                case "Tahoma":
                    return new FontFamily("Tahoma");
                case "Arial":
                    return new FontFamily("Arial");
                default:
                    // Default fallback
                    return new FontFamily(new Uri("pack://application:,,,/"), "./Assets/Fonts/#Cairo, Segoe UI, Cairo, Tahoma");
            }
        }

        public static void ApplyFont(string fontName)
        {
            try
            {
                FontFamily fontFamily = GetFontFamily(fontName);
                Application.Current.Resources["AppFontFamily"] = fontFamily;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error applying font: " + ex.Message);
            }
        }
    }
}
