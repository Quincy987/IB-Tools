using Autodesk.Revit.UI;
using FoundationProjectionToggle.Availability;
using FoundationProjectionToggle.Commands;
using FoundationProjectionToggle.Infrastructure;
using FoundationProjectionToggle.Services;
using FoundationProjectionToggle.UI;

namespace FoundationProjectionToggle
{
    /// <summary>
    /// Main entry point for the IB Tools Revit add-in (assembly FoundationProjectionToggle).
    ///
    /// <para>
    /// <b>Lifecycle:</b>
    /// - OnStartup: Called when Revit loads add-in (after .addin manifest processed)
    /// - OnShutdown: Called when Revit closes (cleanup opportunity)
    /// </para>
    ///
    /// <para>
    /// <b>Responsibilities:</b>
    /// - Create and wire up all services (poor man's DI)
    /// - Create ribbon UI
    /// - Clean up on shutdown
    /// </para>
    ///
    /// <para>
    /// <b>Error Handling:</b>
    /// OnStartup failures prevent add-in from loading. We log errors but
    /// let exceptions propagate so Revit shows appropriate error.
    /// </para>
    /// </summary>
    public class App : IExternalApplication
    {
        private RibbonManager _ribbonManager;

        /// <summary>
        /// Called when Revit loads the add-in.
        /// </summary>
        /// <param name="application">Revit UI controlled application.</param>
        /// <returns>Success if add-in loaded, Failed otherwise.</returns>
        public Result OnStartup(UIControlledApplication application)
        {
            Logger.Info("========================================");
            Logger.Info("IB Tools (FoundationProjectionToggle) OnStartup");
            Logger.Info($"Revit version: {application.ControlledApplication.VersionNumber}");
            Logger.Info($"Add-in version: {typeof(App).Assembly.GetName().Version}");
            Logger.Info("========================================");

            try
            {
                // Create services (dependency injection, manual wiring)
                var localization = new LocalizationService();
                var visibility = new CategoryVisibilityService(localization);

                // Create ribbon UI
                _ribbonManager = new RibbonManager(visibility);
                _ribbonManager.CreateRibbon(application);

                // --- IB Tools buttons (one line each) ---
                RibbonManager.AddPushButton<PlaceCoordinatePointCommand>(application, "Coördinaten", "CoordinatePoint", "Modelpunt",
                    "Plaatst een coördinaatpunt op een RD-coördinaat en NAP-hoogte (via de gedeelde coördinaten van het project).",
                    iconName: "coordpoint", availability: typeof(ProjectDocumentAvailability));

                // Set references on command for access during execution
                // This is a simple alternative to full DI container
                ToggleProjectionCommand.VisibilityService = visibility;
                ToggleProjectionCommand.RibbonManager = _ribbonManager; // For immediate button state update

                Logger.Info("Add-in startup completed successfully");
                return Result.Succeeded;
            }
            catch (System.Exception ex)
            {
                Logger.Error("Add-in startup failed", ex);

                // Return failed so Revit knows add-in didn't load
                return Result.Failed;
            }
        }

        /// <summary>
        /// Called when Revit is closing.
        /// </summary>
        /// <param name="application">Revit UI controlled application.</param>
        /// <returns>Always returns Succeeded.</returns>
        public Result OnShutdown(UIControlledApplication application)
        {
            Logger.Info("IB Tools (FoundationProjectionToggle) OnShutdown");

            try
            {
                // Cleanup ribbon manager (unregister events)
                _ribbonManager?.Cleanup(application);

                // Clear static references to prevent memory leaks
                ToggleProjectionCommand.VisibilityService = null;
                ToggleProjectionCommand.RibbonManager = null;

                Logger.Info("Add-in shutdown completed");
            }
            catch (System.Exception ex)
            {
                Logger.Error("Error during shutdown", ex);
                // Don't fail shutdown - Revit is closing anyway
            }

            return Result.Succeeded;
        }
    }
}
