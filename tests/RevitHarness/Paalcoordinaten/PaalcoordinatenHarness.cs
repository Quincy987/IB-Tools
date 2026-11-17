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
using IBTools.Tools.Paalcoordinaten;
using IBTools.Tools.Paalcoordinaten.Core;

namespace IBTools.RevitHarness
{
    /// <summary>
    /// Revit integration test for the automatic Paalcoördinaten update (job folder contains
    /// paalcoordinaten.txt). Revit is started with a copy of the IB template; everything else
    /// happens in documents the harness makes itself.
    /// </summary>
    /// <remarks>
    /// Stage 0 (first idle), in a new project from the template (not a UI document):
    ///   F. model NOT on coordinates: place 4 piles, move one: parameters stay empty, one log line;
    ///   A. put the model on coordinates (true north +10°, RD position, base point moved), with
    ///      the updater switched off, and run the service for the whole model: all written;
    ///   I. the service again: everything unchanged (the button's report);
    ///   B. move a pile: its values follow, exactly one updater run in the log;
    ///   C. copy a pile: the copy gets its own values; D. a new pile gets values;
    ///   E. change the coordinate system (project location; survey point): all piles follow;
    ///   then a wrong X on one pile (parameter edit: the updater must not react), save as paal.rvt.
    /// Stages 1-4 (ExternalEvent opens/closes documents in the UI, the open check asks):
    ///   G1 "Niet nu" (answered in DialogBoxShowing): value stays wrong;
    ///   G2 real dialog, ui-test-paalcoordinaten.ps1 clicks "Nu bijwerken" and closes the report: fixed;
    ///   G3 reopen: no question; G4 wrong again + IBTOOLS_PILECHECK=0: no question, counts in the log.
    /// The values are checked against Revit's own Report Shared Coordinates (GetProjectPosition),
    /// not against the add-in's transform.
    /// </remarks>
    public sealed class PaalcoordinatenHarness
    {
        private const double Ft = 1.0 / 0.3048;
        private const double ToleranceM = 0.0011; // 1 mm plus rounding to 3 decimals

        private readonly string _job;
        private readonly StringBuilder _log = new StringBuilder();
        private readonly string _paal;
        private readonly string _other;
        private int _stage;
        private int _fails;
        private DateTime _waitFrom;
        private ExternalEvent _openEvent;
        private ReopenHandler _opener;
        private string _dialogMode = "ok";     // "niet-nu" | "real" | "ok" (unexpected: IDOK)
        private readonly List<string> _ourDialogs = new List<string>();
        private long _logMark;
        private int _pileCount;
        private int _wrongPileId;
        private ElementId WrongPile => new ElementId(_wrongPileId);
        private const double WrongX = 1.234;

        public PaalcoordinatenHarness(string job)
        {
            _job = job;
            _paal = Path.Combine(job, "paal.rvt");
            _other = Path.Combine(job, "ander.rvt");
        }

        // ------------------------------------------------------------------ events

        public void OnDialogBoxShowing(object sender, DialogBoxShowingEventArgs e)
        {
            var td = e as TaskDialogShowingEventArgs;
            Log("Dialoog: " + e.DialogId + (td != null ? " | " + td.Message : ""));
            try
            {
                if (e.DialogId == PileCoordinateOpenCheck.DialogId)
                {
                    _ourDialogs.Add(td?.Message ?? "");
                    if (_dialogMode == "real")
                    {
                        Log("   laten staan voor de UI-test");
                        return;
                    }
                    var code = _dialogMode == "niet-nu" ? (int)TaskDialogResult.CommandLink2 : 1;
                    Log("   automatisch beantwoord: " + (_dialogMode == "niet-nu" ? "Niet nu" : "IDOK") + " -> " + e.OverrideResult(code));
                    return;
                }
                bool handled = e.DialogId == "TaskDialog_Unresolved_References" ? e.OverrideResult(1002) : e.OverrideResult(1);
                Log("   " + (handled ? "automatisch beantwoord" : "niet te beantwoorden"));
            }
            catch (Exception ex)
            {
                Log("   OverrideResult mislukt: " + ex.Message);
            }
        }

