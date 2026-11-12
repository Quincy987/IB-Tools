using System;
using System.Collections.Generic;
using System.Linq;
using FoundationProjectionToggle.Core.Remmingwerk;
using Xunit;

namespace FoundationProjectionToggle.Core.Tests
{
    public class RemmingwerkTests
    {
        private const double Tol = 1e-6;

        private static SteelDimensions Hea450AndTube610()
        {
            return new SteelDimensions { GordingHoogteMm = 440, GordingBreedteMm = 300, StijlHoogteMm = 440, PaalDiameterMm = 610 };
        }

        private static RemmingwerkPlan PlanLine(Pt2 a, Pt2 b, RemmingwerkSettings s = null, double z = 0, double nap = 0)
        {
            var lines = new List<RemmingwerkLine> { new RemmingwerkLine(new PlanLine(a, b), z, null) };
            return RemmingwerkPlanner.Plan(lines, s ?? new RemmingwerkSettings(), Hea450AndTube610(), nap);
        }

        // ---------------------------------------------------------------- stations

        [Fact]
        public void Stations_20000_gives_5_points_from_1500()
        {
            var s = RemmingwerkPlanner.Stations(20000, 4250);
            Assert.Equal(new[] { 1500.0, 5750, 10000, 14250, 18500 }, s.ToArray());
        }

        [Fact]
        public void Stations_17000_puts_piles_on_the_ends()
        {
            var s = RemmingwerkPlanner.Stations(17000, 4250);
            Assert.Equal(new[] { 0.0, 4250, 8500, 12750, 17000 }, s.ToArray());
        }

        [Fact]
        public void Stations_short_line_gives_one_point_in_the_middle()
        {
            var s = RemmingwerkPlanner.Stations(3000, 4250);
            Assert.Single(s);
            Assert.Equal(1500, s[0], 6);
        }

        [Fact]
        public void Stations_survive_rounding_of_a_multiple()
        {
            // 17000 read back from feet is never exactly 17000.
            var s = RemmingwerkPlanner.Stations(16999.9999999, 4250);
            Assert.Equal(5, s.Count);
            Assert.True(s.Last() <= 16999.9999999);
        }

        // ---------------------------------------------------------------- offsets and heights

        [Fact]
        public void Offsets_are_0_160_380_1105_to_the_right()
        {
            var plan = PlanLine(new Pt2(0, 0), new Pt2(20000, 0));
            Assert.True(plan.IsValid);

            // Line along +X: right is -Y.
            var post = plan.Posts.First();
            var stijl = plan.Columns.First(c => c.Part == RemmingwerkPart.StalenStijl && c.Index == 0);
            var paal = plan.Columns.First(c => c.Part == RemmingwerkPart.Buispaal && c.Index == 0);
            var gording = plan.Beams.First(b => b.Part == RemmingwerkPart.StalenGording);
            var wrijf = plan.Beams.First(b => b.Part == RemmingwerkPart.Wrijfgording);

            Assert.Equal(0, post.Position.Y, 6);
            Assert.Equal(0, wrijf.Curve.Start.Y, 6);
            Assert.Equal(-160, gording.Curve.Start.Y, 6);
            Assert.Equal(-380, stijl.Position.Y, 6);
            Assert.Equal(-1105, paal.Position.Y, 6);
            Assert.Equal(1500, paal.Position.X, 6);
            Assert.Equal(1500, post.Position.X, 6);
        }

        [Fact]
        public void Heights_follow_the_script_with_line_on_NAP()
        {
            var plan = PlanLine(new Pt2(0, 0), new Pt2(20000, 0));
            var stijl = plan.Columns.First(c => c.Part == RemmingwerkPart.StalenStijl);
            var paal = plan.Columns.First(c => c.Part == RemmingwerkPart.Buispaal);
            var post = plan.Posts.First();

            Assert.Equal(0, stijl.BottomMm, 6);
            Assert.Equal(1400, stijl.TopMm, 6);
            Assert.Equal(-20000, paal.BottomMm, 6);
            Assert.Equal(1800, paal.TopMm, 6); // 1400 + 300/2 + 250
            Assert.Equal(21800, paal.LengthMm, 6);
            Assert.Equal(0, post.ZMm, 6);
            Assert.Equal(1400, post.LengteMm, 6);
            Assert.Equal(300, post.HoogteMm, 6);
            Assert.Equal(160, post.BreedteMm, 6);

            var rows = plan.Beams.Where(b => b.Part == RemmingwerkPart.Wrijfgording).Select(b => b.ZMm).ToArray();
            Assert.Equal(new[] { 0.0, 700, 1400 }, rows);
            Assert.All(plan.Beams.Where(b => b.Part == RemmingwerkPart.Wrijfgording), b =>
            {
                Assert.True(b.ZJustificationOrigin);
                Assert.Equal(180, b.CrossSectionRotationDegrees, 6);
                Assert.Equal(300, b.WrijfHoogteMm, 6);
                Assert.Equal(160, b.WrijfBreedteMm, 6);
            });
            Assert.All(plan.Beams.Where(b => b.Part == RemmingwerkPart.StalenGording), b =>
            {
                Assert.False(b.ZJustificationOrigin);
                Assert.Equal(90, b.CrossSectionRotationDegrees, 6);
            });
        }

