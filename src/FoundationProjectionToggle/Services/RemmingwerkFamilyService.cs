using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using FoundationProjectionToggle.Core.Remmingwerk;

namespace FoundationProjectionToggle.Services
{
    /// <summary>
    /// Family types for "Remmingwerk": the two timber families ship with IB Tools (loaded when
    /// missing); the steel types must be in the project and are recognised by the type
    /// parameters NLRS_C_hoogte / NLRS_C_breedte / NLRS_C_diameter, like the Dynamo script.
    /// </summary>
    public static class RemmingwerkFamilyService
    {
        public const string WrijfgordingFamily = "SB_wrijfgording";
        public const string WrijfstijlFamily = "SC_wrijfstijl_generic model";

        public const string ParamHoogte = "NLRS_C_hoogte";
        public const string ParamBreedte = "NLRS_C_breedte";
        public const string ParamDiameter = "NLRS_C_diameter";

        private const string Library = @"K:\ALG\Autodesk\2023\REVIT\Revit bibliotheek";

        /// <summary>Where the steel families are in the IB library (for the "missing" message).</summary>
        public const string StalenGordingSource = Library + @"\Strucural beams\NLRS_28_SF_balken_staal\NLRS_28_SF_LIB_balk HEA_gen_ISR\NLRS_28_SF_LIB_balk HEA_gen_ISR.rfa (type balk HEA450)";
        public const string StalenStijlSource = Library + @"\Structural columns\NLRS_28_SC_Kolommen_staal\NLRS_28_SC_TLB_kolom HEA_gen_ISR\NLRS_28_SC_TLB_kolom HEA_gen_ISR.rfa (type kolom HEA450)";
        public const string BuispaalSource = Library + @"\Structural columns\NLRS_28_SC_Kolommen_staal\NLRS_28_SC_TLB_kolom buis_gen_ISR\NLRS_28_SC_TLB_kolom buis_gen_ISR.rfa (type kolom ø610x12.5)";

        /// <summary>Types of the IB wrijfgording family (or the embedded one if not in the project).</summary>
        public static List<TypeOption> WrijfgordingOptions(Document doc)
        {
            return TimberOptions(doc, WrijfgordingFamily);
        }

        /// <summary>Types of the IB wrijfstijl family (or the embedded one if not in the project).</summary>
        public static List<TypeOption> WrijfstijlOptions(Document doc)
        {
            return TimberOptions(doc, WrijfstijlFamily);
        }

        /// <summary>Structural framing types with NLRS_C_hoogte and NLRS_C_breedte.</summary>
        public static List<TypeOption> StalenGordingOptions(Document doc)
        {
            return SteelOptions(doc, BuiltInCategory.OST_StructuralFraming, o => !double.IsNaN(o.HoogteMm) && !double.IsNaN(o.BreedteMm), "HEA450");
        }

        /// <summary>Structural column types with NLRS_C_hoogte.</summary>
        public static List<TypeOption> StalenStijlOptions(Document doc)
        {
            return SteelOptions(doc, BuiltInCategory.OST_StructuralColumns, o => !double.IsNaN(o.HoogteMm), "HEA450");
        }

        /// <summary>Structural column types with NLRS_C_diameter.</summary>
        public static List<TypeOption> BuispaalOptions(Document doc)
        {
            return SteelOptions(doc, BuiltInCategory.OST_StructuralColumns, o => !double.IsNaN(o.DiameterMm), "610");
        }

        /// <summary>
        /// Index of the option to preselect: the remembered type, else the first whose name
        /// contains one of <paramref name="hints"/> (tried in order), else the first.
        /// </summary>
        public static int ChooseDefault(IList<TypeOption> options, string remembered, params string[] hints)
        {
            for (int i = 0; i < options.Count; i++)
            {
                if (remembered != null && string.Equals(options[i].Label, remembered, StringComparison.OrdinalIgnoreCase)) return i;
            }
            foreach (var hint in hints ?? new string[0])
            {
                for (int i = 0; i < options.Count; i++)
                {
                    if (hint != null && options[i].TypeName.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0) return i;
                }
            }
            return options.Count > 0 ? 0 : -1;
        }

