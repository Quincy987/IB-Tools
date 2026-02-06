using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using FoundationProjectionToggle.Infrastructure;
using FoundationProjectionToggle.Models;

namespace FoundationProjectionToggle.Services
{
    /// <summary>
    /// Service for toggling category visibility in Revit views.
    ///
    /// <para>
    /// <b>Core Responsibility:</b>
    /// Encapsulates all Revit API calls for modifying and querying category visibility.
    /// The presentation layer (commands, UI) should not directly call Revit APIs.
    /// </para>
    ///
    /// <para>
    /// <b>Transaction Management:</b>
    /// All modifications are wrapped in named transactions for undo/redo support.
    /// Transaction names appear in Edit menu: "Undo Toggle Projection Visibility"
    /// </para>
    ///
    /// <para>
    /// <b>Error Philosophy:</b>
    /// Never throw exceptions up to caller. Always return ToggleResult with
    /// appropriate success/failure state and user-friendly message.
    /// </para>
    /// </summary>
    public class CategoryVisibilityService
    {
        private readonly LocalizationService _localization;

        /// <summary>
        /// Creates a new CategoryVisibilityService instance.
        /// </summary>
        /// <param name="localization">
        /// Service for finding localized subcategory names.
        /// </param>
        public CategoryVisibilityService(LocalizationService localization)
        {
            _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        }

        /// <summary>
        /// Result of attempting to find the projection subcategory.
        /// </summary>
        private class CategoryLookupResult
        {
            public bool Success { get; set; }
            public Category ProjectionCategory { get; set; }
            public string ErrorMessage { get; set; }
        }

        /// <summary>
        /// Finds the Projection subcategory for Structural Foundations.
        /// Extracted to avoid code duplication between toggle and query methods.
        /// </summary>
        private CategoryLookupResult FindProjectionCategory(Document doc)
        {
            Category structFoundationCat;
            try
            {
                structFoundationCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_StructuralFoundation);
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to get Structural Foundation category", ex);
                return new CategoryLookupResult
                {
                    Success = false,
                    ErrorMessage = "Structural Foundations category not found. Is this a structural project?"
                };
            }

            if (structFoundationCat == null)
            {
                return new CategoryLookupResult
                {
                    Success = false,
                    ErrorMessage = "Structural Foundations category not found in this project."
                };
            }

            var projectionCat = _localization.FindSubcategory(structFoundationCat, "Projection");
            if (projectionCat == null)
            {
                Logger.Error(
                    $"Projection subcategory not found. " +
                    $"Parent: {structFoundationCat.Name}, " +
                    $"Subcategory count: {structFoundationCat.SubCategories?.Size ?? 0}"
                );
                return new CategoryLookupResult
                {
                    Success = false,
                    ErrorMessage = "Projection subcategory not found. " +
                                   "This may be a Revit language we don't support yet. " +
                                   "Please report this issue with your Revit language setting."
                };
            }

            return new CategoryLookupResult
            {
                Success = true,
                ProjectionCategory = projectionCat
            };
        }