        public void OnIdling(object sender, IdlingEventArgs e)
        {
            var uiapp = sender as UIApplication;
            var uidoc = uiapp?.ActiveUIDocument;
            if (uidoc == null || _stage < 0)
            {
                return;
            }
            e.SetRaiseWithoutDelay(); // keep ticking while waiting for documents and the UI test
            try
            {
                switch (_stage)
                {
                    case 0:
                        _stage = 1;
                        _openEvent = ExternalEvent.Create(_opener = new ReopenHandler(this));
                        RunModelTests(uiapp, uidoc.Document);
                        // G1: open paal.rvt in the UI, answer "Niet nu".
                        Environment.SetEnvironmentVariable(PileCoordinateOpenCheck.PromptVariable, "1");
                        StartReopen("niet-nu");
                        break;
                    case 1:
                        if (!Waited(4)) return;
                        _stage = 2;
                        G1(uidoc.Document);
                        // G2: real dialog, clicked by the UI test.
                        StartReopen("real");
                        File.WriteAllText(Path.Combine(_job, "ui-ready.txt"), "ready");
                        Flush();
                        break;
                    case 2:
                        if (!File.Exists(Path.Combine(_job, "ui-done.txt")))
                        {
                            if ((DateTime.Now - _waitFrom).TotalMinutes > 10)
                            {
                                Fail("G2: geen ui-done.txt binnen 10 minuten.");
                                Done();
                            }
                            return;
                        }
                        if (!Waited(2)) return;
                        _stage = 3;
                        G2(uidoc.Document);
                        StartReopen("ok");
                        break;
                    case 3:
                        if (!Waited(6)) return;
                        _stage = 4;
                        G3(uidoc.Document);
                        PrepareG4(uidoc.Document);
                        Environment.SetEnvironmentVariable(PileCoordinateOpenCheck.PromptVariable, "0");
                        StartReopen("ok");
                        break;
                    case 4:
                        if (!Waited(6)) return;
                        _stage = 5;
                        G4(uidoc.Document);
                        Done();
                        break;
                }
            }
            catch (Exception ex)
            {
                Log("HARNESS EXCEPTION: " + ex);
                _fails++;
                Done();
            }
        }

        /// <summary>True once the reopen is done and <paramref name="seconds"/> of idle time passed after it.</summary>
        private bool Waited(int seconds)
        {
            if (!_opener.Finished)
            {
                if ((DateTime.Now - _waitFrom).TotalMinutes > 5)
                {
                    Fail("Heropenen kwam niet klaar binnen 5 minuten.");
                    _opener.Finished = true;
                    _opener.FinishedAt = DateTime.Now;
                }
                return false;
            }
            return (DateTime.Now - _opener.FinishedAt).TotalSeconds >= seconds;
        }

        private void StartReopen(string dialogMode)
        {
            _dialogMode = dialogMode;
            _ourDialogs.Clear();
            _logMark = IbLogLength();
            _opener.Finished = false;
            _opener.Error = null;
            _waitFrom = DateTime.Now;
            Log("Heropenen van paal.rvt (dialoog: " + dialogMode + ", " + PileCoordinateOpenCheck.PromptVariable + "=" +
                Environment.GetEnvironmentVariable(PileCoordinateOpenCheck.PromptVariable) + ") -> " + _openEvent.Raise());
            Flush();
        }

        /// <summary>Opens ander.rvt, closes paal.rvt, opens paal.rvt again and closes ander.rvt.</summary>
        private sealed class ReopenHandler : IExternalEventHandler
        {
            private readonly PaalcoordinatenHarness _h;
            public bool Finished;
            public DateTime FinishedAt;
            public string Error;

            public ReopenHandler(PaalcoordinatenHarness h)
            {
                _h = h;
            }

            public void Execute(UIApplication app)
            {
                try
                {
                    var other = app.OpenAndActivateDocument(_h._other);
                    foreach (Document d in app.Application.Documents.Cast<Document>().ToList())
                    {
                        if (!d.IsLinked && SamePath(d.PathName, _h._paal))
                        {
                            d.Close(false);
                        }
                    }
                    var paal = app.OpenAndActivateDocument(_h._paal);
                    other.Document.Close(false);
                    _h.Log("   heropend: actief = " + app.ActiveUIDocument.Document.Title + " (" + paal.Document.PathName + ")");
                }
                catch (Exception ex)
                {
                    Error = ex.ToString();
                    _h.Log("   heropenen MISLUKT: " + ex);
                    _h._fails++;
                }
                Finished = true;
                FinishedAt = DateTime.Now;
            }