        [Fact]
        public void Pile_tip_is_relative_to_the_NAP_level_the_rest_to_the_line()
        {
            // Line 1 m above the NAP level at internal z = 500.
            var plan = PlanLine(new Pt2(0, 0), new Pt2(20000, 0), z: 1500, nap: 500);
            var paal = plan.Columns.First(c => c.Part == RemmingwerkPart.Buispaal);
            var stijl = plan.Columns.First(c => c.Part == RemmingwerkPart.StalenStijl);
            Assert.Equal(500 - 20000, paal.BottomMm, 6);
            Assert.Equal(1500 + 1800, paal.TopMm, 6);
            Assert.Equal(1500, stijl.BottomMm, 6);
            Assert.Contains(plan.Warnings, w => w.Contains("t.o.v. het NAP-level"));
        }

        [Fact]
        public void Counts_for_20m_line()
        {
            var plan = PlanLine(new Pt2(0, 0), new Pt2(20000, 0));
            Assert.Equal(3, plan.Count(RemmingwerkPart.Wrijfgording));
            Assert.Equal(3, plan.Count(RemmingwerkPart.StalenGording));
            Assert.Equal(5, plan.Count(RemmingwerkPart.StalenStijl));
            Assert.Equal(5, plan.Count(RemmingwerkPart.Buispaal));
            Assert.Equal(5, plan.Count(RemmingwerkPart.Wrijfstijl));
            Assert.StartsWith("5 palen à 21,8 m, eerste op 1,500 m", plan.Overview());
        }

        // ---------------------------------------------------------------- side and direction

        [Fact]
        public void Left_side_mirrors_everything_to_the_other_side()
        {
            var right = PlanLine(new Pt2(0, 0), new Pt2(20000, 0));
            var left = PlanLine(new Pt2(0, 0), new Pt2(20000, 0), new RemmingwerkSettings { PilesLeft = true });

            var pr = right.Columns.Where(c => c.Part == RemmingwerkPart.Buispaal).Select(c => c.Position).OrderBy(p => p.X).ToList();
            var pl = left.Columns.Where(c => c.Part == RemmingwerkPart.Buispaal).Select(c => c.Position).OrderBy(p => p.X).ToList();
            Assert.Equal(pr.Count, pl.Count);
            for (int i = 0; i < pr.Count; i++)
            {
                Assert.Equal(pr[i].X, pl[i].X, 6);
                Assert.Equal(-pr[i].Y, pl[i].Y, 6);
            }
            Assert.All(left.Posts, p => Assert.Equal(180, p.RotationDegrees, 6));
            Assert.All(right.Posts, p => Assert.Equal(0, p.RotationDegrees, 6));
        }

        [Fact]
        public void Reversed_line_with_left_side_equals_original_with_right_side()
        {
            var a = PlanLine(new Pt2(0, 0), new Pt2(20000, 0));
            var b = PlanLine(new Pt2(20000, 0), new Pt2(0, 0), new RemmingwerkSettings { PilesLeft = true });
            var pa = a.Columns.Select(c => c.Position).OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
            var pb = b.Columns.Select(c => c.Position).OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
            for (int i = 0; i < pa.Count; i++)
            {
                Assert.True(pa[i].DistanceTo(pb[i]) < 1e-6);
            }
            Assert.Equal(a.Posts.Select(p => p.RotationDegrees).Distinct().Single(), b.Posts.Select(p => p.RotationDegrees).Distinct().Single(), 6);
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(90, 90)]
        [InlineData(180, 180)]
        [InlineData(-90, 270)]
        [InlineData(30, 30)]
        [InlineData(-30, 330)]
        public void Rotation_is_the_line_direction_in_0_360(double directionDeg, double expected)
        {
            var r = directionDeg * Math.PI / 180;
            var plan = PlanLine(new Pt2(0, 0), new Pt2(20000 * Math.Cos(r), 20000 * Math.Sin(r)));
            Assert.All(plan.Posts, p => Assert.Equal(expected, p.RotationDegrees, 6));
            Assert.All(plan.Columns, c => Assert.Equal(expected, c.RotationDegrees, 6));

            // Right of the direction, perpendicular.
            var paal = plan.Columns.First(c => c.Part == RemmingwerkPart.Buispaal && c.Index == 0);
            var post = plan.Posts.First(p => p.Index == 0);
            var d = paal.Position - post.Position;
            Assert.Equal(1105, d.Length, 6);
            Assert.Equal(1105 * Math.Sin(r), d.X, 6);
            Assert.Equal(-1105 * Math.Cos(r), d.Y, 6);
        }

