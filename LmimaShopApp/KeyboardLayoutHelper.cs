using System;
using System.Runtime.InteropServices;
using System.Windows.Controls;

namespace LmimaShopApp
{
    public static class KeyboardLayoutHelper
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadKeyboardLayout(string pwszKLID, uint Flags);

        [DllImport("user32.dll")]
        private static extern IntPtr ActivateKeyboardLayout(IntPtr hkl, uint Flags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetKeyboardLayout(uint idThread);

        private static IntPtr hklFrench = IntPtr.Zero;
        private static IntPtr hklOriginal = IntPtr.Zero;

        public static void SwitchToFrench()
        {
            try
            {
                if (hklFrench == IntPtr.Zero)
                {
                    hklFrench = LoadKeyboardLayout("0000040c", 1); // French (France) AZERTY
                }
                hklOriginal = GetKeyboardLayout(0);
                ActivateKeyboardLayout(hklFrench, 0);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error switching keyboard layout to French: " + ex.Message);
            }
        }

        public static void RestoreOriginal()
        {
            try
            {
                if (hklOriginal != IntPtr.Zero)
                {
                    ActivateKeyboardLayout(hklOriginal, 0);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error restoring keyboard layout: " + ex.Message);
            }
        }

        public static void BindToTextBox(TextBox textBox)
        {
            if (textBox == null) return;
            textBox.GotFocus += (s, e) => SwitchToFrench();
            textBox.LostFocus += (s, e) => RestoreOriginal();
        }
    }
}