            public string GetName() => "IB Tools harness: heropenen";
        }

        // ------------------------------------------------------------------ stage 0

        private void RunModelTests(UIApplication uiapp, Document startDoc)
        {
            var app = uiapp.Application;
            Log("Startdocument: " + startDoc.PathName);
            Log("IB Tools: " + typeof(PileCoordinateService).Assembly.GetName().Version + " uit " + typeof(PileCoordinateService).Assembly.Location);
            var updaterId = PileCoordinateUpdater.RegisteredId;
            Check(updaterId != null && UpdaterRegistry.IsUpdaterRegistered(updaterId), "Updater geregistreerd");
            if (updaterId == null)
            {
                return;
            }
            Check(UpdaterRegistry.GetIsUpdaterOptional(updaterId), "Updater is optioneel (geen melding bij collega's zonder IB Tools)");

            var template = Path.Combine(_job, "IB_template.rte");
            var doc = app.NewProjectDocument(template);
            Log("Nieuw project uit " + template + ": '" + doc.Title + "'");
            EnsureParameters(app, doc);
            var symbol = LoadPileFamily(doc);
            var level = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => Math.Abs(l.Elevation)).First();
            Log("Level: " + level.Name + "; familie " + symbol.FamilyName + ": " + symbol.Name + " (" + symbol.Family.FamilyPlacementType + ")");
            LogCoordinates(doc, "template");

            // F. Not on coordinates: place 4 piles and move one; nothing may be written.
            var mark = IbLogLength();
            var piles = new List<ElementId>();
            using (var t = new Transaction(doc, "harness: palen"))
            {
                t.Start();
                foreach (var p in new[] { new XYZ(0, 0, 0), new XYZ(10, 0, 0), new XYZ(10, 8, 0), new XYZ(0, 8, 0) })
                {
                    piles.Add(PlacePile(doc, symbol, level, p * Ft).Id);
                }
                t.Commit();
            }
            Move(doc, piles[0], new XYZ(1, 1, 0) * Ft, "harness: F verplaatsen");
            var emptyF = piles.Count(id => ReadX(doc, id) == null && ReadY(doc, id) == null);
            Check(emptyF == 4, "F. Niet op coördinaten: na plaatsen en verplaatsen 4/4 palen nog leeg (gevonden " + emptyF + ")");
            var linesF = IbLogSince(mark).Where(l => l.Contains("Paalcoördinaten updater:")).ToList();
            foreach (var l in linesF) Log("   log: " + l);
            Check(linesF.Count == 1 && linesF[0].Contains("staat niet op coördinaten"),
                "F. Eén logregel 'staat niet op coördinaten' voor twee transacties (gevonden " + linesF.Count + ")");

            // A. On coordinates, with the updater switched off; then the service like the button.
            UpdaterRegistry.DisableUpdater(updaterId);
            using (var t = new Transaction(doc, "harness: op coördinaten"))
            {
                t.Start();
                doc.ActiveProjectLocation.SetProjectPosition(XYZ.Zero, new ProjectPosition(121000 * Ft, 487000 * Ft, 0, 10.0 * Math.PI / 180.0));
                var bp = BasePoint.GetProjectBasePoint(doc);
                if (bp.Pinned) bp.Pinned = false;
                ElementTransformUtils.MoveElement(doc, bp.Id, new XYZ(100 * Ft, 40 * Ft, 0));
                t.Commit();
            }
            Check(piles.All(id => ReadX(doc, id) == null), "A. Updater uit: op coördinaten zetten schrijft niets");
            UpdaterRegistry.EnableUpdater(updaterId);
            LogCoordinates(doc, "op coördinaten");
            var evalA = PileCoordinateService.Evaluate(doc, null, PileScope.WholeModel);
            Check(evalA.AllowsAutomaticWrite, "A. Model op coördinaten, zelftest geslaagd");
            PileCoordinateService.ApplyInTransaction(doc, evalA);
            Log("A. Rapport: " + evalA.Report.BuildSummary());
            Check(evalA.Report.Written == 4, "A. Service hele model: 4 palen geschreven");
            CheckAll(doc, piles, "A.");

