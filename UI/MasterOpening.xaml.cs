using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace RevitUI.UI
{
    /// <summary>
    /// Interaction logic for MasterOpening.xaml
    /// </summary>
    public partial class MasterOpening : Window
    {
        private Document doc;
        private UIDocument uidoc;
        public MasterOpening(Document doc,UIDocument uidoc)
        {
            InitializeComponent();
            this.doc = doc;
            this.uidoc = uidoc;
        }
        private void ToggleTheme(bool isDark)
        {
            var dict = new ResourceDictionary();

            if (isDark)
                dict.Source = new Uri("DarkTheme.xaml", UriKind.Relative);
            else
                dict.Source = new Uri("Styles.xaml", UriKind.Relative);

            Application.Current.Resources.MergedDictionaries.Clear();
            Application.Current.Resources.MergedDictionaries.Add(dict);
        }

        private void DarkMode_On(object sender, RoutedEventArgs e)
        {
            ToggleTheme(true);
        }

        private void DarkMode_Off(object sender, RoutedEventArgs e)
        {
            ToggleTheme(false);
        }
    }
}
