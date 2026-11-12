using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using FoundationProjectionToggle.Core.Remmingwerk;
using FoundationProjectionToggle.Services;

namespace IBTools.RevitHarness
{
    /// <summary>
    /// Revit integration test for "Remmingwerk" (job folder contains remmingwerk.txt).
    /// </summary>
    /// <remarks>
    /// Stage 0 (first idle with a document):
    ///   - steel types: if the model lacks them, case F (refusal, 0 elements) is checked first and
    ///     then the types are loaded from &lt;job&gt;\staal\... (copied from K: by the test script);
    ///   - test lines A (20 m), B (17 m), C (20 m drawn the other way), D (arc R 30 m), E (3 m)
    ///     away from the model, on the NAP level; each placed via RemmingwerkService.Place and
    ///     checked against independently written expectations (positions, heights, parameters);
    ///   - F2: a type that does not exist → refused, 0 elements, model unchanged;
    ///   - a 3D export of the test area; then line U is selected and the real ribbon button is
    ///     posted; ui-test-remmingwerk.ps1 confirms the window, reads the result and clicks Undo.
    /// Stage 1: after ui-done.txt, check that the button placed the expected elements and that
    ///   Undo removed them again; save the model copy; done.txt.
    /// </remarks>
    public sealed class RemmingwerkHarness
    {
        private const string ButtonId = "CustomCtrl_%CustomCtrl_%IB Tools%Kademuren%Remmingwerk";
        private const double Mm = RemmingwerkService.MmPerFoot;

        private readonly string _job;
        private readonly StringBuilder _log = new StringBuilder();
        private int _stage;
        private int _fails;
        private int _countBeforeUi;
        private DateTime _postedAt;
        private ElementId _lineU;

        public RemmingwerkHarness(string job)
        {
            _job = job;
        }