            // I. The button's report on the same model: all unchanged.
            var evalI = PileCoordinateService.Evaluate(doc, null, PileScope.WholeModel);
            Log("I. Rapport: " + evalI.Report.BuildSummary());
            Check(evalI.Report.BuildSummary() == "Alles ongewijzigd: 4 palen (hele model).", "I. Knop-rapport tweede keer: alles ongewijzigd");

            // B. Move one pile: exactly one updater run, values follow.
            mark = IbLogLength();
            Move(doc, piles[1], new XYZ(3.21, -1.5, 0) * Ft, "harness: B verplaatsen");
            CheckPile(doc, piles[1], "B. Verplaatste paal");
            var linesB = UpdaterLines(mark, doc);
            foreach (var l in linesB) Log("   log: " + l);
            Check(linesB.Count == 1 && linesB[0].Contains("1 geschreven"), "B. Precies één updater-run per verplaatsing (gevonden " + linesB.Count + ")");

            // C. Copy a pile.
            ElementId copy;
            using (var t = new Transaction(doc, "harness: C kopiëren"))
            {
                t.Start();
                copy = ElementTransformUtils.CopyElement(doc, piles[2], new XYZ(5, 5, 0) * Ft).First();
                t.Commit();
            }
            CheckPile(doc, copy, "C. Kopie");
            Check(Math.Abs((ReadX(doc, copy) ?? 0) - (ReadX(doc, piles[2]) ?? 0)) > 1, "C. Kopie heeft eigen X, niet die van het origineel");
            piles.Add(copy);

            // D. A new pile.
            ElementId added;
            using (var t = new Transaction(doc, "harness: D nieuwe paal"))
            {
                t.Start();
                added = PlacePile(doc, symbol, level, new XYZ(-6, 3, 0) * Ft).Id;
                t.Commit();
            }
            CheckPile(doc, added, "D. Nieuwe paal");
            piles.Add(added);
            _pileCount = piles.Count;

            // E1. Coordinate system: project location (Manage › Specify coordinates).
            var before = ReadX(doc, piles[0]);
            using (var t = new Transaction(doc, "harness: E1 locatie"))
            {
                t.Start();
                var loc = doc.ActiveProjectLocation;
                var p0 = loc.GetProjectPosition(XYZ.Zero);
                loc.SetProjectPosition(XYZ.Zero, new ProjectPosition(p0.EastWest + 10 * Ft, p0.NorthSouth - 4 * Ft, p0.Elevation, p0.Angle + 5.0 * Math.PI / 180.0));
                t.Commit();
            }
            LogCoordinates(doc, "E1");
            Log(string.Format(CultureInfo.InvariantCulture, "E1. X paal 1 was {0:0.000}, nu {1:0.000}", before, ReadX(doc, piles[0])));
            CheckAll(doc, piles, "E1. Na wijzigen projectlocatie:");

            // E2. Coordinate system: move the survey point.
            before = ReadX(doc, piles[0]);
            using (var t = new Transaction(doc, "harness: E2 meetpunt"))
            {
                t.Start();
                var sp = BasePoint.GetSurveyPoint(doc);
                if (sp.Pinned) sp.Pinned = false;
                Log("E2. Meetpunt clipped=" + sp.Clipped);
                ElementTransformUtils.MoveElement(doc, sp.Id, new XYZ(7, 2, 0) * Ft);
                t.Commit();
            }
            LogCoordinates(doc, "E2");
            Log(string.Format(CultureInfo.InvariantCulture, "E2. X paal 1 was {0:0.000}, nu {1:0.000} (gelijk = meetpunt verplaatsen verandert de gedeelde coördinaten niet)", before, ReadX(doc, piles[0])));
            CheckAll(doc, piles, "E2. Na verplaatsen meetpunt:");

            // Parameter edit (with the updater on): must stay, no loop. Then save for G.
            _wrongPileId = piles[0].IntegerValue;
            using (var t = new Transaction(doc, "harness: foute X"))
            {
                t.Start();
                WriteX(doc, piles[0], WrongX);
                t.Commit();
            }
            Check(Math.Abs((ReadX(doc, piles[0]) ?? 0) - WrongX) < 1e-6, "Parameterwijziging start de updater niet (foute X blijft staan, geen lus)");
            doc.SaveAs(_paal, new SaveAsOptions { OverwriteExistingFile = true });
            Log("Opgeslagen: " + _paal);
            doc.Close(false);

