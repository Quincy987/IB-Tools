using System;
using System.Collections.Generic;
using FoundationProjectionToggle.Services;
using Xunit;

namespace IBTools.Tests
{
    /// <summary>
    /// A project is simulated the way Revit sets it up: shared = R(angle)·internal + offset.
    /// Revit may hand out that transform or its inverse; the math must measure which.
    /// </summary>
    public class SharedCoordinateMathTests
    {
        private const double Ft = SharedCoordinateMath.FeetPerMetre;

        private static Vec3 M(double x, double y, double z) => new Vec3(x, y, z) * Ft;

        private sealed class FakeProject
        {
            public RigidTransform InternalToShared;
            public double AngleRad;
            public Vec3 BasePointInternal;
            public Vec3 SurveyInternal;

            public Vec3 SharedOf(Vec3 internalFeet) => InternalToShared.OfPoint(internalFeet);

            public List<ReferencePoint> References() => new List<ReferencePoint>
            {
                new ReferencePoint("basispunt", BasePointInternal, SharedOf(BasePointInternal)),
                new ReferencePoint("survey point", SurveyInternal, SharedOf(SurveyInternal)),
            };
        }

        /// <summary>
        /// Base point at <paramref name="bpInternalM"/> (internal, m) with shared RD
        /// <paramref name="bpRdM"/>, true north rotated <paramref name="angleDeg"/>.
        /// </summary>
        private static FakeProject Project(Vec3 bpInternalM, Vec3 bpRdM, double angleDeg, Vec3? surveyInternalM = null)
        {
            double a = angleDeg * Math.PI / 180.0;
            var rot = RigidTransform.PlanRotation(a, new Vec3(0, 0, 0));
            var bpInt = bpInternalM * Ft;
            // offset chosen so that the base point lands on its RD value
            var offset = bpRdM * Ft - rot.OfPoint(bpInt);
            return new FakeProject
            {
                InternalToShared = RigidTransform.PlanRotation(a, offset),
                AngleRad = a,
                BasePointInternal = bpInt,
                SurveyInternal = (surveyInternalM ?? new Vec3(0, 0, 0)) * Ft,
            };
        }

