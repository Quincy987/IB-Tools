using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using FoundationProjectionToggle.Infrastructure;
using FoundationProjectionToggle.Models;
using FoundationProjectionToggle.Services;
using FoundationProjectionToggle.UI;

namespace FoundationProjectionToggle.Commands
{
    /// <summary>
    /// "Worksets" button (panel Model): switches worksharing on if needed and creates the
    /// IB standard worksets. Replaces the Dynamo script "Worksets".
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateIbWorksetsCommand : IExternalCommand
    {
        private const string Title = "IB Worksets";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Logger.Info("CreateIbWorksetsCommand.Execute called");
            UIApplication uiapp = commandData.Application;
            Document doc = uiapp.ActiveUIDocument?.Document;
            var service = new WorksetService();
            var result = new WorksetResult();

            string blocked = service.GetBlockingReason(doc);
            if (blocked != null)
            {
                Logger.Warn($"IB Worksets blocked: {blocked}");
                ResultDialog.ShowTaskDialog(Title, blocked);
                return Result.Cancelled;
            }

            bool wasWorkshared = doc.IsWorkshared;
            try
            {
                if (!wasWorkshared)
                {
                    if (!ConfirmEnableWorksharing())
                    {
                        Logger.Info("User declined enabling worksharing");
                        return Result.Cancelled;
                    }
                    service.EnableWorksharing(doc, result);
                }

                service.ApplyStandard(doc, result);
            }
            catch (Exception ex)
            {
                Logger.Error("IB Worksets failed", ex);
                string done = result.HasChanges ? "Wat wel gelukt is:\n\n" + result.Details() : null;
                ResultDialog.Show(uiapp, Title, $"Er ging iets mis: {ex.Message}", done, copyImmediately: true);
                message = ex.Message;
                return Result.Failed;
            }

            string summary = result.Summary();
            if (result.WorksharingEnabled)
            {
                summary += " Sla het model nu op: het wordt dan het central-model (Opslaan als voor een andere naam of plek).";
            }
            else if (result.HasChanges)
            {
                summary += IsCentralModel(doc)
                    ? " Sla het model op om de wijzigingen te bewaren."
                    : " Synchroniseer met de central om de worksets te delen.";
            }

            Logger.Info($"IB Worksets: {summary}");
            ResultDialog.Show(uiapp, Title, summary, result.Details());
            return Result.Succeeded;
        }

        /// <summary>True if this file is the central model itself (not a local copy).</summary>
        private static bool IsCentralModel(Document doc)
        {
            try
            {
                ModelPath central = doc.GetWorksharingCentralModelPath();
                if (central == null || central.ServerPath || string.IsNullOrEmpty(doc.PathName))
                {
                    return false;
                }
                string centralPath = ModelPathUtils.ConvertModelPathToUserVisiblePath(central);
                return string.Equals(
                    System.IO.Path.GetFullPath(centralPath),
                    System.IO.Path.GetFullPath(doc.PathName),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                Logger.Warn($"Could not determine central model path: {ex.Message}");
                return false;
            }
        }

        private static bool ConfirmEnableWorksharing()
        {
            var dialog = new TaskDialog(Title)
            {
                MainInstruction = "Worksharing aanzetten?",
                MainContent =
                    "Dit model is nog niet workshared. De knop zet worksharing aan en maakt de IB-worksets.\n\n" +
                    "Worksharing aanzetten kan niet ongedaan worden gemaakt en wist de lijst met ongedaan maken (Ctrl+Z).",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.No
            };
            return dialog.Show() == TaskDialogResult.Yes;
        }
    }
}