            var other = app.NewProjectDocument(template);
            other.SaveAs(_other, new SaveAsOptions { OverwriteExistingFile = true });
            other.Close(false);
            Log("Hulpmodel zonder palen: " + _other);
            Flush();
        }

        // ------------------------------------------------------------------ G

        private void G1(Document active)
        {
            var doc = PaalDoc(active);
            Log("G1. Dialogen: " + _ourDialogs.Count + (_ourDialogs.Count > 0 ? " | " + _ourDialogs[0] : ""));
            Check(_ourDialogs.Count == 1, "G1. Bij openen precies één vraag");
            var expected = "Van 1 van de " + _pileCount + " palen kloppen de X-/Y-coördinaten niet meer";
            Check(_ourDialogs.Count == 1 && _ourDialogs[0].Contains(expected), "G1. Vraag noemt '" + expected + "'");
            Check(doc != null && Math.Abs((ReadX(doc, WrongPile) ?? 0) - WrongX) < 1e-6, "G1. 'Niet nu': foute X ongewijzigd");
            LogIb("G1");
        }

        private void G2(Document active)
        {
            var doc = PaalDoc(active);
            var ui = Path.Combine(_job, "ui-result.txt");
            if (File.Exists(ui)) foreach (var l in File.ReadAllLines(ui)) Log("   ui: " + l);
            Check(_ourDialogs.Count == 1, "G2. Vraag getoond (" + _ourDialogs.Count + ")");
            Check(doc != null && Math.Abs((ReadX(doc, WrongPile) ?? 0) - WrongX) > 1, "G2. 'Nu bijwerken' (echte klik): X hersteld");
            if (doc != null) CheckPile(doc, WrongPile, "G2. Herstelde paal");
            var lines = IbLogSince(_logMark).Where(l => l.Contains("open check")).ToList();
            foreach (var l in lines) Log("   log: " + l);
            Check(lines.Any(l => l.Contains("1 paal geschreven") || l.Contains("1 paal geschreven,")), "G2. Rapport: 1 paal geschreven");
            Check(doc != null && doc.IsModified, "G2. Model gewijzigd na bijwerken (één transactie)");
            if (doc != null) doc.Save();
        }

        private void G3(Document active)
        {
            Check(_ourDialogs.Count == 0, "G3. Opnieuw openen na bijwerken: geen vraag (" + _ourDialogs.Count + ")");
            var lines = IbLogSince(_logMark).Where(l => l.Contains("open check")).ToList();
            foreach (var l in lines) Log("   log: " + l);
            Check(lines.Any(l => l.Contains("paal") && l.Contains("geen vraag (niets te schrijven)")), "G3. Log: geen vraag, niets te schrijven");
        }

        private void PrepareG4(Document active)
        {
            var doc = PaalDoc(active);
            using (var t = new Transaction(doc, "harness: foute X (G4)"))
            {
                t.Start();
                WriteX(doc, WrongPile, WrongX);
                t.Commit();
            }
            doc.Save();
        }

        private void G4(Document active)
        {
            var doc = PaalDoc(active);
            Check(_ourDialogs.Count == 0, "G4. " + PileCoordinateOpenCheck.PromptVariable + "=0: geen vraag (" + _ourDialogs.Count + ")");
            var lines = IbLogSince(_logMark).Where(l => l.Contains("open check")).ToList();
            foreach (var l in lines) Log("   log: " + l);
            Check(lines.Any(l => l.Contains(_pileCount + " palen met coördinaatparameters, 1 verouderd (1 schrijfbaar, 0 geblokkeerd)")
                              && l.Contains("geen vraag (" + PileCoordinateOpenCheck.PromptVariable + "=0)")),
                "G4. Log: telling (" + _pileCount + " palen, 1 verouderd) en geen vraag");
            Check(doc != null && Math.Abs((ReadX(doc, WrongPile) ?? 0) - WrongX) < 1e-6, "G4. Niets geschreven");
        }

        private Document PaalDoc(Document active)
        {
            return SamePath(active.PathName, _paal) ? active : null;
        }

        // ------------------------------------------------------------------ helpers

