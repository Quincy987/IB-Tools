using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.DB;
using FoundationProjectionToggle.Infrastructure;

namespace FoundationProjectionToggle.Services
{
    /// <summary>
    /// Families shipped inside the add-in (Resources\families\*.rfa, embedded resources):
    /// find them in a project, read their bytes, load them. Never touches the network.
    /// </summary>
    public static class EmbeddedFamilyLoader
    {
        /// <summary>Resource name of Resources\families\&lt;familyName&gt;.rfa (see the csproj LogicalName).</summary>
        public static string ResourceNameOf(string familyName)
        {
            return "FoundationProjectionToggle.Resources.families." + familyName + ".rfa";
        }

        public static Family FindFamily(Document doc, string familyName)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .FirstOrDefault(f => string.Equals(f.Name, familyName, StringComparison.OrdinalIgnoreCase));
        }

        public static byte[] ReadBytes(string familyName)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceNameOf(familyName)))
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

        /// <summary>
        /// Loads the embedded family into <paramref name="doc"/> (inside an open transaction) and
        /// returns it; an existing family of that name is kept as it is.
        /// </summary>
        public static Family Load(Document doc, string familyName)
        {
            var bytes = ReadBytes(familyName);
            if (bytes == null)
            {
                throw new InvalidOperationException($"De familie {familyName} zit niet in de add-in (resource ontbreekt).");
            }

            // Own folder per load, so parallel Revit sessions never share a file; the file name
            // stays the family name because Revit names the family after it.
            var dir = Path.Combine(Path.GetTempPath(), "IBTools", "families", Guid.NewGuid().ToString("N"));
            var path = Path.Combine(dir, familyName + ".rfa");
            try
            {
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(path, bytes);
                Family family;
                if (!doc.LoadFamily(path, new KeepExistingLoadOptions(), out family) || family == null)
                {
                    family = FindFamily(doc, familyName);
                }
                Logger.Info($"Loaded {familyName} from embedded resource: {(family != null ? "ok" : "failed")}");
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
