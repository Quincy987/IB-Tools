using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using IBTools.Shared;
using IBTools.Tools.Paalcoordinaten.Core;

namespace IBTools.Tools.Paalcoordinaten
{
    /// <summary>Which elements an evaluation looks at, and how they are reported.</summary>
    public enum PileScope
    {
        /// <summary>All structural foundations; foundations without the parameters are ignored.</summary>
        WholeModel,

        /// <summary>The user's selection: every selected foundation gets a line in the report.</summary>
        Selection,

        /// <summary>
        /// Foundations changed in a transaction (updater): as for the whole model, foundations
        /// without the parameters are ignored; logging stays at debug level.
        /// </summary>
        Changed
    }

    /// <summary>
    /// Outcome of <see cref="PileCoordinateService.Evaluate"/>: the report (as the button shows
    /// it) plus the writes that would bring the stale piles up to date. Nothing is written yet.
    /// </summary>
    public sealed class PileCoordinateEvaluation
    {
        internal PileCoordinateEvaluation(PileCoordinateReport report)
        {
            Report = report;
        }

        public PileCoordinateReport Report { get; }

        /// <summary>The base point self-test passed (otherwise nothing may be written at all).</summary>
        public bool SelfTestPassed { get; internal set; }

        /// <summary>
        /// The model is on coordinates (the internal-to-shared transform is not the identity).
        /// The button writes anyway, with a warning; automatic writing only happens when true.
        /// </summary>
        public bool OnCoordinates { get; internal set; }

        /// <summary>Automatic writing (updater, open check) is allowed for this model.</summary>
        public bool AllowsAutomaticWrite => SelfTestPassed && OnCoordinates;

        /// <summary>Number of piles that would be written.</summary>
        public int PendingCount => Pending.Count;

        /// <summary>Element ids of the piles that would be written (for the log).</summary>
        public IEnumerable<long> PendingIds => Pending.Select(p => p.Outcome.ElementId);

        internal List<PileCoordinateService.PendingWrite> Pending { get; } = new List<PileCoordinateService.PendingWrite>();

        internal ForgeTypeId LengthUnit { get; set; }
    }

    /// <summary>
    /// Revit side of "Paalcoördinaten": collects the piles, converts their insertion point to
    /// shared (RD) coordinates with the project location transform and writes
    /// X-/Y-coördinaat_funderingspaal, only where the value changes.
    /// The calculations live in <see cref="PileCoordinateMath"/> (Revit-free, unit-tested).
    /// Three callers share <see cref="Evaluate"/>: the button (<see cref="Run"/>), the updater
    /// that follows moved piles (<see cref="PileCoordinateUpdater"/>) and the check when a model
    /// is opened (<see cref="PileCoordinateOpenCheck"/>).
    /// </summary>
    public class PileCoordinateService
    {
        public const string TransactionName = "IB Tools: Paalcoördinaten";

        internal sealed class PendingWrite
        {
            public PileOutcome Outcome;
            public Parameter XParam;
            public Parameter YParam;
            public CoordinateParameterKind XKind;
            public CoordinateParameterKind YKind;
        }

        /// <summary>The button: selected foundations, or the whole model; writes in one transaction.</summary>
        public PileCoordinateReport Run(UIDocument uidoc)
        {
            var doc = uidoc.Document;
            var selIds = uidoc.Selection.GetElementIds();
            var evaluation = selIds.Count > 0
                ? Evaluate(doc, selIds, PileScope.Selection)
                : Evaluate(doc, null, PileScope.WholeModel);

            if (evaluation.PendingCount > 0)
            {
                ApplyInTransaction(doc, evaluation);
            }

            Logger.Info($"Paalcoördinaten: {evaluation.Report.BuildSummary()}");
            return evaluation.Report;
        }

