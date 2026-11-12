using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using FoundationProjectionToggle.Core.Remmingwerk;
using FoundationProjectionToggle.Infrastructure;

namespace FoundationProjectionToggle.Services
{
    /// <summary>What the button needs to know before showing its window.</summary>
    public sealed class RemmingwerkContext
    {
        public List<ElementId> LineIds { get; } = new List<ElementId>();
        public List<RemmingwerkLine> Lines { get; } = new List<RemmingwerkLine>();
        public List<string> LineErrors { get; } = new List<string>();
        public List<LevelOption> Levels { get; } = new List<LevelOption>();
        public int DefaultLevel { get; set; } = -1;
        public List<TypeOption> Wrijfgording { get; set; }
        public List<TypeOption> Wrijfstijl { get; set; }
        public List<TypeOption> StalenGording { get; set; }
        public List<TypeOption> StalenStijl { get; set; }
        public List<TypeOption> Buispaal { get; set; }

        /// <summary>Steel types that are not in the project, with where to get them.</summary>
        public List<string> Missing { get; } = new List<string>();
    }

    /// <summary>Everything needed to place a remmingwerk without UI (the window fills it; tests too).</summary>
    public sealed class RemmingwerkRequest
    {
        public IList<ElementId> LineIds { get; set; }
        public ElementId LevelId { get; set; }
        public RemmingwerkSettings Settings { get; set; } = new RemmingwerkSettings();
        public TypeOption Wrijfgording { get; set; }
        public TypeOption Wrijfstijl { get; set; }
        public TypeOption StalenGording { get; set; }
        public TypeOption StalenStijl { get; set; }
        public TypeOption Buispaal { get; set; }

        public SteelDimensions Steel()
        {
            return SteelDimensions.FromTypes(StalenGording, StalenStijl, Buispaal);
        }
    }

    public sealed class RemmingwerkResult
    {
        public bool Succeeded { get; internal set; }
        public string Error { get; internal set; }
        public string Summary { get; internal set; }
        public string Report { get; internal set; }
        public RemmingwerkPlan Plan { get; internal set; }
        public List<ElementId> Created { get; } = new List<ElementId>();
        public Dictionary<RemmingwerkPart, List<ElementId>> ByPart { get; } = new Dictionary<RemmingwerkPart, List<ElementId>>();
        public double MaxDeviationMm { get; internal set; }

        internal static RemmingwerkResult Fail(string error, string report = null, RemmingwerkPlan plan = null)
        {
            return new RemmingwerkResult { Succeeded = false, Error = error, Report = report, Plan = plan };
        }
    }

    /// <summary>
    /// Revit side of "Remmingwerk": reads the chosen model lines, lets the Revit-free
    /// <see cref="RemmingwerkPlanner"/> compute every element, places them in one transaction
    /// (one Ctrl+Z) and reads them back (≤ 1 mm, else nothing is kept).
    /// </summary>
    public static class RemmingwerkService
    {
        public const string TransactionName = "IB Tools: Remmingwerk";
        public const double MmPerFoot = 304.8;
        public const double ToleranceMm = 1.0;
        private const double RotationToleranceDeg = 0.1;

        public const string ParamWrijfHoogte = "IB_wrijfstijl_hoogte";
        public const string ParamWrijfBreedte = "IB_wrijfstijl_breedte";
        public const string ParamWrijfLengte = "IB_wrijfstijl_lengte";

        /// <summary>Is this element a model line or arc the button can use?</summary>
        public static bool IsUsableLine(Element e)
        {
            var mc = e as ModelCurve;
            if (mc == null)
            {
                return false;
            }
            var c = mc.GeometryCurve;
            return c != null && c.IsBound && (c is Line || c is Arc);
        }

        public static RemmingwerkContext Analyse(Document doc, IEnumerable<ElementId> lineIds, string rememberedLevel)
        {
            var ctx = new RemmingwerkContext();
            foreach (var id in lineIds)
            {
                ctx.LineIds.Add(id);
            }
            ctx.Lines.AddRange(ReadLines(doc, ctx.LineIds, ctx.LineErrors));

            foreach (var level in new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.ProjectElevation))
            {
                ctx.Levels.Add(new LevelOption { Name = level.Name, ElevationMm = level.ProjectElevation * MmPerFoot, Tag = level.Id });
            }
            ctx.DefaultLevel = ChooseLevel(ctx.Levels, rememberedLevel);

