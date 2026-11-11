using System;
using System.IO;
using FoundationProjectionToggle.Services;
using Xunit;

namespace IBTools.Tests
{
    public class FamilyPartAtomReaderTests
    {
        private static byte[] Rfa() =>
            File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "IB_GM_coordinaatpunt.rfa"));

        [Fact]
        public void Reads_the_13_scale_types_of_the_coordinate_point()
        {
            var names = FamilyPartAtomReader.ReadTypeNames(Rfa());
            Assert.Equal(13, names.Count);
            Assert.Equal("Schaal 1op1", names[0]);
            Assert.Contains("Schaal 1op20", names);
            Assert.Equal("Schaal 1op5000", names[12]);
        }

        [Fact]
        public void Empty_or_foreign_bytes_give_no_types()
        {
            Assert.Empty(FamilyPartAtomReader.ReadTypeNames(null));
            Assert.Empty(FamilyPartAtomReader.ReadTypeNames(new byte[] { 1, 2, 3 }));
        }

        [Theory]
        [InlineData("Schaal 1op20", 20)]
        [InlineData("Schaal 1 op 500", 500)]
        [InlineData("Default", null)]
        public void Scale_of_type_name(string name, int? expected)
        {
            Assert.Equal(expected, FamilyPartAtomReader.ScaleOf(name));
        }

        [Fact]
        public void Default_type_choice()
        {
            var names = FamilyPartAtomReader.ReadTypeNames(Rfa());
            Assert.Equal("Schaal 1op50", FamilyPartAtomReader.ChooseDefault(names, null, 50));
            Assert.Equal("Schaal 1op20", FamilyPartAtomReader.ChooseDefault(names, null, 75));
            Assert.Equal("Schaal 1op100", FamilyPartAtomReader.ChooseDefault(names, "Schaal 1op100", 50));
            Assert.Equal("Schaal 1op50", FamilyPartAtomReader.ChooseDefault(names, "bestaat niet", 50));
            Assert.Equal("A", FamilyPartAtomReader.ChooseDefault(new[] { "A", "B" }, null, 20));
            Assert.Null(FamilyPartAtomReader.ChooseDefault(new string[0], null, 20));
        }
    }
}