        public static IEnumerable<object[]> Cases()
        {
            // base point internal (m), base point RD (m), angle
            yield return new object[] { 0.0, 0.0, 0.0, 0.0 };
            yield return new object[] { 0.0, 0.0, 0.0, 10.0 };
            yield return new object[] { 100.0, 0.0, 0.0, 10.0 };
            yield return new object[] { 35.0, -60.0, 2.5, -23.7 };
            yield return new object[] { -250.0, 400.0, -1.0, 179.0 };
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void Resolves_direction_from_either_transform_and_round_trips(double bpx, double bpy, double bpz, double angleDeg)
        {
            var p = Project(new Vec3(bpx, bpy, bpz), new Vec3(121000, 487000, 1.0), angleDeg, new Vec3(12, -7, 0));
            var origin = p.SharedOf(new Vec3(0, 0, 0));

            foreach (var given in new[] { p.InternalToShared, p.InternalToShared.Inverse })
            {
                var r = SharedCoordinateMath.Resolve(given, origin, p.AngleRad, p.References());
                Assert.True(r.IsValid, r.Error + " | " + string.Join(" | ", r.Checks));
                Assert.True(r.InternalToShared.IsAlmostEqualTo(p.InternalToShared, 1e-9));

                // RD point → internal → back to RD: exact
                var target = new Vec3(121234.567, 486789.012, -2.345);
                var intern = SharedCoordinateMath.SharedMetresToInternal(r.InternalToShared, target.X, target.Y, target.Z);
                var back = SharedCoordinateMath.InternalToSharedMetres(r.InternalToShared, intern);
                Assert.True(back.DistanceTo(target) < 1e-6, back.ToString());

                // and that internal point really has that shared position in the project
                Assert.True(p.SharedOf(intern).DistanceTo(target * Ft) * SharedCoordinateMath.MmPerFoot < 1e-3);
            }
        }

        [Fact]
        public void Base_point_maps_to_its_own_RD_value()
        {
            var p = Project(new Vec3(100, 0, 0), new Vec3(121000, 487000, 0), 10.0);
            var r = SharedCoordinateMath.Resolve(p.InternalToShared.Inverse, p.SharedOf(new Vec3(0, 0, 0)), p.AngleRad, p.References());
            Assert.True(r.IsValid);
            Assert.True(r.UsedInverse);

            var intern = SharedCoordinateMath.SharedMetresToInternal(r.InternalToShared, 121000, 487000, 0);
            Assert.True(intern.DistanceTo(p.BasePointInternal) * SharedCoordinateMath.MmPerFoot < 1e-6);
        }

        [Fact]
        public void Dynamo_formula_misses_by_17_m_at_100_m_and_10_degrees()
        {
            // The case from the plan: base point 100 m from the internal origin, 10° rotation.
            var p = Project(new Vec3(100, 0, 0), new Vec3(121000, 487000, 0), 10.0);
            var r = SharedCoordinateMath.Resolve(p.InternalToShared, p.SharedOf(new Vec3(0, 0, 0)), p.AngleRad, p.References());
            var targetShared = M(121050, 487020, 0);

            var ours = r.InternalToShared.Inverse.OfPoint(targetShared);
            var bpShared = p.SharedOf(p.BasePointInternal);

            // Dynamo rotated by "Angle to True North" in whichever direction; neither is right.
            double best = double.MaxValue;
            foreach (var sign in new[] { 1.0, -1.0 })
            {
                var dyn = SharedCoordinateMath.DynamoScriptInternal(p.BasePointInternal, bpShared, targetShared, sign * p.AngleRad);
                best = Math.Min(best, dyn.DistanceTo(ours) / Ft);
            }
            Assert.True(best > 17.0 && best < 18.0, best.ToString());
        }

        [Fact]
        public void Refuses_when_a_reference_point_does_not_match()
        {
            var p = Project(new Vec3(100, 0, 0), new Vec3(121000, 487000, 0), 10.0);
            var refs = p.References();
            // Revit reports the survey point 5 mm off from what the transform gives.
            refs[1] = new ReferencePoint("survey point", refs[1].InternalFeet, refs[1].ReportedSharedFeet + M(0.005, 0, 0));

            var r = SharedCoordinateMath.Resolve(p.InternalToShared, p.SharedOf(new Vec3(0, 0, 0)), p.AngleRad, refs);
            Assert.False(r.IsValid);
            Assert.Contains(r.Checks, l => l.StartsWith("FOUT survey point"));
        }

        [Fact]
        public void Refuses_when_the_angle_does_not_match()
        {
            var p = Project(new Vec3(100, 0, 0), new Vec3(121000, 487000, 0), 10.0);
            var r = SharedCoordinateMath.Resolve(p.InternalToShared, p.SharedOf(new Vec3(0, 0, 0)), p.AngleRad + 0.01, p.References());
            Assert.False(r.IsValid);
            Assert.Contains(r.Checks, l => l.StartsWith("FOUT draaiing"));
        }

        [Fact]
        public void Refuses_when_the_transform_does_not_fit_the_project_position()
        {
            var p = Project(new Vec3(100, 0, 0), new Vec3(121000, 487000, 0), 10.0);
            var wrongOrigin = p.SharedOf(new Vec3(0, 0, 0)) + M(1, 0, 0);
            var r = SharedCoordinateMath.Resolve(p.InternalToShared, wrongOrigin, p.AngleRad, p.References());
            Assert.False(r.IsValid);
            Assert.Null(r.InternalToShared);
        }

        [Fact]
        public void Refuses_when_rotation_direction_cannot_be_told()
        {
            // Shared origin = internal origin, everything at the origin, but rotated: the two
            // directions differ for every other point and nothing tells which one is right.
            var p = Project(new Vec3(0, 0, 0), new Vec3(0, 0, 0), 10.0);
            var r = SharedCoordinateMath.Resolve(p.InternalToShared, new Vec3(0, 0, 0), p.AngleRad, p.References());
            Assert.False(r.IsValid);
            Assert.Contains("eenduidig", r.Error);
        }

        [Fact]
        public void Unrotated_project_without_offset_is_fine()
        {
            var p = Project(new Vec3(0, 0, 0), new Vec3(0, 0, 0), 0.0);
            var r = SharedCoordinateMath.Resolve(p.InternalToShared, new Vec3(0, 0, 0), 0.0, p.References());
            Assert.True(r.IsValid, r.Error);
        }

        [Fact]
        public void Inverse_is_exact()
        {
            var t = RigidTransform.PlanRotation(0.3, M(5, -7, 2));
            var q = M(13, 17, -19);
            Assert.True(t.Inverse.OfPoint(t.OfPoint(q)).DistanceTo(q) < 1e-9);
            Assert.True(t.Inverse.Inverse.IsAlmostEqualTo(t, 1e-9));
        }

        [Fact]
        public void Horizontal_distance()
        {
            Assert.Equal(5000.0, SharedCoordinateMath.HorizontalDistanceMetres(3000, 4000, 0, 0), 9);
        }
    }
}
