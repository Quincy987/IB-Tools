using System;
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
    /// The Hide/Show button of the Projectielijnen tool (panel Weergave). Unlike the other IB
    /// buttons it is stateful: its text and icon follow the projection-line visibility of the
    /// active view, updated on every view activation and right after a toggle.
    /// </summary>
    public class ProjectionToggleButton
    {
        public const string PanelName = "Weergave";
        private const string ButtonName = "ToggleProjection";

        private readonly CategoryVisibilityService _visibilityService;
        private PushButton _button;

        // Cached icons for the toggle states
        private BitmapImage _iconOnLarge;
        private BitmapImage _iconOnSmall;
        private BitmapImage _iconOffLarge;
        private BitmapImage _iconOffSmall;

        public ProjectionToggleButton(CategoryVisibilityService visibilityService)
        {
            _visibilityService = visibilityService ?? throw new ArgumentNullException(nameof(visibilityService));
        }

        /// <summary>
        /// Adds the button to the IB Tools tab and starts following view activations. Throws when
        /// the button or its icons cannot be created (the add-in startup then fails, as before).
        /// </summary>
        public void Create(UIControlledApplication application)
        {
            Logger.Info("Creating ribbon UI");

            try
            {
                RibbonManager.EnsureTab(application);

                LoadToggleIcons();
                _button = RibbonManager.AddPushButton<ToggleProjectionCommand>(
                    application,
                    PanelName,
                    ButtonName,
                    "Hide", // updated per view by UpdateButtonState
                    "Toggle projection line visibility for Structural Foundations",
                    availability: typeof(ViewBasedAvailability),
                    longDescription:
                        "Toggles the visibility of projection lines (<Projection> subcategory) " +
                        "for Structural Foundations in the current view.\n\n" +
                        "This affects only the active view and can be undone with Ctrl+Z.");

                if (_button == null)
                {
                    throw new InvalidOperationException("Failed to create toggle button");
                }
                _button.LargeImage = _iconOnLarge;
                _button.Image = _iconOnSmall;

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
        /// Event handler called when user activates a different view.
        /// </summary>
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
        /// Updates button text and icon based on current visibility state: projection visible →
        /// "Hide" with the eye-open icon; hidden → "Show" with the eye-closed icon. Called on view
        /// activation and by <see cref="ToggleProjectionCommand"/> right after a toggle.
        /// </summary>
        public void UpdateButtonState(Autodesk.Revit.DB.View view)
        {
            if (_button == null)
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
                    _button.ItemText = "Show";
                    _button.LargeImage = _iconOffLarge;
                    _button.Image = _iconOffSmall;
                    _button.ToolTip = "Show projection lines for Structural Foundations";
                }
                else
                {
                    // Projection is visible → offer to hide it
                    _button.ItemText = "Hide";
                    _button.LargeImage = _iconOnLarge;
                    _button.Image = _iconOnSmall;
                    _button.ToolTip = "Hide projection lines for Structural Foundations";
                }

                Logger.Info($"Button state updated: {_button.ItemText}");
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
        /// Unregisters the view-activation handler. Called from App.OnShutdown().
        /// </summary>
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