        /// <summary>
        /// Works out, without writing, what the button would do: transform + base point
        /// self-test, then per pile the new values and whether they differ from the stored ones.
        /// </summary>
        /// <param name="doc">Project document.</param>
        /// <param name="candidateIds">Elements to look at; null = all structural foundations.</param>
        /// <param name="scope">How candidates and missing parameters are reported.</param>
        public static PileCoordinateEvaluation Evaluate(Document doc, ICollection<ElementId> candidateIds, PileScope scope)
        {
            var report = new PileCoordinateReport();
            var evaluation = new PileCoordinateEvaluation(report);
            var quiet = scope == PileScope.Changed; // the updater runs on every move: keep the log readable

            // --- Transform + self-test on the project base point -------------------------------
            var total = doc.ActiveProjectLocation.GetTotalTransform();
            var planar = new PlanarTransform(
                total.BasisX.X, total.BasisX.Y, total.BasisY.X, total.BasisY.Y, total.Origin.X, total.Origin.Y);

            var basePoint = BasePoint.GetProjectBasePoint(doc);
            var ew = basePoint.get_Parameter(BuiltInParameter.BASEPOINT_EASTWEST_PARAM).AsDouble();
            var ns = basePoint.get_Parameter(BuiltInParameter.BASEPOINT_NORTHSOUTH_PARAM).AsDouble();
            var bpPos = basePoint.Position;

            var selfTest = PileCoordinateMath.SelfTest(planar, bpPos.X, bpPos.Y, ew, ns);
            if (!quiet)
            {
                Logger.Info($"Paalcoördinaten: base point internal ({bpPos.X:0.######}, {bpPos.Y:0.######}) ft, " +
                            $"E/W {ew * PileCoordinateMath.FeetToMeters:0.###} m, N/S {ns * PileCoordinateMath.FeetToMeters:0.###} m; " +
                            $"self-test {selfTest.Direction} {selfTest.DeviationMm:0.####} mm (other {selfTest.OtherDeviationMm:0.####} mm)");
            }

            evaluation.OnCoordinates = !planar.IsIdentity(1.0 / PileCoordinateMath.FeetToMillimeters);

            if (!selfTest.Passed)
            {
                report.Error =
                    $"Zelftest mislukt: het projectbasispunt omgerekend wijkt {selfTest.DeviationMm:0.#} mm af van zijn eigen " +
                    "E/W en N/S (max 1 mm). De coördinaattransformatie van dit model is niet betrouwbaar.";
                report.Scope = "-";
                return evaluation;
            }
            evaluation.SelfTestPassed = true;
            report.Notes.Add($"Zelftest projectbasispunt: afwijking {selfTest.DeviationMm:0.###} mm (max {PileCoordinateMath.SelfTestToleranceMm:0} mm).");

            if (!evaluation.OnCoordinates)
            {
                report.Warnings.Add("Het model staat niet op coördinaten: gedeelde coördinaten zijn gelijk aan de interne. " +
                                    "De geschreven waarden zijn dan geen RD-coördinaten.");
            }

            var lengthUnit = doc.GetUnits().GetFormatOptions(SpecTypeId.Length).GetUnitTypeId();
            evaluation.LengthUnit = lengthUnit;

            // --- Candidates: given ids (foundations only) or whole model ------------------------
            var foundationCat = new ElementId(BuiltInCategory.OST_StructuralFoundation);
            var fromSelection = scope == PileScope.Selection;
            List<Element> candidates;
            var ignoredSelection = 0;
            if (candidateIds != null)
            {
                candidates = new List<Element>();
                foreach (var id in candidateIds)
                {
                    var e = doc.GetElement(id);
                    if (e != null && e.Category != null && e.Category.Id == foundationCat && !(e is ElementType))
                    {
                        candidates.Add(e);
                    }
                    else
                    {
                        ignoredSelection++;
                    }
                }
                var count = $"{candidates.Count} {(candidates.Count == 1 ? "fundering" : "funderingen")}";
                report.Scope = fromSelection ? "selectie, " + count : "gewijzigd, " + count;
            }
            else
            {
                candidates = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_StructuralFoundation)
                    .WhereElementIsNotElementType()
                    .ToElements()
                    .ToList();
                report.Scope = "hele model";
            }

