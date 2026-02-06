using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using FoundationProjectionToggle.Infrastructure;
using FoundationProjectionToggle.Services;
using FoundationProjectionToggle.UI;

namespace FoundationProjectionToggle.Commands
{
    /// <summary>
    /// Command executed when user clicks the toggle button.
    ///
    /// <para>
    /// <b>Responsibility:</b>
    /// - Delegate to CategoryVisibilityService for actual work
    /// - Display user feedback (success/error messages)
    /// - Return appropriate Result to Revit
    /// </para>
    ///
    /// <para>
    /// <b>Attributes:</b>
    /// - Transaction(Manual): We handle our own transactions in the service
    /// - Regeneration(Manual): We don't need automatic regeneration
    /// </para>
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ToggleProjectionCommand : IExternalCommand
    {
        // Service and UI references set by App.cs during startup
        // This avoids creating new instances on every command execution
        internal static CategoryVisibilityService VisibilityService { get; set; }

        // Reference to RibbonManager for updating button state after toggle
        // This ensures immediate visual feedback (Decision 2: Hybrid approach)
        internal static RibbonManager RibbonManager { get; set; }

        /// <summary>
        /// Executes the toggle command.
        /// </summary>
        /// <param name="commandData">Data passed by Revit.</param>
        /// <param name="message">Error message to display if Result.Failed.</param>
        /// <param name="elements">Elements to highlight if Result.Failed.</param>
        /// <returns>Result indicating success, failure, or cancellation.</returns>
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            Logger.Info("ToggleProjectionCommand.Execute called");

            // Validate service is initialized
            if (VisibilityService == null)
            {
                Logger.Error("VisibilityService is null - App.OnStartup may have failed");
                message = "Add-in initialization error. Please restart Revit.";
                return Result.Failed;
            }

            // Execute toggle via service
            var result = VisibilityService.ToggleProjectionLines(commandData.Application);

            if (result.Success)
            {
                // Use null-conditional to prevent potential NullReferenceException
                var state = result.IsNowVisible == true ? "VISIBLE" : "HIDDEN";

                // Update button state IMMEDIATELY after toggle
                // This provides instant visual feedback without waiting for view switch
                var activeView = commandData.Application.ActiveUIDocument?.Document?.ActiveView;
                RibbonManager?.UpdateButtonState(activeView);

                // NO modal dialog for success - the button state change IS the feedback
                // Modal dialogs interrupt workflow for a simple toggle operation
                // Power users will appreciate the speed
                Logger.Info($"Toggle succeeded: {state} in '{result.ViewName}'");
                return Result.Succeeded;
            }
            else
            {
                // Show error feedback - errors DO warrant a dialog
                TaskDialog.Show(
                    "Projection Toggle - Error",
                    result.ErrorMessage,
                    TaskDialogCommonButtons.Ok
                );

                // Also set message for Revit's error handling
                message = result.ErrorMessage;

                // Log exception if present
                if (result.Exception != null)
                {
                    Logger.Error("Toggle failed with exception", result.Exception);
                }
                else
                {
                    Logger.Warn($"Toggle failed: {result.ErrorMessage}");
                }

                return Result.Failed;
            }
        }
    }
}
