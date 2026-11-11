using System;
using System.Globalization;
using System.Threading;
using FoundationProjectionToggle.Core.PileCoordinates;
using Xunit;

namespace FoundationProjectionToggle.Core.Tests
{
    public class PlanarTransformTests
    {
        [Fact]
        public void Inverse_RoundTrips()
        {
            var t = PlanarTransform.FromRotationAndTranslation(0.4, 1234.5, -987.6);
            t.Apply(10, 20, out var x, out var y);
            t.Inverse().Apply(x, y, out var bx, out var by);
            Assert.Equal(10, bx, 9);
            Assert.Equal(20, by, 9);
        }

        [Fact]
        public void Rotation_IsCounterClockwise()
        {
            var t = PlanarTransform.FromRotationAndTranslation(Math.PI / 2, 0, 0);
            t.Apply(1, 0, out var x, out var y);
            Assert.Equal(0, x, 12);
            Assert.Equal(1, y, 12);
        }

        [Fact]
        public void IsIdentity()
        {
            Assert.True(PlanarTransform.Identity.IsIdentity(1e-6));
            Assert.False(PlanarTransform.FromRotationAndTranslation(0, 100, 0).IsIdentity(1e-6));
            Assert.False(PlanarTransform.FromRotationAndTranslation(0.01, 0, 0).IsIdentity(1e-6));
        }
    }

    public class SelfTestTests
    {
        private const double M = 1 / PileCoordinateMath.FeetToMeters; // meters -> feet

        // Amsterdam-ish RD position of the project base point.
        private static readonly double Ew = 121000.0 * M;
        private static readonly double Ns = 487000.0 * M;

        /// <summary>
        /// Internal -> shared like a real model: base point at internal (bpX, bpY) gets (Ew, Ns),
        /// rotated by the angle to true north.
        /// </summary>
        private static PlanarTransform InternalToShared(double angle, double bpX, double bpY)
        {
            var rot = PlanarTransform.FromRotationAndTranslation(angle, 0, 0);
            rot.Apply(bpX, bpY, out var rx, out var ry);
            return PlanarTransform.FromRotationAndTranslation(angle, Ew - rx, Ns - ry);
        }

        [Theory]
        [InlineData(0, 0, 0)]
        [InlineData(0.35, 0, 0)]
        [InlineData(-1.2, 50, -30)] // base point moved away from the internal origin
        public void RevitStyleTotalTransform_IsInverted(double angle, double bpX, double bpY)
        {
            var internalToShared = InternalToShared(angle, bpX, bpY);
            var revitTotal = internalToShared.Inverse(); // GetTotalTransform: shared -> internal

            var r = PileCoordinateMath.SelfTest(revitTotal, bpX, bpY, Ew, Ns);

            Assert.True(r.Passed);
            Assert.Equal("inverse", r.Direction);
            Assert.True(r.DeviationMm < 1e-3);
        }

        [Fact]
        public void DirectTransform_IsAlsoRecognised()
        {
            var internalToShared = InternalToShared(0.2, 10, 20);
            var r = PileCoordinateMath.SelfTest(internalToShared, 10, 20, Ew, Ns);
            Assert.True(r.Passed);
            Assert.Equal("direct", r.Direction);
        }

        [Fact]
        public void PileCoordinates_FollowRotationAroundBasePoint()
        {
            // Base point at internal (0,0); 30 degrees to true north; pile 100 m "east" in the model.
            var angle = Math.PI / 6;
            var r = PileCoordinateMath.SelfTest(InternalToShared(angle, 0, 0).Inverse(), 0, 0, Ew, Ns);
            r.InternalToShared.Apply(100 * M, 0, out var x, out var y);

            Assert.Equal(121000 + 100 * Math.Cos(angle), PileCoordinateMath.FeetToRoundedMeters(x), 3);
            Assert.Equal(487000 + 100 * Math.Sin(angle), PileCoordinateMath.FeetToRoundedMeters(y), 3);
        }

        [Fact]
        public void Fails_WhenBasePointDoesNotMatch()
        {
            var t = InternalToShared(0.3, 0, 0);
            // E/W 5 mm off: neither direction fits within 1 mm.
            var r = PileCoordinateMath.SelfTest(t.Inverse(), 0, 0, Ew + 0.005 * M, Ns);
            Assert.False(r.Passed);
            Assert.InRange(r.DeviationMm, 4.99, 5.01);
        }

        [Fact]
        public void PassesWithin1mm()
        {
            var t = InternalToShared(0.3, 0, 0);
            var r = PileCoordinateMath.SelfTest(t.Inverse(), 0, 0, Ew + 0.0008 * M, Ns);
            Assert.True(r.Passed);
        }

        [Fact]
        public void IdentityModel_PassesWithBasePointAtOrigin()
        {
            var r = PileCoordinateMath.SelfTest(PlanarTransform.Identity, 0, 0, 0, 0);
            Assert.True(r.Passed);
        }
    }

    public class MathTests
    {
        [Fact]
        public void FeetToRoundedMeters_RoundsTo3Decimals()
        {
            Assert.Equal(121.457, PileCoordinateMath.FeetToRoundedMeters(121.4567 / 0.3048), 9);
            Assert.Equal(-0.001, PileCoordinateMath.FeetToRoundedMeters(-0.0012 / 0.3048), 9);
        }

        [Theory]
        [InlineData(121457.123, 487123.456, true)]
        [InlineData(0, 0, false)]
        [InlineData(121.457, 487.123, false)]
        [InlineData(-5, 487000, false)]
        public void IsWithinRd(double x, double y, bool expected)
        {
            Assert.Equal(expected, PileCoordinateMath.IsWithinRd(x, y));
        }