            var notTargets = 0;
            var outsideRd = 0;
            var kindsSeen = new HashSet<string>();

            foreach (var e in candidates)
            {
                var id = IdValue(e.Id);
                var label = Label(e);

                var xParam = FindParameter(e, PileCoordinateParameters.XGuid, PileCoordinateParameters.XName);
                var yParam = FindParameter(e, PileCoordinateParameters.YGuid, PileCoordinateParameters.YName);
                if (xParam == null || yParam == null)
                {
                    var type = doc.GetElement(e.GetTypeId());
                    var onType = type != null
                        && FindParameter(type, PileCoordinateParameters.XGuid, PileCoordinateParameters.XName) != null
                        && FindParameter(type, PileCoordinateParameters.YGuid, PileCoordinateParameters.YName) != null;
                    if (onType)
                    {
                        report.Outcomes.Add(PileOutcome.Skip(id, label, "parameters staan op het type, niet per paal"));
                    }
                    else if (fromSelection)
                    {
                        report.Outcomes.Add(PileOutcome.Skip(id, label,
                            xParam == null && yParam == null ? "geen X-/Y-coördinaat-parameters" :
                            xParam == null ? $"parameter {PileCoordinateParameters.XName} ontbreekt" :
                            $"parameter {PileCoordinateParameters.YName} ontbreekt"));
                    }
                    else
                    {
                        notTargets++; // not a pile with coordinate parameters: not our business
                    }
                    continue;
                }

                var xKind = KindOf(xParam);
                var yKind = KindOf(yParam);
                kindsSeen.Add($"X: {Describe(xParam, xKind)}; Y: {Describe(yParam, yKind)}");
                if (xKind == CoordinateParameterKind.Unsupported || yKind == CoordinateParameterKind.Unsupported)
                {
                    report.Outcomes.Add(PileOutcome.Skip(id, label,
                        $"parametertype niet ondersteund (X {Describe(xParam, xKind)}, Y {Describe(yParam, yKind)})"));
                    continue;
                }

                // New values first (when there is an insertion point), so that a pile that cannot
                // be written now still tells whether it is out of date (open check).
                var location = e.Location as LocationPoint;
                CoordinateChange xChange = null;
                CoordinateChange yChange = null;
                var withinRd = true;
                if (location != null)
                {
                    var p = location.Point;
                    selfTest.InternalToShared.Apply(p.X, p.Y, out var sharedX, out var sharedY);
                    var xMeters = PileCoordinateMath.FeetToRoundedMeters(sharedX);
                    var yMeters = PileCoordinateMath.FeetToRoundedMeters(sharedY);
                    withinRd = PileCoordinateMath.IsWithinRd(xMeters, yMeters);
                    xChange = Decide(xParam, xKind, xMeters, lengthUnit);
                    yChange = Decide(yParam, yKind, yMeters, lengthUnit);
                }

                // Same skip reasons, in the same order as before.
                string block;
                if (xParam.IsReadOnly || yParam.IsReadOnly)
                {
                    block = "parameter is alleen-lezen (formule of vergrendeld)";
                }
                else if (e.GroupId != ElementId.InvalidElementId && (!VariesAcrossGroups(xParam) || !VariesAcrossGroups(yParam)))
                {
                    block = "zit in een groep en de parameter mag niet per groep verschillen";
                }
                else
                {
                    block = WorksharingBlock(doc, e.Id);
                }
                if (block == null && location == null)
                {
                    block = "geen invoegpunt";
                }
                if (block != null)
                {
                    var skipped = PileOutcome.Skip(id, label, block);
                    skipped.X = xChange;
                    skipped.Y = yChange;
                    report.Outcomes.Add(skipped);
                    continue;
                }

                if (!withinRd)
                {
                    outsideRd++;
                }
                var outcome = new PileOutcome
                {
                    ElementId = id,
                    Label = label,
                    X = xChange,
                    Y = yChange,
                    Status = xChange.Unchanged && yChange.Unchanged ? PileStatus.Unchanged : PileStatus.Written
                };
                report.Outcomes.Add(outcome);
                if (outcome.Status == PileStatus.Written)
                {
                    evaluation.Pending.Add(new PendingWrite { Outcome = outcome, XParam = xParam, YParam = yParam, XKind = xKind, YKind = yKind });
                }
            }

