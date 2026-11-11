using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using FoundationProjectionToggle.Config;
using FoundationProjectionToggle.Infrastructure;
using FoundationProjectionToggle.Models;

namespace FoundationProjectionToggle.Services
{
    /// <summary>
    /// Creates the IB standard worksets (<see cref="IbWorksetStandard"/>).
    /// </summary>
    /// <remarks>
    /// Rules (plan q-1843):
    /// - Not workshared: <see cref="EnableWorksharing"/> switches it on with the IB names for
    ///   the two default worksets (levels/grids -> 02, the rest -> 01). Must run outside a transaction.
    /// - Workshared with exactly 2 user worksets: rename them. Which one holds the levels and
    ///   grids is read from a level (or grid), not from its name, so it works in any Revit language.
    ///   A workset owned by someone else is not renamed. With more than 2 worksets nothing is renamed.
    /// - Then create only the missing IB worksets. Renames + creates are one transaction (one Ctrl+Z).
    /// </remarks>
    public class WorksetService
    {
        private static readonly StringComparer NameComparer = StringComparer.OrdinalIgnoreCase;

        /// <summary>
        /// Why the button cannot work on this document, or null if it can.
        /// </summary>
        public string GetBlockingReason(Document doc)
        {
            if (doc == null)
            {
                return "Er is geen project geopend.";
            }
            if (doc.IsFamilyDocument)
            {
                return "Dit werkt alleen in een project, niet in de familie-editor.";
            }
            if (doc.IsReadOnly)
            {
                return "Het model is alleen-lezen.";
            }
            if (doc.IsModelInCloud)
            {
                return "Cloudmodellen worden (nog) niet ondersteund.";
            }
            if (!doc.IsWorkshared && !doc.CanEnableWorksharing())
            {
                return "Revit staat worksharing voor dit model niet toe.";
            }
            return null;
        }

        /// <summary>
        /// Switches worksharing on with the IB names for the two default worksets.
        /// Cannot be undone and clears the undo list. Must be called outside a transaction.
        /// </summary>
        public void EnableWorksharing(Document doc, WorksetResult result)
        {
            Logger.Info($"EnableWorksharing('{IbWorksetStandard.LevelsAndGrids}', '{IbWorksetStandard.Default}')");
            doc.EnableWorksharing(IbWorksetStandard.LevelsAndGrids, IbWorksetStandard.Default);
            result.WorksharingEnabled = true;
            result.CreatedByEnable.Add(IbWorksetStandard.Default);
            result.CreatedByEnable.Add(IbWorksetStandard.LevelsAndGrids);
        }

        /// <summary>
        /// Renames the two default worksets (if applicable) and creates the missing IB worksets,
        /// in one transaction. The document must be workshared.
        /// </summary>
        public void ApplyStandard(Document doc, WorksetResult result)
        {
            if (!doc.IsWorkshared)
            {
                throw new InvalidOperationException("Model is not workshared.");
            }

            using (var t = new Transaction(doc, "IB worksets"))
            {
                t.Start();

                var renamedTo = RenameDefaultWorksets(doc, result);
                CreateMissingWorksets(doc, result, renamedTo);

                if (result.Created.Count > 0 || result.Renamed.Count > 0)
                {
                    t.Commit();
                    Logger.Info($"IB worksets committed: {result.Created.Count} created, {result.Renamed.Count} renamed");
                }
                else
                {
                    t.RollBack();
                    Logger.Info("IB worksets: nothing to change");
                }
            }
        }

        private static List<Workset> GetUserWorksets(Document doc)
        {
            return new FilteredWorksetCollector(doc)
                .OfKind(WorksetKind.UserWorkset)
                .ToWorksets()
                .ToList();
        }

        /// <returns>The IB names that were given to existing worksets by renaming.</returns>
        private static HashSet<string> RenameDefaultWorksets(Document doc, WorksetResult result)
        {
            var renamedTo = new HashSet<string>(NameComparer);
            var worksets = GetUserWorksets(doc);
            if (worksets.Count != 2)
            {
                Logger.Info($"{worksets.Count} user worksets: no renaming");
                NoteLevelsWorkset(doc, worksets, result);
                return renamedTo;
            }

            // Names already right (e.g. just created by EnableWorksharing): nothing to rename.
            if (worksets.All(ws => IbWorksetStandard.All.Contains(ws.Name, NameComparer)))
            {
                return renamedTo;
            }

            WorksetId levelWorksetId = FindLevelsAndGridsWorkset(doc);
            Workset levels = worksets.FirstOrDefault(ws => ws.Id == levelWorksetId);
            if (levels == null)
            {
                result.Skipped.Add(
                    "Hernoemen: niet te bepalen welke workset de levels en grids bevat (geen level of grid gevonden). " +
                    "De bestaande worksets zijn niet hernoemd.");
                return renamedTo;
            }
            Workset other = worksets.First(ws => ws.Id != levels.Id);

            if (TryRename(doc, levels, IbWorksetStandard.LevelsAndGrids, result))
            {
                renamedTo.Add(IbWorksetStandard.LevelsAndGrids);
            }
            if (TryRename(doc, other, IbWorksetStandard.Default, result))
            {
                renamedTo.Add(IbWorksetStandard.Default);
            }
            return renamedTo;
        }

