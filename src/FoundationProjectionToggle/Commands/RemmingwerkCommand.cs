using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using FoundationProjectionToggle.Core.Remmingwerk;
using FoundationProjectionToggle.Infrastructure;
using FoundationProjectionToggle.Services;
using FoundationProjectionToggle.UI;

namespace FoundationProjectionToggle.Commands
{
    /// <summary>
    /// "Remmingwerk" (panel Kademuren): places a standard remmingwerk (tube piles, steel posts and
    /// gordingen, timber wrijfgordingen and wrijfstijlen) along one or more model lines/arcs.
    /// Replaces the Dynamo script "Remmingwerk v1.2".
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RemmingwerkCommand : IExternalCommand
    {
        public const string Title = "Remmingwerk";

        // Remembered per Revit session.
        private static RemmingwerkSettings _settings;
        private static string _level;
        private static string _wrijfgording, _wrijfstijl, _gording, _stijl, _paal;

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
                var lineIds = uidoc.Selection.GetElementIds()
                    .Where(id => RemmingwerkService.IsUsableLine(doc.GetElement(id)))
                    .ToList();
                if (lineIds.Count == 0)
                {
                    try
                    {
                        var picked = uidoc.Selection.PickObjects(ObjectType.Element, new ModelLineFilter(),
                            "Remmingwerk: kies de voorzijde (modellijnen of -bogen; de tekenrichting bepaalt de kant) en klik Finish");
                        lineIds = picked.Select(r => r.ElementId).ToList();
                    }
                    catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                    {
                        return Result.Cancelled;
                    }
                }
                if (lineIds.Count == 0)
                {
                    return Result.Cancelled;
                }

                var ctx = RemmingwerkService.Analyse(doc, lineIds, _level);
                if (ctx.LineErrors.Count > 0 || ctx.Lines.Count == 0)
                {
                    ResultDialog.Show(uiapp, Title, "Niets geplaatst: deze lijn(en) kan de knop niet gebruiken.",
                        string.Join(Environment.NewLine, ctx.LineErrors));
                    return Result.Cancelled;
                }
                if (ctx.Missing.Count > 0)
                {
                    ResultDialog.Show(uiapp, Title, "Niets geplaatst: er ontbreken typen in dit project. Laad ze en klik de knop opnieuw.",
                        string.Join(Environment.NewLine, ctx.Missing));
                    return Result.Cancelled;
                }

                var window = new RemmingwerkWindow(uiapp, ctx.Lines, ctx.Levels, ctx.DefaultLevel,
                    ctx.Wrijfgording, RemmingwerkFamilyService.ChooseDefault(ctx.Wrijfgording, _wrijfgording),
                    ctx.Wrijfstijl, RemmingwerkFamilyService.ChooseDefault(ctx.Wrijfstijl, _wrijfstijl),
                    ctx.StalenGording, RemmingwerkFamilyService.ChooseDefault(ctx.StalenGording, _gording, RemmingwerkFamilyService.StalenGordingHints),
                    ctx.StalenStijl, RemmingwerkFamilyService.ChooseDefault(ctx.StalenStijl, _stijl, RemmingwerkFamilyService.StalenStijlHints),
                    ctx.Buispaal, RemmingwerkFamilyService.ChooseDefault(ctx.Buispaal, _paal, RemmingwerkFamilyService.BuispaalHints),
                    _settings);
                if (!window.ShowModal())
                {
                    return Result.Cancelled;
                }

                _settings = window.Settings.Clone();
                _level = window.Level.Name;
                _wrijfgording = window.Wrijfgording.Label;
                _wrijfstijl = window.Wrijfstijl.Label;
                _gording = window.StalenGording.Label;
                _stijl = window.StalenStijl.Label;
                _paal = window.Buispaal.Label;

                var result = RemmingwerkService.Place(doc, new RemmingwerkRequest
                {
                    LineIds = ctx.LineIds,
                    LevelId = (ElementId)window.Level.Tag,
                    Settings = window.Settings,
                    Wrijfgording = window.Wrijfgording,
                    Wrijfstijl = window.Wrijfstijl,
                    StalenGording = window.StalenGording,
                    StalenStijl = window.StalenStijl,
                    Buispaal = window.Buispaal,
                });
                if (!result.Succeeded)
                {
                    ResultDialog.Show(uiapp, Title, "Niets geplaatst. " + result.Error, result.Report, copyImmediately: true);
                    return Result.Cancelled;
                }

                try
                {
                    uidoc.Selection.SetElementIds(result.Created);
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Selecting the new elements failed: {ex.Message}");
                }

                ResultDialog.Show(uiapp, Title, result.Summary, result.Report);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                Logger.Error("RemmingwerkCommand failed", ex);
                ResultDialog.Show(uiapp, Title, "Er ging iets mis: " + ex.Message, ex.ToString(), copyImmediately: true);
                return Result.Failed;
            }
        }

        private sealed class ModelLineFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem) => RemmingwerkService.IsUsableLine(elem);

            public bool AllowReference(Reference reference, XYZ position) => false;
        }
    }
}
