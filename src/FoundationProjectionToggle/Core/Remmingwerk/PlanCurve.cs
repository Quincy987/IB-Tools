using System;

namespace FoundationProjectionToggle.Core.Remmingwerk
{
    /// <summary>A point or vector in plan (XY), in whatever unit the caller uses (here: mm).</summary>
    public struct Pt2
    {
        public Pt2(double x, double y)
        {
            X = x;
            Y = y;
        }

        public double X { get; }
        public double Y { get; }

        public double Length => Math.Sqrt(X * X + Y * Y);

        public static Pt2 operator +(Pt2 a, Pt2 b) => new Pt2(a.X + b.X, a.Y + b.Y);
        public static Pt2 operator -(Pt2 a, Pt2 b) => new Pt2(a.X - b.X, a.Y - b.Y);
        public static Pt2 operator *(Pt2 a, double f) => new Pt2(a.X * f, a.Y * f);

        public double DistanceTo(Pt2 other) => (this - other).Length;

        public override string ToString() => string.Format(System.Globalization.CultureInfo.InvariantCulture, "({0:0.###}; {1:0.###})", X, Y);
    }

    /// <summary>
    /// A horizontal plan curve (straight line or circular arc), walked by arc length s from the
    /// start. "Right" is right of the drawing direction seen from above (tangent × Z), which is
    /// where Dynamo's Curve.Offset with a positive distance puts a line (measured with DynamoCLI).
    /// </summary>
    public abstract class PlanCurve
    {
        public abstract double Length { get; }

        /// <summary>Point at arc length <paramref name="s"/> from the start (0 ≤ s ≤ Length).</summary>
        public abstract Pt2 PointAt(double s);

        /// <summary>Unit tangent (drawing direction) at arc length <paramref name="s"/>.</summary>
        public abstract Pt2 TangentAt(double s);

        /// <summary>Same curve, drawn the other way.</summary>
        public abstract PlanCurve Reversed();

        /// <summary>
        /// The curve moved <paramref name="distance"/> to the right (parallel line, concentric
        /// arc). Null when that is impossible (arc radius would become 0 or negative).
        /// </summary>
        public abstract PlanCurve OffsetRight(double distance);

        public Pt2 Start => PointAt(0);
        public Pt2 End => PointAt(Length);

        /// <summary>Unit normal to the right of the drawing direction.</summary>
        public Pt2 RightNormalAt(double s)
        {
            var t = TangentAt(s);
            return new Pt2(t.Y, -t.X);
        }

        /// <summary>Point at <paramref name="s"/>, moved <paramref name="offset"/> to the right (perpendicular).</summary>
        public Pt2 PointAt(double s, double offset)
        {
            return PointAt(s) + RightNormalAt(s) * offset;
        }

        /// <summary>Drawing direction at <paramref name="s"/>, degrees counter-clockwise from +X, in [0, 360).</summary>
        public double DirectionDegreesAt(double s)
        {
            var t = TangentAt(s);
            return Angles.Normalize(Math.Atan2(t.Y, t.X) * 180.0 / Math.PI);
        }
    }

    public sealed class PlanLine : PlanCurve
    {
        public PlanLine(Pt2 start, Pt2 end)
        {
            if (start.DistanceTo(end) <= 0)
            {
                throw new ArgumentException("Lijn heeft lengte 0.");
            }
            LineStart = start;
            LineEnd = end;
        }

        public Pt2 LineStart { get; }
        public Pt2 LineEnd { get; }

        public override double Length => LineStart.DistanceTo(LineEnd);

        public override Pt2 PointAt(double s)
        {
            return LineStart + TangentAt(s) * s;
        }

        public override Pt2 TangentAt(double s)
        {
            var d = LineEnd - LineStart;
            return d * (1.0 / d.Length);
        }

        public override PlanCurve Reversed() => new PlanLine(LineEnd, LineStart);

        public override PlanCurve OffsetRight(double distance)
        {
            var n = RightNormalAt(0) * distance;
            return new PlanLine(LineStart + n, LineEnd + n);
        }
    }

    /// <summary>
    /// Circular arc: centre, radius, start angle and signed sweep (radians; positive = counter-
    /// clockwise seen from above).
    /// </summary>
    public sealed class PlanArc : PlanCurve
    {
        public PlanArc(Pt2 center, double radius, double startAngle, double sweep)
        {
            if (radius <= 0 || Math.Abs(sweep) <= 0)
            {
                throw new ArgumentException("Boog heeft straal of hoek 0.");
            }
            Center = center;
            Radius = radius;
            StartAngle = startAngle;
            Sweep = sweep;
        }

        /// <summary>Arc from start to end through <paramref name="counterClockwise"/> direction around <paramref name="center"/>.</summary>
        public static PlanArc FromEnds(Pt2 center, Pt2 start, Pt2 end, bool counterClockwise)
        {
            var r = center.DistanceTo(start);
            var a0 = Math.Atan2(start.Y - center.Y, start.X - center.X);
            var a1 = Math.Atan2(end.Y - center.Y, end.X - center.X);
            var sweep = a1 - a0;
            if (counterClockwise)
            {
                while (sweep <= 0) sweep += 2 * Math.PI;
            }
            else
            {
                while (sweep >= 0) sweep -= 2 * Math.PI;
            }
            return new PlanArc(center, r, a0, sweep);
        }

        public Pt2 Center { get; }
        public double Radius { get; }
        public double StartAngle { get; }
        public double Sweep { get; }

        public bool CounterClockwise => Sweep > 0;

        public override double Length => Radius * Math.Abs(Sweep);

        private double AngleAt(double s) => StartAngle + Math.Sign(Sweep) * s / Radius;

        public override Pt2 PointAt(double s)
        {
            var a = AngleAt(s);
            return new Pt2(Center.X + Radius * Math.Cos(a), Center.Y + Radius * Math.Sin(a));
        }

        public override Pt2 TangentAt(double s)
        {
            var a = AngleAt(s);
            return CounterClockwise ? new Pt2(-Math.Sin(a), Math.Cos(a)) : new Pt2(Math.Sin(a), -Math.Cos(a));
        }

        public override PlanCurve Reversed() => new PlanArc(Center, Radius, StartAngle + Sweep, -Sweep);

        public override PlanCurve OffsetRight(double distance)
        {
            // Right of a counter-clockwise arc is outward, of a clockwise arc inward.
            var r = CounterClockwise ? Radius + distance : Radius - distance;
            return r > 1e-6 ? new PlanArc(Center, r, StartAngle, Sweep) : null;
        }

        /// <summary>Point halfway along the arc (for creating a Revit arc through three points).</summary>
        public Pt2 MidPoint => PointAt(Length / 2);
    }

    public static class Angles
    {
        /// <summary>Degrees in [0, 360).</summary>
        public static double Normalize(double degrees)
        {
            var d = degrees % 360.0;
            if (d < 0) d += 360.0;
            if (d >= 360.0 - 1e-9) d = 0;
            return d;
        }
    }
}
