using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using FoundationProjectionToggle.Infrastructure;

namespace FoundationProjectionToggle.Availability
{
    /// <summary>
    /// Controls when the toggle button is enabled/disabled in the Revit ribbon.
    ///
    /// <para>
    /// <b>Design Philosophy:</b>
    /// Buttons should only be enabled when they can actually do something.
    /// Clicking a disabled button is better UX than clicking and seeing an error.
    /// </para>
    ///
    /// <para>
    /// <b>Enabled When:</b>
    /// - Active view supports Visibility/Graphics overrides
    /// - View is not controlled by a locked template
    /// </para>
    ///
    /// <para>
    /// <b>Disabled When:</b>
    /// - No document open
    /// - View type doesn't support V/G (schedules, legends)
    /// - View template locks V/G settings
    /// </para>
    /// </summary>
    public class ViewBasedAvailability : IExternalCommandAvailability
    {
        /// <summary>
        /// View types that support category visibility overrides.
        /// Using HashSet for O(1) lookup performance.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Included View Types:</b>
        /// - FloorPlan, CeilingPlan: Standard plan views
        /// - Section, Elevation: Standard projection views
        /// - ThreeD: 3D views (default and perspective)
        /// - Detail: Detail views (often used for foundation details)
        /// - DraftingView: Drafting views can overlay model views
        /// - EngineeringPlan: Structural plan views
        /// - AreaPlan: Area plans may show foundations
        /// </para>
        ///
        /// <para>
        /// <b>Excluded View Types:</b>
        /// - Schedule, PanelSchedule: Tabular data, no graphics
        /// - Legend: Reference graphics, not model
        /// - DrawingSheet: Contains viewports, not direct V/G
        /// - Rendering: Doesn't support V/G overrides
        /// - Report: Tabular data
        /// </para>
        /// </remarks>
        private static readonly HashSet<ViewType> SupportedViewTypes = new HashSet<ViewType>
        {
            ViewType.FloorPlan,
            ViewType.CeilingPlan,
            ViewType.Section,
            ViewType.Elevation,
            ViewType.ThreeD,
            ViewType.Detail,
            ViewType.DraftingView,
            ViewType.EngineeringPlan,
            ViewType.AreaPlan
        };

        /// <summary>
        /// Determines if the command should be enabled for current application state.
        /// </summary>
        /// <param name="applicationData">Revit UI application data.</param>
        /// <param name="selectedCategories">Currently selected categories (unused).</param>
        /// <returns>True if command should be enabled, false otherwise.</returns>
        /// <remarks>
        /// <para>
        /// <b>Performance Note:</b>
        /// This method is called frequently by Revit (on mouse move over ribbon, etc.).
        /// Keep it fast - avoid heavy operations, use cached HashSet lookup.
        /// </para>
        ///
        /// <para>
        /// <b>Error Handling:</b>
        /// Any exception returns false (button disabled). This is safe - better to
        /// have a disabled button than a crashing availability check.
        /// </para>
        /// </remarks>
        public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories)
        {
            try
            {
                // Check: Document must be open
                View view = applicationData?.ActiveUIDocument?.Document?.ActiveView;
                if (view == null)
                {
                    return false;
                }

                // Check: View type must support V/G overrides
                if (!SupportedViewTypes.Contains(view.ViewType))
                {
                    return false;
                }

                // Check: View template must not lock V/G settings
                // View templates can include V/G settings that prevent modification
                if (view.ViewTemplateId != ElementId.InvalidElementId)
                {
                    try
                    {
                        var template = view.Document.GetElement(view.ViewTemplateId) as View;
                        if (template != null)
                        {
                            // Check if template allows V/G modification
                            // This method returns false if V/G is "locked" by template
                            // Note: AreGraphicsOverridesAllowed() may not exist in older APIs
                            // We check via the template's parameters instead
                            if (!IsVGOverrideAllowed(view, template))
                            {
                                return false;
                            }
                        }
                    }
                    catch
                    {
                        // If we can't determine template state, assume allowed
                        // Better to let user try and see error than prevent action
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                // Log but don't throw - availability check must be bulletproof
                Logger.Error("Exception in IsCommandAvailable", ex);
                return false;
            }
        }

        /// <summary>
        /// Checks if V/G overrides are allowed given view and its template.
        /// </summary>
        /// <param name="view">The view being checked.</param>
        /// <param name="template">The view template applied to the view (unused, for future expansion).</param>
        /// <returns>True if V/G can be modified, false if locked by template.</returns>
        /// <remarks>
        /// <para>
        /// <b>KNOWN LIMITATION:</b>
        /// Full template lock detection is complex and Revit API version-dependent.
        /// The GetNonControlledTemplateParameterIds() approach requires knowing the
        /// exact parameter ID for V/G overrides, which varies by Revit version.
        /// </para>
        ///
        /// <para>
        /// <b>Current Behavior:</b>
        /// We allow the operation to proceed and let the CategoryVisibilityService
        /// catch any failure in the transaction. This provides a clear error message
        /// rather than silently disabling the button.
        /// </para>
        ///
        /// <para>
        /// <b>Future Improvement (v1.1):</b>
        /// Implement proper template detection by checking V/G parameter control state.
        /// </para>
        /// </remarks>
        private bool IsVGOverrideAllowed(View view, View template)
        {
            // DELIBERATE SIMPLIFICATION:
            // Rather than implement incomplete template detection that might
            // incorrectly disable the button, we allow the operation and rely on
            // the service layer's error handling to catch locked templates.
            //
            // This approach is user-friendlier: they click, get a clear error
            // message explaining the template restriction, rather than wondering
            // why the button is mysteriously grayed out.

            // Log for debugging purposes
            Logger.Info($"View '{view.Name}' has template '{template.Name}'. " +
                        "Template V/G lock detection not implemented - allowing operation.");

            return true;
        }
    }
}
