using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FoundationProjectionToggle.Services
{
    /// <summary>3D vector/point in plain doubles (Revit-free mirror of XYZ).</summary>
    public struct Vec3
    {
        public Vec3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public double X { get; }
        public double Y { get; }
        public double Z { get; }

        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3 operator *(Vec3 a, double f) => new Vec3(a.X * f, a.Y * f, a.Z * f);
        public static Vec3 operator /(Vec3 a, double f) => new Vec3(a.X / f, a.Y / f, a.Z / f);

        public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
        public double HorizontalLength => Math.Sqrt(X * X + Y * Y);

        public double DistanceTo(Vec3 other) => (this - other).Length;

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "({0:0.######}, {1:0.######}, {2:0.######})", X, Y, Z);
        }
    }

    /// <summary>
    /// Rigid transform like Revit's Transform: p' = Origin + BasisX*p.X + BasisY*p.Y + BasisZ*p.Z.
    /// Bases are assumed orthonormal (rotation), which is what project locations use.
    /// </summary>
    public sealed class RigidTransform
    {
        public RigidTransform(Vec3 origin, Vec3 basisX, Vec3 basisY, Vec3 basisZ)
        {
            Origin = origin;
            BasisX = basisX;
            BasisY = basisY;
            BasisZ = basisZ;
        }

        public Vec3 Origin { get; }
        public Vec3 BasisX { get; }
        public Vec3 BasisY { get; }
        public Vec3 BasisZ { get; }

        /// <summary>Rotation about Z by <paramref name="angleRad"/> (counter-clockwise), then translation.</summary>
        public static RigidTransform PlanRotation(double angleRad, Vec3 translation)
        {
            double c = Math.Cos(angleRad), s = Math.Sin(angleRad);
            return new RigidTransform(translation, new Vec3(c, s, 0), new Vec3(-s, c, 0), new Vec3(0, 0, 1));
        }

        public Vec3 OfPoint(Vec3 p) => Origin + OfVector(p);

        public Vec3 OfVector(Vec3 v) => BasisX * v.X + BasisY * v.Y + BasisZ * v.Z;

        /// <summary>Inverse of an orthonormal transform (transposed rotation).</summary>
        public RigidTransform Inverse
        {
            get
            {
                var bx = new Vec3(BasisX.X, BasisY.X, BasisZ.X);
                var by = new Vec3(BasisX.Y, BasisY.Y, BasisZ.Y);
                var bz = new Vec3(BasisX.Z, BasisY.Z, BasisZ.Z);
                var rotOnly = new RigidTransform(new Vec3(0, 0, 0), bx, by, bz);
                return new RigidTransform(rotOnly.OfVector(Origin) * -1.0, bx, by, bz);
            }
        }

        /// <summary>Plan rotation of the X axis, degrees in (-180, 180].</summary>
        public double PlanAngleDegrees => NormalizeDegrees(Math.Atan2(BasisX.Y, BasisX.X) * 180.0 / Math.PI);

        public bool IsAlmostEqualTo(RigidTransform other, double tolerance)
        {
            return Origin.DistanceTo(other.Origin) <= tolerance
                && BasisX.DistanceTo(other.BasisX) <= 1e-9
                && BasisY.DistanceTo(other.BasisY) <= 1e-9
                && BasisZ.DistanceTo(other.BasisZ) <= 1e-9;
        }

        public static double NormalizeDegrees(double deg)
        {
            deg %= 360.0;
            if (deg <= -180.0) deg += 360.0;
            if (deg > 180.0) deg -= 360.0;
            return deg;
        }
    }

    /// <summary>
    /// A point whose shared coordinates Revit reports itself (project base point, survey point):
    /// used to check the internal → shared transform before anything is placed.
    /// </summary>
    public sealed class ReferencePoint
    {
        public ReferencePoint(string name, Vec3 internalFeet, Vec3 reportedSharedFeet)
        {
            Name = name;
            InternalFeet = internalFeet;
            ReportedSharedFeet = reportedSharedFeet;
        }

        public string Name { get; }

        /// <summary>Position in Revit's internal coordinates (feet).</summary>
        public Vec3 InternalFeet { get; }

        /// <summary>E/W, N/S, Elev as Revit shows them (shared coordinates, feet).</summary>
        public Vec3 ReportedSharedFeet { get; }
    }

    /// <summary>Outcome of <see cref="SharedCoordinateMath.Resolve"/>.</summary>
    public sealed class SharedCoordinateResolution
    {
        /// <summary>Internal → shared (feet → feet); null when refused.</summary>
        public RigidTransform InternalToShared { get; internal set; }

        /// <summary>Why the transform was refused (Dutch, for the user); null when OK.</summary>
        public string Error { get; internal set; }

        /// <summary>Per check one line, for the report.</summary>
        public List<string> Checks { get; } = new List<string>();

        /// <summary>The total transform had to be inverted (Revit gave shared → internal).</summary>
        public bool UsedInverse { get; internal set; }

        public bool IsValid => InternalToShared != null && Error == null;
    }

    /// <summary>
    /// The arithmetic of the "Modelpunt op coördinaat" button, without Revit (unit tested).
    /// Units: everything in Revit feet unless the name says metres.
    /// </summary>
    /// <remarks>
    /// Revit's ProjectLocation.GetTotalTransform() is documented loosely; which way it maps is
    /// measured instead of assumed: the transform internal → shared must send the internal
    /// origin to the location's own project position (GetProjectPosition(0,0,0)). Then the
    /// project base point and the survey point must land, within 1 mm, on the E/W, N/S and
    /// Elev that Revit shows for them, and the rotation must equal the location's angle.
    /// Anything else is refused: a coordinate point in the wrong place is worse than none.
    /// </remarks>
    public static class SharedCoordinateMath
    {
        public const double FeetPerMetre = 1.0 / 0.3048;
        public const double MmPerFoot = 304.8;
        public const double OneMmInFeet = 1.0 / 304.8;

        /// <summary>Allowed deviation of a reference point, mm.</summary>
        public const double ToleranceMm = 1.0;

        /// <summary>Allowed difference between rotation and location angle, degrees.</summary>
        public const double AngleToleranceDeg = 1e-4;

        /// <summary>
        /// Determines the internal → shared transform from Revit's total transform of the active
        /// location and checks it against the reference points.
        /// </summary>
        /// <param name="totalTransform">ProjectLocation.GetTotalTransform().</param>
        /// <param name="sharedOfInternalOrigin">ProjectLocation.GetProjectPosition(XYZ.Zero) as (E/W, N/S, Elev), feet.</param>
        /// <param name="locationAngleRad">ProjectPosition.Angle (angle to true north), radians.</param>
        /// <param name="references">Project base point and survey point.</param>
        public static SharedCoordinateResolution Resolve(
            RigidTransform totalTransform,
            Vec3 sharedOfInternalOrigin,
            double locationAngleRad,
            IList<ReferencePoint> references)
        {
            var result = new SharedCoordinateResolution();
            if (totalTransform == null)
            {
                result.Error = "Revit gaf geen transformatie voor de actieve locatie.";
                return result;
            }

            references = references ?? new List<ReferencePoint>();

            var forward = totalTransform;
            var backward = totalTransform.Inverse;
            bool forwardOrigin = forward.Origin.DistanceTo(sharedOfInternalOrigin) <= OneMmInFeet;
            bool backwardOrigin = backward.Origin.DistanceTo(sharedOfInternalOrigin) <= OneMmInFeet;

            var candidates = new List<Tuple<RigidTransform, bool>>();
            if (forwardOrigin) candidates.Add(Tuple.Create(forward, false));
            if (backwardOrigin) candidates.Add(Tuple.Create(backward, true));

            if (candidates.Count == 0)
            {
                result.Error =
                    "De transformatie van de actieve locatie past niet bij zijn eigen projectpositie " +
                    "(interne oorsprong → " + FormatMetres(sharedOfInternalOrigin) + "). Richting niet vast te stellen.";
                return result;
            }

            // Keep the candidates that reproduce every reference point and the location angle.
            var passing = new List<Tuple<RigidTransform, bool, List<string>>>();
            foreach (var c in candidates)
            {
                var lines = new List<string>();
                if (Check(c.Item1, locationAngleRad, references, lines))
                {
                    passing.Add(Tuple.Create(c.Item1, c.Item2, lines));
                }
                else if (candidates.Count == 1)
                {
                    result.Checks.AddRange(lines);
                }
            }

            if (passing.Count == 0)
            {
                if (candidates.Count > 1)
                {
                    // Show the checks of the origin-preferred direction.
                    var lines = new List<string>();
                    Check(candidates[0].Item1, locationAngleRad, references, lines);
                    result.Checks.AddRange(lines);
                }
                result.Error =
                    "De gedeelde coördinaten zijn niet consistent: basispunt en/of survey point komen " +
                    "niet binnen " + ToleranceMm.ToString("0", CultureInfo.InvariantCulture) +
                    " mm uit op hun eigen E/W, N/S en Elev.";
                return result;
            }

            if (passing.Count == 2 && !passing[0].Item1.IsAlmostEqualTo(passing[1].Item1, OneMmInFeet / 100.0))
            {
                result.Checks.AddRange(passing[0].Item3);
                result.Error =
                    "De draairichting van de gedeelde coördinaten is niet eenduidig vast te stellen " +
                    "(interne oorsprong, basispunt en survey point vallen samen).";
                return result;
            }

            result.InternalToShared = passing[0].Item1;
            result.UsedInverse = passing[0].Item2;
            result.Checks.AddRange(passing[0].Item3);
            return result;
        }

        private static bool Check(RigidTransform internalToShared, double locationAngleRad, IList<ReferencePoint> references, List<string> lines)
        {
            bool ok = true;

            double transformAngle = Math.Abs(internalToShared.PlanAngleDegrees);
            double locationAngle = Math.Abs(RigidTransform.NormalizeDegrees(locationAngleRad * 180.0 / Math.PI));
            bool angleOk = Math.Abs(transformAngle - locationAngle) <= AngleToleranceDeg;
            lines.Add(string.Format(CultureInfo.InvariantCulture,
                "{0} draaiing: transformatie {1:0.0000}°, locatie (ware noorden) {2:0.0000}°",
                angleOk ? "OK " : "FOUT", transformAngle, locationAngle));
            ok &= angleOk;

            foreach (var r in references)
            {
                var computed = internalToShared.OfPoint(r.InternalFeet);
                double dMm = computed.DistanceTo(r.ReportedSharedFeet) * MmPerFoot;
                bool pointOk = dMm <= ToleranceMm;
                lines.Add(string.Format(CultureInfo.InvariantCulture,
                    "{0} {1}: berekend {2}, Revit {3}, verschil {4:0.000} mm",
                    pointOk ? "OK " : "FOUT", r.Name, FormatMetres(computed), FormatMetres(r.ReportedSharedFeet), dMm));
                ok &= pointOk;
            }
            return ok;
        }

        /// <summary>RD/NAP metres → Revit internal feet.</summary>
        public static Vec3 SharedMetresToInternal(RigidTransform internalToShared, double xM, double yM, double zM)
        {
            var sharedFeet = new Vec3(xM, yM, zM) * FeetPerMetre;
            return internalToShared.Inverse.OfPoint(sharedFeet);
        }

        /// <summary>Revit internal feet → RD/NAP metres.</summary>
        public static Vec3 InternalToSharedMetres(RigidTransform internalToShared, Vec3 internalFeet)
        {
            return internalToShared.OfPoint(internalFeet) / FeetPerMetre;
        }

        /// <summary>"(x, y, z) m" with 3 decimals, from feet.</summary>
        public static string FormatMetres(Vec3 feet)
        {
            var m = feet / FeetPerMetre;
            return string.Format(CultureInfo.InvariantCulture, "({0:0.000}; {1:0.000}; {2:0.000}) m", m.X, m.Y, m.Z);
        }

        /// <summary>
        /// What the Dynamo script v1.1 computed (for comparison in tests and reports): it rotates
        /// "base point (internal) + (P − base point shared)" about the internal origin by the
        /// project rotation, instead of rotating only the offset from the base point.
        /// </summary>
        public static Vec3 DynamoScriptInternal(Vec3 basePointInternal, Vec3 basePointShared, Vec3 pointShared, double rotationRad)
        {
            var moved = basePointInternal + (pointShared - basePointShared);
            return RigidTransform.PlanRotation(rotationRad, new Vec3(0, 0, 0)).OfPoint(moved);
        }

        /// <summary>Horizontal distance between two shared points given in metres.</summary>
        public static double HorizontalDistanceMetres(double x1, double y1, double x2, double y2)
        {
            double dx = x1 - x2, dy = y1 - y2;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
