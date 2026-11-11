using System;
using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using FoundationProjectionToggle.Availability;
using FoundationProjectionToggle.Commands;
using FoundationProjectionToggle.Infrastructure;
using FoundationProjectionToggle.Services;

namespace FoundationProjectionToggle.UI
{
    /// <summary>
    /// Manages the "IB Tools" ribbon tab.
    ///
    /// <para>
    /// <b>Responsibilities:</b>
    /// - Create the IB Tools tab and its panels (reuse them if they already exist)
    /// - Shared helper <see cref="AddPushButton{TCommand}"/> for all IB buttons
    /// - Create and configure the projection toggle button
    /// - Update button state when active view changes
    /// - Load and manage button icons
    /// </para>
    ///
    /// <para>
    /// <b>Button State Management:</b>
    /// The button dynamically updates its text and icon based on the current
    /// visibility state of projection lines in the active view. This provides
    /// immediate visual feedback without requiring the user to click.
    /// </para>
    /// </summary>
    public class RibbonManager
    {
        /// <summary>Name of the shared ribbon tab for all IB buttons.</summary>
        public const string TabName = "IB Tools";

        // Panel names. Add a constant here when a new panel is introduced.
        public const string PanelWeergave = "Weergave";

        private const string ToggleButtonName = "ToggleProjection";

        private readonly CategoryVisibilityService _visibilityService;
        private PushButton _toggleButton;

        // Cached icons for the toggle states
        private BitmapImage _iconOnLarge;
        private BitmapImage _iconOnSmall;
        private BitmapImage _iconOffLarge;
        private BitmapImage _iconOffSmall;

        public RibbonManager(CategoryVisibilityService visibilityService)
        {
            _visibilityService = visibilityService ?? throw new ArgumentNullException(nameof(visibilityService));
        }

        /// <summary>
        /// Creates the "IB Tools" tab with all panels and buttons. Called once from App.OnStartup.
        /// </summary>
        /// <remarks>
        /// New buttons: add ONE line in the "IB Tools buttons" block below, e.g.
        /// <code>
        /// AddPushButton&lt;CreateIbWorksetsCommand&gt;(application, "Model", "IbWorksets",
        ///     "IB-\nworksets", "Maakt de IB-standaard worksets aan.",
        ///     iconName: "worksets", availability: typeof(ProjectDocumentAvailability));
        /// </code>
        /// </remarks>
        public void CreateRibbon(UIControlledApplication application)
        {
            Logger.Info("Creating ribbon UI");

            try
            {
                EnsureTab(application);

                // --- Weergave: projectielijnen toggle (stateful: text/icon follow the active view) ---
                LoadToggleIcons();
                _toggleButton = AddPushButton<ToggleProjectionCommand>(
                    application,
                    PanelWeergave,
                    ToggleButtonName,
                    "Hide", // updated per view by UpdateButtonState
                    "Toggle projection line visibility for Structural Foundations",
                    availability: typeof(ViewBasedAvailability),
                    longDescription:
                        "Toggles the visibility of projection lines (<Projection> subcategory) " +
                        "for Structural Foundations in the current view.\n\n" +
                        "This affects only the active view and can be undone with Ctrl+Z.");

                if (_toggleButton == null)
                {
                    throw new InvalidOperationException("Failed to create toggle button");
                }
                _toggleButton.LargeImage = _iconOnLarge;
                _toggleButton.Image = _iconOnSmall;

                // --- IB Tools buttons: one AddPushButton line per button (see remarks) ---

                application.ViewActivated += OnViewActivated;
                Logger.Info("Ribbon created; ViewActivated handler registered");
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to create ribbon UI", ex);
                throw; // fail add-in startup
            }
        }

        /// <summary>
        /// Creates the "IB Tools" tab, or reuses it if it already exists (e.g. created by
        /// another IB add-in or a second load of this one).
        /// </summary>
        public static void EnsureTab(UIControlledApplication application)
        {
            try
            {
                application.CreateRibbonTab(TabName);
                Logger.Info($"Created ribbon tab '{TabName}'");
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                Logger.Info($"Ribbon tab '{TabName}' already exists, reusing it");
            }
        }

        /// <summary>
        /// Returns the panel <paramref name="panelName"/> on the IB Tools tab, creating it
        /// (and the tab) if needed. Panels appear in the order they are first requested.
        /// </summary>
        public static RibbonPanel GetOrCreatePanel(UIControlledApplication application, string panelName)
        {
            EnsureTab(application);

            foreach (var existing in application.GetRibbonPanels(TabName))
            {
                if (existing.Name == panelName)
                {
                    return existing;
                }
            }

            var panel = application.CreateRibbonPanel(TabName, panelName);
            Logger.Info($"Created ribbon panel '{panelName}' on tab '{TabName}'");
            return panel;
        }

