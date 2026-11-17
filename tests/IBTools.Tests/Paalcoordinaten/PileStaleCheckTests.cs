using System.Collections.Generic;
using IBTools.Tools.Paalcoordinaten.Core;
using Xunit;

namespace IBTools.Tests
{
    public class PileStaleCheckTests
    {
        private static CoordinateChange Same() => new CoordinateChange(true, "121.000", "121.000", 121.0);
        private static CoordinateChange Differs() => new CoordinateChange(false, "120.000", "121.000", 121.0);

        private static PileOutcome Pile(PileStatus status, CoordinateChange x = null, CoordinateChange y = null)
        {
            return new PileOutcome { ElementId = 1, Label = "NLI_Funderingspaal: Beton 250", Status = status, X = x ?? Same(), Y = y ?? Same() };
        }

        private static PileOutcome Blocked(bool stale)
        {
            var o = PileOutcome.Skip(9, "NLI_Funderingspaal: Beton 250", "uitgecheckt door jan");
            o.X = stale ? Differs() : Same();
            o.Y = Same();
            return o;
        }

        [Fact]
        public void NothingStale_NoPrompt()
        {
            var c = PileStaleCheck.From(new List<PileOutcome> { Pile(PileStatus.Unchanged), Pile(PileStatus.Unchanged) });
            Assert.Equal(2, c.Total);
            Assert.Equal(0, c.Stale);
            Assert.False(c.ShouldPrompt);
        }

        [Fact]
        public void NoPiles_NoPrompt()
        {
            var c = PileStaleCheck.From(new List<PileOutcome>());
            Assert.Equal(0, c.Total);
            Assert.False(c.ShouldPrompt);
        }

        [Fact]
        public void OneOfMany()
        {
            var c = PileStaleCheck.From(new List<PileOutcome>
            {
                Pile(PileStatus.Written, Differs()), Pile(PileStatus.Unchanged), Pile(PileStatus.Unchanged)
            });
            Assert.True(c.ShouldPrompt);
            Assert.Equal("Van 1 van de 3 palen kloppen de X-/Y-coördinaten niet meer met de plek in het model (of zijn ze leeg).",
                c.BuildMessage());
        }

        [Fact]
        public void AllOfMany()
        {
            var c = PileStaleCheck.From(new List<PileOutcome> { Pile(PileStatus.Written), Pile(PileStatus.Written) });
            Assert.Equal("Van alle 2 palen kloppen de X-/Y-coördinaten niet meer met de plek in het model (of zijn ze leeg).",
                c.BuildMessage());
        }

        [Fact]
        public void SinglePile()
        {
            var c = PileStaleCheck.From(new List<PileOutcome> { Pile(PileStatus.Written) });
            Assert.True(c.ShouldPrompt);
            Assert.Equal("De X-/Y-coördinaten van de paal in dit model kloppen niet meer met de plek in het model (of zijn leeg).",
                c.BuildMessage());
        }

        [Fact]
        public void BlockedStale_CountedAndMentioned()
        {
            var c = PileStaleCheck.From(new List<PileOutcome>
            {
                Pile(PileStatus.Written), Pile(PileStatus.Written), Blocked(true), Blocked(false), Pile(PileStatus.Unchanged)
            });
            Assert.Equal(5, c.Total);
            Assert.Equal(2, c.StaleWritable);
            Assert.Equal(1, c.StaleBlocked);
            Assert.Equal(3, c.Stale);
            Assert.Equal("Van 3 van de 5 palen kloppen de X-/Y-coördinaten niet meer met de plek in het model (of zijn ze leeg). " +
                         "Daarvan kan 1 paal nu niet worden bijgewerkt (bijvoorbeeld uitgecheckt door een collega); die staat in het rapport.",
                c.BuildMessage());
        }

        [Fact]
        public void SeveralBlocked_Plural()
        {
            var c = PileStaleCheck.From(new List<PileOutcome> { Pile(PileStatus.Written), Blocked(true), Blocked(true) });
            Assert.EndsWith("Daarvan kunnen 2 palen nu niet worden bijgewerkt (bijvoorbeeld uitgecheckt door een collega); die staan in het rapport.",
                c.BuildMessage());
        }

        [Fact]
        public void OnlyBlockedStale_NoPrompt()
        {
            var c = PileStaleCheck.From(new List<PileOutcome> { Blocked(true), Pile(PileStatus.Unchanged) });
            Assert.Equal(1, c.StaleBlocked);
            Assert.False(c.ShouldPrompt);
        }

        [Fact]
        public void SkippedWithoutValues_IsNotStale()
        {
            // e.g. parameters on the type or no insertion point: nothing is known about the value.
            var c = PileStaleCheck.From(new List<PileOutcome> { PileOutcome.Skip(3, "x", "geen invoegpunt"), Pile(PileStatus.Written) });
            Assert.Equal(0, c.StaleBlocked);
            Assert.Equal(1, c.StaleWritable);
        }

        [Fact]
        public void LogLine()
        {
            var c = PileStaleCheck.From(new List<PileOutcome> { Pile(PileStatus.Written), Blocked(true), Pile(PileStatus.Unchanged) });
            Assert.Equal("3 palen met coördinaatparameters, 2 verouderd (1 schrijfbaar, 1 geblokkeerd)", c.BuildLogLine());
        }
    }
}
