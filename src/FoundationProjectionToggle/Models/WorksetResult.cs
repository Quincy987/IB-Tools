using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FoundationProjectionToggle.Models
{
    /// <summary>
    /// Report of one run of the IB Worksets button: what was created, renamed, already there
    /// or skipped (with the reason).
    /// </summary>
    public class WorksetResult
    {
        /// <summary>Worksharing was switched on by this run.</summary>
        public bool WorksharingEnabled { get; set; }

        /// <summary>Worksets that got their IB name when worksharing was switched on.</summary>
        public List<string> CreatedByEnable { get; } = new List<string>();

        /// <summary>Worksets created by this run.</summary>
        public List<string> Created { get; } = new List<string>();

        /// <summary>Renames done by this run, as "old -> new".</summary>
        public List<string> Renamed { get; } = new List<string>();

        /// <summary>IB worksets that already existed (nothing changed).</summary>
        public List<string> Existing { get; } = new List<string>();

        /// <summary>Things that were not done, each with the reason.</summary>
        public List<string> Skipped { get; } = new List<string>();

        /// <summary>Points for the user to check (nothing was changed for these).</summary>
        public List<string> Notes { get; } = new List<string>();

        public bool HasChanges => WorksharingEnabled || Created.Count > 0 || Renamed.Count > 0;

        public string Summary()
        {
            var parts = new List<string>();
            if (WorksharingEnabled)
            {
                parts.Add("Worksharing aangezet");
            }
            int created = Created.Count + CreatedByEnable.Count;
            parts.Add($"{created} {(created == 1 ? "workset" : "worksets")} gemaakt");
            parts.Add($"{Renamed.Count} hernoemd");
            parts.Add($"{Existing.Count} bestonden al");
            var text = string.Join(", ", parts) + ".";
            if (Skipped.Count > 0)
            {
                text += $" {Skipped.Count} overgeslagen, zie hieronder.";
            }
            if (Notes.Count > 0)
            {
                text += " Zie 'Let op' hieronder.";
            }
            return text;
        }

        public string Details()
        {
            var sb = new StringBuilder();
            Section(sb, "Gemaakt bij aanzetten worksharing", CreatedByEnable);
            Section(sb, "Gemaakt", Created);
            Section(sb, "Hernoemd", Renamed);
            Section(sb, "Bestond al", Existing);
            Section(sb, "Overgeslagen", Skipped);
            Section(sb, "Let op", Notes);
            return sb.ToString().TrimEnd();
        }

        private static void Section(StringBuilder sb, string title, IEnumerable<string> items)
        {
            var list = items.ToList();
            if (list.Count == 0)
            {
                return;
            }
            sb.AppendLine($"{title}:");
            foreach (var item in list)
            {
                sb.AppendLine($"  - {item}");
            }
            sb.AppendLine();
        }
    }
}
