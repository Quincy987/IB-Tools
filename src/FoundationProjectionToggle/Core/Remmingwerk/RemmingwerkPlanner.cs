using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FoundationProjectionToggle.Core.Remmingwerk
{
    public enum RemmingwerkPart
    {
        Wrijfgording,
        StalenGording,
        StalenStijl,
        Buispaal,
        Wrijfstijl,
    }

    /// <summary>A beam (wrijfgording or steel gording) along a plan curve at height ZMm.</summary>
    public sealed class PlannedBeam
    {
        public RemmingwerkPart Part { get; set; }
        public int LineIndex { get; set; }
        public int Row { get; set; }
        public PlanCurve Curve { get; set; }
        public double ZMm { get; set; }

        /// <summary>Revit "Cross-Section Rotation" in degrees.</summary>
        public double CrossSectionRotationDegrees { get; set; }

        /// <summary>Set Revit "z Justification" to Origin (Dynamo: value 2).</summary>
        public bool ZJustificationOrigin { get; set; }

        /// <summary>Wrijfgording only: IB_wrijfstijl_hoogte / IB_wrijfstijl_breedte (mm), NaN = leave.</summary>
        public double WrijfHoogteMm { get; set; } = double.NaN;
        public double WrijfBreedteMm { get; set; } = double.NaN;
    }

    /// <summary>A vertical column (steel post or tube pile).</summary>
    public sealed class PlannedColumn
    {
        public RemmingwerkPart Part { get; set; }
        public int LineIndex { get; set; }
        public int Index { get; set; }
        public Pt2 Position { get; set; }
        public double BottomMm { get; set; }
        public double TopMm { get; set; }

        /// <summary>Rotation about the vertical axis (degrees CCW from +X) = drawing direction of the line here.</summary>
        public double RotationDegrees { get; set; }

        public double LengthMm => TopMm - BottomMm;
    }

    /// <summary>A wrijfstijl (point-based generic model).</summary>
    public sealed class PlannedPost
    {
        public int LineIndex { get; set; }
        public int Index { get; set; }
        public Pt2 Position { get; set; }
        public double ZMm { get; set; }
        public double RotationDegrees { get; set; }
        public double HoogteMm { get; set; }
        public double BreedteMm { get; set; }
        public double LengteMm { get; set; }
    }

    /// <summary>Per selected line: what will be placed.</summary>
    public sealed class LinePlanSummary
    {
        public string Label { get; set; }
        public bool IsArc { get; set; }
        public double LengthMm { get; set; }
        public double ZMm { get; set; }
        public int Stations { get; set; }
        public double FirstStationMm { get; set; }
    }

    public sealed class RemmingwerkPlan
    {
        public List<string> Errors { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();
        public List<PlannedBeam> Beams { get; } = new List<PlannedBeam>();
        public List<PlannedColumn> Columns { get; } = new List<PlannedColumn>();
        public List<PlannedPost> Posts { get; } = new List<PlannedPost>();
        public List<LinePlanSummary> Lines { get; } = new List<LinePlanSummary>();

        public bool IsValid => Errors.Count == 0;

        public int Count(RemmingwerkPart part)
        {
            switch (part)
            {
                case RemmingwerkPart.Wrijfgording:
                case RemmingwerkPart.StalenGording:
                    return Beams.Count(b => b.Part == part);
                case RemmingwerkPart.StalenStijl:
                case RemmingwerkPart.Buispaal:
                    return Columns.Count(c => c.Part == part);
                default:
                    return Posts.Count;
            }
        }

        /// <summary>One line for the window, e.g. "5 palen à 21,8 m, eerste op 1,500 m".</summary>
        public string Overview()
        {
            if (!IsValid)
            {
                return string.Join(Environment.NewLine, Errors);
            }
            var piles = Columns.Where(c => c.Part == RemmingwerkPart.Buispaal).ToList();
            var sb = new StringBuilder();
            sb.Append(piles.Count).Append(piles.Count == 1 ? " paal" : " palen");
            if (piles.Count > 0)
            {
                sb.Append(" à ").Append(RemmingwerkFormat.MetresShort(piles[0].LengthMm)).Append(" m");
            }
            if (Lines.Count == 1)
            {
                sb.Append(", eerste op ").Append(RemmingwerkFormat.Metres(Lines[0].FirstStationMm)).Append(" m van het begin");
            }
            sb.Append("; ").Append(Count(RemmingwerkPart.StalenStijl)).Append(" stalen stijlen, ")
              .Append(Count(RemmingwerkPart.Wrijfstijl)).Append(" wrijfstijlen, ")
              .Append(Count(RemmingwerkPart.Wrijfgording)).Append(" wrijfgordingen, ")
              .Append(Count(RemmingwerkPart.StalenGording)).Append(" stalen gordingen.");
            foreach (var w in Warnings)
            {
                sb.Append(Environment.NewLine).Append(w);
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Revit-free calculation of a remmingwerk along one or more horizontal lines/arcs, following
    /// the Dynamo script Remmingwerk v1.2 (K:\ALG\Autodesk\2023\DYNAMO\Dynamo script Revit\Remmingwerk).
    /// </summary>
    /// <remarks>
    /// <para>Per line, with n rows, a = row spacing, b × h = wrijfgording, p = pile spacing (mm):</para>
    /// <list type="bullet">
    /// <item>Stations: floor(L/p)+1 points p apart, the rest split equally over both ends (s0).</item>
    /// <item>Wrijfgordingen: on the line at z0 + k·a (k = 0..n−1), z Justification Origin, cross-section 180°.</item>
    /// <item>Steel gordingen: b to the right, same heights, cross-section 90°.</item>
    /// <item>Steel posts: at each station, b + ½·post height to the right, z0 .. z0 + (n−1)·a.</item>
    /// <item>Tube piles: at each station, gording height + b + ½D + 200 to the right, from the
    /// pile tip (NAP level + tip) to z0 + (n−1)·a + ½·gording width + overlength.</item>
    /// <item>Wrijfstijlen: at each station on the line, z0, length (n−1)·a, turned along the line.</item>
    /// </list>
    /// <para>Deviations from Dynamo, on purpose: pile tip relative to the NAP level (Dynamo: to the
    /// line; same when the line lies on NAP 0); rotations are the exact line direction (Dynamo:
    /// minus the direction, rounded to whole degrees, which is only right for lines along X); on
    /// arcs the offsets are radial at the same station (Dynamo measured along the offset arc), and
    /// "right" is always right of the drawing direction (Dynamo: outward for every arc).</para>
    /// </remarks>
    public static class RemmingwerkPlanner
    {
        /// <summary>Fixed gap gording – pile in the Dynamo script (the steel plate on the drawing).</summary>
        public const double PlateGapMm = 200;

        public const double CrossSectionWrijfgording = 180;
        public const double CrossSectionStalenGording = 90;

        /// <summary>
        /// Arc-length stations along a line of length <paramref name="lengthMm"/>: as many as fit at
        /// <paramref name="spacingMm"/>, centred (Dynamo: s0 = (L − max(0..L..p)) / 2).
        /// </summary>
        public static List<double> Stations(double lengthMm, double spacingMm)
        {
            var result = new List<double>();
            if (lengthMm <= 0 || spacingMm <= 0)
            {
                return result;
            }
            // DesignScript's range 0..L..p includes L when it is a multiple of p (within rounding).
            int intervals = (int)Math.Floor(lengthMm / spacingMm + 1e-9);
            double s0 = (lengthMm - intervals * spacingMm) / 2.0;
            if (s0 < 0) s0 = 0;
            for (int i = 0; i <= intervals; i++)
            {
                result.Add(Math.Min(s0 + i * spacingMm, lengthMm));
            }
            return result;
        }

        /// <summary>Distance line → steel post axis: b + ½·post height (Dynamo N031).</summary>
        public static double PostOffsetMm(RemmingwerkSettings s, SteelDimensions d) => s.WrijfBreedteMm + d.StijlHoogteMm / 2.0;

        /// <summary>Distance line → pile axis: gording height + b + ½D + 200 (Dynamo N059).</summary>
        public static double PileOffsetMm(RemmingwerkSettings s, SteelDimensions d) => d.GordingHoogteMm + s.WrijfBreedteMm + d.PaalDiameterMm / 2.0 + PlateGapMm;

        /// <summary>Height of the top row above the line: (n−1)·a.</summary>
        public static double RiseMm(RemmingwerkSettings s) => (s.Rows - 1) * s.RowSpacingMm;

        /// <summary>Pile top above the line: (n−1)·a + ½·gording width + overlength (Dynamo N058).</summary>
        public static double PileTopAboveLineMm(RemmingwerkSettings s, SteelDimensions d) => RiseMm(s) + d.GordingBreedteMm / 2.0 + s.PileOverlengthMm;

        public static RemmingwerkPlan Plan(IList<RemmingwerkLine> lines, RemmingwerkSettings settings, SteelDimensions steel, double napLevelElevationMm)
        {
            var plan = new RemmingwerkPlan();
            Validate(lines, settings, steel, plan);
            if (!plan.IsValid)
            {
                return plan;
            }

            double rise = RiseMm(settings);
            double postOffset = PostOffsetMm(settings, steel);
            double pileOffset = PileOffsetMm(settings, steel);
            double pileTopAbove = PileTopAboveLineMm(settings, steel);
            double pileBottom = napLevelElevationMm + settings.PileTipNapMm;

            for (int li = 0; li < lines.Count; li++)
            {
                var line = lines[li];
                var curve = settings.PilesLeft ? line.Curve.Reversed() : line.Curve;
                var name = LineName(line, li, lines.Count);
                double z0 = line.ZMm;

                var gordingCurve = curve.OffsetRight(settings.WrijfBreedteMm);
                if (gordingCurve == null || curve.OffsetRight(pileOffset) == null)
                {
                    plan.Errors.Add(name + ": de boog is te krap voor dit remmingwerk aan deze kant (straal kleiner dan " +
                                    RemmingwerkFormat.Metres(pileOffset) + " m). Kies de andere kant of een grotere boog.");
                    continue;
                }

                for (int k = 0; k < settings.Rows; k++)
                {
                    double z = z0 + k * settings.RowSpacingMm;
                    plan.Beams.Add(new PlannedBeam
                    {
                        Part = RemmingwerkPart.Wrijfgording,
                        LineIndex = li,
                        Row = k,
                        Curve = curve,
                        ZMm = z,
                        CrossSectionRotationDegrees = CrossSectionWrijfgording,
                        ZJustificationOrigin = true,
                        WrijfHoogteMm = settings.WrijfHoogteMm,
                        WrijfBreedteMm = settings.WrijfBreedteMm,
                    });
                    plan.Beams.Add(new PlannedBeam
                    {
                        Part = RemmingwerkPart.StalenGording,
                        LineIndex = li,
                        Row = k,
                        Curve = gordingCurve,
                        ZMm = z,
                        CrossSectionRotationDegrees = CrossSectionStalenGording,
                    });
                }

                if (pileBottom >= z0 + pileTopAbove)
                {
                    plan.Errors.Add(name + ": de paalpunt (" + RemmingwerkFormat.Metres(settings.PileTipNapMm) +
                                    " m t.o.v. NAP) ligt niet onder de bovenkant van de paal.");
                    continue;
                }

                var stations = Stations(curve.Length, settings.PileSpacingMm);
                for (int i = 0; i < stations.Count; i++)
                {
                    double s = stations[i];
                    double dir = curve.DirectionDegreesAt(s);
                    plan.Columns.Add(new PlannedColumn
                    {
                        Part = RemmingwerkPart.StalenStijl,
                        LineIndex = li,
                        Index = i,
                        Position = curve.PointAt(s, postOffset),
                        BottomMm = z0,
                        TopMm = z0 + rise,
                        RotationDegrees = dir,
                    });
                    plan.Columns.Add(new PlannedColumn
                    {
                        Part = RemmingwerkPart.Buispaal,
                        LineIndex = li,
                        Index = i,
                        Position = curve.PointAt(s, pileOffset),
                        BottomMm = pileBottom,
                        TopMm = z0 + pileTopAbove,
                        RotationDegrees = dir,
                    });
                    plan.Posts.Add(new PlannedPost
                    {
                        LineIndex = li,
                        Index = i,
                        Position = curve.PointAt(s),
                        ZMm = z0,
                        RotationDegrees = dir,
                        HoogteMm = settings.WrijfHoogteMm,
                        BreedteMm = settings.WrijfBreedteMm,
                        LengteMm = rise,
                    });
                }

                plan.Lines.Add(new LinePlanSummary
                {
                    Label = name,
                    IsArc = curve is PlanArc,
                    LengthMm = curve.Length,
                    ZMm = z0,
                    Stations = stations.Count,
                    FirstStationMm = stations.Count > 0 ? stations[0] : 0,
                });

                if (curve.Length < settings.PileSpacingMm)
                {
                    plan.Warnings.Add(name + " is korter dan de h.o.h. van de buispalen: één paal in het midden.");
                }
                if (Math.Abs(z0 - napLevelElevationMm) > 0.5)
                {
                    plan.Warnings.Add(name + " ligt op " + RemmingwerkFormat.Metres(z0 - napLevelElevationMm) +
                                      " m t.o.v. het NAP-level; gordingen en stijlen beginnen op de lijn, de paalpunt op het NAP-level " +
                                      RemmingwerkFormat.Metres(settings.PileTipNapMm) + " m.");
                }
            }

            if (plan.Errors.Count > 0)
            {
                plan.Beams.Clear();
                plan.Columns.Clear();
                plan.Posts.Clear();
            }
            return plan;
        }

        private static void Validate(IList<RemmingwerkLine> lines, RemmingwerkSettings s, SteelDimensions d, RemmingwerkPlan plan)
        {
            if (lines == null || lines.Count == 0)
            {
                plan.Errors.Add("Geen lijn gekozen.");
            }
            if (s.Rows < 2)
            {
                plan.Errors.Add("Aantal gordingrijen moet minstens 2 zijn (anders hebben stijlen en wrijfstijlen geen lengte).");
            }
            if (s.Rows > 50)
            {
                plan.Errors.Add("Aantal gordingrijen is groter dan 50.");
            }
            Positive(s.RowSpacingMm, "h.o.h. gordingen", plan);
            Positive(s.WrijfBreedteMm, "breedte wrijfgording", plan);
            Positive(s.WrijfHoogteMm, "hoogte wrijfgording", plan);
            Positive(s.PileSpacingMm, "h.o.h. buispalen", plan);
            if (s.PileOverlengthMm < 0 || double.IsNaN(s.PileOverlengthMm))
            {
                plan.Errors.Add("Overlengte buispaal mag niet negatief zijn.");
            }
            if (double.IsNaN(d.GordingHoogteMm) || double.IsNaN(d.GordingBreedteMm))
            {
                plan.Errors.Add("Het type van de stalen gording heeft geen NLRS_C_hoogte/NLRS_C_breedte.");
            }
            if (double.IsNaN(d.StijlHoogteMm))
            {
                plan.Errors.Add("Het type van de stalen stijl heeft geen NLRS_C_hoogte.");
            }
            if (double.IsNaN(d.PaalDiameterMm))
            {
                plan.Errors.Add("Het type van de buispaal heeft geen NLRS_C_diameter.");
            }
        }

        private static void Positive(double value, string what, RemmingwerkPlan plan)
        {
            if (!(value > 0))
            {
                plan.Errors.Add(char.ToUpper(what[0]) + what.Substring(1) + " moet groter dan 0 zijn.");
            }
        }

        private static string LineName(RemmingwerkLine line, int index, int count)
        {
            if (!string.IsNullOrEmpty(line.Label))
            {
                return "Lijn " + line.Label;
            }
            return count == 1 ? "De lijn" : "Lijn " + (index + 1);
        }
    }
}
