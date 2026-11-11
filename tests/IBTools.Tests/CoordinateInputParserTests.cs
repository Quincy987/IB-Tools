using FoundationProjectionToggle.Services;
using Xunit;

namespace IBTools.Tests
{
    public class CoordinateInputParserTests
    {
        [Theory]
        [InlineData("122285.79", 122285.79)]
        [InlineData("122285,79", 122285.79)]
        [InlineData("  -3,5 ", -3.5)]
        [InlineData("0", 0.0)]
        public void TryParseNumber_accepts_point_and_comma(string text, double expected)
        {
            Assert.True(CoordinateInputParser.TryParseNumber(text, out var v));
            Assert.Equal(expected, v, 9);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("abc")]
        [InlineData("1,2,3")]
        [InlineData("12 34")]
        [InlineData("1.234,5")]
        public void TryParseNumber_rejects_invalid(string text)
        {
            Assert.False(CoordinateInputParser.TryParseNumber(text, out _));
        }

        [Fact]
        public void Line_with_spaces_and_decimal_points()
        {
            var r = CoordinateInputParser.TryParseLine("122285.790 486175.940");
            Assert.NotNull(r);
            Assert.Equal(122285.79, r.X, 6);
            Assert.Equal(486175.94, r.Y, 6);
            Assert.Null(r.Z);
            Assert.False(r.WasMillimetres);
        }

        [Fact]
        public void Line_with_decimal_commas_and_height()
        {
            var r = CoordinateInputParser.TryParseLine("122285,790 486175,940 -1,25");
            Assert.NotNull(r);
            Assert.Equal(122285.79, r.X, 6);
            Assert.Equal(486175.94, r.Y, 6);
            Assert.Equal(-1.25, r.Z.Value, 6);
        }

        [Fact]
        public void Line_with_tab_and_semicolon()
        {
            Assert.NotNull(CoordinateInputParser.TryParseLine("122285,79\t486175,94"));
            var r = CoordinateInputParser.TryParseLine("122285,79;486175,94;0,5");
            Assert.NotNull(r);
            Assert.Equal(0.5, r.Z.Value, 6);
        }

        [Fact]
        public void Comma_is_separator_only_without_spaces()
        {
            var r = CoordinateInputParser.TryParseLine("122285.79,486175.94");
            Assert.NotNull(r);
            Assert.Equal(122285.79, r.X, 6);
            Assert.Equal(486175.94, r.Y, 6);

            // Decimal commas with comma separators are ambiguous: four numbers, refused.
            Assert.Null(CoordinateInputParser.TryParseLine("122285,79,486175,94"));
        }

        [Fact]
        public void Labels_are_ignored()
        {
            var r = CoordinateInputParser.TryParseLine("X: 122285,790  Y: 486175,940");
            Assert.NotNull(r);
            Assert.Equal(122285.79, r.X, 6);

            var r2 = CoordinateInputParser.TryParseLine("E/W 122285.79 N/S 486175.94 Elev 1.5");
            Assert.NotNull(r2);
            Assert.Equal(486175.94, r2.Y, 6);
            Assert.Equal(1.5, r2.Z.Value, 6);
        }

        [Fact]
        public void Millimetres_from_the_Dynamo_script_become_metres()
        {
            var r = CoordinateInputParser.TryParseLine("122285790 486175940 1500");
            Assert.NotNull(r);
            Assert.True(r.WasMillimetres);
            Assert.Equal(122285.79, r.X, 6);
            Assert.Equal(486175.94, r.Y, 6);
            Assert.Equal(1.5, r.Z.Value, 6);
        }

        [Theory]
        [InlineData("hello world")]
        [InlineData("1 2")]
        [InlineData("122285.79")]
        [InlineData("1 2 3 4")]
        [InlineData("486175.94 122285.79")] // swapped x/y: outside RD
        [InlineData("1222857900 4861759400")] // neither m nor mm
        public void Non_coordinates_are_refused(string text)
        {
            Assert.Null(CoordinateInputParser.TryParseLine(text));
        }

        [Fact]
        public void Long_clipboard_text_is_ignored()
        {
            var text = "122285.79 486175.94 " + new string('x', 300);
            Assert.Null(CoordinateInputParser.TryParseLine(text));
        }

        [Fact]
        public void Rd_bounds()
        {
            Assert.True(CoordinateInputParser.IsWithinRd(121000, 487000));
            Assert.False(CoordinateInputParser.IsWithinRd(0, 0));
            Assert.False(CoordinateInputParser.IsWithinRd(487000, 121000));
        }

        [Fact]
        public void Format_uses_point_and_three_decimals()
        {
            Assert.Equal("122285.790", CoordinateInputParser.FormatMetres(122285.79));
        }
    }
}
