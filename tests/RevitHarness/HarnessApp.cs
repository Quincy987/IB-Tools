using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using FoundationProjectionToggle.Commands;
using FoundationProjectionToggle.Services;
using FoundationProjectionToggle.UI;

namespace IBTools.RevitHarness
{
    /// <summary>
    /// Revit integration test for "Modelpunt op coördinaat" (see README.md). Runs only when
    /// IBTOOLS_HARNESS_JOB is set; writes its findings to that folder.
    /// </summary>
    /// <remarks>
    /// Stage 0 (first idle with a document): on the opened model copy
    ///   A. read a corner of the kade wall, its shared coordinates as Revit reports them
    ///      (= Manage › Report Shared Coordinates), place a point there, read back;
    ///   B. refusal without RD coordinates (rolled back);
    ///   C. variant: true north +10° and the project base point moved off the internal origin,
    ///      then A again (plus what the Dynamo formula would have done);
    ///   D. put another corner on the clipboard and press the real ribbon button (PostCommand);
    ///      ui-test.ps1 fills/confirms the window and writes ui-done.txt.
    /// Stage 1: after ui-done.txt, check the point the button placed, save the variant, done.txt.
    /// </remarks>
    public class HarnessApp : IExternalApplication
    {
        private const string ButtonId = "CustomCtrl_%CustomCtrl_%IB Tools%Coördinaten%CoordinatePoint";
        private const double Ft = 1.0 / 0.3048;

        private string _job;
        private int _stage;
        private XYZ _uiCorner;
        private int _countBeforeUi;
        private DateTime _postedAt;
        private readonly StringBuilder _log = new StringBuilder();

