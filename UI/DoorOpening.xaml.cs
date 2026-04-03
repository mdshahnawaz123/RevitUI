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
    /// Interaction logic for DoorOpening.xaml
    /// </summary>
    public partial class DoorOpening : Window
    {
        private Document doc;
        private UIDocument uidoc;
        public DoorOpening(Document doc,UIDocument uidoc)
        {
            InitializeComponent();
            this.doc = doc;
            this.uidoc = uidoc;
        }
    }
}
