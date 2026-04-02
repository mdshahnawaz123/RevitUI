using Autodesk.Revit.DB;
using System.ComponentModel;

namespace RevitUI.UI
{
    /// <summary>
    /// Represents one detected clash between a MEP element and a host element.
    /// All geometry is stored in host-document coordinates.
    /// </summary>
    public class ClashItem : INotifyPropertyChanged
    {
        private bool _process = true;
        private string _status = "Pending";

        // ── Identity ──────────────────────────────────────────────────────────
        public int MEPId { get; set; }
        public int HostId { get; set; }
        public string MEPType { get; set; } = "";
        public string HostType { get; set; } = "";

        // ── Source labels (shown in the DataGrid) ─────────────────────────────
        // MEPSource: "Host" or the linked-model title  (MEP can be in a link)
        // HostSource: always "Host"                    (openings only in host doc)
        public string MEPSource { get; set; } = "Host";
        public string HostSource { get; set; } = "Host";

        // ── MEP link tracking (needed only when MEP comes from a linked model) ─
        public bool MEPIsLinked { get; set; }
        public string? MEPLinkName { get; set; }
        public ElementId? MEPLinkInstanceId { get; set; }

        // ── Opening geometry (host-document coordinates) ──────────────────────
        public double HalfWidth { get; set; }   // along-wall or X  (feet)
        public double HalfHeight { get; set; }   // vertical or Y    (feet)
        public double CenterX { get; set; }
        public double CenterY { get; set; }
        public double CenterZ { get; set; }

        // ── Host orientation ──────────────────────────────────────────────────
        // For walls  : the wall's length direction (normalised)
        // For flat hosts (floor/ceiling/beam/column) : XYZ.BasisX as fallback;
        //   structural members use MEP run direction derived at creation time.
        public double WallDirX { get; set; } = 1;
        public double WallDirY { get; set; }
        public double WallDirZ { get; set; }

        // ── Bindable properties ───────────────────────────────────────────────
        public bool Process
        {
            get => _process;
            set { _process = value; OnPropertyChanged(nameof(Process)); }
        }

        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(nameof(Status)); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string n) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}