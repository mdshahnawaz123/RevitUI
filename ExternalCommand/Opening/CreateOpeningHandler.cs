using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Structure;
using System.Reflection;
using Autodesk.Revit.UI;
using DataLab;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitUI.UI
{
    /// <summary>
    /// Runs on Revit's API thread (via ExternalEvent).
    /// Creates openings in HOST-document elements only.
    /// MEP elements may come from linked models — that is fine because we only
    /// read their geometry; we never write into a linked document.
    /// </summary>
    public class CreateOpeningHandler : IExternalEventHandler
    {
        public List<ClashItem>? Items { get; set; }
        public bool UseNative { get; set; } = true;
        // When using family sleeves, the selected FamilySymbol to place
        public ElementId? SleeveSymbolId { get; set; }
        public string? SleeveSymbolName { get; set; }
        public Action<string>? OnComplete { get; set; }

        public void Execute(UIApplication app)
        {
            if (Items is null || Items.Count == 0) return;

            var doc = app.ActiveUIDocument.Document;
            int done = 0;
            int failed = 0;

            try
            {
                // All openings go into the HOST document — one transaction per item
                // so a single failure does not roll back everything.
                foreach (var item in Items.Where(i => i.Process))
                {
                    try
                    {
                        // The host element is ALWAYS in the host document
                        var host = doc.GetElement(new ElementId(item.HostId));
                        var center = new XYZ(item.CenterX, item.CenterY, item.CenterZ);
                        var wallDir = new XYZ(item.WallDirX, item.WallDirY, item.WallDirZ)
                                          .Normalize();

                        bool ok = false;

                        // If user selected family sleeve mode and a sleeve symbol is available,
                        // attempt to place a family instance instead of native NewOpening.
                        if (!UseNative && SleeveSymbolId != null)
                        {
                            doc.DoAction(() =>
                            {
                                ok = TryPlaceSleeveFamily(doc, host, center, item);
                                item.Status = ok ? "Done ✓" : "Skipped";
                            }, $"Place Sleeve [{SleeveSymbolName}]");
                        }
                        else
                        {
                            doc.DoAction(() =>
                            {
                                ok = host switch
                                {
                                    Wall w => CreateWallOpening(doc, w, center, wallDir, item),
                                    Floor f => CreateFloorOpening(doc, f, center, item),
                                    Ceiling c => CreateCeilingOpening(doc, c, center, item),
                                    FamilyInstance fi
                                        when IsStructural(fi) => CreateStructuralOpening(doc, fi, center, item),
                                    _ => Unsupported(item, host)
                                };

                                item.Status = ok ? "Done ✓" : "Skipped";
                            }, $"Create Opening [{item.HostType}]");
                        }

                        if (ok) done++; else failed++;
                    }
                    catch (Exception ex)
                    {
                        item.Status = $"Failed: {ex.GetType().Name}: {ex.Message}";
                        failed++;
                    }
                }

                OnComplete?.Invoke(BuildSummary(done, failed));
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Opening Error", ex.Message);
            }
        }

        // ────────────────────────────────────────────────────────────────────
        // Opening creators — one method per host type
        // ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Wall opening: rectangular CurveArray in the wall-face plane.
        /// API: Document.Create.NewOpening(Wall, CurveArray, false)
        /// </summary>
        private static bool CreateWallOpening(Document doc, Wall wall,
                                               XYZ center, XYZ wallDir,
                                               ClashItem item)
        {
            // Rectangle axes: along the wall length + vertical
            XYZ along = wallDir;          // horizontal in wall face
            XYZ up = XYZ.BasisZ;       // vertical

            XYZ p0 = center + (-item.HalfWidth * along) + (-item.HalfHeight * up);
            XYZ p1 = center + (item.HalfWidth * along) + (-item.HalfHeight * up);
            XYZ p2 = center + (item.HalfWidth * along) + (item.HalfHeight * up);
            XYZ p3 = center + (-item.HalfWidth * along) + (item.HalfHeight * up);

            // Try the Wall, XYZ, XYZ overload first (start/end opposite corners)
            var start = p0;
            var end = p2;
            if (TryCreateOpening(doc, wall, null, start, end)) return true;

            // Fallback to CurveArray overload
            if (TryCreateOpening(doc, wall, BuildLoop(p0, p1, p2, p3), null, null)) return true;

            return false;
        }

        /// <summary>
        /// Floor opening: rectangular CurveArray in the XY plane.
        /// API: Document.Create.NewOpening(Floor, CurveArray, true)
        /// </summary>
        private static bool CreateFloorOpening(Document doc, Floor floor,
                                                XYZ center, ClashItem item)
        {
            XYZ p0 = center + new XYZ(-item.HalfWidth, -item.HalfHeight, 0);
            XYZ p1 = center + new XYZ(item.HalfWidth, -item.HalfHeight, 0);
            XYZ p2 = center + new XYZ(item.HalfWidth, item.HalfHeight, 0);
            XYZ p3 = center + new XYZ(-item.HalfWidth, item.HalfHeight, 0);

            return TryCreateOpening(doc, floor, BuildLoop(p0, p1, p2, p3), null, null);
        }

        /// <summary>
        /// Ceiling opening: Revit has no direct NewOpening(Ceiling,…) overload.
        /// We use a shaft opening (XYZ min / XYZ max) which cuts any ceiling,
        /// floor or roof whose elevation falls inside the Z range.
        /// API: Document.Create.NewOpening(Element, XYZ, XYZ)
        /// </summary>
        private static bool CreateCeilingOpening(Document doc, Ceiling ceiling,
                                                  XYZ center, ClashItem item)
        {
            // Give the shaft enough vertical depth to fully penetrate the ceiling slab.
            // 0.5 ft (≈ 150 mm) is sufficient for most ceiling thicknesses.
            const double shaftHalfDepth = 0.5; // feet

            XYZ p0 = center + new XYZ(-item.HalfWidth, -item.HalfHeight, 0);
            XYZ p1 = center + new XYZ(item.HalfWidth, -item.HalfHeight, 0);
            XYZ p2 = center + new XYZ(item.HalfWidth, item.HalfHeight, 0);
            XYZ p3 = center + new XYZ(-item.HalfWidth, item.HalfHeight, 0);

            return TryCreateOpening(doc, ceiling, BuildLoop(p0, p1, p2, p3), null, null);
        }

        /// <summary>
        /// Structural column / beam opening.
        /// API: Document.Create.NewOpening(FamilyInstance, XYZ start, XYZ end)
        /// Cuts a void along the MEP run axis through the structural member.
        /// The two XYZ points are the entry and exit of the MEP.
        /// </summary>
        private static bool CreateStructuralOpening(Document doc,
                                                     FamilyInstance fi,
                                                     XYZ center,
                                                     ClashItem item)
        {
            // Derive the MEP run direction.
            // For horizontal runs (pipes through columns) the stored WallDir
            // is a reasonable proxy.  For vertical drops through beams we use Z.
            var rawDir = new XYZ(item.WallDirX, item.WallDirY, item.WallDirZ);
            var mepDir = rawDir.IsAlmostEqualTo(XYZ.Zero) ? XYZ.BasisX : rawDir.Normalize();

            // Build a rectangular profile perpendicular to the MEP run direction
            // Choose two orthogonal axes in the cross-section plane
            XYZ axis = mepDir;
            XYZ perp1 = axis.CrossProduct(XYZ.BasisZ);
            if (perp1.IsAlmostEqualTo(XYZ.Zero)) perp1 = axis.CrossProduct(XYZ.BasisX);
            perp1 = perp1.Normalize();
            XYZ perp2 = axis.CrossProduct(perp1).Normalize();

            XYZ p0 = center + (-item.HalfWidth * perp1) + (-item.HalfHeight * perp2);
            XYZ p1 = center + (item.HalfWidth * perp1) + (-item.HalfHeight * perp2);
            XYZ p2 = center + (item.HalfWidth * perp1) + (item.HalfHeight * perp2);
            XYZ p3 = center + (-item.HalfWidth * perp1) + (item.HalfHeight * perp2);

            return TryCreateOpening(doc, fi, BuildLoop(p0, p1, p2, p3), null, null);
        }

        // Attempts to call an appropriate NewOpening overload via reflection.
        // Tries (Wall, XYZ, XYZ), then (Element, CurveArray, bool) and finally (Element, CurveArray, enum).
        private static bool TryCreateOpening(Document doc, Element host, CurveArray? profile, XYZ? start, XYZ? end, ClashItem? item = null)
        {
            var creator = doc.Create;
            var methods = creator.GetType().GetMethods().Where(m => m.Name == "NewOpening");

            var attempts = new System.Collections.Generic.List<string>();
            var exceptions = new System.Collections.Generic.List<string>();

            foreach (var m in methods)
            {
                var ps = m.GetParameters();
                if (ps.Length != 3) continue;

                var p0 = ps[0].ParameterType;
                var p1 = ps[1].ParameterType;
                var p2 = ps[2].ParameterType;

                if (!p0.IsAssignableFrom(host.GetType())) continue;

                try
                {
                    object? result = null;

                    var sig = $"{m.Name}({ps[0].ParameterType.Name}, {ps[1].ParameterType.Name}, {ps[2].ParameterType.Name})";
                    attempts.Add(sig);

                    // Wall, XYZ, XYZ
                    if (p1 == typeof(XYZ) && p2 == typeof(XYZ) && start != null && end != null)
                    {
                        result = m.Invoke(creator, new object?[] { host, start, end });
                        if (result != null) return true;
                    }
                    // Element, CurveArray, bool
                    else if (p1 == typeof(CurveArray) && p2 == typeof(bool) && profile != null)
                    {
                        result = m.Invoke(creator, new object?[] { host, profile, true });
                        if (result != null) return true;
                    }
                    // Element, CurveArray, enum
                    else if (p1 == typeof(CurveArray) && p2.IsEnum && profile != null)
                    {
                        var enumVals = Enum.GetValues(p2);
                        for (int ei = 0; ei < enumVals.Length; ei++)
                        {
                            var ev = enumVals.GetValue(ei);
                            try
                            {
                                var tmp = m.Invoke(creator, new object?[] { host, profile, ev! });
                                if (tmp != null) return true;
                            }
                            catch (Exception ex)
                            {
                                exceptions.Add($"{m.Name} enum={ev}: {ex.GetType().Name}: {ex.Message}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add($"{m.Name}: {ex.GetType().Name}: {ex.Message}");
                }
            }

            // All attempts failed — write diagnostics to temp file and set item status if available
            try
            {
                var lines = new System.Collections.Generic.List<string>();
                lines.Add($"Failed to create opening for hostId={(host?.Id?.Value.ToString() ?? "?")}, hostType={host?.GetType().Name}");
                if (item != null) lines.Add($"ClashItem: MEPId={item.MEPId}, HostId={item.HostId}, HostSource={item.MEPSource}");
                lines.Add("Attempted overloads:");
                lines.AddRange(attempts);
                if (exceptions.Count > 0)
                {
                    lines.Add("Exceptions:");
                    lines.AddRange(exceptions);
                }

                var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"opening_attempts_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                System.IO.File.WriteAllLines(path, lines);

                if (item != null)
                    item.Status = "Failed: Could not create opening. See log: " + path;
            }
            catch { }

            return false;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>Builds a closed rectangular CurveArray from four corner points.</summary>
        private static CurveArray BuildLoop(XYZ p0, XYZ p1, XYZ p2, XYZ p3)
        {
            var ca = new CurveArray();
            ca.Append(Line.CreateBound(p0, p1));
            ca.Append(Line.CreateBound(p1, p2));
            ca.Append(Line.CreateBound(p2, p3));
            ca.Append(Line.CreateBound(p3, p0));
            return ca;
        }

        private static bool IsStructural(FamilyInstance fi) =>
            fi.Category?.Id.Value is
                (int)BuiltInCategory.OST_StructuralColumns or
                (int)BuiltInCategory.OST_StructuralFraming;

        private static bool Unsupported(ClashItem item, Element host)
        {
            item.Status =
                $"Skipped: unsupported host type " +
                $"{host?.GetType().Name} (category={host?.Category?.Name})";
            return false;
        }

        private string BuildSummary(int done, int failed)
        {
            var summary = $"{done} opening(s) created." +
                          (failed > 0 ? $" {failed} failed." : "");

            var failures = Items!
                .Where(i => i.Status?.StartsWith("Failed") == true)
                .Select(i => $"HostId={i.HostId} MEPId={i.MEPId} Source={i.MEPSource} → {i.Status}")
                .ToList();

            if (!failures.Any()) return summary;

            summary += "\n\nFailures:\n" + string.Join('\n', failures.Take(10));

            try
            {
                var path = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    $"opening_failures_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                System.IO.File.WriteAllLines(path, failures);
                summary += $"\n\nFull log saved to:\n{path}";
            }
            catch { /* ignore log-write failures */ }

            return summary;
        }

        public string GetName() => "Create Openings";

        // Place a family sleeve (FamilySymbol) at the clash center and orient it along the MEP run.
        private bool TryPlaceSleeveFamily(Document doc, Element host, XYZ center, ClashItem item)
        {
            if (SleeveSymbolId == null) return false;

            var sym = doc.GetElement(SleeveSymbolId) as FamilySymbol;
            if (sym == null) return false;

            if (!sym.IsActive) sym.Activate();

            var dir = new XYZ(item.WallDirX, item.WallDirY, item.WallDirZ);
            if (dir.IsAlmostEqualTo(XYZ.Zero)) dir = XYZ.BasisX;
            dir = dir.Normalize();

            FamilyInstance fi = null;

            // 1) Try face-hosted placement if a nearby face exists
            try
            {
                var opts = new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine };
                var geom = host.get_Geometry(opts);
                if (geom != null)
                {
                    Face bestFace = null;
                    double bestDist = double.MaxValue;
                    foreach (GeometryObject go in geom)
                    {
                        if (go is Solid s)
                        {
                            foreach (Face f in s.Faces)
                            {
                                try
                                {
                                    var proj = f.Project(center);
                                    if (proj != null)
                                    {
                                        var pt = proj.XYZPoint;
                                        var d = pt.DistanceTo(center);
                                        if (d < bestDist)
                                        {
                                            bestDist = d;
                                            bestFace = f;
                                        }
                                    }
                                }
                                catch { }
                            }
                        }
                    }

                    if (bestFace != null && bestDist < 3.0) // tolerance in feet
                    {
                        // Try possible NewFamilyInstance overloads that accept a Reference
                        var creator = doc.Create;
                        var methods = creator.GetType().GetMethods().Where(m => m.Name == "NewFamilyInstance");
                        var rf = bestFace.Reference;
                        var candidates = new List<object?[]>
                        {
                            new object?[] { rf, center, sym },
                            new object?[] { rf, sym, center }
                        };

                        foreach (var m in methods)
                        {
                            foreach (var args in candidates)
                            {
                                try
                                {
                                    var created = m.Invoke(creator, args);
                                    if (created is FamilyInstance createdFi)
                                    {
                                        fi = createdFi;
                                        break;
                                    }
                                }
                                catch { }
                            }
                            if (fi != null) break;
                        }
                    }
                }
            }
            catch { }

            // 2) Fallback: point or level placement
            if (fi == null)
            {
                try
                {
                    fi = doc.Create.NewFamilyInstance(center, sym, StructuralType.NonStructural);
                }
                catch
                {
                    var lvl = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().FirstOrDefault();
                    if (lvl != null)
                    {
                        try { fi = doc.Create.NewFamilyInstance(center, sym, lvl, StructuralType.NonStructural); } catch { }
                    }
                }
            }

            if (fi == null) return false;

            // Align family origin to center (compensate internal origin offsets)
            try
            {
                var bb = fi.get_BoundingBox(null);
                if (bb != null)
                {
                    var bbCenter = (bb.Min + bb.Max) / 2;
                    var delta = center - bbCenter;
                    if (!delta.IsAlmostEqualTo(XYZ.Zero)) ElementTransformUtils.MoveElement(doc, fi.Id, delta);
                }
            }
            catch { }

            // Rotate instance about Z to align with run direction
            try
            {
                var angle = Math.Atan2(dir.Y, dir.X) - Math.Atan2(1, 0);
                var axis = Line.CreateBound(center, center + XYZ.BasisZ);
                ElementTransformUtils.RotateElement(doc, fi.Id, axis, angle);
            }
            catch { }

            // 3) Attempt to cut host with instance void or host utilities via reflection
            try
            {
                var revitAsm = typeof(Element).Assembly;

                var ivType = revitAsm.GetType("Autodesk.Revit.DB.InstanceVoidCutUtils");
                if (ivType != null)
                {
                    var methods = ivType.GetMethods(BindingFlags.Public | BindingFlags.Static);
                    foreach (var m in methods.Where(x => x.Name.IndexOf("AddInstanceVoidCut", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        try
                        {
                            var parms = m.GetParameters();
                            if (parms.Length == 3 && parms[0].ParameterType == typeof(Document)) m.Invoke(null, new object?[] { doc, fi, host });
                            else if (parms.Length == 2) m.Invoke(null, new object?[] { fi, host });
                        }
                        catch { }
                    }
                }

                var hoType = revitAsm.GetType("Autodesk.Revit.DB.HostObjectUtils");
                if (hoType != null)
                {
                    var hoMethods = hoType.GetMethods(BindingFlags.Public | BindingFlags.Static);
                    foreach (var m in hoMethods.Where(x => x.Name.IndexOf("Cut", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        try
                        {
                            var parms = m.GetParameters();
                            if (parms.Length == 3 && parms[0].ParameterType == typeof(Document)) m.Invoke(null, new object?[] { doc, host, fi });
                            else if (parms.Length == 2) m.Invoke(null, new object?[] { host, fi });
                        }
                        catch { }
                    }
                }
            }
            catch { }

            // Diagnostic log
            try
            {
                var lines = new List<string>();
                lines.Add($"Sleeve placement for Clash MEPId={item.MEPId} HostId={item.HostId}");
                if (fi != null)
                {
                    lines.Add($"Placed FamilyInstance Id={fi.Id} Symbol={sym.Family?.Name}/{sym.Name}");
                    var bb = fi.get_BoundingBox(null);
                    if (bb != null) lines.Add($"Instance bbox: Min={bb.Min}, Max={bb.Max}");
                }
                else
                {
                    lines.Add("Placement failed: no FamilyInstance created.");
                }

                var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"sleeve_place_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                System.IO.File.WriteAllLines(path, lines);
                item.Status = fi != null ? "Done ✓" : ("Failed: sleeve not placed. See log: " + path);
            }
            catch { }

            return fi != null;
        }
    }
}