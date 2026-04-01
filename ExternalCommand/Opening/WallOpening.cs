using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RevitUI.ExternalCommand.Opening
{
    public class WallOpening : IExternalEventHandler
    {
        public void Execute(UIApplication app)
        {
            TaskDialog.Show("Wall Opening", "A wall opening has been created successfully.");
        }

        public string GetName()
        {
            return "Wall Opening Created";
        }
    }
}
