using System;
using System.Globalization;

namespace FoundationProjectionToggle.Core.PileCoordinates
{
    /// <summary>How a coordinate parameter stores its value.</summary>
    public enum CoordinateParameterKind
    {
        /// <summary>Text parameter: written as "121.457" (meters, 3 decimals, point).</summary>
        Text,
        /// <summary>Unitless number: written as the value in meters (121.457).</summary>
        Number,
        /// <summary>
        /// Length parameter (the IB shared parameter file defines both as LENGTH): written like the
        /// Dynamo script did, as the meter value expressed in the project's length display unit, so
        /// the Palenstaat keeps showing "121.457". The Revit glue does that unit conversion.
        /// </summary>
        Length,
        /// <summary>Anything else (integer, angle, ...): skipped.</summary>
        Unsupported
    }

    /// <summary>Outcome of the base point self-test that also fixes the transform direction.</summary>
    public sealed class SelfTestResult
    {
        public bool Passed { get; set; }

        /// <summary>Transform internal -> shared (feet), valid only when <see cref="Passed"/>.</summary>
        public PlanarTransform InternalToShared { get; set; }

        /// <summary>"inverse" or "direct": which form of Revit's total transform matched.</summary>
        public string Direction { get; set; }

        /// <summary>Deviation in mm of the chosen (or best) direction.</summary>
        public double DeviationMm { get; set; }

        /// <summary>Deviation in mm of the other direction (diagnostics).</summary>
        public double OtherDeviationMm { get; set; }
    }

    /// <summary>
    /// Revit-free calculations for the "Paalcoördinaten" button: feet -> RD meters, rounding,
    /// self-test, change detection and text formatting.
    /// </summary>
    public static class PileCoordinateMath
    {
        public const double FeetToMeters = 0.3048;
        public const double FeetToMillimeters = 304.8;

        /// <summary>Maximum base point self-test deviation.</summary>
        public const double SelfTestToleranceMm = 1.0;

        /// <summary>Decimals in meters (= mm precision), as in the Dynamo script.</summary>
        public const int Decimals = 3;

        /// <summary>Two numeric values within this (meters) count as unchanged.</summary>
        public const double UnchangedToleranceMeters = 1e-7;

        /// <summary>
        /// Converts Revit's total project-location transform into internal -> shared by checking
        /// the project base point: transformed, its internal position must give its own E/W and
        /// N/S (all in feet). Tries the inverse first (Revit's GetTotalTransform maps shared to
        /// internal), then the direct form. Neither within <see cref="SelfTestToleranceMm"/>:
        /// not passed, do not write anything.
        /// </summary>
        public static SelfTestResult SelfTest(
            PlanarTransform totalTransform,
            double basePointInternalX,
            double basePointInternalY,
            double basePointEastWest,
            double basePointNorthSouth)
        {
            if (totalTransform == null)
            {
                throw new ArgumentNullException(nameof(totalTransform));
            }

            var inverse = totalTransform.Inverse();
            var inverseDev = DeviationMm(inverse, basePointInternalX, basePointInternalY, basePointEastWest, basePointNorthSouth);
            var directDev = DeviationMm(totalTransform, basePointInternalX, basePointInternalY, basePointEastWest, basePointNorthSouth);

            var useInverse = inverseDev <= directDev;
            var best = useInverse ? inverseDev : directDev;
            return new SelfTestResult
            {
                Passed = best <= SelfTestToleranceMm,
                InternalToShared = useInverse ? inverse : totalTransform,
                Direction = useInverse ? "inverse" : "direct",
                DeviationMm = best,
                OtherDeviationMm = useInverse ? directDev : inverseDev
            };
        }

        private static double DeviationMm(PlanarTransform t, double x, double y, double expectedX, double expectedY)
        {
            t.Apply(x, y, out var sx, out var sy);
            var dx = sx - expectedX;
            var dy = sy - expectedY;
            return Math.Sqrt(dx * dx + dy * dy) * FeetToMillimeters;
        }

        /// <summary>Shared coordinate in feet -> meters, rounded to 3 decimals.</summary>
        public static double FeetToRoundedMeters(double feet)
        {
            return Math.Round(feet * FeetToMeters, Decimals, MidpointRounding.AwayFromZero);
        }

        /// <summary>Rough check that a point lies within the Dutch RD grid (meters).</summary>
        public static bool IsWithinRd(double xMeters, double yMeters)
        {
            return xMeters >= 0 && xMeters <= 300000 && yMeters >= 289000 && yMeters <= 629000;
        }

        /// <summary>"121.457": invariant culture, always 3 decimals.</summary>
        public static string FormatMeters(double meters)
        {
            return meters.ToString("0.000", CultureInfo.InvariantCulture);
        }

        /// <summary>Parses "121.457" and "121,457" (no thousands separators).</summary>
        public static bool TryParseMeters(string text, out double meters)
        {
            meters = 0;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }
            var normalized = text.Trim().Replace(',', '.');
            return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out meters);
        }

        /// <summary>
        /// Decides whether one coordinate parameter must be written.
        /// </summary>
        /// <param name="kind">Storage kind of the parameter (not Unsupported).</param>
        /// <param name="newMeters">Rounded new value in meters.</param>
        /// <param name="hasValue">Whether the parameter currently has a value.</param>
        /// <param name="existingText">Current text (Text kind).</param>
        /// <param name="existingMeters">Current value converted to meters (Number/Length kind).</param>
        public static CoordinateChange Decide(
            CoordinateParameterKind kind,
            double newMeters,
            bool hasValue,
            string existingText,
            double existingMeters)
        {
            var newDisplay = FormatMeters(newMeters);
            switch (kind)
            {
                case CoordinateParameterKind.Text:
                {
                    var old = hasValue ? (existingText ?? string.Empty) : string.Empty;
                    var unchanged = hasValue && old.Trim() == newDisplay;
                    return new CoordinateChange(unchanged, old.Length == 0 ? "(leeg)" : old, newDisplay, newMeters);
                }
                case CoordinateParameterKind.Number:
                case CoordinateParameterKind.Length:
                {
                    var unchanged = hasValue && Math.Abs(existingMeters - newMeters) < UnchangedToleranceMeters;
                    var oldDisplay = hasValue ? FormatMeters(existingMeters) : "(leeg)";
                    return new CoordinateChange(unchanged, oldDisplay, newDisplay, newMeters);
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported parameter kind");
            }
        }
    }

    /// <summary>Result of <see cref="PileCoordinateMath.Decide"/> for one parameter.</summary>
    public sealed class CoordinateChange
    {
        public CoordinateChange(bool unchanged, string oldDisplay, string newDisplay, double newMeters)
        {
            Unchanged = unchanged;
            OldDisplay = oldDisplay;
            NewDisplay = newDisplay;
            NewMeters = newMeters;
        }

        public bool Unchanged { get; }
        public string OldDisplay { get; }
        public string NewDisplay { get; }
        public double NewMeters { get; }
    }
}
