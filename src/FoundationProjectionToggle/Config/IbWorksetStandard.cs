using System.Collections.Generic;

namespace FoundationProjectionToggle.Config
{
    /// <summary>
    /// The IB standard worksets (replaces the Dynamo script "Worksets" and its Excel tab
    /// "IB_Worksets"). Changing the list = a new release of IB Tools.
    /// </summary>
    public static class IbWorksetStandard
    {
        /// <summary>Workset for scope boxes, reference planes and everything else by default.</summary>
        public const string Default = "01_Scope_boxes_en_reference_planes";

        /// <summary>Workset that holds the levels and grids.</summary>
        public const string LevelsAndGrids = "02_Shared_levels_en_grids";

        /// <summary>All IB worksets, in the order they are reported.</summary>
        public static readonly IReadOnlyList<string> All = new[]
        {
            Default,
            LevelsAndGrids,
            "03_Linked_files",
            "04_Hulpconstructies",
            "05_Projectspecifiek_workset_01",
            "06_Projectspecifiek_workset_02",
        };
    }
}