        public Result OnStartup(UIControlledApplication application)
        {
            _job = Environment.GetEnvironmentVariable("IBTOOLS_HARNESS_JOB");
            if (string.IsNullOrEmpty(_job) || !Directory.Exists(_job))
            {
                return Result.Succeeded; // inactive in every other Revit
            }
            File.WriteAllText(Path.Combine(_job, "started.txt"), System.Diagnostics.Process.GetCurrentProcess().Id.ToString());
            application.Idling += OnIdling;
            application.DialogBoxShowing += OnDialogBoxShowing;
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;

        /// <summary>
        /// Unattended run: "Ignore and continue" for unresolved links, OK for Revit warnings
        /// (moving the base point and placing two points on one spot give warnings). The
        /// add-in's own windows are WPF, not Revit dialogs, so ui-test.ps1 still sees them.
        /// </summary>
        private void OnDialogBoxShowing(object sender, DialogBoxShowingEventArgs e)
        {
            Log("Dialog: " + e.DialogId);
            try
            {
                bool handled = e.DialogId == "TaskDialog_Unresolved_References"
                    ? e.OverrideResult(1002)
                    : e.OverrideResult(1); // IDOK
                Log("   " + (handled ? "automatisch beantwoord" : "niet te beantwoorden"));
            }
            catch (Exception ex)
            {
                Log("   OverrideResult mislukt: " + ex.Message);
            }
        }

        private void OnIdling(object sender, IdlingEventArgs e)
        {
            var uiapp = sender as UIApplication;
            var uidoc = uiapp?.ActiveUIDocument;
            if (uidoc == null)
            {
                return;
            }

            try
            {
                if (_stage == 0)
                {
                    _stage = 1;
                    RunCoreTests(uiapp, uidoc.Document);
                }
                else if (_stage == 1 && File.Exists(Path.Combine(_job, "ui-done.txt")))
                {
                    _stage = 2;
                    FinishUiTest(uidoc.Document);
                }
                else if (_stage == 1 && (DateTime.Now - _postedAt).TotalMinutes > 10)
                {
                    _stage = 2;
                    Log("UI test: no ui-done.txt within 10 minutes.");
                    Done(uidoc.Document);
                }
            }
            catch (Exception ex)
            {
                _stage = 2;
                Log("HARNESS EXCEPTION: " + ex);
                Flush();
                File.WriteAllText(Path.Combine(_job, "done.txt"), "error");
            }
        }

        private void RunCoreTests(UIApplication uiapp, Document doc)
        {
            Log("Document: " + doc.PathName);
            LogCoordinates(doc, "origineel");

            // A. Corner of the kade wall in the model as delivered.
            var corners = FindCorners(doc);
            var ctxA = SharedCoordinateService.Analyse(doc);
            if (!ctxA.IsValid)
            {
                Log("A. Origineel: knop weigert: " + ctxA.Error);
                foreach (var c in ctxA.Checks) Log("   " + c);
            }
            else
            {
                PlaceAtCorner(doc, "A. Origineel", corners.Item1);
            }

            // B. Refusal: shared coordinates without RD (rolled back afterwards).
            using (var t = new Transaction(doc, "harness: no RD"))
            {
                t.Start();
                doc.ActiveProjectLocation.SetProjectPosition(XYZ.Zero, new ProjectPosition(0, 0, 0, 0));
                doc.Regenerate();
                var ctxB = SharedCoordinateService.Analyse(doc);
                Log("B. Zonder RD: valid=" + ctxB.IsValid + "; melding: " + ctxB.Error);
                var placeB = PlaceCoordinatePointCommand.Place(doc, 121000, 487000, 0, "Schaal 1op20");
                Log("B. Place(): succeeded=" + placeB.Succeeded + "; " + placeB.Error);
                t.RollBack();
            }

            // C. Variant: true north +10°, base point 100 m / 40 m off the internal origin.
            using (var t = new Transaction(doc, "harness: variant"))
            {
                t.Start();
                var loc = doc.ActiveProjectLocation;
                var p0 = loc.GetProjectPosition(XYZ.Zero);
                double e0 = p0.EastWest, n0 = p0.NorthSouth;
                if (!CoordinateInputParser.IsWithinRd(e0 / Ft, n0 / Ft))
                {
                    // No RD in the model: give it RD coordinates first so the variant still tests.
                    e0 = 121000 * Ft;
                    n0 = 487000 * Ft;
                    Log("C. Model had geen RD; variant krijgt E/W 121000, N/S 487000 op de interne oorsprong.");
                }
                loc.SetProjectPosition(XYZ.Zero, new ProjectPosition(e0, n0, p0.Elevation, p0.Angle + 10.0 * Math.PI / 180.0));
                var bp = BasePoint.GetProjectBasePoint(doc);
                if (bp.Pinned) bp.Pinned = false;
                ElementTransformUtils.MoveElement(doc, bp.Id, new XYZ(100 * Ft, 40 * Ft, 0));
                t.Commit();
            }
            LogCoordinates(doc, "variant");
            var ctxC = PlaceAtCorner(doc, "C. Variant", corners.Item1);
            if (ctxC != null && ctxC.IsValid)
            {
                LogDynamoComparison(doc, ctxC, corners.Item1);
            }

            // D. The real button, with the second corner on the clipboard (decimal commas).
            _uiCorner = corners.Item2;
            var pos = doc.ActiveProjectLocation.GetProjectPosition(_uiCorner);
            var clip = string.Format(CultureInfo.InvariantCulture, "{0:0.000} {1:0.000} {2:0.000}",
                pos.EastWest / Ft, pos.NorthSouth / Ft, pos.Elevation / Ft).Replace('.', ',');
            Log("D. Klembord voor de knop: \"" + clip + "\" (hoek 2, intern " + Fmt(_uiCorner) + ")");
            ClipboardHelper.TrySetText(clip);
            File.WriteAllText(Path.Combine(_job, "ui-expected.txt"), clip);
            _countBeforeUi = CountPoints(doc);
            _postedAt = DateTime.Now;
            uiapp.PostCommand(RevitCommandId.LookupCommandId(ButtonId));
            Log("D. PostCommand(" + ButtonId + ") verstuurd.");
            Flush();
            File.WriteAllText(Path.Combine(_job, "ui-ready.txt"), "ready");
        }

        private void FinishUiTest(Document doc)
        {
            int count = CountPoints(doc);
            Log("D. Punten voor/na de knop: " + _countBeforeUi + " → " + count);
            var newest = new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
                .Where(i => i.Symbol.FamilyName == CoordinatePointFamilyService.FamilyName)
                .OrderByDescending(i => i.Id.IntegerValue).FirstOrDefault();
            if (newest != null && count > _countBeforeUi)
            {
                var p = ((LocationPoint)newest.Location).Point;
                var pos = doc.ActiveProjectLocation.GetProjectPosition(p);
                Log(string.Format(CultureInfo.InvariantCulture,
                    "D. Knop-punt {0}: afstand tot hoek 2 = {1:0.000} mm; Report-Shared-Coordinates van het punt: {2:0.000} {3:0.000} {4:0.000}",
                    newest.Id, p.DistanceTo(_uiCorner) * 304.8, pos.EastWest / Ft, pos.NorthSouth / Ft, pos.Elevation / Ft));
            }
            Done(doc);
        }

        private void Done(Document doc)
        {
            try
            {
                var path = Path.Combine(_job, "Prinsengracht kademuur - variant (harness).rvt");
                doc.SaveAs(path, new SaveAsOptions { OverwriteExistingFile = true });
                Log("Opgeslagen als " + path);
            }
            catch (Exception ex)
            {
                Log("SaveAs mislukt: " + ex.Message);
            }
            Flush();
            File.WriteAllText(Path.Combine(_job, "done.txt"), "ok");
        }

        private SharedCoordinateContext PlaceAtCorner(Document doc, string label, XYZ corner)
        {
            var loc = doc.ActiveProjectLocation;
            var pos = loc.GetProjectPosition(corner);
            double x = pos.EastWest / Ft, y = pos.NorthSouth / Ft, z = pos.Elevation / Ft;
            Log(string.Format(CultureInfo.InvariantCulture,
                "{0}: hoek intern {1}; Report Shared Coordinates (GetProjectPosition): {2:0.000} {3:0.000} {4:0.000}",
                label, Fmt(corner), x, y, z));

            var ctx = SharedCoordinateService.Analyse(doc);
            foreach (var c in ctx.Checks) Log("   " + c);
            if (!ctx.IsValid)
            {
                Log(label + ": knop weigert: " + ctx.Error);
                return ctx;
            }

            var r = PlaceCoordinatePointCommand.Place(doc, ctx, x, y, z, "Schaal 1op20");
            if (!r.Succeeded)
            {
                Log(label + ": plaatsen MISLUKT: " + r.Error + Environment.NewLine + r.Report);
                return ctx;
            }
            var inst = (FamilyInstance)doc.GetElement(r.ElementId);
            var p = ((LocationPoint)inst.Location).Point;
            var back = loc.GetProjectPosition(p);
            double dInternal = p.DistanceTo(corner) * 304.8;
            double dShared = new XYZ(back.EastWest / Ft - x, back.NorthSouth / Ft - y, back.Elevation / Ft - z).GetLength() * 1000.0;
            Log(string.Format(CultureInfo.InvariantCulture,
                "{0}: punt {1} ({2}); teruggelezen {3:0.000} {4:0.000} {5:0.000}; verschil gedeeld {6:0.000} mm, afstand tot hoek {7:0.000} mm → {8}",
                label, r.ElementId, inst.Symbol.Name, back.EastWest / Ft, back.NorthSouth / Ft, back.Elevation / Ft,
                dShared, dInternal, dShared <= 1.0 && dInternal <= 1.0 ? "OK" : "FOUT"));
            return ctx;
        }

        private void LogDynamoComparison(Document doc, SharedCoordinateContext ctx, XYZ corner)
        {
            var bp = BasePoint.GetProjectBasePoint(doc);
            var bpInt = new Vec3(bp.Position.X, bp.Position.Y, bp.Position.Z);
            var bpShared = new Vec3(
                bp.get_Parameter(BuiltInParameter.BASEPOINT_EASTWEST_PARAM).AsDouble(),
                bp.get_Parameter(BuiltInParameter.BASEPOINT_NORTHSOUTH_PARAM).AsDouble(),
                bp.get_Parameter(BuiltInParameter.BASEPOINT_ELEVATION_PARAM).AsDouble());
            var pos = doc.ActiveProjectLocation.GetProjectPosition(corner);
            var target = new Vec3(pos.EastWest, pos.NorthSouth, pos.Elevation);
            // Revit reports e.g. 350° for -10°; normalise so the sign comparison below means something.
            double angle = RigidTransform.NormalizeDegrees(
                bp.get_Parameter(BuiltInParameter.BASEPOINT_ANGLETON_PARAM).AsDouble() * 180.0 / Math.PI) * Math.PI / 180.0;
            foreach (var sign in new[] { 1.0, -1.0 })
            {
                var dyn = SharedCoordinateMath.DynamoScriptInternal(bpInt, bpShared, target, sign * angle);
                var d = new XYZ(dyn.X, dyn.Y, dyn.Z).DistanceTo(corner) / Ft;
                Log(string.Format(CultureInfo.InvariantCulture,
                    "C. Dynamo-formule (draaiing {0}{1:0.###}°): {2:0.000} m naast de hoek", sign > 0 ? "+" : "-", angle * 180 / Math.PI, d));
            }
        }

        private void LogCoordinates(Document doc, string label)
        {
            var loc = doc.ActiveProjectLocation;
            var p0 = loc.GetProjectPosition(XYZ.Zero);
            var t = loc.GetTotalTransform();
            Log(string.Format(CultureInfo.InvariantCulture,
                "Coördinaten {0}: locatie '{1}', interne oorsprong → {2:0.000} {3:0.000} {4:0.000}, hoek {5:0.####}°; GetTotalTransform origin {6}, BasisX {7}",
                label, loc.Name, p0.EastWest / Ft, p0.NorthSouth / Ft, p0.Elevation / Ft, p0.Angle * 180 / Math.PI, Fmt(t.Origin), t.BasisX));
            foreach (var bp in new[] { BasePoint.GetProjectBasePoint(doc), BasePoint.GetSurveyPoint(doc) })
            {
                Log(string.Format(CultureInfo.InvariantCulture,
                    "   {0}: intern {1}; E/W {2:0.000} N/S {3:0.000} Elev {4:0.000}",
                    bp.IsShared ? "survey point" : "basispunt", Fmt(bp.Position),
                    bp.get_Parameter(BuiltInParameter.BASEPOINT_EASTWEST_PARAM).AsDouble() / Ft,
                    bp.get_Parameter(BuiltInParameter.BASEPOINT_NORTHSOUTH_PARAM).AsDouble() / Ft,
                    bp.get_Parameter(BuiltInParameter.BASEPOINT_ELEVATION_PARAM).AsDouble() / Ft));
            }
        }

        /// <summary>
        /// Two corners of the kade wall: extreme vertices (min and max of x+y) of the element that
        /// looks most like it (name with "kade"/"muur", else the biggest wall/generic model/floor).
        /// </summary>
        private Tuple<XYZ, XYZ> FindCorners(Document doc)
        {
            var cats = new[] { BuiltInCategory.OST_Walls, BuiltInCategory.OST_GenericModel, BuiltInCategory.OST_Floors,
                               BuiltInCategory.OST_StructuralFoundation, BuiltInCategory.OST_StructuralFraming };
            var candidates = new FilteredElementCollector(doc)
                .WherePasses(new ElementMulticategoryFilter(cats.ToList()))
                .WhereElementIsNotElementType()
                .ToElements()
                .Where(el => el.get_BoundingBox(null) != null)
                .ToList();

            Func<Element, string> describe = el =>
            {
                var type = doc.GetElement(el.GetTypeId());
                return (el.Category?.Name + " | " + el.Name + " | " + type?.Name + " | " +
                        (type as FamilySymbol)?.FamilyName + " | " +
                        el.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString()).Trim();
            };
            Func<Element, double> volume = el =>
            {
                var bb = el.get_BoundingBox(null);
                var d = bb.Max - bb.Min;
                return d.X * d.Y * Math.Max(d.Z, 0.01);
            };

            var named = candidates.Where(el => describe(el).IndexOf("kade", StringComparison.OrdinalIgnoreCase) >= 0
                                            || describe(el).IndexOf("muur", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            var pool = named.Count > 0 ? named : candidates;
            var chosen = pool.OrderByDescending(volume).First();
            Log("Kademuur-element: " + chosen.Id + " (" + describe(chosen) + "), uit " + pool.Count + " kandidaten");

            var vertices = new List<XYZ>();
            CollectVertices(chosen.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine }), vertices);
            if (vertices.Count == 0)
            {
                var bb = chosen.get_BoundingBox(null);
                vertices.Add(bb.Min);
                vertices.Add(bb.Max);
            }
            var a = vertices.OrderBy(v => v.X + v.Y).ThenBy(v => v.Z).First();
            var b = vertices.OrderByDescending(v => v.X + v.Y).ThenByDescending(v => v.Z).First();
            Log("Hoek 1 (intern): " + Fmt(a) + "; hoek 2: " + Fmt(b) + "; " + vertices.Count + " hoekpunten");
            return Tuple.Create(a, b);
        }

        private static void CollectVertices(GeometryElement geom, List<XYZ> into)
        {
            if (geom == null) return;
            foreach (var obj in geom)
            {
                if (obj is Solid solid && solid.Volume > 0)
                {
                    foreach (Edge edge in solid.Edges)
                    {
                        var c = edge.AsCurve();
                        into.Add(c.GetEndPoint(0));
                        into.Add(c.GetEndPoint(1));
                    }
                }
                else if (obj is GeometryInstance gi)
                {
                    CollectVertices(gi.GetInstanceGeometry(), into);
                }
            }
        }

        private static int CountPoints(Document doc)
        {
            return new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
                .Count(i => i.Symbol.FamilyName == CoordinatePointFamilyService.FamilyName);
        }

        private static string Fmt(XYZ p)
        {
            return string.Format(CultureInfo.InvariantCulture, "({0:0.000}; {1:0.000}; {2:0.000}) m", p.X / Ft, p.Y / Ft, p.Z / Ft);
        }

        private void Log(string line)
        {
            _log.AppendLine(DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " " + line);
        }

        private void Flush()
        {
            File.AppendAllText(Path.Combine(_job, "results.txt"), _log.ToString(), Encoding.UTF8);
            _log.Clear();
        }
    }
}