            ctx.Wrijfgording = RemmingwerkFamilyService.WrijfgordingOptions(doc);
            ctx.Wrijfstijl = RemmingwerkFamilyService.WrijfstijlOptions(doc);
            ctx.StalenGording = RemmingwerkFamilyService.StalenGordingOptions(doc);
            ctx.StalenStijl = RemmingwerkFamilyService.StalenStijlOptions(doc);
            ctx.Buispaal = RemmingwerkFamilyService.BuispaalOptions(doc);

            if (ctx.StalenGording.Count == 0)
                ctx.Missing.Add("Stalen gording: geen Structural Framing-type met NLRS_C_hoogte en NLRS_C_breedte. Laad bv. " + RemmingwerkFamilyService.StalenGordingSource);
            if (ctx.StalenStijl.Count == 0)
                ctx.Missing.Add("Stalen stijl: geen Structural Column-type met NLRS_C_hoogte. Laad bv. " + RemmingwerkFamilyService.StalenStijlSource);
            if (ctx.Buispaal.Count == 0)
                ctx.Missing.Add("Buispaal: geen Structural Column-type met NLRS_C_diameter. Laad bv. " + RemmingwerkFamilyService.BuispaalSource);
            if (ctx.Levels.Count == 0)
                ctx.Missing.Add("Er is geen level in dit project.");
            return ctx;
        }

        /// <summary>Default NAP level: the remembered one, else "N.A.P."/"NAP", else the one closest to 0.</summary>
        public static int ChooseLevel(IList<LevelOption> levels, string remembered)
        {
            if (levels.Count == 0) return -1;
            for (int i = 0; i < levels.Count; i++)
                if (remembered != null && levels[i].Name == remembered) return i;
            for (int i = 0; i < levels.Count; i++)
                if (levels[i].Name.Replace(".", string.Empty).Trim().Equals("NAP", StringComparison.OrdinalIgnoreCase)) return i;
            for (int i = 0; i < levels.Count; i++)
                if (levels[i].Name.Replace(".", string.Empty).IndexOf("NAP", StringComparison.OrdinalIgnoreCase) >= 0) return i;
            int best = 0;
            for (int i = 1; i < levels.Count; i++)
                if (Math.Abs(levels[i].ElevationMm) < Math.Abs(levels[best].ElevationMm)) best = i;
            return best;
        }

        /// <summary>Model lines/arcs as plan curves in mm (internal coordinates). Unusable ones go to <paramref name="errors"/>.</summary>
        public static List<RemmingwerkLine> ReadLines(Document doc, IList<ElementId> ids, IList<string> errors)
        {
            var result = new List<RemmingwerkLine>();
            foreach (var id in ids)
            {
                var e = doc.GetElement(id);
                var label = id.ToString();
                var mc = e as ModelCurve;
                if (mc == null)
                {
                    errors.Add($"Element {label} is geen modellijn.");
                    continue;
                }
                var c = mc.GeometryCurve;
                if (c == null || !c.IsBound)
                {
                    errors.Add($"Modellijn {label}: een volle cirkel of open curve wordt niet ondersteund.");
                    continue;
                }
                var p0 = c.GetEndPoint(0);
                var p1 = c.GetEndPoint(1);
                if (Math.Abs(p0.Z - p1.Z) * MmPerFoot > 1.0)
                {
                    errors.Add($"Modellijn {label} loopt niet horizontaal (hoogteverschil {Math.Abs(p0.Z - p1.Z) * MmPerFoot:0} mm).");
                    continue;
                }
                PlanCurve curve;
                if (c is Line)
                {
                    curve = new PlanLine(Mm(p0), Mm(p1));
                }
                else if (c is Arc arc)
                {
                    if (Math.Abs(arc.Normal.Z) < 0.9999)
                    {
                        errors.Add($"Modellijn {label} is een boog die niet in een horizontaal vlak ligt.");
                        continue;
                    }
                    curve = PlanArc.FromEnds(Mm(arc.Center), Mm(p0), Mm(p1), arc.Normal.Z > 0);
                }
                else
                {
                    errors.Add($"Modellijn {label} is geen rechte lijn of cirkelboog ({c.GetType().Name}).");
                    continue;
                }
                result.Add(new RemmingwerkLine(curve, p0.Z * MmPerFoot, label));
            }
            return result;
        }

