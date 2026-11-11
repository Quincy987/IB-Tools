using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using FoundationProjectionToggle.Core.PileCoordinates;
using FoundationProjectionToggle.Infrastructure;

namespace FoundationProjectionToggle.Services
{
    /// <summary>
    /// Revit side of "Paalcoördinaten": collects the piles, converts their insertion point to
    /// shared (RD) coordinates with the project location transform and writes
    /// X-/Y-coördinaat_funderingspaal in one transaction, only where the value changes.
    /// The calculations live in <see cref="PileCoordinateMath"/> (Revit-free, unit-tested).
    /// </summary>
    public class PileCoordinateService
    {
        public const string TransactionName = "IB Tools: Paalcoördinaten";

        private sealed class PendingWrite
        {
            public PileOutcome Outcome;
            public Parameter XParam;
            public Parameter YParam;
            public CoordinateParameterKind XKind;
            public CoordinateParameterKind YKind;
        }

        public PileCoordinateReport Run(UIDocument uidoc)
        {
            var doc = uidoc.Document;
            var report = new PileCoordinateReport();

            // --- Transform + self-test on the project base point -------------------------------
            var total = doc.ActiveProjectLocation.GetTotalTransform();
            var planar = new PlanarTransform(
                total.BasisX.X, total.BasisX.Y, total.BasisY.X, total.BasisY.Y, total.Origin.X, total.Origin.Y);

            var basePoint = BasePoint.GetProjectBasePoint(doc);
            var ew = basePoint.get_Parameter(BuiltInParameter.BASEPOINT_EASTWEST_PARAM).AsDouble();
            var ns = basePoint.get_Parameter(BuiltInParameter.BASEPOINT_NORTHSOUTH_PARAM).AsDouble();
            var bpPos = basePoint.Position;

            var selfTest = PileCoordinateMath.SelfTest(planar, bpPos.X, bpPos.Y, ew, ns);
            Logger.Info($"Paalcoördinaten: base point internal ({bpPos.X:0.######}, {bpPos.Y:0.######}) ft, " +
                        $"E/W {ew * PileCoordinateMath.FeetToMeters:0.###} m, N/S {ns * PileCoordinateMath.FeetToMeters:0.###} m; " +
                        $"self-test {selfTest.Direction} {selfTest.DeviationMm:0.####} mm (other {selfTest.OtherDeviationMm:0.####} mm)");

            if (!selfTest.Passed)
            {
                report.Error =
                    $"Zelftest mislukt: het projectbasispunt omgerekend wijkt {selfTest.DeviationMm:0.#} mm af van zijn eigen " +
                    "E/W en N/S (max 1 mm). De coördinaattransformatie van dit model is niet betrouwbaar.";
                report.Scope = "-";
                return report;
            }
            report.Notes.Add($"Zelftest projectbasispunt: afwijking {selfTest.DeviationMm:0.###} mm (max {PileCoordinateMath.SelfTestToleranceMm:0} mm).");

            if (planar.IsIdentity(1.0 / PileCoordinateMath.FeetToMillimeters))
            {
                report.Warnings.Add("Het model staat niet op coördinaten: gedeelde coördinaten zijn gelijk aan de interne. " +
                                    "De geschreven waarden zijn dan geen RD-coördinaten.");
            }

            var lengthUnit = doc.GetUnits().GetFormatOptions(SpecTypeId.Length).GetUnitTypeId();

            // --- Candidates: selection (foundations only) or whole model ------------------------
            var foundationCat = new ElementId(BuiltInCategory.OST_StructuralFoundation);
            var selIds = uidoc.Selection.GetElementIds();
            var fromSelection = selIds.Count > 0;
            List<Element> candidates;
            var ignoredSelection = 0;
            if (fromSelection)
            {
                candidates = new List<Element>();
                foreach (var id in selIds)
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
                report.Scope = $"selectie, {candidates.Count} {(candidates.Count == 1 ? "fundering" : "funderingen")}";
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
            var pending = new List<PendingWrite>();
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
                if (xParam.IsReadOnly || yParam.IsReadOnly)
                {
                    report.Outcomes.Add(PileOutcome.Skip(id, label, "parameter is alleen-lezen (formule of vergrendeld)"));
                    continue;
                }
                if (e.GroupId != ElementId.InvalidElementId && (!VariesAcrossGroups(xParam) || !VariesAcrossGroups(yParam)))
                {
                    report.Outcomes.Add(PileOutcome.Skip(id, label, "zit in een groep en de parameter mag niet per groep verschillen"));
                    continue;
                }
                var editBlock = WorksharingBlock(doc, e.Id);
                if (editBlock != null)
                {
                    report.Outcomes.Add(PileOutcome.Skip(id, label, editBlock));
                    continue;
                }
                var location = e.Location as LocationPoint;
                if (location == null)
                {
                    report.Outcomes.Add(PileOutcome.Skip(id, label, "geen invoegpunt"));
                    continue;
                }

                var p = location.Point;
                selfTest.InternalToShared.Apply(p.X, p.Y, out var sharedX, out var sharedY);
                var xMeters = PileCoordinateMath.FeetToRoundedMeters(sharedX);
                var yMeters = PileCoordinateMath.FeetToRoundedMeters(sharedY);
                if (!PileCoordinateMath.IsWithinRd(xMeters, yMeters))
                {
                    outsideRd++;
                }

                var xChange = Decide(xParam, xKind, xMeters, lengthUnit);
                var yChange = Decide(yParam, yKind, yMeters, lengthUnit);
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
                    pending.Add(new PendingWrite { Outcome = outcome, XParam = xParam, YParam = yParam, XKind = xKind, YKind = yKind });
                }
            }

            foreach (var k in kindsSeen)
            {
                report.Notes.Add("Parametertype " + k);
                Logger.Info("Paalcoördinaten: parameter types " + k);
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

            if (pending.Count > 0)
            {
                Write(doc, pending, lengthUnit, report);
            }

            Logger.Info($"Paalcoördinaten: {report.BuildSummary()}");
            return report;
        }

        private static void Write(Document doc, List<PendingWrite> pending, ForgeTypeId lengthUnit, PileCoordinateReport report)
        {
            using (var t = new Transaction(doc, TransactionName))
            {
                t.Start();
                foreach (var w in pending)
                {
                    try
                    {
                        var ok = (w.Outcome.X.Unchanged || Set(w.XParam, w.XKind, w.Outcome.X.NewMeters, lengthUnit))
                              && (w.Outcome.Y.Unchanged || Set(w.YParam, w.YKind, w.Outcome.Y.NewMeters, lengthUnit));
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

                var status = t.Commit();
                if (status != TransactionStatus.Committed)
                {
                    report.Error = $"Revit heeft de wijziging teruggedraaid (transactie: {status}).";
                    Logger.Warn("Paalcoördinaten: transaction not committed: " + status);
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