        private void Done()
        {
            _stage = -1;
            Log(_fails == 0 ? "KLAAR: alles OK" : "KLAAR: " + _fails + " FOUT");
            Flush();
            File.WriteAllText(Path.Combine(_job, "done.txt"), _fails == 0 ? "ok" : "fout " + _fails);
        }

        private void EnsureParameters(Autodesk.Revit.ApplicationServices.Application app, Document doc)
        {
            var map = doc.ParameterBindings;
            var it = map.ForwardIterator();
            var bound = new List<string>();
            while (it.MoveNext())
            {
                var name = it.Key?.Name;
                if (name == PileCoordinateParameters.XName || name == PileCoordinateParameters.YName)
                {
                    bound.Add(name + " (" + (it.Current is InstanceBinding ? "instance" : "type") + ")");
                }
            }
            Log("Template-bindingen: " + (bound.Count == 0 ? "geen X-/Y-coördinaat-parameters" : string.Join(", ", bound)));
            if (bound.Count == 2)
            {
                return;
            }

            // Not in the template: bind them from a shared parameter file with the IB GUIDs.
            var file = Path.Combine(_job, "IB_paal_parameters.txt");
            File.WriteAllText(file,
                "# This is a Revit shared parameter file.\r\n*META\tVERSION\tMINVERSION\r\nMETA\t2\t1\r\n*GROUP\tID\tNAME\r\nGROUP\t1\tFunderingspaal\r\n" +
                "*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE\tHIDEWHENNOEDITABLE\r\n" +
                "PARAM\t" + PileCoordinateParameters.XGuid + "\t" + PileCoordinateParameters.XName + "\tLENGTH\t\t1\t1\t\t1\t0\r\n" +
                "PARAM\t" + PileCoordinateParameters.YGuid + "\t" + PileCoordinateParameters.YName + "\tLENGTH\t\t1\t1\t\t1\t0\r\n",
                new UTF8Encoding(false));
            var old = app.SharedParametersFilename;
            try
            {
                app.SharedParametersFilename = file;
                var group = app.OpenSharedParameterFile().Groups.get_Item("Funderingspaal");
                var cats = app.Create.NewCategorySet();
                cats.Insert(doc.Settings.Categories.get_Item(BuiltInCategory.OST_StructuralFoundation));
                using (var t = new Transaction(doc, "harness: parameters"))
                {
                    t.Start();
                    foreach (var name in new[] { PileCoordinateParameters.XName, PileCoordinateParameters.YName })
                    {
                        doc.ParameterBindings.Insert(group.Definitions.get_Item(name), app.Create.NewInstanceBinding(cats), GroupTypeId.Data);
                    }
                    t.Commit();
                }
                Log("Parameters gebonden uit " + file);
            }
            finally
            {
                app.SharedParametersFilename = old ?? "";
            }
        }

        private FamilySymbol LoadPileFamily(Document doc)
        {
            var path = Directory.GetFiles(_job, "*.rfa").First();
            Family family;
            using (var t = new Transaction(doc, "harness: paalfamilie"))
            {
                t.Start();
                if (!doc.LoadFamily(path, out family))
                {
                    family = new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>()
                        .First(f => f.Name == Path.GetFileNameWithoutExtension(path));
                }
                var sym = (FamilySymbol)doc.GetElement(family.GetFamilySymbolIds().First());
                if (!sym.IsActive) sym.Activate();
                t.Commit();
                return sym;
            }
        }

        private static FamilyInstance PlacePile(Document doc, FamilySymbol sym, Level level, XYZ p)
        {
            var at = new XYZ(p.X, p.Y, level.Elevation);
            switch (sym.Family.FamilyPlacementType)
            {
                case FamilyPlacementType.WorkPlaneBased:
                    return doc.Create.NewFamilyInstance(level.GetPlaneReference(), at, XYZ.BasisX, sym);
                default:
                    return doc.Create.NewFamilyInstance(at, sym, level, StructuralType.Footing);
            }
        }

        private static void Move(Document doc, ElementId id, XYZ by, string name)
        {
            using (var t = new Transaction(doc, name))
            {
                t.Start();
                ElementTransformUtils.MoveElement(doc, id, by);
                t.Commit();
            }
        }

