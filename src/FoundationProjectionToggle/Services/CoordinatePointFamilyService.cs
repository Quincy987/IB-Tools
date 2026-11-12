using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace FoundationProjectionToggle.Services
{
    /// <summary>
    /// The coordinate point family (IB_GM_coordinaatpunt): find it in the project, or load it
    /// from the copy embedded in this add-in (never from the network).
    /// </summary>
    public static class CoordinatePointFamilyService
    {
        public const string FamilyName = "IB_GM_coordinaatpunt";

        public static Family FindFamily(Document doc)
        {
            return EmbeddedFamilyLoader.FindFamily(doc, FamilyName);
        }

        /// <summary>
        /// Type names to offer: from the family in the project if loaded, else from the
        /// embedded family file.
        /// </summary>
        public static IList<string> GetTypeNames(Document doc, out bool inProject)
        {
            var family = FindFamily(doc);
            inProject = family != null;
            if (family != null)
            {
                var names = family.GetFamilySymbolIds()
                    .Select(id => doc.GetElement(id) as FamilySymbol)
                    .Where(s => s != null)
                    .Select(s => s.Name)
                    .ToList();
                if (names.Count > 0)
                {
                    return SortByScale(names);
                }
            }
            var fromFile = FamilyPartAtomReader.ReadTypeNames(EmbeddedFamilyLoader.ReadBytes(FamilyName));
            return fromFile.Count > 0 ? fromFile : new List<string> { "Schaal 1op20" };
        }

        /// <summary>
        /// Returns the type <paramref name="typeName"/>, loading the family first when needed.
        /// Must run inside an open transaction. Notes for the report are added to
        /// <paramref name="notes"/>. Returns null when no usable type exists.
        /// </summary>
        public static FamilySymbol GetOrLoadSymbol(Document doc, string typeName, IList<string> notes)
        {
            var family = FindFamily(doc);
            if (family == null)
            {
                family = EmbeddedFamilyLoader.Load(doc, FamilyName);
                notes.Add($"Familie {FamilyName} geladen uit IB Tools.");
            }
            if (family == null)
            {
                return null;
            }

            var symbols = family.GetFamilySymbolIds()
                .Select(id => doc.GetElement(id) as FamilySymbol)
                .Where(s => s != null)
                .ToList();
            var symbol = symbols.FirstOrDefault(s => string.Equals(s.Name, typeName, StringComparison.OrdinalIgnoreCase));
            if (symbol == null && symbols.Count > 0)
            {
                symbol = symbols.FirstOrDefault(s => FamilyPartAtomReader.ScaleOf(s.Name) == 20) ?? symbols[0];
                notes.Add($"Type '{typeName}' bestaat niet in dit project; '{symbol.Name}' gebruikt.");
            }
            if (symbol != null && !symbol.IsActive)
            {
                symbol.Activate();
                doc.Regenerate();
            }
            return symbol;
        }

        private static IList<string> SortByScale(List<string> names)
        {
            return names
                .OrderBy(n => FamilyPartAtomReader.ScaleOf(n) ?? int.MaxValue)
                .ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
