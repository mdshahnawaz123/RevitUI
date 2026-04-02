using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DataLab;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;

namespace RevitUI.UI
{
    public partial class MasterOpening : Window
    {
        // ── Win32 singleton helpers ───────────────────────────────────────────
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        private const int SW_RESTORE = 9;

        // ── External events ───────────────────────────────────────────────────
        private readonly ClashScanHandler _scanHandler = new();
        private readonly CreateOpeningHandler _openHandler = new();
        private readonly ExternalEvent _scanEvent;
        private readonly ExternalEvent _openEvent;

        // ── State ─────────────────────────────────────────────────────────────
        private readonly ObservableCollection<ClashItem> _clashes = new();
        private readonly Document _doc;
        private readonly UIDocument _uidoc;

        // ── Singleton factory ─────────────────────────────────────────────────
        public static void GetOrCreate(Document doc, UIDocument uidoc)
        {
            const string title = "BIM Digital Design";
            IntPtr hwnd = FindWindow(null, title);
            if (hwnd != IntPtr.Zero && IsWindow(hwnd))
            {
                ShowWindow(hwnd, SW_RESTORE);
                SetForegroundWindow(hwnd);
                return;
            }
            new MasterOpening(doc, uidoc).Show();
        }

        // ── Constructor ───────────────────────────────────────────────────────
        public MasterOpening(Document doc, UIDocument uidoc)
        {
            InitializeComponent();
            _doc = doc;
            _uidoc = uidoc;

            // Create external events
            _scanEvent = ExternalEvent.Create(_scanHandler);
            _openEvent = ExternalEvent.Create(_openHandler);

            // Bind DataGrid
            ClashGrid.ItemsSource = _clashes;

            // ── Scan callback (fires on UI thread via Dispatcher) ─────────────
            _scanHandler.OnComplete = count => Dispatcher.Invoke(() =>
            {
                _clashes.Clear();
                foreach (var item in _scanHandler.Results)
                    _clashes.Add(item);

                TxtClashCount.Text = $"Clashes found: {count}";
                TxtDoneCount.Text = "Openings done: —";
                SetProgress(100, count > 0
                    ? $"Scan complete — {count} clash(es) detected."
                    : "Scan complete — no clashes found.");
            });

            // ── Create-openings callback ──────────────────────────────────────
            _openHandler.OnComplete = msg => Dispatcher.Invoke(() =>
            {
                int done = _clashes.Count(c => c.Status.StartsWith("Done"));
                TxtDoneCount.Text = $"Openings done: {done}";
                SetProgress(100, msg.Split('\n')[0]);   // first line only in progress bar
                ClashGrid.Items.Refresh();

                // Show full report (includes failure list if any) in a dialog
                if (msg.Contains("failed") || msg.Contains("Failed"))
                    TaskDialog.Show("Opening Report", msg);
            });

            LoadModelSummary();

            // Populate sleeve family combo with FamilySymbol entries from the host document
            try
            {
                var symbols = new FilteredElementCollector(_doc)
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>()
                    .OrderBy(s => s.Name)
                    .ToList();

                SleeveCombo.ItemsSource = symbols;
                if (symbols.Count > 0) SleeveCombo.SelectedIndex = 0;
            }
            catch { /* non-critical */ }
        }

        private void RadioNative_Checked(object sender, RoutedEventArgs e)
        {
            if (SleeveCombo != null) SleeveCombo.Visibility = System.Windows.Visibility.Collapsed;
            if (TxtSleeveComing != null) TxtSleeveComing.Visibility = System.Windows.Visibility.Collapsed;
        }

        private void RadioSleeve_Checked(object sender, RoutedEventArgs e)
        {
            if (SleeveCombo != null) SleeveCombo.Visibility = System.Windows.Visibility.Visible;
            if (TxtSleeveComing != null) TxtSleeveComing.Visibility = System.Windows.Visibility.Visible;
        }

        // ── Model info (read-only, no transaction needed) ─────────────────────
        private void LoadModelSummary()
        {
            try
            {
                int pipes = _doc.GetPipes().Count;
                int ducts = _doc.GetDucts().Count;
                int trays = _doc.GetCableTrays().Count;

                int links = new FilteredElementCollector(_doc)
                    .OfClass(typeof(RevitLinkInstance))
                    .GetElementCount();

                TxtProgress.Text =
                    $"Model: {pipes} pipe(s), {ducts} duct(s), {trays} cable tray(s)" +
                    (links > 0 ? $", {links} linked model(s) loaded." : ".");
            }
            catch { /* non-critical */ }
        }