        /// <summary>Places the remmingwerk in one transaction. No UI; public for the test harness.</summary>
        public static RemmingwerkResult Place(Document doc, RemmingwerkRequest req)
        {
            var lineErrors = new List<string>();
            var lines = ReadLines(doc, req.LineIds ?? new List<ElementId>(), lineErrors);
            if (lineErrors.Count > 0)
            {
                return RemmingwerkResult.Fail(string.Join(" ", lineErrors));
            }
            var level = req.LevelId != null ? doc.GetElement(req.LevelId) as Level : null;
            if (level == null)
            {
                return RemmingwerkResult.Fail("Geen NAP-level gekozen.");
            }
            foreach (var o in new[] { req.Wrijfgording, req.Wrijfstijl, req.StalenGording, req.StalenStijl, req.Buispaal })
            {
                if (o == null)
                {
                    return RemmingwerkResult.Fail("Niet alle typen zijn gekozen.");
                }
            }

            double levelZ = level.ProjectElevation * MmPerFoot;
            var plan = RemmingwerkPlanner.Plan(lines, req.Settings, req.Steel(), levelZ);
            if (!plan.IsValid)
            {
                return RemmingwerkResult.Fail(string.Join(" ", plan.Errors), null, plan);
            }

            var notes = new List<string>();
            var failures = new FailureCollector();
            using (var t = new Transaction(doc, TransactionName))
            {
                var fho = t.GetFailureHandlingOptions();
                fho.SetFailuresPreprocessor(failures);
                fho.SetClearAfterRollback(true);
                t.SetFailureHandlingOptions(fho);
                t.Start();
                try
                {
                    var symWrijfgording = RemmingwerkFamilyService.Resolve(doc, req.Wrijfgording, notes);
                    var symWrijfstijl = RemmingwerkFamilyService.Resolve(doc, req.Wrijfstijl, notes);
                    var symGording = RemmingwerkFamilyService.Resolve(doc, req.StalenGording, notes);
                    var symStijl = RemmingwerkFamilyService.Resolve(doc, req.StalenStijl, notes);
                    var symPaal = RemmingwerkFamilyService.Resolve(doc, req.Buispaal, notes);
                    var missing = new List<string>();
                    if (symWrijfgording == null) missing.Add(req.Wrijfgording.Label);
                    if (symWrijfstijl == null) missing.Add(req.Wrijfstijl.Label);
                    if (symGording == null) missing.Add(req.StalenGording.Label);
                    if (symStijl == null) missing.Add(req.StalenStijl.Label);
                    if (symPaal == null) missing.Add(req.Buispaal.Label);
                    if (missing.Count > 0)
                    {
                        t.RollBack();
                        return RemmingwerkResult.Fail("Deze typen konden niet worden gevonden of geladen: " + string.Join("; ", missing) + ".", string.Join(Environment.NewLine, notes), plan);
                    }

                    var placedBeams = new List<Tuple<PlannedBeam, FamilyInstance>>();
                    var placedColumns = new List<Tuple<PlannedColumn, FamilyInstance>>();
                    var placedPosts = new List<Tuple<PlannedPost, FamilyInstance>>();
                    var paramNotes = new HashSet<string>();

                    foreach (var b in plan.Beams)
                    {
                        var symbol = b.Part == RemmingwerkPart.Wrijfgording ? symWrijfgording : symGording;
                        placedBeams.Add(Tuple.Create(b, CreateBeam(doc, b, symbol, level, paramNotes)));
                    }
                    foreach (var c in plan.Columns)
                    {
                        var symbol = c.Part == RemmingwerkPart.StalenStijl ? symStijl : symPaal;
                        placedColumns.Add(Tuple.Create(c, CreateColumn(doc, c, symbol, level, levelZ)));
                    }
                    foreach (var p in plan.Posts)
                    {
                        placedPosts.Add(Tuple.Create(p, CreatePost(doc, p, symWrijfstijl, level)));
                    }
                    doc.Regenerate();
                    foreach (var pp in placedPosts)
                    {
                        FinishPost(doc, pp.Item1, pp.Item2, paramNotes);
                    }
                    doc.Regenerate();

                    // Read back.
                    var deviations = new List<string>();
                    double maxDev = 0, maxRot = 0;
                    foreach (var pb in placedBeams)
                    {
                        double d = BeamDeviationMm(pb.Item2, pb.Item1);
                        maxDev = Math.Max(maxDev, d);
                        if (d > ToleranceMm) deviations.Add($"{RemmingwerkParts.Name(pb.Item1.Part)} rij {pb.Item1.Row + 1} ({pb.Item2.Id}): {d:0.000} mm");
                    }
                    foreach (var pc in placedColumns)
                    {
                        double rot;
                        double d = ColumnDeviationMm(doc, pc.Item2, pc.Item1, out rot);
                        maxDev = Math.Max(maxDev, d);
                        maxRot = Math.Max(maxRot, rot);
                        if (d > ToleranceMm || rot > RotationToleranceDeg) deviations.Add($"{RemmingwerkParts.Name(pc.Item1.Part)} {pc.Item1.Index + 1} ({pc.Item2.Id}): {d:0.000} mm, draaiing {rot:0.000}°");
                    }
                    foreach (var pp in placedPosts)
                    {
                        double rot;
                        double d = PostDeviationMm(pp.Item2, pp.Item1, out rot);
                        maxDev = Math.Max(maxDev, d);
                        maxRot = Math.Max(maxRot, rot);
                        if (d > ToleranceMm || rot > RotationToleranceDeg) deviations.Add($"Wrijfstijl {pp.Item1.Index + 1} ({pp.Item2.Id}): {d:0.000} mm, draaiing {rot:0.000}°");
                    }

                    var result = new RemmingwerkResult { Plan = plan, MaxDeviationMm = maxDev };
                    foreach (var pb in placedBeams) Add(result, pb.Item1.Part, pb.Item2.Id);
                    foreach (var pc in placedColumns) Add(result, pc.Item1.Part, pc.Item2.Id);
                    foreach (var pp in placedPosts) Add(result, RemmingwerkPart.Wrijfstijl, pp.Item2.Id);

                    notes.AddRange(paramNotes);
                    if (deviations.Count > 0)
                    {
                        t.RollBack();
                        return RemmingwerkResult.Fail(
                            $"{deviations.Count} elementen kwamen meer dan {ToleranceMm:0} mm (of {RotationToleranceDeg}°) naast de berekende plek. Niets geplaatst.",
                            BuildReport(req, plan, level, levelZ, result, placedColumns, notes, failures, deviations, false), plan);
                    }

                    var status = t.Commit();
                    if (status != TransactionStatus.Committed || failures.Errors.Count > 0)
                    {
                        return RemmingwerkResult.Fail("Revit heeft het plaatsen geweigerd: " + string.Join(" ", failures.Errors.Distinct()),
                            BuildReport(req, plan, level, levelZ, result, placedColumns, notes, failures, deviations, false), plan);
                    }

                    result.Succeeded = true;
                    result.Summary = Summary(plan, result);
                    result.Report = BuildReport(req, plan, level, levelZ, result, placedColumns, notes, failures, deviations, true);
                    Logger.Info($"Remmingwerk placed: {result.Created.Count} elements, max deviation {maxDev:0.000} mm, max rotation {maxRot:0.000}°");
                    return result;
                }
                catch
                {
                    if (t.HasStarted() && !t.HasEnded())
                    {
                        t.RollBack();
                    }
                    throw;
                }
            }
        }

