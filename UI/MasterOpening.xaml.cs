using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Runtime.InteropServices;
using System.Windows;

namespace RevitUI.UI
{
    public partial class MasterOpening : Window
    {
        // 1. Import FindWindow from Windows API
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private const int SW_RESTORE = 9;

        private Document _doc;
        private UIDocument _uidoc;

        public static void GetOrCreate(Document doc, UIDocument uidoc)
        {
            // 2. Define your exact window title (Make sure this matches the Title="XYZ" in your XAML)
            string windowTitle = "Master Opening";

            // 3. Ask the OS if a window with this title already exists globally
            IntPtr existingHwnd = FindWindow(null, windowTitle);

            if (existingHwnd != IntPtr.Zero && IsWindow(existingHwnd))
            {
                ShowWindow(existingHwnd, SW_RESTORE);   // restore if minimized
                SetForegroundWindow(existingHwnd);      // bring to front
                return;
            }

            // No valid window found — create fresh
            var window = new MasterOpening(doc, uidoc);
            window.Title = windowTitle; // Ensure this is set so FindWindow can catch it next time
            window.Show();
        }

        public MasterOpening(Document doc, UIDocument uidoc)
        {
            InitializeComponent();
            _doc = doc;
            _uidoc = uidoc;
        }

        private void ToggleTheme(bool isDark)
        {
            var dict = new ResourceDictionary();
            dict.Source = isDark
                ? new Uri("pack://application:,,,/RevitUI;component/UI/DarkTheme.xaml", UriKind.Absolute)
                : new Uri("pack://application:,,,/RevitUI;component/UI/Styles.xaml", UriKind.Absolute);

            // ⚠️ Pro-Tip for Revit Plugins: Use `this.Resources` instead of `Application.Current.Resources`
            this.Resources.MergedDictionaries.Clear();
            this.Resources.MergedDictionaries.Add(dict);
        }

        private void DarkMode_On(object sender, RoutedEventArgs e) => ToggleTheme(true);
        private void DarkMode_Off(object sender, RoutedEventArgs e) => ToggleTheme(false);
    }
}
