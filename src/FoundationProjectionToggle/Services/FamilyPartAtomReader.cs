using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace FoundationProjectionToggle.Services
{
    /// <summary>
    /// Reads the type names of a family file (.rfa) without Revit, from the "PartAtom" XML that
    /// Revit stores inside every family file. Used to offer the scale types of the coordinate
    /// point before the family is loaded, so loading and placing can be one undo step.
    /// Revit-free (unit tested).
    /// </summary>
    public static class FamilyPartAtomReader
    {
        private static readonly Regex TypeTitle = new Regex(
            "<A:part\\s+type=\"user\"\\s*>\\s*<title>([^<]*)</title>",
            RegexOptions.CultureInvariant);

        /// <summary>
        /// Type names in the order of the family file; empty when the PartAtom cannot be found.
        /// </summary>
        public static IList<string> ReadTypeNames(byte[] rfaBytes)
        {
            var names = new List<string>();
            if (rfaBytes == null || rfaBytes.Length == 0)
            {
                return names;
            }

            int start = IndexOf(rfaBytes, Encoding.ASCII.GetBytes("<entry"), 0);
            if (start < 0)
            {
                return names;
            }
            int end = IndexOf(rfaBytes, Encoding.ASCII.GetBytes("</entry>"), start);
            if (end < 0)
            {
                return names;
            }

            var xml = Encoding.UTF8.GetString(rfaBytes, start, end - start);
            foreach (Match m in TypeTitle.Matches(xml))
            {
                var name = System.Net.WebUtility.HtmlDecode(m.Groups[1].Value).Trim();
                if (name.Length > 0 && !names.Contains(name))
                {
                    names.Add(name);
                }
            }
            return names;
        }

        /// <summary>
        /// Scale denominator in a type name like "Schaal 1op20" (20), or null.
        /// </summary>
        public static int? ScaleOf(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                return null;
            }
            var m = Regex.Match(typeName, @"1\s*op\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            int scale;
            return m.Success && int.TryParse(m.Groups[1].Value, out scale) ? scale : (int?)null;
        }

        /// <summary>
        /// The type to preselect: the remembered choice if it exists, else the type whose scale
        /// equals the view scale, else "Schaal 1op20", else the first.
        /// </summary>
        public static string ChooseDefault(IList<string> typeNames, string remembered, int viewScale)
        {
            if (typeNames == null || typeNames.Count == 0)
            {
                return null;
            }
            if (!string.IsNullOrEmpty(remembered) && typeNames.Contains(remembered))
            {
                return remembered;
            }
            foreach (var n in typeNames)
            {
                if (ScaleOf(n) == viewScale)
                {
                    return n;
                }
            }
            foreach (var n in typeNames)
            {
                if (ScaleOf(n) == 20)
                {
                    return n;
                }
            }
            return typeNames[0];
        }

        private static int IndexOf(byte[] haystack, byte[] needle, int from)
        {
            for (int i = Math.Max(0, from); i <= haystack.Length - needle.Length; i++)
            {
                int j = 0;
                while (j < needle.Length && haystack[i + j] == needle[j])
                {
                    j++;
                }
                if (j == needle.Length)
                {
                    return i;
                }
            }
            return -1;
        }
    }
}