        /// <summary>
        /// More than 2 worksets (nothing renamed): tell the user if the levels and grids are not on
        /// the IB workset, so they can move them themselves.
        /// </summary>
        private static void NoteLevelsWorkset(Document doc, List<Workset> worksets, WorksetResult result)
        {
            WorksetId levelWorksetId = FindLevelsAndGridsWorkset(doc);
            Workset levels = worksets.FirstOrDefault(ws => ws.Id == levelWorksetId);
            if (levels != null && !NameComparer.Equals(levels.Name, IbWorksetStandard.LevelsAndGrids))
            {
                result.Notes.Add(
                    $"Het model had al {worksets.Count} worksets, daarom is niets hernoemd. " +
                    $"De levels en grids staan op '{levels.Name}', niet op '{IbWorksetStandard.LevelsAndGrids}'.");
            }
        }

        /// <summary>Workset of the first level, else of the first grid; null if the model has neither.</summary>
        private static WorksetId FindLevelsAndGridsWorkset(Document doc)
        {
            Element element = new FilteredElementCollector(doc).OfClass(typeof(Level)).FirstElement()
                              ?? new FilteredElementCollector(doc).OfClass(typeof(Grid)).FirstElement();
            return element?.WorksetId;
        }

        /// <returns>True if the workset was renamed.</returns>
        private static bool TryRename(Document doc, Workset workset, string newName, WorksetResult result)
        {
            string oldName = workset.Name;
            if (NameComparer.Equals(oldName, newName))
            {
                return false; // already right; reported as existing by CreateMissingWorksets
            }

            string owner = workset.Owner;
            string me = doc.Application.Username;
            if (!string.IsNullOrEmpty(owner) && !NameComparer.Equals(owner, me))
            {
                result.Skipped.Add($"Hernoemen '{oldName}' -> '{newName}': workset is uitgecheckt door {owner}.");
                return false;
            }

            if (!WorksetTable.IsWorksetNameUnique(doc, newName))
            {
                result.Skipped.Add($"Hernoemen '{oldName}' -> '{newName}': de naam bestaat al.");
                return false;
            }

            try
            {
                WorksetTable.RenameWorkset(doc, workset.Id, newName);
                result.Renamed.Add($"{oldName} -> {newName}");
                Logger.Info($"Renamed workset '{oldName}' -> '{newName}'");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Rename '{oldName}' -> '{newName}' failed", ex);
                result.Skipped.Add($"Hernoemen '{oldName}' -> '{newName}': {ex.Message}");
                return false;
            }
        }

        private static void CreateMissingWorksets(Document doc, WorksetResult result, ISet<string> renamedTo)
        {
            var existing = new HashSet<string>(GetUserWorksets(doc).Select(ws => ws.Name), NameComparer);
            var seen = new HashSet<string>(NameComparer);

            foreach (string raw in IbWorksetStandard.All)
            {
                string name = raw?.Trim();
                if (string.IsNullOrEmpty(name) || !seen.Add(name))
                {
                    continue; // empty or duplicate entry
                }

                if (existing.Contains(name))
                {
                    // Worksets just named by EnableWorksharing or by renaming are reported there.
                    if (!result.CreatedByEnable.Contains(name, NameComparer) && !renamedTo.Contains(name))
                    {
                        result.Existing.Add(name);
                    }
                    continue;
                }

                if (!WorksetTable.IsWorksetNameUnique(doc, name))
                {
                    // Not a user workset, but the name is taken (e.g. by a view or family workset).
                    result.Skipped.Add($"'{name}': de naam is al in gebruik.");
                    continue;
                }

                try
                {
                    Workset.Create(doc, name);
                    result.Created.Add(name);
                    Logger.Info($"Created workset '{name}'");
                }
                catch (Exception ex)
                {
                    Logger.Error($"Create workset '{name}' failed", ex);
                    result.Skipped.Add($"'{name}' maken: {ex.Message}");
                }
            }
        }
    }
}