        [Fact]
        public void Line_north_puts_piles_east_like_Dynamo()
        {
            // DynamoCLI: Line (0,0)->(0,1000), Offset(100) -> x = +100.
            var plan = PlanLine(new Pt2(0, 0), new Pt2(0, 20000));
            Assert.All(plan.Columns, c => Assert.True(c.Position.X > 0));
        }

        // ---------------------------------------------------------------- arcs

        [Fact]
        public void Arc_counter_clockwise_puts_everything_outward_and_radial()
        {
            // Quarter circle R = 30 m, CCW (Dynamo: offset outward as well).
            var arc = new PlanArc(new Pt2(0, 0), 30000, 0, Math.PI / 2);
            var lines = new List<RemmingwerkLine> { new RemmingwerkLine(arc, 0, null) };
            var plan = RemmingwerkPlanner.Plan(lines, new RemmingwerkSettings(), Hea450AndTube610(), 0);
            Assert.True(plan.IsValid);

            Assert.Equal(arc.Length, plan.Lines[0].LengthMm, 6);
            Assert.True(plan.Lines[0].IsArc);
            foreach (var paal in plan.Columns.Where(c => c.Part == RemmingwerkPart.Buispaal))
            {
                var post = plan.Posts.First(p => p.Index == paal.Index);
                Assert.Equal(30000, post.Position.Length, 6);
                Assert.Equal(30000 + 1105, paal.Position.Length, 6);
                // Same radial: post and pile on one line through the centre.
                var cross = post.Position.X * paal.Position.Y - post.Position.Y * paal.Position.X;
                Assert.Equal(0, cross, 3);
            }
            var gording = (PlanArc)plan.Beams.First(b => b.Part == RemmingwerkPart.StalenGording).Curve;
            Assert.Equal(30160, gording.Radius, 6);
            var wrijf = (PlanArc)plan.Beams.First(b => b.Part == RemmingwerkPart.Wrijfgording).Curve;
            Assert.Equal(30000, wrijf.Radius, 6);

            // Stations by arc length, centred.
            var expected = RemmingwerkPlanner.Stations(arc.Length, 4250);
            Assert.Equal(expected.Count, plan.Posts.Count);
            Assert.Equal(expected[0], plan.Lines[0].FirstStationMm, 6);

            // Rotation = tangent direction: at angle a on a CCW arc the tangent points to a + 90°.
            var first = plan.Posts.First();
            var a = Math.Atan2(first.Position.Y, first.Position.X) * 180 / Math.PI;
            Assert.Equal(Angles.Normalize(a + 90), first.RotationDegrees, 6);
        }

        [Fact]
        public void Arc_clockwise_puts_everything_inward_right_of_the_drawing_direction()
        {
            var arc = new PlanArc(new Pt2(0, 0), 30000, Math.PI / 2, -Math.PI / 2);
            var lines = new List<RemmingwerkLine> { new RemmingwerkLine(arc, 0, null) };
            var plan = RemmingwerkPlanner.Plan(lines, new RemmingwerkSettings(), Hea450AndTube610(), 0);
            Assert.True(plan.IsValid);
            Assert.All(plan.Columns.Where(c => c.Part == RemmingwerkPart.Buispaal), c => Assert.Equal(30000 - 1105, c.Position.Length, 6));
            Assert.Equal(29840, ((PlanArc)plan.Beams.First(b => b.Part == RemmingwerkPart.StalenGording).Curve).Radius, 6);
        }