        /// <summary>
        /// Adds a push button for command <typeparamref name="TCommand"/> to a named panel on
        /// the IB Tools tab. Never throws: a failing button is logged and skipped (returns null),
        /// so one broken button cannot take down the whole tab.
        /// </summary>
        /// <param name="application">Revit UI application (from OnStartup).</param>
        /// <param name="panelName">Panel on the IB Tools tab, created if missing.</param>
        /// <param name="internalName">Unique, stable id (Revit uses it for shortcuts); no spaces.</param>
        /// <param name="text">Button label; use "\n" for a two-line label.</param>
        /// <param name="toolTip">Short tooltip.</param>
        /// <param name="iconName">
        /// Base name of embedded icons: loads "{iconName}-32.png" and "{iconName}-16.png" from
        /// UI\Resources\icons. Null or missing files: button without icon.
        /// </param>
        /// <param name="availability">
        /// Optional IExternalCommandAvailability type, e.g. typeof(ProjectDocumentAvailability).
        /// </param>
        /// <param name="longDescription">Optional extended tooltip.</param>
        public static PushButton AddPushButton<TCommand>(
            UIControlledApplication application,
            string panelName,
            string internalName,
            string text,
            string toolTip,
            string iconName = null,
            Type availability = null,
            string longDescription = null)
            where TCommand : IExternalCommand
        {
            try
            {
                if (availability != null && !typeof(IExternalCommandAvailability).IsAssignableFrom(availability))
                {
                    throw new ArgumentException(
                        $"{availability.FullName} does not implement IExternalCommandAvailability",
                        nameof(availability));
                }

                var panel = GetOrCreatePanel(application, panelName);

                var data = new PushButtonData(
                    internalName,
                    text,
                    Assembly.GetExecutingAssembly().Location,
                    typeof(TCommand).FullName)
                {
                    ToolTip = toolTip
                };

                if (!string.IsNullOrEmpty(longDescription))
                {
                    data.LongDescription = longDescription;
                }
                if (availability != null)
                {
                    data.AvailabilityClassName = availability.FullName;
                }
                if (!string.IsNullOrEmpty(iconName))
                {
                    data.LargeImage = IconLoader.Load(iconName + "-32.png");
                    data.Image = IconLoader.Load(iconName + "-16.png");
                }

                var button = panel.AddItem(data) as PushButton;
                Logger.Info($"Added button '{internalName}' to panel '{panelName}'");
                return button;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to add button '{internalName}' to panel '{panelName}'", ex);
                return null;
            }
        }

        /// <summary>
        /// Event handler called when user activates a different view.
        /// </summary>
        /// <param name="sender">Event sender.</param>
        /// <param name="e">Event arguments containing view information.</param>
        private void OnViewActivated(object sender, ViewActivatedEventArgs e)
        {
            try
            {
                UpdateButtonState(e.CurrentActiveView);
            }
            catch (Exception ex)
            {
                // Don't let event handler crash Revit
                Logger.Error("Exception in OnViewActivated", ex);
            }
        }

        /// <summary>
        /// Updates button text and icon based on current visibility state.
        /// </summary>
        /// <param name="view">The currently active view.</param>
        /// <remarks>
        /// <para>
        /// <b>Button States:</b>
        /// - Visible (not hidden): Button shows "Hide Projectielijnen" with eye-open icon
        /// - Hidden: Button shows "Show Projectielijnen" with eye-closed icon
        /// </para>
        ///
        /// <para>
        /// <b>Called From:</b>
        /// - OnViewActivated: When user switches views
        /// - Could also be called after toggle completes (for immediate update)
        /// </para>
        /// </remarks>
        public void UpdateButtonState(Autodesk.Revit.DB.View view)
        {
            if (_toggleButton == null)
            {
                Logger.Warn("UpdateButtonState called but button is null");
                return;
            }

            if (view == null)
            {
                Logger.Warn("UpdateButtonState called with null view");
                return;
            }

            try
            {
                bool isHidden = _visibilityService.IsProjectionHidden(view);

                if (isHidden)
                {
                    // Projection is hidden → offer to show it
                    _toggleButton.ItemText = "Show";
                    _toggleButton.LargeImage = _iconOffLarge;
                    _toggleButton.Image = _iconOffSmall;
                    _toggleButton.ToolTip = "Show projection lines for Structural Foundations";
                }
                else
                {
                    // Projection is visible → offer to hide it
                    _toggleButton.ItemText = "Hide";
                    _toggleButton.LargeImage = _iconOnLarge;
                    _toggleButton.Image = _iconOnSmall;
                    _toggleButton.ToolTip = "Hide projection lines for Structural Foundations";
                }

                Logger.Info($"Button state updated: {_toggleButton.ItemText}");
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to update button state", ex);
                // Leave button in current state rather than crash
            }
        }

        private void LoadToggleIcons()
        {
            _iconOnLarge = IconLoader.Load("projection-on-32.png");
            _iconOnSmall = IconLoader.Load("projection-on-16.png");
            _iconOffLarge = IconLoader.Load("projection-off-32.png");
            _iconOffSmall = IconLoader.Load("projection-off-16.png");

            if (_iconOnLarge == null || _iconOnSmall == null ||
                _iconOffLarge == null || _iconOffSmall == null)
            {
                throw new InvalidOperationException(
                    "Failed to load one or more toggle icons. Ensure icon files are embedded resources.");
            }
        }

        /// <summary>
        /// Cleans up event handlers and resources.
        /// </summary>
        /// <param name="application">Revit UI controlled application.</param>
        /// <remarks>
        /// Called from App.OnShutdown() to prevent memory leaks.
        /// </remarks>
        public void Cleanup(UIControlledApplication application)
        {
            try
            {
                application.ViewActivated -= OnViewActivated;
                Logger.Info("Unregistered ViewActivated event handler");
            }
            catch (Exception ex)
            {
                Logger.Error("Error during cleanup", ex);
            }
        }
    }
}
