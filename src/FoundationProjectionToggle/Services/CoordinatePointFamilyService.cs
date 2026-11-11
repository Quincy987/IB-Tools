using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.DB;
using FoundationProjectionToggle.Infrastructure;

namespace FoundationProjectionToggle.Services
{
    /// <summary>
    /// The coordinate point family (IB_GM_coordinaatpunt): find it in the project, or load it
    /// from the copy embedded in this add-in (never from the network).
    /// </summary>
    public static class CoordinatePointFamilyService
    {
        public const string FamilyName = "IB_GM_coordinaatpunt";
        private const string ResourceName = "FoundationProjectionToggle.Resources.families.IB_GM_coordinaatpunt.rfa";

        public static Family FindFamily(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .FirstOrDefault(f => string.Equals(f.Name, FamilyName, StringComparison.OrdinalIgnoreCase));
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
            var fromFile = FamilyPartAtomReader.ReadTypeNames(ReadEmbeddedRfa());
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
                family = LoadEmbeddedFamily(doc);
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

        private static Family LoadEmbeddedFamily(Document doc)
        {
            var bytes = ReadEmbeddedRfa();
            if (bytes == null)
            {
                throw new InvalidOperationException("De familie zit niet in de add-in (resource ontbreekt).");
            }

            // Own folder per load, so parallel Revit sessions never share a file; the file name
            // stays the family name because Revit names the family after it.
            var dir = Path.Combine(Path.GetTempPath(), "IBTools", "coordpoint", Guid.NewGuid().ToString("N"));
            var path = Path.Combine(dir, FamilyName + ".rfa");
            try
            {
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(path, bytes);
                Family family;
                if (!doc.LoadFamily(path, new KeepExistingLoadOptions(), out family) || family == null)
                {
                    family = FindFamily(doc);
                }
                Logger.Info($"Loaded {FamilyName} from embedded resource: {(family != null ? "ok" : "failed")}");
                return family;
            }
            finally
            {
                try
                {
                    Directory.Delete(dir, true);
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Could not delete temp folder {dir}: {ex.Message}");
                }
            }
        }

        private static byte[] ReadEmbeddedRfa()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName))
            {
                if (stream == null)
                {
                    return null;
                }
                using (var ms = new MemoryStream())
                {
                    stream.CopyTo(ms);
                    return ms.ToArray();
                }
            }
        }

        private static IList<string> SortByScale(List<string> names)
        {
            return names
                .OrderBy(n => FamilyPartAtomReader.ScaleOf(n) ?? int.MaxValue)
                .ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private sealed class KeepExistingLoadOptions : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
            {
                overwriteParameterValues = false;
                return false;
            }

            public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
            {
                source = FamilySource.Project;
                overwriteParameterValues = false;
                return false;
            }
        }
    }
}
