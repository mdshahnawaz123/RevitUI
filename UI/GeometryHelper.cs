using Autodesk.Revit.DB;
using System.Collections.Generic;
using System.Linq;

namespace RevitUI.UI
{
    /// <summary>
    /// Shared geometry utilities used by both the scan and create handlers.
    /// </summary>
    public static class GeometryHelper
    {
        // ── Solid extraction ──────────────────────────────────────────────────

        /// <summary>
        /// Returns all non-empty solids from an element's geometry.
        /// Pass a <paramref name="transform"/> to convert linked-model geometry
        /// into host-document coordinates before returning.
        /// </summary>
        public static List<Solid> GetSolids(Element element, Options opt,
                                             Transform? transform = null)
        {
            var result = new List<Solid>();
            var geom = element.get_Geometry(opt);
            if (geom is null) return result;

            CollectSolids(geom, transform, result);
            return result;
        }

        private static void CollectSolids(GeometryElement geom,
                                           Transform? xform,
                                           List<Solid> result)
        {
            foreach (GeometryObject obj in geom)
            {
                switch (obj)
                {
                    case Solid s when s.Volume > 1e-9:
                        result.Add(xform is null
                            ? s
                            : SolidUtils.CreateTransformed(s, xform));
                        break;

                    case GeometryInstance gi:
                        // Recurse into nested geometry (in-place families, etc.)
                        var nested = gi.GetInstanceGeometry();
                        var nestedXform = xform is null
                            ? gi.Transform
                            : xform.Multiply(gi.Transform);
                        CollectSolids(nested, nestedXform, result);
                        break;
                }
            }
        }

        // ── Intersection ──────────────────────────────────────────────────────

        /// <summary>
        /// Returns the centroid of the boolean intersection of two solid lists,
        /// or <c>null</c> when no meaningful intersection exists.
        /// </summary>
        public static XYZ? IntersectionCentroid(List<Solid> solidsA, List<Solid> solidsB)
        {
            foreach (var a in solidsA)
                foreach (var b in solidsB)
                {
                    try
                    {
                        var inter = BooleanOperationsUtils.ExecuteBooleanOperation(
                            a, b, BooleanOperationsType.Intersect);

                        if (inter?.Volume > 1e-9)
                            return inter.ComputeCentroid();
                    }
                    catch { /* non-manifold or degenerate geometry — skip */ }
                }
            return null;
        }

        // ── Bounding-box overlap ──────────────────────────────────────────────

        /// <summary>
        /// Fast AABB pre-filter. Transforms all 8 corners of the MEP bounding box
        /// when a link transform is present (naively transforming only Min/Max is
        /// wrong when the transform contains rotation).
        /// </summary>
        public static bool BoundingBoxesOverlap(Element mep, Element host,
                                                 Transform? mepXform)
        {
            var mepBb = mep.get_BoundingBox(null);
            var hostBb = host.get_BoundingBox(null);
            if (mepBb is null || hostBb is null) return true; // unknown — allow

            XYZ min, max;

            if (mepXform is null)
            {
                min = mepBb.Min;
                max = mepBb.Max;
            }
            else
            {
                // Transform all 8 corners then recompute axis-aligned extents
                var corners = new[]
                {
                    mepXform.OfPoint(new XYZ(mepBb.Min.X, mepBb.Min.Y, mepBb.Min.Z)),
                    mepXform.OfPoint(new XYZ(mepBb.Max.X, mepBb.Min.Y, mepBb.Min.Z)),
                    mepXform.OfPoint(new XYZ(mepBb.Min.X, mepBb.Max.Y, mepBb.Min.Z)),
                    mepXform.OfPoint(new XYZ(mepBb.Max.X, mepBb.Max.Y, mepBb.Min.Z)),
                    mepXform.OfPoint(new XYZ(mepBb.Min.X, mepBb.Min.Y, mepBb.Max.Z)),
                    mepXform.OfPoint(new XYZ(mepBb.Max.X, mepBb.Min.Y, mepBb.Max.Z)),
                    mepXform.OfPoint(new XYZ(mepBb.Min.X, mepBb.Max.Y, mepBb.Max.Z)),
                    mepXform.OfPoint(new XYZ(mepBb.Max.X, mepBb.Max.Y, mepBb.Max.Z)),
                };
                min = new XYZ(corners.Min(c => c.X), corners.Min(c => c.Y), corners.Min(c => c.Z));
                max = new XYZ(corners.Max(c => c.X), corners.Max(c => c.Y), corners.Max(c => c.Z));
            }

            return min.X <= hostBb.Max.X && max.X >= hostBb.Min.X &&
                   min.Y <= hostBb.Max.Y && max.Y >= hostBb.Min.Y &&
                   min.Z <= hostBb.Max.Z && max.Z >= hostBb.Min.Z;
        }

        // ── MEP sizing ────────────────────────────────────────────────────────

        /// <summary>
        /// Returns approximate half cross-section sizes for the MEP element (feet).
        /// Tries diameter parameters first (pipes), then width/height (ducts/trays).
        /// Falls back to 50 mm when no recognised parameter is found.
        /// </summary>
        public static (double halfW, double halfH) GetMepHalfSize(Element mep)
        {
            double? diameter =
                mep.LookupParameter("Outer Diameter")?.AsDouble() ??
                mep.LookupParameter("Diameter")?.AsDouble();

            if (diameter.HasValue)
                return (diameter.Value / 2.0, diameter.Value / 2.0);

            double w = mep.LookupParameter("Width")?.AsDouble() ?? MmToFeet(50);
            double h = mep.LookupParameter("Height")?.AsDouble() ?? MmToFeet(50);
            return (w / 2.0, h / 2.0);
        }

        // ── Unit conversion ───────────────────────────────────────────────────

        /// <summary>Parses a millimetre string and converts to Revit internal feet.</summary>
        public static double MmToFeet(string text, double fallbackMm = 50)
        {
            if (!double.TryParse(text, out double mm) || mm <= 0) mm = fallbackMm;
            return mm / 304.8;
        }

        /// <summary>Converts a millimetre value directly to feet.</summary>
        public static double MmToFeet(double mm) => mm / 304.8;
    }
}