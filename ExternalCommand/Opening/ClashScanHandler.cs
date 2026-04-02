using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using DataLab;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace RevitUI.UI
{
    /// <summary>
    /// Runs on Revit's API thread (via ExternalEvent).
    /// Scans MEP elements (host + linked) against host-document structural
    /// elements and populates <see cref="Results"/>.
    /// </summary>
    public class ClashScanHandler : IExternalEventHandler
    {
        // ── Settings injected by the window before raising the event ──────────
        public bool ScanPipes { get; set; }
        public bool ScanDucts { get; set; }
        public bool ScanCableTrays { get; set; }
        public bool ScanWalls { get; set; }
        public bool ScanFloors { get; set; }
        public bool ScanCeilings { get; set; }
        public bool ScanBeamsColumns { get; set; }
        public double ClearanceFeet { get; set; }
        // Restrict scanning to elements visible in the specified active view
        public bool OnlyActiveView { get; set; }
        public ElementId? ActiveViewId { get; set; }

        // ── Output ────────────────────────────────────────────────────────────
        public ObservableCollection<ClashItem> Results { get; } = new();

        /// <summary>Called on the UI thread after the scan finishes.</summary>
        public Action<int>? OnComplete { get; set; }

        // ─────────────────────────────────────────────────────────────────────

        public void Execute(UIApplication app)
        {
            var doc = app.ActiveUIDocument.Document;
            Results.Clear();

            var opt = new Options
            {
                ComputeReferences = true,
                DetailLevel = ViewDetailLevel.Fine
            };

            try
            {
                // 1. Collect MEP elements from host doc + all loaded links
                var mepElements = CollectAllMepElements(doc);

                // 2. Collect host-document structural elements by type
                //    NOTE: openings are ALWAYS cut into the HOST document,
                //    so we only ever collect host elements here.
                var hostWalls = ScanWalls ? CollectWalls(doc) : Empty();
                var hostFloors = ScanFloors ? CollectFloors(doc) : Empty();
                var hostCeilings = ScanCeilings ? CollectCeilings(doc) : Empty();
                var hostStructural = ScanBeamsColumns ? CollectStructural(doc) : Empty();

                // 3. For every MEP element, test against every host group
                foreach (var mepInfo in mepElements)
                {
                    // Geometry already transformed into host coordinates
                    var mepSolids = GeometryHelper.GetSolids(
                        mepInfo.Element, opt,
                        mepInfo.IsLinked ? mepInfo.LinkTransform : null);

                    if (mepSolids.Count == 0) continue;

                    (double halfW, double halfH) = GeometryHelper.GetMepHalfSize(mepInfo.Element);
                    double w = halfW + ClearanceFeet;
                    double h = halfH + ClearanceFeet;

                    var mepXform = mepInfo.IsLinked ? mepInfo.LinkTransform : null;

                    ScanGroup(mepInfo, mepSolids, w, h, mepXform, hostWalls, "Wall", opt);
                    ScanGroup(mepInfo, mepSolids, w, h, mepXform, hostFloors, "Floor", opt);
                    ScanGroup(mepInfo, mepSolids, w, h, mepXform, hostCeilings, "Ceiling", opt);
                    ScanGroup(mepInfo, mepSolids, w, h, mepXform, hostStructural, "Structural", opt);
                }

                OnComplete?.Invoke(Results.Count);
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Scan Error", ex.Message);
            }
        }

        // ── MEP collection ────────────────────────────────────────────────────

        private List<LinkedElementInfo> CollectAllMepElements(Document doc)
        {
            var list = new List<LinkedElementInfo>();

            // ── Host document ─────────────────────────────────────────────────
            if (ScanPipes)
                foreach (var e in doc.GetPipes())
                    list.Add(new LinkedElementInfo(e));

            if (ScanDucts)
                foreach (var e in doc.GetDucts())
                    list.Add(new LinkedElementInfo(e));

            if (ScanCableTrays)
                foreach (var e in doc.GetCableTrays())
                    list.Add(new LinkedElementInfo(e));

            // ── Linked documents ──────────────────────────────────────────────
            var links = new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .ToList();

            foreach (var link in links)
            {
                var linkDoc = link.GetLinkDocument();
                if (linkDoc is null) continue;      // link not loaded

                var xform = link.GetTransform();
                var linkName = linkDoc.Title;
                var linkInstId = link.Id;

                if (ScanPipes)
                    foreach (var e in linkDoc.GetPipes())
                        list.Add(new LinkedElementInfo(e, xform, linkName));

                if (ScanDucts)
                    foreach (var e in linkDoc.GetDucts())
                        list.Add(new LinkedElementInfo(e, xform, linkName));

                if (ScanCableTrays)
                    foreach (var e in linkDoc.GetCableTrays())
                        list.Add(new LinkedElementInfo(e, xform, linkName));
            }

            return list;
        }

        // ── Host element collectors ───────────────────────────────────────────

        private static List<Element> CollectWalls(Document doc) =>
            new FilteredElementCollector(doc)
                .OfClass(typeof(Wall))
                .WhereElementIsNotElementType()
                .Cast<Element>().ToList();

        private static List<Element> CollectFloors(Document doc) =>
            new FilteredElementCollector(doc)
                .OfClass(typeof(Floor))
                .WhereElementIsNotElementType()
                .Cast<Element>().ToList();

        private static List<Element> CollectCeilings(Document doc)
        {
            // Revit 2023+: dedicated Ceiling class
            var list = new FilteredElementCollector(doc)
                .OfClass(typeof(Ceiling))
                .WhereElementIsNotElementType()
                .Cast<Element>().ToList();

            // Older Revit: ceilings stored as a category only
            if (list.Count == 0)
                list = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Ceilings)
                    .WhereElementIsNotElementType()
                    .Cast<Element>().ToList();

            return list;
        }

        private static List<Element> CollectStructural(Document doc) =>
            new FilteredElementCollector(doc)
                .OfClass(typeof(FamilyInstance))
                .WherePasses(new LogicalOrFilter(
                    new ElementCategoryFilter(BuiltInCategory.OST_StructuralColumns),
                    new ElementCategoryFilter(BuiltInCategory.OST_StructuralFraming)))
                .WhereElementIsNotElementType()
                .Cast<Element>().ToList();

        private static List<Element> Empty() => new();

        // ── Intersection test for one MEP vs one host group ───────────────────

        private void ScanGroup(
            LinkedElementInfo mepInfo,
            List<Solid> mepSolids,
            double halfW, double halfH,
            Transform? mepXform,
            List<Element> hosts,
            string hostLabel,
            Options opt)
        {
            foreach (var host in hosts)
            {
                // If OnlyActiveView is set, skip hosts not visible in the active view
                if (OnlyActiveView && ActiveViewId != null)
                {
                    try
                    {
                        var coll = new FilteredElementCollector(host.Document, ActiveViewId)
                            .WhereElementIsNotElementType();
                        if (!coll.Any(e => e.Id == host.Id))
                            continue;
                    }
                    catch
                    {
                        // ignore view lookup errors and continue
                    }
                }
                // Cheap bounding-box pre-filter
                if (!GeometryHelper.BoundingBoxesOverlap(mepInfo.Element, host, mepXform))
                    continue;

                var hostSolids = GeometryHelper.GetSolids(host, opt);
                if (hostSolids.Count == 0) continue;

                var center = GeometryHelper.IntersectionCentroid(mepSolids, hostSolids);
                if (center is null) continue;

                var hostDir = GetHostDirection(host);

                Results.Add(new ClashItem
                {
                    // MEP
                    MEPId = (int)mepInfo.Element.Id.Value,
                    MEPType = GetMepLabel(mepInfo.Element),
                    MEPSource = mepInfo.DisplayLabel,
                    MEPIsLinked = mepInfo.IsLinked,
                    MEPLinkName = mepInfo.IsLinked ? mepInfo.LinkName : null,

                    // Host (always in host document)
                    HostId = (int)host.Id.Value,
                    HostType = hostLabel,
                    HostSource = "Host",

                    // Geometry (host-doc coordinates)
                    HalfWidth = halfW,
                    HalfHeight = halfH,
                    CenterX = center.X,
                    CenterY = center.Y,
                    CenterZ = center.Z,
                    WallDirX = hostDir.X,
                    WallDirY = hostDir.Y,
                    WallDirZ = hostDir.Z,

                    Status = "Found"
                });
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// For walls returns the wall's length direction.
        /// For all other hosts returns XYZ.BasisX (the create handler derives
        /// the actual cut direction from the MEP geometry at creation time).
        /// </summary>
        private static XYZ GetHostDirection(Element host)
        {
            if (host is Wall wall &&
                wall.Location is LocationCurve lc &&
                lc.Curve is Line line)
                return line.Direction.Normalize();

            return XYZ.BasisX;
        }

        private static string GetMepLabel(Element e) => e switch
        {
            Pipe => "Pipe",
            Duct => "Duct",
            CableTray => "Cable Tray",
            _ => e.Category?.Name ?? "MEP"
        };

        public string GetName() => "Scan For Clashes";
    }
}