        // ------------------------------------------------------------------ creation

        private static FamilyInstance CreateBeam(Document doc, PlannedBeam b, FamilySymbol symbol, Level level, ISet<string> notes)
        {
            var curve = ToRevitCurve(b.Curve, b.ZMm);
            var fi = doc.Create.NewFamilyInstance(curve, symbol, level, StructuralType.Beam);
            // Keep the beams exactly where they are computed: no automatic joins with the posts/piles.
            StructuralFramingUtils.DisallowJoinAtEnd(fi, 0);
            StructuralFramingUtils.DisallowJoinAtEnd(fi, 1);
            SetBuiltIn(fi, BuiltInParameter.STRUCTURAL_BEND_DIR_ANGLE, b.CrossSectionRotationDegrees * Math.PI / 180.0, "Cross-Section Rotation", notes);
            if (b.ZJustificationOrigin)
            {
                var zj = fi.get_Parameter(BuiltInParameter.Z_JUSTIFICATION);
                if (zj == null || zj.IsReadOnly || !zj.Set((int)ZJustification.Origin))
                {
                    notes.Add($"z Justification kon niet op Origin gezet worden ({symbol.FamilyName}).");
                }
            }
            if (!double.IsNaN(b.WrijfHoogteMm)) SetLengthByName(fi, ParamWrijfHoogte, b.WrijfHoogteMm, notes);
            if (!double.IsNaN(b.WrijfBreedteMm)) SetLengthByName(fi, ParamWrijfBreedte, b.WrijfBreedteMm, notes);
            return fi;
        }

