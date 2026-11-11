using System;

namespace FoundationProjectionToggle.Core.PileCoordinates
{
    /// <summary>
    /// 2D affine transform in plan (XY): p' = Origin + X * BasisX + Y * BasisY.
    /// Revit-free mirror of the XY part of an Autodesk.Revit.DB.Transform, so the coordinate
    /// math can be unit-tested without Revit. Units are whatever the caller uses (Revit: feet).
    /// </summary>
    public sealed class PlanarTransform
    {
        public double BasisXx { get; }
        public double BasisXy { get; }
        public double BasisYx { get; }
        public double BasisYy { get; }
        public double OriginX { get; }
        public double OriginY { get; }

        public PlanarTransform(double basisXx, double basisXy, double basisYx, double basisYy, double originX, double originY)
        {
            BasisXx = basisXx;
            BasisXy = basisXy;
            BasisYx = basisYx;
            BasisYy = basisYy;
            OriginX = originX;
            OriginY = originY;
        }

        public static PlanarTransform Identity => new PlanarTransform(1, 0, 0, 1, 0, 0);

        /// <summary>
        /// Rotation by <paramref name="angleRadians"/> (counter-clockwise) followed by a
        /// translation. Mainly for tests.
        /// </summary>
        public static PlanarTransform FromRotationAndTranslation(double angleRadians, double translateX, double translateY)
        {
            var c = Math.Cos(angleRadians);
            var s = Math.Sin(angleRadians);
            return new PlanarTransform(c, s, -s, c, translateX, translateY);
        }

        public void Apply(double x, double y, out double resultX, out double resultY)
        {
            resultX = OriginX + x * BasisXx + y * BasisYx;
            resultY = OriginY + x * BasisXy + y * BasisYy;
        }

        public PlanarTransform Inverse()
        {
            var det = BasisXx * BasisYy - BasisYx * BasisXy;
            if (Math.Abs(det) < 1e-12)
            {
                throw new InvalidOperationException("Transform is not invertible (determinant 0).");
            }

            // Inverse of the 2x2 matrix [[a, b], [c, d]] with columns BasisX = (a, c), BasisY = (b, d).
            var a = BasisXx;
            var b = BasisYx;
            var c = BasisXy;
            var d = BasisYy;
            var ia = d / det;
            var ib = -b / det;
            var ic = -c / det;
            var id = a / det;

            // -M^-1 * origin
            var ox = -(ia * OriginX + ib * OriginY);
            var oy = -(ic * OriginX + id * OriginY);

            // Columns of the inverse matrix: BasisX' = (ia, ic), BasisY' = (ib, id).
            return new PlanarTransform(ia, ic, ib, id, ox, oy);
        }

        /// <summary>True when this transform does (almost) nothing: model not on coordinates.</summary>
        public bool IsIdentity(double tolerance)
        {
            return Math.Abs(BasisXx - 1) < 1e-9 && Math.Abs(BasisXy) < 1e-9
                && Math.Abs(BasisYx) < 1e-9 && Math.Abs(BasisYy - 1) < 1e-9
                && Math.Abs(OriginX) < tolerance && Math.Abs(OriginY) < tolerance;
        }
    }
}