        /// <summary>Expected X/Y in meters: Revit's Report Shared Coordinates of the insertion point, 3 decimals.</summary>
        private static void Expected(Document doc, ElementId id, out double x, out double y)
        {
            var p = ((LocationPoint)doc.GetElement(id).Location).Point;
            var pos = doc.ActiveProjectLocation.GetProjectPosition(p);
            x = Math.Round(pos.EastWest / Ft, 3, MidpointRounding.AwayFromZero);
            y = Math.Round(pos.NorthSouth / Ft, 3, MidpointRounding.AwayFromZero);
        }

        private void CheckPile(Document doc, ElementId id, string label)
        {
            Expected(doc, id, out var ex, out var ey);
            var x = ReadX(doc, id);
            var y = ReadY(doc, id);
            var ok = x.HasValue && y.HasValue && Math.Abs(x.Value - ex) <= ToleranceM && Math.Abs(y.Value - ey) <= ToleranceM;
            Check(ok, string.Format(CultureInfo.InvariantCulture, "{0} {1}: X {2} Y {3}, verwacht {4:0.000} {5:0.000}",
                label, IdValue(id), Fmt(x), Fmt(y), ex, ey));
        }

        private void CheckAll(Document doc, List<ElementId> ids, string label)
        {
            foreach (var id in ids) CheckPile(doc, id, label);
        }

        private static Parameter XParam(Element e) => e.get_Parameter(PileCoordinateParameters.XGuid) ?? e.LookupParameter(PileCoordinateParameters.XName);
        private static Parameter YParam(Element e) => e.get_Parameter(PileCoordinateParameters.YGuid) ?? e.LookupParameter(PileCoordinateParameters.YName);

        private static double? ReadX(Document doc, ElementId id) => Read(doc, XParam(doc.GetElement(id)));
        private static double? ReadY(Document doc, ElementId id) => Read(doc, YParam(doc.GetElement(id)));

        private static double? Read(Document doc, Parameter p)
        {
            if (p == null || !p.HasValue) return null;
            switch (p.StorageType)
            {
                case StorageType.String:
                    return PileCoordinateMath.TryParseMeters(p.AsString(), out var m) ? m : (double?)null;
                case StorageType.Double:
                    if (p.Definition.GetDataType() == SpecTypeId.Length)
                    {
                        var unit = doc.GetUnits().GetFormatOptions(SpecTypeId.Length).GetUnitTypeId();
                        return UnitUtils.ConvertFromInternalUnits(p.AsDouble(), unit);
                    }
                    return p.AsDouble();
                default:
                    return null;
            }
        }

        private static void WriteX(Document doc, ElementId id, double meters)
        {
            var p = XParam(doc.GetElement(id));
            if (p.StorageType == StorageType.String)
            {
                p.Set(PileCoordinateMath.FormatMeters(meters));
            }
            else if (p.Definition.GetDataType() == SpecTypeId.Length)
            {
                p.Set(UnitUtils.ConvertToInternalUnits(meters, doc.GetUnits().GetFormatOptions(SpecTypeId.Length).GetUnitTypeId()));
            }
            else
            {
                p.Set(meters);
            }
        }

        private void LogCoordinates(Document doc, string label)
        {
            var p0 = doc.ActiveProjectLocation.GetProjectPosition(XYZ.Zero);
            var bp = BasePoint.GetProjectBasePoint(doc);
            var sp = BasePoint.GetSurveyPoint(doc);
            Log(string.Format(CultureInfo.InvariantCulture,
                "Coördinaten {0}: interne oorsprong -> {1:0.000} {2:0.000}, hoek {3:0.###}°; basispunt intern ({4:0.000}; {5:0.000}); meetpunt intern ({6:0.000}; {7:0.000})",
                label, p0.EastWest / Ft, p0.NorthSouth / Ft, p0.Angle * 180 / Math.PI, bp.Position.X / Ft, bp.Position.Y / Ft, sp.Position.X / Ft, sp.Position.Y / Ft));
        }

        // --- IB Tools log (the harness run starts Revit with -Debug) ---

