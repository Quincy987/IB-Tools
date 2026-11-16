using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace IBTools.Tests
{
    /// <summary>
    /// Every ribbon icon and every family the add-in loads at run time must exist exactly once under
    /// src\IBTools\Tools\*\Resources\, because the csproj embeds them with a name that depends only
    /// on the file name ("IBTools.icons.&lt;file&gt;", "IBTools.families.&lt;file&gt;"). A missing toggle
    /// icon makes the whole IB Tools tab fail at startup; a missing button icon fails silently.
    /// </summary>
    public class EmbeddedResourceTests
    {
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "IBTools.sln")))
            {
                dir = dir.Parent;
            }
            Assert.True(dir != null, "IBTools.sln not found above " + AppContext.BaseDirectory);
            return dir.FullName;
        }

        private static string Src(params string[] parts)
        {
            return Path.Combine(new[] { RepoRoot(), "src", "IBTools" }.Concat(parts).ToArray());
        }

        private static List<string> ResourceFiles(string kind)
        {
            return Directory.GetDirectories(Src("Tools"))
                .Select(t => Path.Combine(t, "Resources", kind))
                .Where(Directory.Exists)
                .SelectMany(Directory.GetFiles)
                .ToList();
        }

        /// <summary>Icon base names the add-in asks for: iconName: "x" in App.cs plus IconLoader.Load("x-NN.png") anywhere.</summary>
        private static List<string> RequestedIconFiles()
        {
            var files = new List<string>();
            var app = File.ReadAllText(Src("App.cs"));
            foreach (Match m in Regex.Matches(app, "iconName:\\s*\"([^\"]+)\""))
            {
                files.Add(m.Groups[1].Value + "-32.png");
                files.Add(m.Groups[1].Value + "-16.png");
            }
            foreach (var cs in Directory.GetFiles(Src(), "*.cs", SearchOption.AllDirectories))
            {
                if (cs.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)) continue;
                foreach (Match m in Regex.Matches(File.ReadAllText(cs), "IconLoader\\.Load\\(\"([^\"]+)\"\\)"))
                {
                    files.Add(m.Groups[1].Value);
                }
            }
            return files.Distinct().OrderBy(f => f).ToList();
        }

        [Fact]
        public void Every_requested_icon_exists_once_in_a_tool_folder()
        {
            var requested = RequestedIconFiles();
            // Sanity check that the scan found something: at least the 4 Hide/Show icons plus one button (2 sizes).
            Assert.True(requested.Count >= 6, "found only " + requested.Count + " requested icons; is the regex still right?");
            Assert.Contains("projection-on-32.png", requested);
            var present = ResourceFiles("icons").GroupBy(Path.GetFileName).ToDictionary(g => g.Key, g => g.Count());
            foreach (var icon in requested)
            {
                Assert.True(present.ContainsKey(icon), "icon missing: " + icon);
                Assert.Equal(1, present[icon]);
            }
        }

        [Fact]
        public void Icon_and_family_file_names_are_unique_across_tools()
        {
            foreach (var kind in new[] { "icons", "families" })
            {
                var dupes = ResourceFiles(kind).GroupBy(Path.GetFileName).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
                Assert.True(dupes.Count == 0, kind + " with the same file name in two tools: " + string.Join(", ", dupes));
            }
        }

        [Theory]
        [InlineData("Modelpunt", "IB_GM_coordinaatpunt")]
        [InlineData("Remmingwerk", "SB_wrijfgording")]
        [InlineData("Remmingwerk", "SC_wrijfstijl_generic model")]
        public void Embedded_family_exists_in_its_tool(string tool, string family)
        {
            Assert.True(File.Exists(Src("Tools", tool, "Resources", "families", family + ".rfa")), family + ".rfa missing in " + tool);
        }

        [Fact]
        public void Project_embeds_icons_and_families_by_file_name_only()
        {
            var csproj = File.ReadAllText(Src("IBTools.csproj"));
            Assert.Contains("Include=\"Tools\\*\\Resources\\icons\\*.png\" LogicalName=\"IBTools.icons.%(Filename)%(Extension)\"", csproj);
            Assert.Contains("Include=\"Tools\\*\\Resources\\families\\*.rfa\" LogicalName=\"IBTools.families.%(Filename)%(Extension)\"", csproj);
            Assert.Contains("\"IBTools.icons.\"", File.ReadAllText(Src("Shared", "IconLoader.cs")));
            Assert.Contains("\"IBTools.families.\"", File.ReadAllText(Src("Shared", "EmbeddedFamilyLoader.cs")));
        }
    }
}