            foreach (var k in kindsSeen)
            {
                report.Notes.Add("Parametertype " + k);
                if (!quiet)
                {
                    Logger.Info("Paalcoördinaten: parameter types " + k);
                }
            }
            if (kindsSeen.Count > 0)
            {
                report.Notes.Add($"Lengte-eenheid project: {LabelUtils.GetLabelForUnit(lengthUnit)} (lengte-parameter krijgt de meterwaarde in die eenheid, zoals het Dynamo-script).");
            }
            if (fromSelection && ignoredSelection > 0)
            {
                report.Notes.Add($"{ignoredSelection} geselecteerde elementen genegeerd: geen fundering.");
            }
            if (notTargets > 0)
            {
                report.Notes.Add($"{notTargets} andere funderingen zonder deze parameters genegeerd (geen paal).");
            }
            if (outsideRd > 0)
            {
                report.Warnings.Add($"{outsideRd} {(outsideRd == 1 ? "paal ligt" : "palen liggen")} buiten het RD-bereik: staat het model op coördinaten?");
            }

            return evaluation;
        }

        /// <summary>
        /// Writes the pending values in one transaction "IB Tools: Paalcoördinaten" (one Ctrl+Z).
        /// Used by the button and the open check.
        /// </summary>
        public static void ApplyInTransaction(Document doc, PileCoordinateEvaluation evaluation)
        {
            using (var t = new Transaction(doc, TransactionName))
            {
                t.Start();
                ApplyValues(evaluation);
                var status = t.Commit();
                if (status != TransactionStatus.Committed)
                {
                    evaluation.Report.Error = $"Revit heeft de wijziging teruggedraaid (transactie: {status}).";
                    Logger.Warn("Paalcoördinaten: transaction not committed: " + status);
                }
            }
        }

        /// <summary>
        /// Writes the pending values in the transaction that is already open. Only for the
        /// updater: Revit runs it inside the user's own transaction, where starting one is not
        /// allowed, and the write then belongs to the user's change (the same Ctrl+Z).
        /// </summary>
        public static void ApplyWithoutTransaction(PileCoordinateEvaluation evaluation)
        {
            ApplyValues(evaluation);
        }

        private static void ApplyValues(PileCoordinateEvaluation evaluation)
        {
            foreach (var w in evaluation.Pending)
            {
                try
                {
                    var ok = (w.Outcome.X.Unchanged || Set(w.XParam, w.XKind, w.Outcome.X.NewMeters, evaluation.LengthUnit))
                          && (w.Outcome.Y.Unchanged || Set(w.YParam, w.YKind, w.Outcome.Y.NewMeters, evaluation.LengthUnit));
                    if (!ok)
                    {
                        MarkFailed(w.Outcome, "Revit weigerde de waarde");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"Paalcoördinaten: writing element {w.Outcome.ElementId} failed", ex);
                    MarkFailed(w.Outcome, "schrijven mislukt: " + ex.Message);
                }
            }
        }

        private static void MarkFailed(PileOutcome o, string reason)
        {
            o.Status = PileStatus.Skipped;
            o.Reason = reason;
        }

