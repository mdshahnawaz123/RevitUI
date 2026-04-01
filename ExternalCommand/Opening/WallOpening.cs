using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using DataLab;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace RevitUI.ExternalCommand.Opening
{
    public class WallOpening : IExternalEventHandler
    {
        private IList<Pipe>? pipe { get; set; }
        private IList<Duct>? duct { get; set; }
        private IList<CableTray>? cableTray { get; set; }
        private IList<Wall>? wall { get; set; }
        private IList<FamilyInstance>? fi { get; set; }
        private CheckBox? CheckBoxPipes { get; set; }
        private CheckBox? CheckBoxDucts { get; set; }
        private CheckBox? CheckBoxCableTrays { get; set; }
        private CheckBox? CheckBoxWalls { get; set; }
        private CheckBox? CheckBoxFamilyInstances { get; set; }
        private CheckBox ? CheckBoxFloor { get; set; }
        private CheckBox ? CheckBoxBeam { get; set; }


        public void Execute(UIApplication app)
        {

            var doc = app.ActiveUIDocument.Document;
            try
            {
                var opt = new Options()
                { ComputeReferences = true, 
                  DetailLevel = ViewDetailLevel.Fine
                };

                doc.DoAction(() =>
                {
                    pipe = doc.GetPipes();
                    duct = doc.GetDucts();
                    cableTray = doc.GetCableTrays();
                    wall = doc.GetWalls();
                    fi = doc.GetFamilyInstances();
                    // Here you would add your logic to create the wall opening based on the collected elements.
                    // This is a placeholder for demonstration purposes.

                    if (CheckBoxPipes.IsChecked == true && CheckBoxWalls.IsChecked == true)
                    {

                    }

                }, "Create Wall Opening");
            }
            catch(Exception ex)
            {
                TaskDialog.Show("Error", $"An error occurred: {ex.Message}");
            }
           

            TaskDialog.Show("Wall Opening", "A wall opening has been created successfully.");
        }

        public string GetName()
        {
            return "Wall Opening Created";
        }
    }
}
