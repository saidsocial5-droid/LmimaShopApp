using System;
using System.Diagnostics;
using System.IO;

namespace LmimaShopApp
{
    public static class KeyboardHelper
    {
        /// <summary>
        /// تشغيل لوحة المفاتيح باللمس (Touch Keyboard) لنظام تشغيل ويندوز،
        /// وفي حال عدم توفرها يتم تشغيل لوحة المفاتيح على الشاشة (OSK).
        /// </summary>
        public static void ShowTouchKeyboard()
        {
            try
            {
                // مسار لوحة مفاتيح ويندوز باللمس الافتراضي
                string commonProgramFiles = Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles);
                string tabTipPath = Path.Combine(commonProgramFiles, @"microsoft shared\ink\tabtip.exe");

                if (File.Exists(tabTipPath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = tabTipPath,
                        UseShellExecute = true
                    });
                }
                else
                {
                    // محاولة فتح لوحة المفاتيح التقليدية كبديل
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "osk.exe",
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error launching touch keyboard: " + ex.Message);
                try
                {
                    // محاولة أخيرة لتشغيل OSK
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "osk.exe",
                        UseShellExecute = true
                    });
                }
                catch (Exception fallbackEx)
                {
                    Debug.WriteLine("Error launching fallback keyboard: " + fallbackEx.Message);
                }
            }
        }
    }
}