        private static string IbLogFile()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "IBTools", "logs", "log_" + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".txt");
        }

        private static long IbLogLength()
        {
            var f = IbLogFile();
            return File.Exists(f) ? new FileInfo(f).Length : 0;
        }

        private static List<string> IbLogSince(long mark)
        {
            var f = IbLogFile();
            if (!File.Exists(f)) return new List<string>();
            using (var s = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                s.Seek(Math.Min(mark, s.Length), SeekOrigin.Begin);
                using (var r = new StreamReader(s, Encoding.UTF8))
                {
                    return r.ReadToEnd().Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();
                }
            }
        }

        private static List<string> UpdaterLines(long mark, Document doc)
        {
            return IbLogSince(mark).Where(l => l.Contains("Paalcoördinaten updater:") && l.Contains("'" + doc.Title + "'")).ToList();
        }

        private void LogIb(string label)
        {
            foreach (var l in IbLogSince(_logMark).Where(l => l.Contains("open check")))
            {
                Log("   " + label + " log: " + l);
            }
        }

        private static bool SamePath(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }

        private static long IdValue(ElementId id) => id.IntegerValue;

        private static string Fmt(double? v) => v.HasValue ? v.Value.ToString("0.000", CultureInfo.InvariantCulture) : "(leeg)";

        private void Check(bool ok, string what)
        {
            if (!ok) _fails++;
            Log((ok ? "OK   " : "FOUT ") + what);
        }

        private void Fail(string what)
        {
            _fails++;
            Log("FOUT " + what);
        }

        internal void Log(string line)
        {
            lock (_log)
            {
                _log.AppendLine(DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " " + line);
            }
        }

        private void Flush()
        {
            lock (_log)
            {
                File.AppendAllText(Path.Combine(_job, "results.txt"), _log.ToString(), Encoding.UTF8);
                _log.Clear();
            }
        }
    }

    /// <summary>
    /// Case H: the model that the updater wrote in, opened in a Revit WITHOUT IB Tools (job folder
    /// contains zonder-ibtools.txt). Logs every dialog during opening (a missing non-optional
    /// updater gives one) and whether IB Tools was loaded. Uses only the Revit API.
    /// </summary>
    public sealed class ZonderIbToolsHarness
    {
        private readonly string _job;
        private readonly List<string> _dialogs = new List<string>();
        private bool _done;

        public ZonderIbToolsHarness(string job)
        {
            _job = job;
        }

        public void OnDialogBoxShowing(object sender, DialogBoxShowingEventArgs e)
        {
            var td = e as TaskDialogShowingEventArgs;
            _dialogs.Add(e.DialogId + (td != null ? " | " + td.Message : ""));
            try
            {
                e.OverrideResult(1);
            }
            catch
            {
                // nothing to do: logged above
            }
        }

        public void OnIdling(object sender, IdlingEventArgs e)
        {
            var uidoc = (sender as UIApplication)?.ActiveUIDocument;
            if (_done || uidoc == null)
            {
                return;
            }
            _done = true;
            var lines = new List<string>();
            try
            {
                var doc = uidoc.Document;
                lines.Add("H. Document: " + doc.PathName);
                var ibLoaded = AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "FoundationProjectionToggle");
                var ibApp = AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "FoundationProjectionToggle")
                    && UpdaterRegistry.GetRegisteredUpdaterInfos().Cast<UpdaterInfo>().Any(i => i.UpdaterName.StartsWith("IB Tools"));
                lines.Add("H. IB Tools-assembly geladen: " + ibLoaded + "; IB-updater geregistreerd: " + ibApp);
                lines.Add((ibApp ? "FOUT " : "OK   ") + "H. Revit draait zonder IB Tools-updater");
                foreach (var d in _dialogs) lines.Add("H. Dialoog: " + d);
                var updaterDialogs = _dialogs.Where(d => d.IndexOf("updater", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                lines.Add((updaterDialogs.Count == 0 ? "OK   " : "FOUT ") + "H. Geen melding over een ontbrekende updater bij openen (" + _dialogs.Count + " dialogen in totaal)");
                lines.Add("H. Updaters in deze Revit: " + string.Join("; ", UpdaterRegistry.GetRegisteredUpdaterInfos().Cast<UpdaterInfo>()
                    .Select(i => i.UpdaterName + (i.IsOptional ? " (optioneel)" : ""))));
            }
            catch (Exception ex)
            {
                lines.Add("FOUT H. HARNESS EXCEPTION: " + ex);
            }
            File.AppendAllLines(Path.Combine(_job, "results.txt"), lines.Select(l => DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " " + l), Encoding.UTF8);
            File.WriteAllText(Path.Combine(_job, "done.txt"), "ok");
        }
    }
}
