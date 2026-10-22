using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ContentOptimizer.App.Services
{
    public static class DarkTitleBar
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        public static void Apply(Window window)
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            int dark = 1;
            if (DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int)) != 0)
            {
                DwmSetWindowAttribute(handle, 19, ref dark, sizeof(int));
            }

            int caption = 0x0014100E;
            DwmSetWindowAttribute(handle, 35, ref caption, sizeof(int));
            int text = 0x00F2EBE8;
            DwmSetWindowAttribute(handle, 36, ref text, sizeof(int));
        }
    }
}
