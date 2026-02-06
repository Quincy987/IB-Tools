using System;
using System.IO;
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
    /// Manages the Revit ribbon interface for the add-in.
    ///
    /// <para>
    /// <b>Responsibilities:</b>
    /// - Create ribbon tab and panel
    /// - Create and configure toggle button
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
        // Configuration constants - extracted for maintainability and potential future customization
        // These could be moved to a config file or made settable via constructor for white-labeling
        private const string RibbonPanelName = "Projectielijnen";
        private const string ButtonInternalName = "ToggleProjection";

        private readonly CategoryVisibilityService _visibilityService;
        private PushButton _toggleButton;

        // Cached icons to avoid repeated loading
        private BitmapImage _iconOnLarge;
        private BitmapImage _iconOnSmall;
        private BitmapImage _iconOffLarge;
        private BitmapImage _iconOffSmall;

        /// <summary>
        /// Creates a new RibbonManager instance.
        /// </summary>
        /// <param name="visibilityService">
        /// Service for querying current visibility state.
        /// </param>
        public RibbonManager(CategoryVisibilityService visibilityService)
        {
            _visibilityService = visibilityService ?? throw new ArgumentNullException(nameof(visibilityService));
        }

        /// <summary>
        /// Creates the ribbon UI and registers event handlers.
        /// </summary>
        /// <param name="application">Revit UI controlled application.</param>
        /// <remarks>
        /// <para>
        /// <b>Called From:</b>
        /// App.OnStartup() - once when Revit loads the add-in.
        /// </para>
        ///
        /// <para>
        /// <b>Tab/Panel Strategy:</b>
        /// We add our panel to Revit's built-in "Add-Ins" tab for easy discoverability.
        /// The panel "Projectielijnen" groups projection line visibility commands.
        /// </para>
        /// </remarks>
        public void CreateRibbon(UIControlledApplication application)
        {
            Logger.Info("Creating ribbon UI");

            try
            {
                // Load icons first (fail fast if resources are missing)
                LoadIcons();

                // Create panel on the built-in Add-Ins tab
                // Using single-parameter CreateRibbonPanel places it on Add-Ins tab
                RibbonPanel panel;
                try
                {
                    panel = application.CreateRibbonPanel(RibbonPanelName);
                    Logger.Info($"Created ribbon panel '{RibbonPanelName}' on Add-Ins tab");
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException)
                {
                    // Panel already exists - find it
                    panel = null;
                    foreach (var p in application.GetRibbonPanels())
                    {
                        if (p.Name == RibbonPanelName)
                        {
                            panel = p;
                            break;
                        }
                    }
                    if (panel == null)
                    {
                        throw new InvalidOperationException($"Could not find or create panel: {RibbonPanelName}");
                    }
                    Logger.Info($"Found existing ribbon panel: {RibbonPanelName}");
                }

                // Create button (using class constant for internal name)
                var assemblyPath = Assembly.GetExecutingAssembly().Location;
                var buttonData = new PushButtonData(
                    name: ButtonInternalName,
                    text: "Hide", // Default text, will be updated
                    assemblyName: assemblyPath,
                    className: typeof(ToggleProjectionCommand).FullName
                );

                // Set availability controller
                buttonData.AvailabilityClassName = typeof(ViewBasedAvailability).FullName;

                // Set icons (default to "on" state)
                buttonData.LargeImage = _iconOnLarge;
                buttonData.Image = _iconOnSmall;

                // Set tooltip
                buttonData.ToolTip = "Toggle projection line visibility for Structural Foundations";
                buttonData.LongDescription =
                    "Toggles the visibility of projection lines (<Projection> subcategory) " +
                    "for Structural Foundations in the current view.\n\n" +
                    "This affects only the active view and can be undone with Ctrl+Z.";

                // Add button to panel
                _toggleButton = panel.AddItem(buttonData) as PushButton;

                if (_toggleButton == null)
                {
                    throw new InvalidOperationException("Failed to create toggle button");
                }

                Logger.Info("Toggle button created successfully");

                // Register for view activation events to update button state
                application.ViewActivated += OnViewActivated;
                Logger.Info("Registered ViewActivated event handler");
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to create ribbon UI", ex);
                throw; // Re-throw to fail add-in startup
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

        /// <summary>
        /// Loads button icons from embedded resources.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown if required icon resources are not found.
        /// </exception>
        private void LoadIcons()
        {
            Logger.Info("Loading button icons");

            _iconOnLarge = LoadIcon("projection-on-32.png");
            _iconOnSmall = LoadIcon("projection-on-16.png");
            _iconOffLarge = LoadIcon("projection-off-32.png");
            _iconOffSmall = LoadIcon("projection-off-16.png");

            // Validate all icons loaded
            if (_iconOnLarge == null || _iconOnSmall == null ||
                _iconOffLarge == null || _iconOffSmall == null)
            {
                throw new InvalidOperationException(
                    "Failed to load one or more button icons. " +
                    "Ensure icon files are embedded resources."
                );
            }

            Logger.Info("Button icons loaded successfully");
        }

        /// <summary>
        /// Loads a single icon from embedded resources.
        /// </summary>
        /// <param name="fileName">The icon filename (e.g., "projection-on-32.png").</param>
        /// <returns>BitmapImage for use in Revit ribbon, or null if not found.</returns>
        private BitmapImage LoadIcon(string fileName)
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();

                // Resource name format: Namespace.Folder.Folder.Filename
                // Our structure: FoundationProjectionToggle.UI.Resources.icons.filename.png
                var resourceName = $"FoundationProjectionToggle.UI.Resources.icons.{fileName}";

                using (var stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        Logger.Warn($"Icon resource not found: {resourceName}");

                        // Log available resources for debugging
                        var available = assembly.GetManifestResourceNames();
                        Logger.Info($"Available resources: {string.Join(", ", available)}");

                        return null;
                    }

                    // Load into BitmapImage
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.StreamSource = stream;
                    image.CacheOption = BitmapCacheOption.OnLoad; // Load immediately, close stream
                    image.EndInit();
                    image.Freeze(); // Make thread-safe

                    Logger.Info($"Loaded icon: {fileName}");
                    return image;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to load icon: {fileName}", ex);
                return null;
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
