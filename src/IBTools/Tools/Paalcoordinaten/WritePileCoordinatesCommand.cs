using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using IBTools.Shared;

namespace IBTools.Tools.Paalcoordinaten
{
    /// <summary>
    /// "Paalcoördinaten": writes the RD coordinates (meters, 3 decimals) of each pile's insertion
    /// point into X-/Y-coördinaat_funderingspaal. Replaces Dynamo "Funderingspalen_coordinaten".
    /// Selected foundations only, or the whole model when nothing is selected.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class WritePileCoordinatesCommand : IExternalCommand
    {
        /// <summary>Button label and title of its report window.</summary>
        public const string Title = "Paalcoördinaten";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Logger.Info("WritePileCoordinatesCommand.Execute called");
            var uiapp = commandData.Application;
            var uidoc = uiapp.ActiveUIDocument;
            if (uidoc == null || uidoc.Document.IsFamilyDocument)
            {
                ResultDialog.Show(uiapp, Title, "Open eerst een projectmodel.");
                return Result.Cancelled;
            }

            try
            {
                var report = new PileCoordinateService().Run(uidoc);
                ResultDialog.Show(uiapp, Title, report.BuildSummary(), report.BuildDetails());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                Logger.Error("WritePileCoordinatesCommand failed", ex);
                ResultDialog.Show(uiapp, Title, "Er ging iets mis; er is niets geschreven.", ex.ToString(), copyImmediately: true);
                return Result.Failed;
            }
        }
    }
}
