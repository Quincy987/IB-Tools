using System;

namespace FoundationProjectionToggle.Core.Remmingwerk
{
    /// <summary>
    /// The numbers a user sets for a remmingwerk (all mm). Defaults = Dynamo script Remmingwerk v1.2.
    /// </summary>
    public sealed class RemmingwerkSettings
    {
        /// <summary>Number of rows of wrijfgordingen/stalen gordingen ("Aantal wrijfstijlen" in Dynamo).</summary>
        public int Rows { get; set; } = 3;

        /// <summary>Vertical centre-to-centre distance of the gording rows ("h.o.h. wrijfgordingen").</summary>
        public double RowSpacingMm { get; set; } = 700;

        /// <summary>Width of the timber wrijfgording/wrijfstijl (perpendicular to the line).</summary>
        public double WrijfBreedteMm { get; set; } = 160;

        /// <summary>Height of the timber wrijfgording/wrijfstijl.</summary>
        public double WrijfHoogteMm { get; set; } = 300;

        /// <summary>Centre-to-centre distance of the tube piles (and steel posts, wrijfstijlen) along the line.</summary>
        public double PileSpacingMm { get; set; } = 4250;

        /// <summary>Pile tip relative to the NAP level (Dynamo: relative to the line).</summary>
        public double PileTipNapMm { get; set; } = -20000;

        /// <summary>How far the pile sticks out above the steel gording.</summary>
        public double PileOverlengthMm { get; set; } = 250;

        /// <summary>
        /// False: piles right of the drawing direction (what Dynamo does for lines). True: left,
        /// which is the same as the line drawn the other way.
        /// </summary>
        public bool PilesLeft { get; set; }

        public RemmingwerkSettings Clone()
        {
            return (RemmingwerkSettings)MemberwiseClone();
        }
    }

    /// <summary>
    /// Section sizes read from the chosen steel types (type parameters NLRS_C_hoogte,
    /// NLRS_C_breedte, NLRS_C_diameter, mm). NaN = not found on the type.
    /// </summary>
    public sealed class SteelDimensions
    {
        /// <summary>NLRS_C_hoogte of the steel gording (HEA450: 440).</summary>
        public double GordingHoogteMm { get; set; } = double.NaN;

        /// <summary>NLRS_C_breedte of the steel gording (HEA450: 300).</summary>
        public double GordingBreedteMm { get; set; } = double.NaN;

        /// <summary>NLRS_C_hoogte of the steel post (HEA450: 440).</summary>
        public double StijlHoogteMm { get; set; } = double.NaN;

        /// <summary>NLRS_C_diameter of the tube pile (ø610x12.5: 610).</summary>
        public double PaalDiameterMm { get; set; } = double.NaN;
    }

    /// <summary>One selected line: its plan curve and height (mm, Revit internal coordinates).</summary>
    public sealed class RemmingwerkLine
    {
        public RemmingwerkLine(PlanCurve curve, double zMm, string label)
        {
            Curve = curve ?? throw new ArgumentNullException(nameof(curve));
            ZMm = zMm;
            Label = label ?? string.Empty;
        }

        public PlanCurve Curve { get; }
        public double ZMm { get; }

        /// <summary>For messages, e.g. the element id.</summary>
        public string Label { get; }
    }

    /// <summary>
    /// A family type the user can choose (Revit-free, so the window can use it). Tag carries the
    /// Revit ElementId of the type, or null when the type comes from a family embedded in IB Tools
    /// that is loaded when placing.
    /// </summary>
    public sealed class TypeOption
    {
        public string FamilyName { get; set; }
        public string TypeName { get; set; }
        public object Tag { get; set; }
        public bool FromAddIn { get; set; }
        public double HoogteMm { get; set; } = double.NaN;
        public double BreedteMm { get; set; } = double.NaN;
        public double DiameterMm { get; set; } = double.NaN;

        /// <summary>Text in the combo box, e.g. "kolom HEA450 (h 440, b 300)".</summary>
        public string Label
        {
            get
            {
                var dims = string.Empty;
                if (!double.IsNaN(HoogteMm)) dims += "h " + HoogteMm.ToString("0.#", RemmingwerkFormat.Culture);
                if (!double.IsNaN(BreedteMm)) dims += (dims.Length > 0 ? ", " : string.Empty) + "b " + BreedteMm.ToString("0.#", RemmingwerkFormat.Culture);
                if (!double.IsNaN(DiameterMm)) dims += (dims.Length > 0 ? ", " : string.Empty) + "ø " + DiameterMm.ToString("0.#", RemmingwerkFormat.Culture);
                var name = string.Equals(FamilyName, TypeName, StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(FamilyName)
                    ? TypeName
                    : FamilyName + ": " + TypeName;
                return name + (dims.Length > 0 ? " (" + dims + ")" : string.Empty) + (FromAddIn ? " - wordt geladen uit IB Tools" : string.Empty);
            }
        }

        public override string ToString() => Label;
    }

    /// <summary>A level the user can choose as NAP reference.</summary>
    public sealed class LevelOption
    {
        public string Name { get; set; }

        /// <summary>Height of the level plane in Revit internal coordinates, mm.</summary>
        public double ElevationMm { get; set; }

        public object Tag { get; set; }

        public override string ToString()
        {
            return Name + " (" + RemmingwerkFormat.Metres(ElevationMm) + " m)";
        }
    }

    public static class RemmingwerkFormat
    {
        /// <summary>Dutch number format (decimal comma) for everything the user reads.</summary>
        public static readonly System.Globalization.CultureInfo Culture = System.Globalization.CultureInfo.GetCultureInfo("nl-NL");

        /// <summary>mm as metres with 3 decimals ("21,800").</summary>
        public static string Metres(double mm)
        {
            return (mm / 1000.0).ToString("0.000", Culture);
        }

        /// <summary>mm as metres with 1 decimal ("21,8").</summary>
        public static string MetresShort(double mm)
        {
            return (mm / 1000.0).ToString("0.0", Culture);
        }

        public static string Mm(double mm)
        {
            return mm.ToString("0.#", Culture);
        }
    }
}