        // ── Scan button ───────────────────────────────────────────────────────
        private void ScanClash(object sender, RoutedEventArgs e)
        {
            if (!AnyMepSelected())
            {
                Warn("Select at least one MEP type (Pipes, Ducts or Cable Trays).");
                return;
            }
            if (!AnyHostSelected())
            {
                Warn("Select at least one host type (Wall, Floor, Ceiling or Beam/Column).");
                return;
            }

            // Inject settings into handler
            _scanHandler.ScanPipes = CheckBoxPipes.IsChecked == true;
            _scanHandler.ScanDucts = CheckBoxDucts.IsChecked == true;
            _scanHandler.ScanCableTrays = CheckBoxCable.IsChecked == true;
            _scanHandler.ScanWalls = CheckBoxWall.IsChecked == true;
            _scanHandler.ScanFloors = CheckBoxFloor.IsChecked == true;
            _scanHandler.ScanCeilings = CheckBoxCeil.IsChecked == true;
            _scanHandler.ScanBeamsColumns = CheckBoxBeam.IsChecked == true;
            _scanHandler.ClearanceFeet = GeometryHelper.MmToFeet(TBClear.Text, 50);
            // Active view filtering
            // Active view filtering (use active view id when checkbox exists)
            try
            {
                var onlyActive = false;
                var viewId = (ElementId?)null;
                // CheckOnlyActiveView may not exist in older UI; guard access
                var field = this.GetType().GetField("CheckOnlyActiveView", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                if (field != null)
                {
                    var cb = field.GetValue(this) as System.Windows.Controls.CheckBox;
                    if (cb != null) onlyActive = cb.IsChecked == true;
                }

                viewId = _uidoc?.ActiveView?.Id;

                _scanHandler.OnlyActiveView = onlyActive;
                _scanHandler.ActiveViewId = viewId;
            }
            catch { }

            _clashes.Clear();
            TxtClashCount.Text = "Scanning…";
            TxtDoneCount.Text = "Openings done: —";
            SetProgress(0, "Scanning for clashes…");

            _scanEvent.Raise();
        }

        // ── Create openings button ────────────────────────────────────────────
        private void CreateOpening(object sender, RoutedEventArgs e)
        {
            var ready = _clashes
                .Where(c => c.Process && c.Status == "Found")
                .ToList();

            if (ready.Count == 0)
            {
                Warn("No clashes are ready to process.\nRun a scan first.");
                return;
            }

            _openHandler.Items = ready;
            _openHandler.UseNative = RadioNative.IsChecked == true;

            // Pass sleeve selection when family sleeve mode is active
            if (RadioSleeve.IsChecked == true && SleeveCombo.SelectedItem is FamilySymbol sym)
            {
                _openHandler.SleeveSymbolId = sym.Id;
                _openHandler.SleeveSymbolName = sym.Name;
            }
            else
            {
                _openHandler.SleeveSymbolId = null;
                _openHandler.SleeveSymbolName = null;
            }

            SetProgress(0, $"Creating {ready.Count} opening(s)…");
            _openEvent.Raise();
        }

        // ── Theme ─────────────────────────────────────────────────────────────
        private void ToggleTheme(bool isDark)
        {
            var dict = new System.Windows.ResourceDictionary
            {
                Source = isDark
                    ? new Uri("pack://application:,,,/RevitUI;component/UI/DarkTheme.xaml", UriKind.Absolute)
                    : new Uri("pack://application:,,,/RevitUI;component/UI/Styles.xaml", UriKind.Absolute)
            };
            this.Resources.MergedDictionaries.Clear();
            this.Resources.MergedDictionaries.Add(dict);
        }

        private void DarkMode_On(object sender, RoutedEventArgs e) => ToggleTheme(true);
        private void DarkMode_Off(object sender, RoutedEventArgs e) => ToggleTheme(false);

        // ── Utilities ─────────────────────────────────────────────────────────
        private void SetProgress(int value, string message)
        {
            ProgressBar.Value = value;
            TxtProgress.Text = message;
        }

        private bool AnyMepSelected() =>
            CheckBoxPipes.IsChecked == true ||
            CheckBoxDucts.IsChecked == true ||
            CheckBoxCable.IsChecked == true;

        private bool AnyHostSelected() =>
            CheckBoxWall.IsChecked == true ||
            CheckBoxFloor.IsChecked == true ||
            CheckBoxCeil.IsChecked == true ||
            CheckBoxBeam.IsChecked == true;

        private static void Warn(string msg) =>
            MessageBox.Show(msg, "Selection Required",
                MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}