        /// <summary>
        /// The FamilySymbol for <paramref name="option"/>, loading the embedded family when the
        /// option comes from IB Tools. Must run inside an open transaction. Null when it fails.
        /// </summary>
        public static FamilySymbol Resolve(Document doc, TypeOption option, IList<string> notes)
        {
            FamilySymbol symbol = null;
            if (option.Tag is ElementId id)
            {
                symbol = doc.GetElement(id) as FamilySymbol;
            }
            else if (option.FromAddIn)
            {
                var family = EmbeddedFamilyLoader.FindFamily(doc, option.FamilyName) ?? EmbeddedFamilyLoader.Load(doc, option.FamilyName);
                if (family != null)
                {
                    notes.Add($"Familie {option.FamilyName} geladen uit IB Tools.");
                    var symbols = family.GetFamilySymbolIds().Select(i => doc.GetElement(i) as FamilySymbol).Where(s => s != null).ToList();
                    symbol = symbols.FirstOrDefault(s => string.Equals(s.Name, option.TypeName, StringComparison.OrdinalIgnoreCase)) ?? symbols.FirstOrDefault();
                }
            }
            if (symbol != null && !symbol.IsActive)
            {
                symbol.Activate();
                doc.Regenerate();
            }
            return symbol;
        }

        /// <summary>Value in mm of the first type parameter whose name starts with <paramref name="prefix"/> (Dynamo: IndexOf == 0); NaN if none.</summary>
        public static double ReadTypeMm(Element type, string prefix)
        {
            foreach (Parameter p in type.Parameters)
            {
                var name = p.Definition?.Name;
                if (name == null || !name.StartsWith(prefix, StringComparison.Ordinal) || !p.HasValue)
                {
                    continue;
                }
                if (p.StorageType == StorageType.Double)
                {
                    bool isLength = false;
                    try { isLength = p.Definition.GetDataType() == SpecTypeId.Length; } catch { }
                    return isLength ? UnitUtils.ConvertFromInternalUnits(p.AsDouble(), UnitTypeId.Millimeters) : p.AsDouble();
                }
                if (p.StorageType == StorageType.Integer)
                {
                    return p.AsInteger();
                }
            }
            return double.NaN;
        }

        private static List<TypeOption> TimberOptions(Document doc, string familyName)
        {
            var result = new List<TypeOption>();
            var family = EmbeddedFamilyLoader.FindFamily(doc, familyName);
            if (family != null)
            {
                foreach (var sid in family.GetFamilySymbolIds())
                {
                    var s = doc.GetElement(sid) as FamilySymbol;
                    if (s != null)
                    {
                        result.Add(new TypeOption { FamilyName = family.Name, TypeName = s.Name, Tag = s.Id });
                    }
                }
            }
            if (result.Count == 0)
            {
                var names = FamilyPartAtomReader.ReadTypeNames(EmbeddedFamilyLoader.ReadBytes(familyName));
                result.Add(new TypeOption { FamilyName = familyName, TypeName = names.Count > 0 ? names[0] : familyName, FromAddIn = true });
            }
            return result;
        }

        private static List<TypeOption> SteelOptions(Document doc, BuiltInCategory category, Func<TypeOption, bool> usable, string hint)
        {
            var result = new List<TypeOption>();
            foreach (var s in new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).OfCategory(category).Cast<FamilySymbol>())
            {
                var o = new TypeOption
                {
                    FamilyName = s.FamilyName,
                    TypeName = s.Name,
                    Tag = s.Id,
                    HoogteMm = ReadTypeMm(s, ParamHoogte),
                    BreedteMm = ReadTypeMm(s, ParamBreedte),
                    DiameterMm = ReadTypeMm(s, ParamDiameter),
                };
                if (category == BuiltInCategory.OST_StructuralColumns)
                {
                    // Show only what matters for the choice.
                    if (!double.IsNaN(o.DiameterMm)) { o.HoogteMm = double.NaN; o.BreedteMm = double.NaN; }
                }
                if (usable(o))
                {
                    result.Add(o);
                }
            }
            return result
                .OrderBy(o => o.TypeName.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0 ? 0 : 1)
                .ThenBy(o => o.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