        /// <summary>
        /// Toggles the visibility of Structural Foundation projection lines in the active view.
        /// </summary>
        /// <param name="uiApp">The Revit UIApplication instance.</param>
        /// <returns>
        /// A ToggleResult indicating success or failure with appropriate details.
        /// </returns>
        /// <remarks>
        /// <para>
        /// <b>Preconditions Checked:</b>
        /// - Document must be open
        /// - View must be active
        /// - Structural Foundations category must exist in project
        /// - Projection subcategory must be found
        /// </para>
        ///
        /// <para>
        /// <b>Postconditions on Success:</b>
        /// - Projection visibility is toggled (was hidden → now visible, or vice versa)
        /// - Change is recorded in undo stack
        /// - Result contains previous and new visibility states
        /// </para>
        /// </remarks>
        public ToggleResult ToggleProjectionLines(UIApplication uiApp)
        {
            Logger.Info("ToggleProjectionLines called");

            // Validate: Document must be open
            Document doc = uiApp?.ActiveUIDocument?.Document;
            if (doc == null)
            {
                Logger.Warn("No document open");
                return ToggleResult.Fail("No document open. Please open a project first.");
            }

            // Validate: Must have active view
            View view = doc.ActiveView;
            if (view == null)
            {
                Logger.Warn("No active view");
                return ToggleResult.Fail("No active view.");
            }

            Logger.Info($"Active view: {view.Name} (Type: {view.ViewType})");

            // Find Projection subcategory (uses extracted helper method for DRY)
            var lookup = FindProjectionCategory(doc);
            if (!lookup.Success)
            {
                return ToggleResult.Fail(lookup.ErrorMessage);
            }
            var projectionCat = lookup.ProjectionCategory;

            Logger.Info($"Found projection subcategory: {projectionCat.Name}");

            // Get current visibility state
            // Note: GetCategoryHidden returns true if HIDDEN, false if visible
            // We want WasVisible, so we invert
            bool wasHidden = view.GetCategoryHidden(projectionCat.Id);
            bool wasVisible = !wasHidden;

            Logger.Info($"Current state: {(wasVisible ? "visible" : "hidden")}");

            // Toggle visibility within a transaction
            // Transaction name appears in Edit > Undo menu
            using (Transaction trans = new Transaction(doc, "Toggle Projection Visibility"))
            {
                try
                {
                    trans.Start();

                    // Toggle: if was hidden, make visible (not hidden), and vice versa
                    view.SetCategoryHidden(projectionCat.Id, !wasHidden);

                    trans.Commit();

                    bool isNowVisible = wasHidden; // If was hidden, now visible
                    Logger.Info($"Toggle complete. New state: {(isNowVisible ? "visible" : "hidden")}");

                    return ToggleResult.Succeed(
                        wasVisible: wasVisible,
                        isNowVisible: isNowVisible,
                        viewName: view.Name
                    );
                }
                catch (Autodesk.Revit.Exceptions.InvalidOperationException ex)
                {
                    // This can happen if:
                    // - View template locks V/G overrides
                    // - View is in a state that doesn't allow modification
                    Logger.Error("InvalidOperationException during toggle", ex);

                    if (trans.HasStarted())
                        trans.RollBack();

                    return ToggleResult.Fail(
                        "Could not modify visibility. " +
                        "The view may be controlled by a template that locks Visibility/Graphics settings.",
                        ex
                    );
                }
                catch (Exception ex)
                {
                    // Catch-all for unexpected errors
                    Logger.Error("Unexpected exception during toggle", ex);

                    if (trans.HasStarted())
                        trans.RollBack();

                    return ToggleResult.Fail(
                        "An unexpected error occurred while modifying visibility. " +
                        "Please try again or report this issue.",
                        ex
                    );
                }
            }
        }

        /// <summary>
        /// Queries whether projection lines are currently hidden in the specified view.
        /// </summary>
        /// <param name="view">The view to check.</param>
        /// <returns>
        /// True if projection lines are hidden, false if visible.
        /// Returns false if view is null or category cannot be found (safe default).
        /// </returns>
        /// <remarks>
        /// <para>
        /// <b>Usage:</b>
        /// Called by RibbonManager to update button state on view activation.
        /// This is a query-only method - no modifications, no transaction needed.
        /// </para>
        ///
        /// <para>
        /// <b>Default Behavior:</b>
        /// Returns false (visible) if anything goes wrong. This is the "safe" default
        /// because showing the "Hide Projection" button is less confusing than
        /// showing "Show Projection" when the category doesn't exist.
        /// </para>
        /// </remarks>
        public bool IsProjectionHidden(View view)
        {
            if (view == null)
            {
                Logger.Warn("IsProjectionHidden called with null view");
                return false;
            }

            try
            {
                // Use shared helper method (DRY principle)
                var lookup = FindProjectionCategory(view.Document);
                if (!lookup.Success)
                {
                    Logger.Warn($"IsProjectionHidden: {lookup.ErrorMessage}");
                    return false;
                }

                return view.GetCategoryHidden(lookup.ProjectionCategory.Id);
            }
            catch (Exception ex)
            {
                Logger.Error("Exception in IsProjectionHidden", ex);
                return false;
            }
        }
    }
}
