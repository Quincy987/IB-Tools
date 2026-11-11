using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using FoundationProjectionToggle.Infrastructure;

namespace FoundationProjectionToggle.Availability
{
    /// <summary>
    /// Button enabled only when a project document is active: not with no document open,
    /// and not in the family editor. Any active view counts (plans, 3D, schedules, sheets),
    /// commands that need a specific view type check that themselves.
    /// </summary>
    /// <remarks>
    /// Usage: <c>RibbonManager.AddPushButton&lt;MyCommand&gt;(..., availability: typeof(ProjectDocumentAvailability))</c>.
    /// Called very often by Revit, so it only does cheap checks and never throws.
    /// </remarks>
    public class ProjectDocumentAvailability : IExternalCommandAvailability
    {
        public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories)
        {
            try
            {
                Document doc = applicationData?.ActiveUIDocument?.Document;
                return doc != null && !doc.IsFamilyDocument;
            }
            catch (Exception ex)
            {
                Logger.Error("Exception in ProjectDocumentAvailability", ex);
                return false;
            }
        }
    }
}