        [Fact]
        public void Arc_too_tight_for_the_inner_side_is_refused()
        {
            var arc = new PlanArc(new Pt2(0, 0), 1000, 0, -Math.PI / 2);
            var lines = new List<RemmingwerkLine> { new RemmingwerkLine(arc, 0, "12") };
            var plan = RemmingwerkPlanner.Plan(lines, new RemmingwerkSettings(), Hea450AndTube610(), 0);
            Assert.False(plan.IsValid);
            Assert.Contains(plan.Errors, e => e.Contains("te krap"));
            Assert.Empty(plan.Columns);
            Assert.Empty(plan.Beams);
        }

        [Fact]
        public void Arc_from_ends_and_reverse_round_trip()
        {
            var arc = PlanArc.FromEnds(new Pt2(0, 0), new Pt2(1000, 0), new Pt2(0, 1000), true);
            Assert.Equal(Math.PI / 2, arc.Sweep, 9);
            Assert.True(arc.End.DistanceTo(new Pt2(0, 1000)) < 1e-6);
            var rev = arc.Reversed();
            Assert.True(rev.Start.DistanceTo(new Pt2(0, 1000)) < 1e-6);
            Assert.True(rev.End.DistanceTo(new Pt2(1000, 0)) < 1e-6);
            var cw = PlanArc.FromEnds(new Pt2(0, 0), new Pt2(0, 1000), new Pt2(1000, 0), false);
            Assert.Equal(-Math.PI / 2, cw.Sweep, 9);
            Assert.True(cw.PointAt(cw.Length / 2).DistanceTo(new Pt2(707.1068, 707.1068)) < 1e-3);
        }

        // ---------------------------------------------------------------- refusals

        [Theory]
        [InlineData(1)]
        [InlineData(0)]
        public void Fewer_than_two_rows_is_refused(int rows)
        {
            var plan = PlanLine(new Pt2(0, 0), new Pt2(20000, 0), new RemmingwerkSettings { Rows = rows });
            Assert.False(plan.IsValid);
            Assert.Contains(plan.Errors, e => e.Contains("minstens 2"));
            Assert.Empty(plan.Columns);
        }

        [Fact]
        public void Missing_steel_dimensions_are_refused()
        {
            var lines = new List<RemmingwerkLine> { new RemmingwerkLine(new PlanLine(new Pt2(0, 0), new Pt2(20000, 0)), 0, null) };
            var plan = RemmingwerkPlanner.Plan(lines, new RemmingwerkSettings(), new SteelDimensions(), 0);
            Assert.False(plan.IsValid);
            Assert.Equal(3, plan.Errors.Count);
        }

        [Fact]
        public void Pile_tip_above_pile_top_is_refused()
        {
            var plan = PlanLine(new Pt2(0, 0), new Pt2(20000, 0), new RemmingwerkSettings { PileTipNapMm = 5000 });
            Assert.False(plan.IsValid);
            Assert.Contains(plan.Errors, e => e.Contains("paalpunt"));
        }

        [Fact]
        public void Short_line_warns_and_places_one_pile()
        {
            var plan = PlanLine(new Pt2(0, 0), new Pt2(3000, 0));
            Assert.True(plan.IsValid);
            Assert.Equal(1, plan.Count(RemmingwerkPart.Buispaal));
            Assert.Contains(plan.Warnings, w => w.Contains("korter"));
        }

        [Fact]
        public void Two_lines_are_planned_independently()
        {
            var lines = new List<RemmingwerkLine>
            {
                new RemmingwerkLine(new PlanLine(new Pt2(0, 0), new Pt2(20000, 0)), 0, "1"),
                new RemmingwerkLine(new PlanLine(new Pt2(0, 10000), new Pt2(17000, 10000)), 0, "2"),
            };
            var plan = RemmingwerkPlanner.Plan(lines, new RemmingwerkSettings(), Hea450AndTube610(), 0);
            Assert.True(plan.IsValid);
            Assert.Equal(10, plan.Count(RemmingwerkPart.Buispaal));
            Assert.Equal(6, plan.Count(RemmingwerkPart.Wrijfgording));
            Assert.Equal(2, plan.Lines.Count);
        }

        [Fact]
        public void Type_option_label_shows_dimensions()
        {
            var t = new TypeOption { FamilyName = "NLRS_28_SC_TLB_kolom HEA_gen_ISR", TypeName = "kolom HEA450", HoogteMm = 440, BreedteMm = 300 };
            Assert.Equal("NLRS_28_SC_TLB_kolom HEA_gen_ISR: kolom HEA450 (h 440, b 300)", t.Label);
        }
    }
}