        [Fact]
        public void FormatMeters_IsCultureInvariant()
        {
            var old = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("nl-NL");
                Assert.Equal("121457.100", PileCoordinateMath.FormatMeters(121457.1));
                Assert.Equal("-0.500", PileCoordinateMath.FormatMeters(-0.5));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = old;
            }
        }

        [Theory]
        [InlineData("121.457", 121.457)]
        [InlineData(" 121,457 ", 121.457)]
        public void TryParseMeters(string text, double expected)
        {
            Assert.True(PileCoordinateMath.TryParseMeters(text, out var m));
            Assert.Equal(expected, m, 9);
        }

        [Fact]
        public void TryParseMeters_RejectsGarbage()
        {
            Assert.False(PileCoordinateMath.TryParseMeters("", out _));
            Assert.False(PileCoordinateMath.TryParseMeters("abc", out _));
        }
    }

    public class DecideTests
    {
        [Fact]
        public void Text_SameText_IsUnchanged()
        {
            var c = PileCoordinateMath.Decide(CoordinateParameterKind.Text, 121.457, true, "121.457", 0);
            Assert.True(c.Unchanged);
        }

        [Fact]
        public void Text_Empty_IsWritten()
        {
            var c = PileCoordinateMath.Decide(CoordinateParameterKind.Text, 121.457, false, null, 0);
            Assert.False(c.Unchanged);
            Assert.Equal("(leeg)", c.OldDisplay);
            Assert.Equal("121.457", c.NewDisplay);
        }

        [Fact]
        public void Text_OtherNotation_IsWritten()
        {
            var c = PileCoordinateMath.Decide(CoordinateParameterKind.Text, 121.457, true, "121,457", 0);
            Assert.False(c.Unchanged);
            Assert.Equal("121,457", c.OldDisplay);
        }

        [Theory]
        [InlineData(CoordinateParameterKind.Number)]
        [InlineData(CoordinateParameterKind.Length)]
        public void Numeric_SameValue_IsUnchanged(CoordinateParameterKind kind)
        {
            // Round trip through Revit internal units leaves tiny float noise.
            var c = PileCoordinateMath.Decide(kind, 121.457, true, null, 121.457 + 1e-11);
            Assert.True(c.Unchanged);
        }

        [Theory]
        [InlineData(CoordinateParameterKind.Number)]
        [InlineData(CoordinateParameterKind.Length)]
        public void Numeric_1mmDifference_IsWritten(CoordinateParameterKind kind)
        {
            var c = PileCoordinateMath.Decide(kind, 121.457, true, null, 121.456);
            Assert.False(c.Unchanged);
            Assert.Equal("121.456", c.OldDisplay);
            Assert.Equal("121.457", c.NewDisplay);
        }

        [Fact]
        public void Numeric_NoValue_IsWritten()
        {
            var c = PileCoordinateMath.Decide(CoordinateParameterKind.Length, 0, false, null, 0);
            Assert.False(c.Unchanged);
            Assert.Equal("(leeg)", c.OldDisplay);
        }

        [Fact]
        public void Unsupported_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                PileCoordinateMath.Decide(CoordinateParameterKind.Unsupported, 1, true, null, 1));
        }
    }

    public class ReportTests
    {
        private static PileOutcome Done(long id, PileStatus status)
        {
            var c = new CoordinateChange(status == PileStatus.Unchanged, "1.000", "2.000", 2);
            return new PileOutcome { ElementId = id, Label = "NLI_Funderingspaal: Beton 250", Status = status, X = c, Y = c };
        }

        [Fact]
        public void AllUnchanged()
        {
            var r = new PileCoordinateReport { Scope = "hele model" };
            r.Outcomes.Add(Done(1, PileStatus.Unchanged));
            r.Outcomes.Add(Done(2, PileStatus.Unchanged));
            Assert.Equal("Alles ongewijzigd: 2 palen (hele model).", r.BuildSummary());
        }

        [Fact]
        public void Mixed_CountsAndReasons()
        {
            var r = new PileCoordinateReport { Scope = "hele model" };
            r.Outcomes.Add(Done(1, PileStatus.Written));
            r.Outcomes.Add(Done(2, PileStatus.Unchanged));
            r.Outcomes.Add(PileOutcome.Skip(3, "NLI_Funderingspaal: Beton 250", "uitgecheckt door jan"));
            r.Warnings.Add("buiten RD");

            Assert.Equal("1 paal geschreven, 1 ongewijzigd, 1 overgeslagen (hele model). Let op: 1 waarschuwing.", r.BuildSummary());
            var details = r.BuildDetails();
            Assert.Contains("LET OP: buiten RD", details);
            Assert.Contains("Geschreven (1):", details);
            Assert.Contains("1 NLI_Funderingspaal: Beton 250: X 1.000 -> 2.000", details);
            Assert.Contains("3 NLI_Funderingspaal: Beton 250: uitgecheckt door jan", details);
        }

        [Fact]
        public void Error_SaysNothingWritten()
        {
            var r = new PileCoordinateReport { Error = "Zelftest mislukt." };
            Assert.StartsWith("Niets geschreven.", r.BuildSummary());
        }

        [Fact]
        public void NoPiles()
        {
            var r = new PileCoordinateReport { Scope = "hele model" };
            Assert.Contains("Geen palen gevonden", r.BuildSummary());
        }
    }
}
