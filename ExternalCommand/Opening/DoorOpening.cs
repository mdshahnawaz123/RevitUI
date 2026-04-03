using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RevitUI.ExternalCommand.Opening
{
    public class DoorOpening : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var uidoc = commandData.Application.ActiveUIDocument;
                var doc = uidoc.Document;

                RevitUI.UI.MasterOpening.GetOrCreate(doc, uidoc);
            }
            catch (Exception ex)
            {
                message = ex.Message;
            }

            return Result.Succeeded;
        }
    }
}