        public void OnIdling(object sender, IdlingEventArgs e)
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
                    Run(uiapp, uidoc);
                }
                else if (_stage == 1 && File.Exists(Path.Combine(_job, "ui-done.txt")))
                {
                    _stage = 2;
                    FinishUi(uidoc.Document);
                }
                else if (_stage == 1 && (DateTime.Now - _postedAt).TotalMinutes > 10)
                {
                    _stage = 2;
                    Fail("UI: geen ui-done.txt binnen 10 minuten.");
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

        // ------------------------------------------------------------------ stage 0

        private void Run(UIApplication uiapp, UIDocument uidoc)
        {
            var doc = uidoc.Document;
            Log("Document: " + doc.PathName);
            var ctx0 = RemmingwerkService.Analyse(doc, new ElementId[0], null);
            var nap = ctx0.DefaultLevel >= 0 ? ctx0.Levels[ctx0.DefaultLevel] : null;
            if (nap == null)
            {
                Fail("Geen level in het model.");
                Done(doc);
                return;
            }
            var level = (Level)doc.GetElement((ElementId)nap.Tag);
            Log(string.Format(CultureInfo.InvariantCulture, "NAP-level: {0}, ProjectElevation {1:0.000} mm, Elevation {2:0.000} mm",
                level.Name, level.ProjectElevation * Mm, level.Elevation * Mm));
            double z = level.ProjectElevation * Mm;

            // F. Without steel types: the button refuses before the window.
            if (ctx0.Missing.Count > 0)
            {
                Log("F. Zonder staaltypen: Analyse meldt " + ctx0.Missing.Count + " ontbrekend:");
                foreach (var m in ctx0.Missing) Log("   " + m);
                Check(ctx0.Missing.Count == 3, "F. drie ontbrekende staaltypen gemeld");
                LoadSteel(doc);
            }
            else
            {
                // The model has the steel types: remove them in a transaction that is rolled back.
                using (var t = new Transaction(doc, "harness: zonder staaltypen"))
                {
                    t.Start();
                    var ids = ctx0.StalenGording.Concat(ctx0.StalenStijl).Concat(ctx0.Buispaal)
                        .Select(o => (ElementId)o.Tag).Distinct().ToList();
                    doc.Delete(ids);
                    var ctxF = RemmingwerkService.Analyse(doc, new ElementId[0], null);
                    Log("F. Zonder staaltypen (" + ids.Count + " typen tijdelijk verwijderd): Analyse meldt " + ctxF.Missing.Count + " ontbrekend:");
                    foreach (var m in ctxF.Missing) Log("   " + m);
                    Check(ctxF.Missing.Count == 3, "F. drie ontbrekende staaltypen gemeld, met het K:-pad");
                    t.RollBack();
                }
            }

            var ctx = RemmingwerkService.Analyse(doc, new ElementId[0], null);
            Log("Typen: gording " + ctx.StalenGording.Count + ", stijl " + ctx.StalenStijl.Count + ", buispaal " + ctx.Buispaal.Count +
                "; wrijfgording " + string.Join(" | ", ctx.Wrijfgording.Select(o => o.Label)) + "; wrijfstijl " + string.Join(" | ", ctx.Wrijfstijl.Select(o => o.Label)));
            if (ctx.Missing.Count > 0)
            {
                Fail("Staaltypen ontbreken nog na laden: " + string.Join("; ", ctx.Missing));
                Done(doc);
                return;
            }

            // Test lines, 40+ m away from the model in +Y.
            ElementId a, b, c, d, eShort, aLeft, u;
            using (var t = new Transaction(doc, "harness: testlijnen"))
            {
                t.Start();
                var sp = SketchPlane.Create(doc, Plane.CreateByNormalAndOrigin(XYZ.BasisZ, new XYZ(0, 0, z / Mm)));
                a = Line2(doc, sp, 0, 60000, 20000, 60000, z);
                b = Line2(doc, sp, 0, 80000, 17000, 80000, z);
                c = Line2(doc, sp, 20000, 100000, 0, 100000, z);
                d = ArcCcw(doc, sp, 10000, 130000, 30000, 0, Math.PI / 2, z);
                eShort = Line2(doc, sp, 0, 180000, 3000, 180000, z);
                aLeft = Line2(doc, sp, 0, 230000, 20000, 230000, z);
                u = Line2(doc, sp, 0, 200000, 20000, 200000, z);
                t.Commit();
            }
            _lineU = u;

            var req = Request(ctx, level);
            CaseLine(doc, "A. Recht 20 m", a, req, 0, 60000, 20000, false, new[] { 1500.0, 5750, 10000, 14250, 18500 });
            CaseLine(doc, "B. Recht 17 m (palen op de einden)", b, req, 0, 80000, 17000, false, new[] { 0.0, 4250, 8500, 12750, 17000 });
            CaseLine(doc, "C. 20 m andersom getekend", c, req, 20000, 100000, 20000, true, new[] { 1500.0, 5750, 10000, 14250, 18500 });
            CaseArc(doc, "D. Boog R 30 m", d, req);
            CaseShort(doc, "E. Recht 3 m", eShort, req);
            CaseLeft(doc, "G. 20 m, palen links", aLeft, req);
            CaseMissingType(doc, "F2. Niet-bestaand type", a, req);

            ExportPicture(doc, level, z);

            // UI: select line U and press the real ribbon button.
            _countBeforeUi = CountOurs(doc);
            uidoc.Selection.SetElementIds(new List<ElementId> { u });
            uiapp.PostCommand(RevitCommandId.LookupCommandId(ButtonId));
            _postedAt = DateTime.Now;
            Log("UI. Lijn U (" + u + ") geselecteerd; PostCommand(" + ButtonId + ") verstuurd; elementen vooraf: " + _countBeforeUi);
            Flush();
            File.WriteAllText(Path.Combine(_job, "ui-ready.txt"), "ready");
        }

        private void LoadSteel(Document doc)
        {
            var dir = Path.Combine(_job, "staal");
            var wanted = new[]
            {
                Tuple.Create("NLRS_28_SF_LIB_balk HEA_gen_ISR", "balk HEA450"),
                Tuple.Create("NLRS_28_SC_TLB_kolom HEA_gen_ISR", "kolom HEA450"),
                Tuple.Create("NLRS_28_SC_TLB_kolom buis_gen_ISR", "kolom ø610x12.5"),
            };
            using (var t = new Transaction(doc, "harness: staaltypen laden"))
            {
                t.Start();
                foreach (var w in wanted)
                {
                    var path = Path.Combine(dir, w.Item1, w.Item1 + ".rfa");
                    FamilySymbol s;
                    bool ok = File.Exists(path) && doc.LoadFamilySymbol(path, w.Item2, out s);
                    Log("   laden " + w.Item1 + " / " + w.Item2 + ": " + (ok ? "ok" : "MISLUKT (" + path + ")"));
                }
                t.Commit();
            }
        }

        private static RemmingwerkRequest Request(RemmingwerkContext ctx, Level level)
        {
            return new RemmingwerkRequest
            {
                LevelId = level.Id,
                Settings = new RemmingwerkSettings(),
                Wrijfgording = ctx.Wrijfgording[RemmingwerkFamilyService.ChooseDefault(ctx.Wrijfgording, null)],
                Wrijfstijl = ctx.Wrijfstijl[RemmingwerkFamilyService.ChooseDefault(ctx.Wrijfstijl, null)],
                StalenGording = ctx.StalenGording[RemmingwerkFamilyService.ChooseDefault(ctx.StalenGording, null, RemmingwerkFamilyService.StalenGordingHints)],
                StalenStijl = ctx.StalenStijl[RemmingwerkFamilyService.ChooseDefault(ctx.StalenStijl, null, RemmingwerkFamilyService.StalenStijlHints)],
                Buispaal = ctx.Buispaal[RemmingwerkFamilyService.ChooseDefault(ctx.Buispaal, null, RemmingwerkFamilyService.BuispaalHints)],
            };
        }

        private static RemmingwerkRequest With(RemmingwerkRequest r, ElementId line, Action<RemmingwerkRequest> change = null)
        {
            var copy = new RemmingwerkRequest
            {
                LineIds = new List<ElementId> { line },
                LevelId = r.LevelId,
                Settings = r.Settings.Clone(),
                Wrijfgording = r.Wrijfgording,
                Wrijfstijl = r.Wrijfstijl,
                StalenGording = r.StalenGording,
                StalenStijl = r.StalenStijl,
                Buispaal = r.Buispaal,
            };
            change?.Invoke(copy);
            return copy;
        }

        // ------------------------------------------------------------------ cases

        /// <summary>
        /// Straight line along X at y0 (mm). Independent expectations: right of +X is −Y, of −X is +Y.
        /// </summary>
        private void CaseLine(Document doc, string label, ElementId line, RemmingwerkRequest req, double x0, double y0, double length, bool drawnBackwards, double[] stations)
        {
            var r = PlaceLogged(doc, label, With(req, line));
            if (r == null) return;
            double sideSign = drawnBackwards ? 1 : -1;   // y direction of "right"
            double dirSign = drawnBackwards ? -1 : 1;    // x direction of drawing
            var piles = Instances(doc, r, RemmingwerkPart.Buispaal);
            var posts = Instances(doc, r, RemmingwerkPart.StalenStijl);
            var studs = Instances(doc, r, RemmingwerkPart.Wrijfstijl);
            Check(piles.Count == stations.Length && posts.Count == stations.Length && studs.Count == stations.Length,
                label + ": aantallen palen/stijlen/wrijfstijlen = " + stations.Length + " (" + piles.Count + "/" + posts.Count + "/" + studs.Count + ")");
            Check(Count(r, RemmingwerkPart.Wrijfgording) == 3 && Count(r, RemmingwerkPart.StalenGording) == 3, label + ": 3 + 3 gordingen");

            var level = (Level)doc.GetElement(req.LevelId);
            double napZ = level.ProjectElevation * Mm;
            for (int i = 0; i < stations.Length; i++)
            {
                double x = x0 + dirSign * stations[i];
                CheckPoint(label + " paal " + (i + 1), Pt(piles[i]), x, y0 + sideSign * 1105);
                CheckPoint(label + " stijl " + (i + 1), Pt(posts[i]), x, y0 + sideSign * 380);
                CheckPoint(label + " wrijfstijl " + (i + 1), Pt(studs[i]), x, y0);
            }
            CheckColumnHeights(doc, label + " paal 1", piles[0], napZ - 20000, napZ + 1800);
            CheckColumnHeights(doc, label + " stijl 1", posts[0], napZ, napZ + 1400);
            CheckBeams(doc, label, r, y0, sideSign, napZ);
            CheckParameters(doc, label, r);
            LogBoxes(doc, label, r);
        }

        private void CaseArc(Document doc, string label, ElementId line, RemmingwerkRequest req)
        {
            var r = PlaceLogged(doc, label, With(req, line));
            if (r == null) return;
            var center = new XYZ(10000 / Mm, 130000 / Mm, 0);
            double length = 30000 * Math.PI / 2;
            int n = RemmingwerkPlanner.Stations(length, 4250).Count;
            var piles = Instances(doc, r, RemmingwerkPart.Buispaal);
            var studs = Instances(doc, r, RemmingwerkPart.Wrijfstijl);
            Check(piles.Count == n, label + ": " + n + " palen (" + piles.Count + ")");
            double worstRadius = 0, worstRadial = 0;
            for (int i = 0; i < Math.Min(piles.Count, studs.Count); i++)
            {
                var p = Pt(piles[i]) - center;
                var s = Pt(studs[i]) - center;
                p = new XYZ(p.X, p.Y, 0);
                s = new XYZ(s.X, s.Y, 0);
                worstRadius = Math.Max(worstRadius, Math.Max(Math.Abs(p.GetLength() * Mm - 31105), Math.Abs(s.GetLength() * Mm - 30000)));
                worstRadial = Math.Max(worstRadial, p.Normalize().CrossProduct(s.Normalize()).GetLength() * 31105);
            }
            Check(worstRadius < 1, label + string.Format(CultureInfo.InvariantCulture, ": palen op R 31,105 m en wrijfstijlen op R 30 m (max {0:0.000} mm)", worstRadius));
            Check(worstRadial < 1, label + string.Format(CultureInfo.InvariantCulture, ": paal en wrijfstijl op één straal (max {0:0.000} mm)", worstRadial));
            foreach (var b in Instances(doc, r, RemmingwerkPart.StalenGording))
            {
                var arc = (b.Location as LocationCurve)?.Curve as Arc;
                Check(arc != null && Math.Abs(arc.Radius * Mm - 30160) < 1, label + ": stalen gording is een boog R 30,160 m");
                break;
            }
            CheckParameters(doc, label, r);
        }

        private void CaseShort(Document doc, string label, ElementId line, RemmingwerkRequest req)
        {
            var r = PlaceLogged(doc, label, With(req, line));
            if (r == null) return;
            var piles = Instances(doc, r, RemmingwerkPart.Buispaal);
            Check(piles.Count == 1, label + ": 1 paal");
            if (piles.Count == 1) CheckPoint(label + " paal", Pt(piles[0]), 1500, 180000 - 1105);
            Check(r.Report.Contains("korter dan de h.o.h."), label + ": waarschuwing 'korter' in het verslag");
        }

        private void CaseLeft(Document doc, string label, ElementId line, RemmingwerkRequest req)
        {
            var r = PlaceLogged(doc, label, With(req, line, q => q.Settings.PilesLeft = true));
            if (r == null) return;
            var piles = Instances(doc, r, RemmingwerkPart.Buispaal);
            var xs = new[] { 1500.0, 5750, 10000, 14250, 18500 };
            var got = piles.Select(Pt).OrderBy(p => p.X).ToList();
            Check(got.Count == 5, label + ": 5 palen");
            for (int i = 0; i < Math.Min(5, got.Count); i++)
            {
                CheckPoint(label + " paal " + (i + 1), got[i], xs[i], 230000 + 1105);
            }
        }

        private void CaseMissingType(Document doc, string label, ElementId line, RemmingwerkRequest req)
        {
            int before = CountOurs(doc);
            var bogus = new TypeOption { FamilyName = "bestaat niet", TypeName = "bestaat niet", Tag = ElementId.InvalidElementId, HoogteMm = 440, BreedteMm = 300 };
            var r = RemmingwerkService.Place(doc, With(req, line, q => q.StalenGording = bogus));
            int after = CountOurs(doc);
            Log(label + ": succeeded=" + r.Succeeded + "; " + r.Error);
            Check(!r.Succeeded && after == before, label + ": geweigerd en niets geplaatst (" + before + " → " + after + ")");
        }

        private RemmingwerkResult PlaceLogged(Document doc, string label, RemmingwerkRequest req)
        {
            var r = RemmingwerkService.Place(doc, req);
            if (!r.Succeeded)
            {
                Fail(label + ": plaatsen MISLUKT: " + r.Error + Environment.NewLine + r.Report);
                return null;
            }
            Log(label + ": " + r.Summary);
            foreach (var line in r.Report.Split('\n')) Log("   | " + line.TrimEnd());
            return r;
        }

        // ------------------------------------------------------------------ checks

        private void CheckPoint(string label, XYZ got, double xMm, double yMm)
        {
            double d = Math.Sqrt(Sq(got.X * Mm - xMm) + Sq(got.Y * Mm - yMm));
            if (d > 1)
            {
                Fail(string.Format(CultureInfo.InvariantCulture, "{0}: op ({1:0.0}; {2:0.0}), verwacht ({3:0.0}; {4:0.0}), {5:0.000} mm ernaast",
                    label, got.X * Mm, got.Y * Mm, xMm, yMm, d));
            }
        }

        private void CheckColumnHeights(Document doc, string label, FamilyInstance fi, double bottomMm, double topMm)
        {
            var bb = fi.get_BoundingBox(null);
            double b = Z(doc, fi, BuiltInParameter.FAMILY_BASE_LEVEL_PARAM, BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM);
            double t = Z(doc, fi, BuiltInParameter.FAMILY_TOP_LEVEL_PARAM, BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM);
            Check(Math.Abs(b - bottomMm) < 1 && Math.Abs(t - topMm) < 1, string.Format(CultureInfo.InvariantCulture,
                "{0}: onder {1:0.0} boven {2:0.0} mm (verwacht {3:0.0} / {4:0.0}); bbox z {5:0.0} .. {6:0.0}",
                label, b, t, bottomMm, topMm, bb != null ? bb.Min.Z * Mm : double.NaN, bb != null ? bb.Max.Z * Mm : double.NaN));
        }

        private void CheckBeams(Document doc, string label, RemmingwerkResult r, double y0, double sideSign, double napZ)
        {
            var wrijf = Instances(doc, r, RemmingwerkPart.Wrijfgording).Select(f => ((LocationCurve)f.Location).Curve).ToList();
            var steel = Instances(doc, r, RemmingwerkPart.StalenGording).Select(f => ((LocationCurve)f.Location).Curve).ToList();
            var zs = wrijf.Select(c => Math.Round(c.GetEndPoint(0).Z * Mm - napZ, 1)).OrderBy(v => v).ToArray();
            Check(zs.SequenceEqual(new[] { 0.0, 700, 1400 }), label + ": wrijfgordingen op 0 / 700 / 1400 mm t.o.v. NAP (" + string.Join(" / ", zs) + ")");
            Check(wrijf.All(c => Math.Abs(c.GetEndPoint(0).Y * Mm - y0) < 1 && Math.Abs(c.GetEndPoint(1).Y * Mm - y0) < 1), label + ": wrijfgordingen op de lijn");
            Check(steel.All(c => Math.Abs(c.GetEndPoint(0).Y * Mm - (y0 + sideSign * 160)) < 1), label + ": stalen gordingen 160 mm naar rechts");
        }

        private void CheckParameters(Document doc, string label, RemmingwerkResult r)
        {
            var w = Instances(doc, r, RemmingwerkPart.Wrijfgording).First();
            var g = Instances(doc, r, RemmingwerkPart.StalenGording).First();
            var s = Instances(doc, r, RemmingwerkPart.Wrijfstijl).First();
            Check(Deg(w, BuiltInParameter.STRUCTURAL_BEND_DIR_ANGLE) == 180, label + ": wrijfgording Cross-Section Rotation 180° (" + Deg(w, BuiltInParameter.STRUCTURAL_BEND_DIR_ANGLE) + ")");
            Check(Deg(g, BuiltInParameter.STRUCTURAL_BEND_DIR_ANGLE) == 90, label + ": stalen gording Cross-Section Rotation 90° (" + Deg(g, BuiltInParameter.STRUCTURAL_BEND_DIR_ANGLE) + ")");
            Check(w.get_Parameter(BuiltInParameter.Z_JUSTIFICATION)?.AsInteger() == (int)ZJustification.Origin, label + ": wrijfgording z Justification = Origin");
            Check(LenMm(w, RemmingwerkService.ParamWrijfHoogte) == 300 && LenMm(w, RemmingwerkService.ParamWrijfBreedte) == 160,
                label + ": wrijfgording IB_wrijfstijl_hoogte/breedte 300/160 (" + LenMm(w, RemmingwerkService.ParamWrijfHoogte) + "/" + LenMm(w, RemmingwerkService.ParamWrijfBreedte) + ")");
            Check(LenMm(s, RemmingwerkService.ParamWrijfHoogte) == 300 && LenMm(s, RemmingwerkService.ParamWrijfBreedte) == 160 && LenMm(s, RemmingwerkService.ParamWrijfLengte) == 1400,
                label + ": wrijfstijl hoogte/breedte/lengte 300/160/1400 (" + LenMm(s, RemmingwerkService.ParamWrijfHoogte) + "/" + LenMm(s, RemmingwerkService.ParamWrijfBreedte) + "/" + LenMm(s, RemmingwerkService.ParamWrijfLengte) + ")");
        }

        /// <summary>Bounding boxes of the first element per part (how the families sit around their line/point).</summary>
        private void LogBoxes(Document doc, string label, RemmingwerkResult r)
        {
            foreach (RemmingwerkPart part in Enum.GetValues(typeof(RemmingwerkPart)))
            {
                var fi = Instances(doc, r, part).FirstOrDefault();
                var bb = fi?.get_BoundingBox(null);
                if (bb == null) continue;
                var lp = fi.Location as LocationPoint;
                Log(string.Format(CultureInfo.InvariantCulture, "   bbox {0} {1}: x {2:0} .. {3:0}, y {4:0} .. {5:0}, z {6:0} .. {7:0} mm{8}",
                    label.Split('.')[0], RemmingwerkParts.Name(part), bb.Min.X * Mm, bb.Max.X * Mm, bb.Min.Y * Mm, bb.Max.Y * Mm, bb.Min.Z * Mm, bb.Max.Z * Mm,
                    lp != null ? string.Format(CultureInfo.InvariantCulture, "; punt ({0:0}; {1:0}), draaiing {2:0.###}°", lp.Point.X * Mm, lp.Point.Y * Mm, lp.Rotation * 180 / Math.PI) : string.Empty));
            }
        }

        // ------------------------------------------------------------------ UI stage

        private void FinishUi(Document doc)
        {
            int after = CountOurs(doc);
            Log("UI. Elementen na knop + Undo: " + _countBeforeUi + " → " + after);
            var ui = Path.Combine(_job, "ui-result.txt");
            var text = File.Exists(ui) ? File.ReadAllText(ui) : string.Empty;
            Check(text.Contains("Remmingwerk geplaatst: 5 buispalen"), "UI: resultaatvenster meldt 5 buispalen");
            Check(text.Contains("PaalBox: ") && text.Contains("610x12.5"), "UI: buispaal standaard ø610x12.5 (zoals Dynamo)");
            Check(!text.Contains("UI-TEST FOUT"), "UI: geen fout in ui-test-remmingwerk.ps1");
            Check(text.Contains("Undo geklikt") || text.Contains("Ongedaan"), "UI: Undo uitgevoerd");
            Check(after == _countBeforeUi, "UI: Undo haalde alles van de knop weg (" + _countBeforeUi + " → " + after + ")");
            Done(doc);
        }

        private void Done(Document doc)
        {
            try
            {
                var path = Path.Combine(_job, "Remmingwerk test (harness).rvt");
                doc.SaveAs(path, new SaveAsOptions { OverwriteExistingFile = true });
                Log("Opgeslagen als " + path);
            }
            catch (Exception ex)
            {
                Log("SaveAs mislukt: " + ex.Message);
            }
            Log(_fails == 0 ? "RESULTAAT: ALLE CONTROLES OK" : "RESULTAAT: " + _fails + " CONTROLES MISLUKT");
            Flush();
            File.WriteAllText(Path.Combine(_job, "done.txt"), _fails == 0 ? "ok" : "fail");
        }

        private void ExportPicture(Document doc, Level level, double zMm)
        {
            try
            {
                View3D view;
                using (var t = new Transaction(doc, "harness: 3D-view"))
                {
                    t.Start();
                    var vft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().First(v => v.ViewFamily == ViewFamily.ThreeDimensional);
                    view = View3D.CreateIsometric(doc, vft.Id);
                    view.Name = "Remmingwerk test " + DateTime.Now.ToString("HHmmss");
                    view.DetailLevel = ViewDetailLevel.Fine;
                    view.DisplayStyle = DisplayStyle.ShadingWithEdges;
                    view.SetSectionBox(new BoundingBoxXYZ
                    {
                        Min = new XYZ(-3000 / Mm, 55000 / Mm, (zMm - 21000) / Mm),
                        Max = new XYZ(23000 / Mm, 64000 / Mm, (zMm + 3000) / Mm),
                    });
                    view.IsSectionBoxActive = true;
                    t.Commit();
                }
                var opts = new ImageExportOptions
                {
                    ExportRange = ExportRange.SetOfViews,
                    FilePath = Path.Combine(_job, "remmingwerk-3d"),
                    HLRandWFViewsFileType = ImageFileType.PNG,
                    ShadowViewsFileType = ImageFileType.PNG,
                    ImageResolution = ImageResolution.DPI_150,
                    PixelSize = 1600,
                    ZoomType = ZoomFitType.FitToPage,
                };
                opts.SetViewsAndSheets(new List<ElementId> { view.Id });
                doc.ExportImage(opts);
                Log("3D-beeld geëxporteerd (" + view.Name + ").");
            }
            catch (Exception ex)
            {
                Log("3D-export mislukt: " + ex.Message);
            }
        }

        // ------------------------------------------------------------------ helpers

        private static ElementId Line2(Document doc, SketchPlane sp, double x0, double y0, double x1, double y1, double zMm)
        {
            var l = Line.CreateBound(new XYZ(x0 / Mm, y0 / Mm, zMm / Mm), new XYZ(x1 / Mm, y1 / Mm, zMm / Mm));
            return doc.Create.NewModelCurve(l, sp).Id;
        }

        private static ElementId ArcCcw(Document doc, SketchPlane sp, double cx, double cy, double r, double a0, double a1, double zMm)
        {
            var arc = Arc.Create(new XYZ(cx / Mm, cy / Mm, zMm / Mm), r / Mm, a0, a1, XYZ.BasisX, XYZ.BasisY);
            return doc.Create.NewModelCurve(arc, sp).Id;
        }

        private static List<FamilyInstance> Instances(Document doc, RemmingwerkResult r, RemmingwerkPart part)
        {
            List<ElementId> ids;
            return r.ByPart.TryGetValue(part, out ids) ? ids.Select(i => (FamilyInstance)doc.GetElement(i)).ToList() : new List<FamilyInstance>();
        }

        private static int Count(RemmingwerkResult r, RemmingwerkPart part)
        {
            List<ElementId> ids;
            return r.ByPart.TryGetValue(part, out ids) ? ids.Count : 0;
        }

        private static int CountOurs(Document doc)
        {
            return new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
                .Count(f => f.Symbol.FamilyName == RemmingwerkFamilyService.WrijfgordingFamily
                         || f.Symbol.FamilyName == RemmingwerkFamilyService.WrijfstijlFamily
                         || f.Symbol.FamilyName.StartsWith("NLRS_28_S", StringComparison.Ordinal));
        }

        private static XYZ Pt(FamilyInstance fi) => ((LocationPoint)fi.Location).Point;

        private static double Z(Document doc, Element e, BuiltInParameter lp, BuiltInParameter op)
        {
            var lvl = doc.GetElement(e.get_Parameter(lp).AsElementId()) as Level;
            return ((lvl?.ProjectElevation ?? 0) + e.get_Parameter(op).AsDouble()) * Mm;
        }

        private static double Deg(Element e, BuiltInParameter bip)
        {
            var p = e.get_Parameter(bip);
            return p == null ? double.NaN : Math.Round(p.AsDouble() * 180 / Math.PI, 3);
        }

        private static double LenMm(Element e, string name)
        {
            var p = e.LookupParameter(name);
            return p == null ? double.NaN : Math.Round(UnitUtils.ConvertFromInternalUnits(p.AsDouble(), UnitTypeId.Millimeters), 3);
        }

        private static double Sq(double v) => v * v;

        private void Check(bool ok, string what)
        {
            if (ok) Log("   OK  " + what);
            else Fail(what);
        }

        private void Fail(string what)
        {
            _fails++;
            Log("   FOUT " + what);
        }

        private void Log(string line)
        {
            _log.AppendLine(DateTime.Now.ToString("HH:mm:ss") + " " + line);
            Flush();
        }

        private void Flush()
        {
            File.WriteAllText(Path.Combine(_job, "results.txt"), _log.ToString(), Encoding.UTF8);
        }
    }
}
