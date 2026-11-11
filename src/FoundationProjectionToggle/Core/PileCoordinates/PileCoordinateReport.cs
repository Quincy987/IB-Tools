using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FoundationProjectionToggle.Core.PileCoordinates
{
    public enum PileStatus
    {
        Written,
        Unchanged,
        Skipped
    }

    /// <summary>What happened to one pile.</summary>
    public sealed class PileOutcome
    {
        public long ElementId { get; set; }

        /// <summary>"Family: Type" (or element name).</summary>
        public string Label { get; set; }

        public PileStatus Status { get; set; }

        /// <summary>Why the pile was skipped (Skipped only).</summary>
        public string Reason { get; set; }

        public CoordinateChange X { get; set; }
        public CoordinateChange Y { get; set; }

        public static PileOutcome Skip(long id, string label, string reason)
        {
            return new PileOutcome { ElementId = id, Label = label, Status = PileStatus.Skipped, Reason = reason };
        }
    }

    /// <summary>
    /// Result model of one run of "Paalcoördinaten", plus the texts for the result dialog.
    /// </summary>
    public sealed class PileCoordinateReport
    {
        public List<PileOutcome> Outcomes { get; } = new List<PileOutcome>();

        /// <summary>General remarks shown above the per-pile list (scope, self-test, types...).</summary>
        public List<string> Notes { get; } = new List<string>();

        /// <summary>Warnings (model not on coordinates, outside RD...).</summary>
        public List<string> Warnings { get; } = new List<string>();

        /// <summary>"selectie" or "hele model".</summary>
        public string Scope { get; set; }

        /// <summary>Fatal error: nothing was written.</summary>
        public string Error { get; set; }

        public int Written => Outcomes.Count(o => o.Status == PileStatus.Written);
        public int Unchanged => Outcomes.Count(o => o.Status == PileStatus.Unchanged);
        public int Skipped => Outcomes.Count(o => o.Status == PileStatus.Skipped);

        public string BuildSummary()
        {
            if (!string.IsNullOrEmpty(Error))
            {
                return "Niets geschreven. " + Error;
            }

            if (Outcomes.Count == 0)
            {
                return $"Geen palen gevonden ({Scope}) met de parameters {PileCoordinateParameters.XName} en {PileCoordinateParameters.YName}.";
            }

            var sb = new StringBuilder();
            if (Written == 0 && Skipped == 0)
            {
                sb.Append($"Alles ongewijzigd: {Unchanged} {Palen(Unchanged)} ({Scope}).");
            }
            else
            {
                sb.Append($"{Written} {Palen(Written)} geschreven, {Unchanged} ongewijzigd, {Skipped} overgeslagen ({Scope}).");
            }
            if (Warnings.Count > 0)
            {
                sb.Append(" Let op: ").Append(Warnings.Count == 1 ? "1 waarschuwing" : $"{Warnings.Count} waarschuwingen").Append('.');
            }
            return sb.ToString();
        }

        public string BuildDetails()
        {
            var sb = new StringBuilder();
            foreach (var w in Warnings)
            {
                sb.AppendLine("LET OP: " + w);
            }
            if (Warnings.Count > 0)
            {
                sb.AppendLine();
            }
            foreach (var n in Notes)
            {
                sb.AppendLine(n);
            }

            AppendSection(sb, "Geschreven", PileStatus.Written,
                o => $"{o.ElementId} {o.Label}: X {o.X.OldDisplay} -> {o.X.NewDisplay}, Y {o.Y.OldDisplay} -> {o.Y.NewDisplay}");
            AppendSection(sb, "Overgeslagen", PileStatus.Skipped,
                o => $"{o.ElementId} {o.Label}: {o.Reason}");
            AppendSection(sb, "Ongewijzigd", PileStatus.Unchanged,
                o => $"{o.ElementId} {o.Label}: X {o.X.NewDisplay}, Y {o.Y.NewDisplay}");

            return sb.ToString().TrimEnd();
        }

        private void AppendSection(StringBuilder sb, string title, PileStatus status, Func<PileOutcome, string> line)
        {
            var items = Outcomes.Where(o => o.Status == status).ToList();
            if (items.Count == 0)
            {
                return;
            }
            sb.AppendLine();
            sb.AppendLine($"{title} ({items.Count}):");
            foreach (var o in items)
            {
                sb.AppendLine("  " + line(o));
            }
        }

        private static string Palen(int n) => n == 1 ? "paal" : "palen";
    }

    /// <summary>Names and GUIDs of the target parameters (IB_shared_parameters.txt, group Funderingspaal).</summary>
    public static class PileCoordinateParameters
    {
        public const string XName = "X-coördinaat_funderingspaal";
        public const string YName = "Y-coördinaat_funderingspaal";

        /// <summary>LENGTH, IB_shared_parameters.txt.</summary>
        public static readonly Guid XGuid = new Guid("0e4e2f36-6cf4-46c1-83ce-f5f251e08de5");

        /// <summary>LENGTH, IB_shared_parameters.txt.</summary>
        public static readonly Guid YGuid = new Guid("0bd0d5fb-13db-4ec4-9a2e-8fe4e221f8f6");
    }
}