        private static bool Set(Parameter p, CoordinateParameterKind kind, double meters, ForgeTypeId lengthUnit)
        {
            switch (kind)
            {
                case CoordinateParameterKind.Text:
                    return p.Set(PileCoordinateMath.FormatMeters(meters));
                case CoordinateParameterKind.Number:
                    return p.Set(meters);
                case CoordinateParameterKind.Length:
                    return p.Set(UnitUtils.ConvertToInternalUnits(meters, lengthUnit));
                default:
                    return false;
            }
        }

        private static CoordinateChange Decide(Parameter p, CoordinateParameterKind kind, double newMeters, ForgeTypeId lengthUnit)
        {
            var hasValue = p.HasValue;
            switch (kind)
            {
                case CoordinateParameterKind.Text:
                    return PileCoordinateMath.Decide(kind, newMeters, hasValue, p.AsString(), 0);
                case CoordinateParameterKind.Number:
                    return PileCoordinateMath.Decide(kind, newMeters, hasValue, null, p.AsDouble());
                default: // Length
                    return PileCoordinateMath.Decide(kind, newMeters, hasValue, null,
                        UnitUtils.ConvertFromInternalUnits(p.AsDouble(), lengthUnit));
            }
        }

        private static Parameter FindParameter(Element e, Guid guid, string name)
        {
            return e.get_Parameter(guid) ?? e.LookupParameter(name);
        }

        private static CoordinateParameterKind KindOf(Parameter p)
        {
            switch (p.StorageType)
            {
                case StorageType.String:
                    return CoordinateParameterKind.Text;
                case StorageType.Double:
                    var spec = p.Definition.GetDataType();
                    if (spec == SpecTypeId.Length)
                    {
                        return CoordinateParameterKind.Length;
                    }
                    if (spec == SpecTypeId.Number)
                    {
                        return CoordinateParameterKind.Number;
                    }
                    return CoordinateParameterKind.Unsupported;
                default:
                    return CoordinateParameterKind.Unsupported;
            }
        }

        private static string Describe(Parameter p, CoordinateParameterKind kind)
        {
            var guid = p.IsShared ? p.GUID.ToString() : "geen gedeelde parameter";
            string type;
            switch (kind)
            {
                case CoordinateParameterKind.Text: type = "tekst"; break;
                case CoordinateParameterKind.Number: type = "getal"; break;
                case CoordinateParameterKind.Length: type = "lengte"; break;
                default: type = p.StorageType.ToString(); break;
            }
            return $"{type} ({guid})";
        }

        private static bool VariesAcrossGroups(Parameter p)
        {
            return (p.Definition as InternalDefinition)?.VariesAcrossGroups == true;
        }

        /// <summary>Reason why a workshared element cannot be edited now, or null.</summary>
        private static string WorksharingBlock(Document doc, ElementId id)
        {
            if (!doc.IsWorkshared)
            {
                return null;
            }
            if (WorksharingUtils.GetCheckoutStatus(doc, id) == CheckoutStatus.OwnedByOtherUser)
            {
                var owner = WorksharingUtils.GetWorksharingTooltipInfo(doc, id)?.Owner;
                return $"uitgecheckt door {(string.IsNullOrEmpty(owner) ? "iemand anders" : owner)}";
            }
            var updates = WorksharingUtils.GetModelUpdatesStatus(doc, id);
            if (updates == ModelUpdatesStatus.UpdatedInCentral || updates == ModelUpdatesStatus.DeletedInCentral)
            {
                return "gewijzigd in het centrale model: eerst Reload Latest";
            }
            return null;
        }

        private static string Label(Element e)
        {
            if (e is FamilyInstance fi && fi.Symbol != null)
            {
                return $"{fi.Symbol.FamilyName}: {fi.Symbol.Name}";
            }
            return e.Name;
        }

        private static long IdValue(ElementId id)
        {
#if REVIT2024 || REVIT2025_OR_GREATER
            return id.Value;
#else
            return id.IntegerValue;
#endif
        }
    }
}