        private static FamilyInstance CreateColumn(Document doc, PlannedColumn c, FamilySymbol symbol, Level level, double levelZMm)
        {
            var p = new XYZ(c.Position.X / MmPerFoot, c.Position.Y / MmPerFoot, level.ProjectElevation);
            var fi = doc.Create.NewFamilyInstance(p, symbol, level, StructuralType.Column);
            fi.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_PARAM)?.Set(level.Id);
            fi.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM)?.Set((c.TopMm - levelZMm) / MmPerFoot);
            fi.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM)?.Set((c.BottomMm - levelZMm) / MmPerFoot);
            Rotate(doc, fi, p, c.RotationDegrees);
            return fi;
        }

        private static FamilyInstance CreatePost(Document doc, PlannedPost post, FamilySymbol symbol, Level level)
        {
            var target = PostTarget(post);
            try
            {
                return doc.Create.NewFamilyInstance(target, symbol, level, StructuralType.NonStructural);
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                // Not level-based: place free, like Dynamo's FamilyInstance.ByPoint.
                return doc.Create.NewFamilyInstance(target, symbol, StructuralType.NonStructural);
            }
        }

        /// <summary>After a regenerate: put the wrijfstijl exactly on its point, turn it, set its sizes.</summary>
        private static void FinishPost(Document doc, PlannedPost post, FamilyInstance fi, ISet<string> notes)
        {
            var target = PostTarget(post);
            var lp = fi.Location as LocationPoint;
            if (lp != null)
            {
                var delta = target - lp.Point;
                if (delta.GetLength() * MmPerFoot > 0.01)
                {
                    ElementTransformUtils.MoveElement(doc, fi.Id, delta);
                }
            }
            Rotate(doc, fi, target, post.RotationDegrees);
            SetLengthByName(fi, ParamWrijfHoogte, post.HoogteMm, notes);
            SetLengthByName(fi, ParamWrijfBreedte, post.BreedteMm, notes);
            SetLengthByName(fi, ParamWrijfLengte, post.LengteMm, notes);
        }

        private static XYZ PostTarget(PlannedPost post)
        {
            return new XYZ(post.Position.X / MmPerFoot, post.Position.Y / MmPerFoot, post.ZMm / MmPerFoot);
        }

        /// <summary>Turns <paramref name="fi"/> about the vertical through <paramref name="at"/> to an absolute rotation.</summary>
        private static void Rotate(Document doc, FamilyInstance fi, XYZ at, double degrees)
        {
            double current = (fi.Location as LocationPoint)?.Rotation ?? 0;
            double delta = degrees * Math.PI / 180.0 - current;
            delta = Math.IEEERemainder(delta, 2 * Math.PI);
            if (Math.Abs(delta) < 1e-9)
            {
                return;
            }
            var axisPoint = new XYZ(at.X, at.Y, 0);
            ElementTransformUtils.RotateElement(doc, fi.Id, Line.CreateBound(axisPoint, axisPoint + XYZ.BasisZ), delta);
        }

        private static Curve ToRevitCurve(PlanCurve c, double zMm)
        {
            double z = zMm / MmPerFoot;
            Func<Pt2, XYZ> ft = p => new XYZ(p.X / MmPerFoot, p.Y / MmPerFoot, z);
            var arc = c as PlanArc;
            if (arc != null)
            {
                return Arc.Create(ft(arc.Start), ft(arc.End), ft(arc.MidPoint));
            }
            return Line.CreateBound(ft(c.Start), ft(c.End));
        }

        private static void SetBuiltIn(Element e, BuiltInParameter bip, double internalValue, string label, ISet<string> notes)
        {
            var p = e.get_Parameter(bip);
            if (p == null || p.IsReadOnly || !p.Set(internalValue))
            {
                notes.Add($"{label} kon niet gezet worden op {(e as FamilyInstance)?.Symbol?.FamilyName}.");
            }
        }

        private static void SetLengthByName(Element e, string name, double mm, ISet<string> notes)
        {
            var p = e.LookupParameter(name);
            if (p == null)
            {
                notes.Add($"Parameter {name} ontbreekt op {(e as FamilyInstance)?.Symbol?.FamilyName}; niet gezet.");
                return;
            }
            bool ok;
            if (p.StorageType == StorageType.Double)
            {
                ok = !p.IsReadOnly && p.Set(UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters));
            }
            else if (p.StorageType == StorageType.Integer)
            {
                ok = !p.IsReadOnly && p.Set((int)Math.Round(mm));
            }
            else
            {
                ok = false;
            }
            if (!ok)
            {
                notes.Add($"Parameter {name} op {(e as FamilyInstance)?.Symbol?.FamilyName} kon niet gezet worden.");
            }
        }

        // ------------------------------------------------------------------ read back

        public static double BeamDeviationMm(FamilyInstance fi, PlannedBeam b)
        {
            var lc = fi.Location as LocationCurve;
            if (lc == null) return double.MaxValue;
            var c = lc.Curve;
            var want = ToRevitCurve(b.Curve, b.ZMm);
            double d = Math.Max(c.GetEndPoint(0).DistanceTo(want.GetEndPoint(0)), c.GetEndPoint(1).DistanceTo(want.GetEndPoint(1)));
            d = Math.Max(d, c.Evaluate(0.5, true).DistanceTo(want.Evaluate(0.5, true)));
            return d * MmPerFoot;
        }

        public static double ColumnDeviationMm(Document doc, FamilyInstance fi, PlannedColumn c, out double rotationDevDeg)
        {
            rotationDevDeg = 0;
            double bottom, top;
            var lp = fi.Location as LocationPoint;
            if (lp != null)
            {
                bottom = LevelZ(doc, fi, BuiltInParameter.FAMILY_BASE_LEVEL_PARAM, BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM);
                top = LevelZ(doc, fi, BuiltInParameter.FAMILY_TOP_LEVEL_PARAM, BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM);
                double xy = Math.Sqrt(Sq(lp.Point.X * MmPerFoot - c.Position.X) + Sq(lp.Point.Y * MmPerFoot - c.Position.Y));
                rotationDevDeg = AngleDiff(lp.Rotation * 180 / Math.PI, c.RotationDegrees);
                return Math.Max(xy, Math.Max(Math.Abs(bottom - c.BottomMm), Math.Abs(top - c.TopMm)));
            }
            var lc = fi.Location as LocationCurve;
            if (lc == null) return double.MaxValue;
            var p0 = lc.Curve.GetEndPoint(0);
            var p1 = lc.Curve.GetEndPoint(1);
            var lo = p0.Z < p1.Z ? p0 : p1;
            var hi = p0.Z < p1.Z ? p1 : p0;
            double dxy = Math.Max(Math.Sqrt(Sq(lo.X * MmPerFoot - c.Position.X) + Sq(lo.Y * MmPerFoot - c.Position.Y)),
                                  Math.Sqrt(Sq(hi.X * MmPerFoot - c.Position.X) + Sq(hi.Y * MmPerFoot - c.Position.Y)));
            return Math.Max(dxy, Math.Max(Math.Abs(lo.Z * MmPerFoot - c.BottomMm), Math.Abs(hi.Z * MmPerFoot - c.TopMm)));
        }

        public static double PostDeviationMm(FamilyInstance fi, PlannedPost p, out double rotationDevDeg)
        {
            rotationDevDeg = 0;
            var lp = fi.Location as LocationPoint;
            if (lp == null) return double.MaxValue;
            rotationDevDeg = AngleDiff(lp.Rotation * 180 / Math.PI, p.RotationDegrees);
            return lp.Point.DistanceTo(PostTarget(p)) * MmPerFoot;
        }

        private static double LevelZ(Document doc, Element e, BuiltInParameter levelParam, BuiltInParameter offsetParam)
        {
            var lid = e.get_Parameter(levelParam)?.AsElementId();
            var lvl = lid != null ? doc.GetElement(lid) as Level : null;
            double off = e.get_Parameter(offsetParam)?.AsDouble() ?? 0;
            return ((lvl != null ? lvl.ProjectElevation : 0) + off) * MmPerFoot;
        }

        private static double AngleDiff(double a, double b)
        {
            double d = Math.Abs(Angles.Normalize(a) - Angles.Normalize(b));
            return Math.Min(d, 360 - d);
        }

        private static double Sq(double v) => v * v;

        private static Pt2 Mm(XYZ p) => new Pt2(p.X * MmPerFoot, p.Y * MmPerFoot);

        // ------------------------------------------------------------------ report

        private static void Add(RemmingwerkResult r, RemmingwerkPart part, ElementId id)
        {
            r.Created.Add(id);
            List<ElementId> list;
            if (!r.ByPart.TryGetValue(part, out list))
            {
                list = new List<ElementId>();
                r.ByPart[part] = list;
            }
            list.Add(id);
        }

        private static string Summary(RemmingwerkPlan plan, RemmingwerkResult r)
        {
            return string.Format(RemmingwerkFormat.Culture,
                "Remmingwerk geplaatst: {0} buispalen, {1} stalen stijlen, {2} wrijfstijlen, {3} wrijfgordingen en {4} stalen gordingen ({5} elementen, afwijking max {6:0.000} mm). Ctrl+Z maakt alles in één keer ongedaan.",
                plan.Count(RemmingwerkPart.Buispaal), plan.Count(RemmingwerkPart.StalenStijl), plan.Count(RemmingwerkPart.Wrijfstijl),
                plan.Count(RemmingwerkPart.Wrijfgording), plan.Count(RemmingwerkPart.StalenGording), r.Created.Count, r.MaxDeviationMm);
        }

        private static string BuildReport(RemmingwerkRequest req, RemmingwerkPlan plan, Level level, double levelZ, RemmingwerkResult r,
            List<Tuple<PlannedColumn, FamilyInstance>> columns, IList<string> notes, FailureCollector failures, IList<string> deviations, bool placed)
        {
            var s = req.Settings;
            var sb = new StringBuilder();
            sb.AppendLine("NAP-level: " + level.Name);
            sb.AppendLine("Kant: palen " + (s.PilesLeft ? "links" : "rechts") + " van de tekenrichting" + (s.PilesLeft ? string.Empty : " (zoals Dynamo)"));
            foreach (var l in plan.Lines)
            {
                sb.AppendLine(string.Format("{0}: {1}, {2} m, op {3} m t.o.v. NAP; {4} palen, eerste op {5} m",
                    l.Label, l.IsArc ? "boog" : "recht", RemmingwerkFormat.Metres(l.LengthMm), RemmingwerkFormat.Metres(l.ZMm - levelZ),
                    l.Stations, RemmingwerkFormat.Metres(l.FirstStationMm)));
            }
            sb.AppendLine();
            sb.AppendLine("Typen:");
            sb.AppendLine("  Wrijfgording:   " + req.Wrijfgording.Label);
            sb.AppendLine("  Wrijfstijl:     " + req.Wrijfstijl.Label);
            sb.AppendLine("  Stalen gording: " + req.StalenGording.Label);
            sb.AppendLine("  Stalen stijl:   " + req.StalenStijl.Label);
            sb.AppendLine("  Buispaal:       " + req.Buispaal.Label);
            sb.AppendLine(string.Format("Invoer: {0} rijen h.o.h. {1} mm; wrijfgording {2} x {3} mm; palen h.o.h. {4} mm, o.k. {5} m NAP, overlengte {6} mm.",
                s.Rows, RemmingwerkFormat.Mm(s.RowSpacingMm), RemmingwerkFormat.Mm(s.WrijfBreedteMm), RemmingwerkFormat.Mm(s.WrijfHoogteMm),
                RemmingwerkFormat.Mm(s.PileSpacingMm), RemmingwerkFormat.Metres(s.PileTipNapMm), RemmingwerkFormat.Mm(s.PileOverlengthMm)));
            var steel = req.Steel();
            sb.AppendLine(string.Format("Afstand tot de lijn: stalen gording {0} mm, stalen stijl {1} mm, buispaal {2} mm.",
                RemmingwerkFormat.Mm(s.WrijfBreedteMm), RemmingwerkFormat.Mm(RemmingwerkPlanner.PostOffsetMm(s, steel)), RemmingwerkFormat.Mm(RemmingwerkPlanner.PileOffsetMm(s, steel))));
            sb.AppendLine();

            foreach (RemmingwerkPart part in Enum.GetValues(typeof(RemmingwerkPart)))
            {
                List<ElementId> ids;
                if (!r.ByPart.TryGetValue(part, out ids)) continue;
                // Element ids only mean something when the elements were kept.
                sb.AppendLine(placed
                    ? string.Format("{0} ({1}): {2}", RemmingwerkParts.Name(part), ids.Count, string.Join(", ", ids.Select(i => i.ToString())))
                    : string.Format("{0}: {1} berekend, niet geplaatst", RemmingwerkParts.Name(part), ids.Count));
            }
            var pile = columns.FirstOrDefault(c => c.Item1.Part == RemmingwerkPart.Buispaal);
            var post = columns.FirstOrDefault(c => c.Item1.Part == RemmingwerkPart.StalenStijl);
            if (pile != null)
            {
                sb.AppendLine(string.Format("Buispaal: o.k. {0} m NAP, b.k. {1} m NAP, lengte {2} m.",
                    RemmingwerkFormat.Metres(pile.Item1.BottomMm - levelZ), RemmingwerkFormat.Metres(pile.Item1.TopMm - levelZ), RemmingwerkFormat.Metres(pile.Item1.LengthMm)));
            }
            if (post != null)
            {
                sb.AppendLine(string.Format("Stalen stijl: {0} .. {1} m NAP.",
                    RemmingwerkFormat.Metres(post.Item1.BottomMm - levelZ), RemmingwerkFormat.Metres(post.Item1.TopMm - levelZ)));
            }
            sb.AppendLine(string.Format(RemmingwerkFormat.Culture, "Teruggelezen: grootste afwijking {0:0.000} mm (max {1:0} mm).", r.MaxDeviationMm, ToleranceMm));
            foreach (var d in deviations) sb.AppendLine("  TE GROOT: " + d);

            var warnings = plan.Warnings.Concat(notes).Concat(failures.WarningSummary()).ToList();
            if (failures.Errors.Count > 0) warnings.AddRange(failures.Errors.Distinct().Select(e => "Revit-fout: " + e));
            if (warnings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Meldingen:");
                foreach (var w in warnings) sb.AppendLine("  " + w);
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>Keeps Revit's warnings out of the user's way (reported instead), rolls back on errors.</summary>
        private sealed class FailureCollector : IFailuresPreprocessor
        {
            private readonly Dictionary<string, int> _warnings = new Dictionary<string, int>();

            public List<string> Errors { get; } = new List<string>();

            public IEnumerable<string> WarningSummary()
            {
                return _warnings.Select(kv => "Revit: " + kv.Key + (kv.Value > 1 ? " (" + kv.Value + "x)" : string.Empty));
            }

            public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
            {
                bool hasError = false;
                foreach (var m in accessor.GetFailureMessages())
                {
                    var text = m.GetDescriptionText();
                    if (m.GetSeverity() == FailureSeverity.Warning)
                    {
                        int n;
                        _warnings.TryGetValue(text, out n);
                        _warnings[text] = n + 1;
                        accessor.DeleteWarning(m);
                    }
                    else
                    {
                        Errors.Add(text);
                        hasError = true;
                    }
                }
                return hasError ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
            }
        }
    }
}
