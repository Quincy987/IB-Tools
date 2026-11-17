using System.Collections.Generic;

namespace IBTools.Tools.Paalcoordinaten.Core
{
    /// <summary>
    /// Counts of the check when a model is opened: how many piles carry stored coordinates that
    /// differ from what the button would write (or are empty), and how many of those can be
    /// written now. Built from the same outcomes as the button's report, so "stale" means exactly
    /// "the button would write something here".
    /// </summary>
    public sealed class PileStaleCheck
    {
        /// <summary>Piles with both coordinate parameters (every outcome of a whole-model run).</summary>
        public int Total { get; private set; }

        /// <summary>Stale piles that would be written now.</summary>
        public int StaleWritable { get; private set; }

        /// <summary>
        /// Stale piles that cannot be written now (owned by a colleague, updated in central,
        /// read-only parameter, group): their new value is known, but they are skipped.
        /// </summary>
        public int StaleBlocked { get; private set; }

        public int Stale => StaleWritable + StaleBlocked;

        /// <summary>Only ask when there is something the update would actually change.</summary>
        public bool ShouldPrompt => StaleWritable > 0;

        public static PileStaleCheck From(IEnumerable<PileOutcome> outcomes)
        {
            var check = new PileStaleCheck();
            foreach (var o in outcomes)
            {
                check.Total++;
                if (o.Status == PileStatus.Written)
                {
                    check.StaleWritable++;
                }
                else if (o.Status == PileStatus.Skipped && IsStale(o))
                {
                    check.StaleBlocked++;
                }
            }
            return check;
        }

        /// <summary>A skipped pile whose new value was worked out and differs from the stored one.</summary>
        private static bool IsStale(PileOutcome o)
        {
            return o.X != null && o.Y != null && !(o.X.Unchanged && o.Y.Unchanged);
        }

        /// <summary>Main text of the prompt.</summary>
        public string BuildMessage()
        {
            string lead;
            if (Total == 1)
            {
                lead = "De X-/Y-coördinaten van de paal in dit model kloppen niet meer met de plek in het model (of zijn leeg).";
            }
            else if (Stale == Total)
            {
                lead = $"Van alle {Total} palen kloppen de X-/Y-coördinaten niet meer met de plek in het model (of zijn ze leeg).";
            }
            else
            {
                lead = $"Van {Stale} van de {Total} palen kloppen de X-/Y-coördinaten niet meer met de plek in het model (of zijn ze leeg).";
            }

            if (StaleBlocked == 0)
            {
                return lead;
            }
            var blocked = StaleBlocked == 1
                ? "Daarvan kan 1 paal nu niet worden bijgewerkt (bijvoorbeeld uitgecheckt door een collega); die staat in het rapport."
                : $"Daarvan kunnen {StaleBlocked} palen nu niet worden bijgewerkt (bijvoorbeeld uitgecheckt door een collega); die staan in het rapport.";
            return lead + " " + blocked;
        }

        /// <summary>One line for the log.</summary>
        public string BuildLogLine()
        {
            return $"{Total} palen met coördinaatparameters, {Stale} verouderd ({StaleWritable} schrijfbaar, {StaleBlocked} geblokkeerd)";
        }
    }
}
