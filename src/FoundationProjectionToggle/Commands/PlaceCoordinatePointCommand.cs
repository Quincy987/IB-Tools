using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using FoundationProjectionToggle.Infrastructure;
using FoundationProjectionToggle.Services;
using FoundationProjectionToggle.UI;

namespace FoundationProjectionToggle.Commands
{
    /// <summary>
    /// "Modelpunt op coördinaat": places the IB coordinate point family on an RD/NAP position,
    /// via Revit's shared coordinates (checked against base point and survey point first).
    /// Replaces the Dynamo script "Modelpoint op coordinaat v1.1".
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlaceCoordinatePointCommand : IExternalCommand
    {
        public const string Title = "Modelpunt op coördinaat";

        /// <summary>Ask before placing further than this from the project base point (m).</summary>
        public const double FarAwayMetres = 5000.0;

        /// <summary>Correct the placed point when it deviates more than this (mm).</summary>
        private const double NudgeMm = 0.5;

        // Remembered per Revit session.
        private static string _rememberedType;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiapp = commandData.Application;
            var uidoc = uiapp.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null || doc.IsFamilyDocument)
            {
                message = "Open eerst een project.";
                return Result.Cancelled;
            }

            try
            {
                var ctx = SharedCoordinateService.Analyse(doc);
                if (!ctx.IsValid)
                {
                    ResultDialog.Show(uiapp, Title,
                        "Niets geplaatst. " + ctx.Error,
                        "Controles:" + Environment.NewLine + string.Join(Environment.NewLine, ctx.Checks));
                    return Result.Cancelled;
                }

                bool inProject;
                var typeNames = CoordinatePointFamilyService.GetTypeNames(doc, out inProject);
                int viewScale = 20;
                try { viewScale = doc.ActiveView?.Scale ?? 20; } catch { }
                var defaultType = FamilyPartAtomReader.ChooseDefault(typeNames, _rememberedType, viewScale);

                var clip = ClipboardHelper.TryGetText();
                var prefill = CoordinateInputParser.TryParseLine(clip);

                var info = string.Format(CultureInfo.InvariantCulture,
                    "Locatie: {0}. Projectbasispunt: RD {1} / {2}, NAP {3} m; ware noorden {4:0.###}°.{5}",
                    ctx.LocationName,
                    CoordinateInputParser.FormatMetres(ctx.BasePointSharedMetres.X),
                    CoordinateInputParser.FormatMetres(ctx.BasePointSharedMetres.Y),
                    CoordinateInputParser.FormatMetres(ctx.BasePointSharedMetres.Z),
                    ctx.AngleDegrees,
                    inProject ? string.Empty : " De familie wordt bij het plaatsen geladen.");

                var window = new CoordinateInputWindow(uiapp, info, typeNames, defaultType, prefill,
                    prefill != null ? "Van klembord: " + clip.Trim() : null);
                if (!window.ShowModal())
                {
                    return Result.Cancelled;
                }
                _rememberedType = window.TypeName;

                double distance = SharedCoordinateMath.HorizontalDistanceMetres(
                    window.X, window.Y, ctx.BasePointSharedMetres.X, ctx.BasePointSharedMetres.Y);
                if (distance > FarAwayMetres)
                {
                    var answer = TaskDialog.Show(Title,
                        string.Format(CultureInfo.InvariantCulture,
                            "Het punt ligt {0:0.0} km van het projectbasispunt. Toch plaatsen?", distance / 1000.0),
                        TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                        TaskDialogResult.No);
                    if (answer != TaskDialogResult.Yes)
                    {
                        return Result.Cancelled;
                    }
                }

                var result = Place(doc, ctx, window.X, window.Y, window.Z, window.TypeName);
                if (!result.Succeeded)
                {
                    ResultDialog.Show(uiapp, Title, "Niets geplaatst. " + result.Error, result.Report, copyImmediately: true);
                    return Result.Cancelled;
                }

                try
                {
                    uidoc.Selection.SetElementIds(new List<ElementId> { result.ElementId });
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Selecting the new point failed: {ex.Message}");
                }

                ResultDialog.Show(uiapp, Title, result.Summary, result.Report);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                Logger.Error("PlaceCoordinatePointCommand failed", ex);
                ResultDialog.Show(uiapp, Title, "Er ging iets mis: " + ex.Message, ex.ToString(), copyImmediately: true);
                return Result.Failed;
            }
        }

        /// <summary>
        /// Places the coordinate point at RD (x, y) and NAP z, all in metres, in one transaction
        /// (loading the family included, so one Ctrl+Z undoes everything). No UI; public so it
        /// can be driven from tests.
        /// </summary>
        public static PlacementResult Place(Document doc, double x, double y, double z, string typeName)
        {
            var ctx = SharedCoordinateService.Analyse(doc);
            if (!ctx.IsValid)
            {
                return PlacementResult.Fail(ctx.Error, string.Join(Environment.NewLine, ctx.Checks));
            }
            return Place(doc, ctx, x, y, z, typeName);
        }

        public static PlacementResult Place(Document doc, SharedCoordinateContext ctx, double x, double y, double z, string typeName)
        {
            var notes = new List<string>();
            var target = ctx.ToInternal(x, y, z);

            using (var t = new Transaction(doc, "IB Modelpunt op coördinaat"))
            {
                t.Start();
                try
                {
                    var symbol = CoordinatePointFamilyService.GetOrLoadSymbol(doc, typeName, notes);
                    if (symbol == null)
                    {
                        t.RollBack();
                        return PlacementResult.Fail($"De familie {CoordinatePointFamilyService.FamilyName} kon niet worden geladen.", string.Join(Environment.NewLine, notes));
                    }

                    // Same placement as Dynamo's FamilyInstance.ByPoint.
                    var instance = doc.Create.NewFamilyInstance(target, symbol, StructuralType.NonStructural);
                    doc.Regenerate();

                    var placed = PointOf(instance);
                    if (placed == null)
                    {
                        t.RollBack();
                        return PlacementResult.Fail("Het geplaatste punt heeft geen locatiepunt.", null);
                    }

                    double offMm = placed.DistanceTo(target) * SharedCoordinateMath.MmPerFoot;
                    if (offMm > NudgeMm)
                    {
                        notes.Add(string.Format(CultureInfo.InvariantCulture, "Na plaatsen {0:0.000} mm afwijking; bijgeschoven.", offMm));
                        ElementTransformUtils.MoveElement(doc, instance.Id, target - placed);
                        doc.Regenerate();
                        placed = PointOf(instance);
                    }

                    var readBack = ctx.ToSharedMetres(placed);
                    double devMm = readBack.DistanceTo(new Vec3(x, y, z)) * 1000.0;
                    var revit = ctx.RevitSharedMetres(placed);

                    var report = new StringBuilder();
                    report.AppendLine("Gevraagd (RD/NAP, m):   " + Triple(x, y, z));
                    report.AppendLine("Teruggelezen (m):       " + Triple(readBack.X, readBack.Y, readBack.Z));
                    if (revit.HasValue)
                    {
                        report.AppendLine("Revit GetProjectPosition: " + Triple(revit.Value.X, revit.Value.Y, revit.Value.Z));
                    }
                    report.AppendLine(string.Format(CultureInfo.InvariantCulture, "Afwijking:              {0:0.000} mm", devMm));
                    report.AppendLine("Type:                   " + symbol.Name);
                    report.AppendLine("Element-id:             " + instance.Id);
                    report.AppendLine("Locatie:                " + ctx.LocationName);
                    foreach (var n in notes)
                    {
                        report.AppendLine(n);
                    }
                    report.AppendLine();
                    report.AppendLine("Controles gedeelde coördinaten:");
                    foreach (var c in ctx.Checks)
                    {
                        report.AppendLine(c);
                    }

                    if (devMm > SharedCoordinateMath.ToleranceMm)
                    {
                        t.RollBack();
                        return PlacementResult.Fail(
                            string.Format(CultureInfo.InvariantCulture, "Het punt kwam {0:0.000} mm naast de gevraagde positie.", devMm),
                            report.ToString());
                    }

                    t.Commit();
                    Logger.Info($"Coordinate point {instance.Id} placed at {Triple(x, y, z)}, deviation {devMm:0.000} mm");

                    return new PlacementResult
                    {
                        Succeeded = true,
                        ElementId = instance.Id,
                        DeviationMm = devMm,
                        ReadBackMetres = readBack,
                        Summary = string.Format(CultureInfo.InvariantCulture,
                            "Modelpunt geplaatst op RD {0} / {1}, NAP {2} m ({3}). Afwijking {4:0.000} mm. Ctrl+Z maakt het ongedaan.",
                            CoordinateInputParser.FormatMetres(x), CoordinateInputParser.FormatMetres(y),
                            CoordinateInputParser.FormatMetres(z), symbol.Name, devMm),
                        Report = report.ToString().TrimEnd(),
                    };
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

        private static XYZ PointOf(FamilyInstance instance)
        {
            return (instance.Location as LocationPoint)?.Point;
        }

        private static string Triple(double x, double y, double z)
        {
            return CoordinateInputParser.FormatMetres(x) + "  " + CoordinateInputParser.FormatMetres(y) + "  " + CoordinateInputParser.FormatMetres(z);
        }
    }

    /// <summary>Outcome of <see cref="PlaceCoordinatePointCommand.Place(Document,double,double,double,string)"/>.</summary>
    public sealed class PlacementResult
    {
        public bool Succeeded { get; internal set; }
        public string Error { get; internal set; }
        public string Summary { get; internal set; }
        public string Report { get; internal set; }
        public ElementId ElementId { get; internal set; }
        public double DeviationMm { get; internal set; }
        public Vec3 ReadBackMetres { get; internal set; }

        internal static PlacementResult Fail(string error, string report)
        {
            return new PlacementResult { Succeeded = false, Error = error, Report = report };
        }
    }
}
