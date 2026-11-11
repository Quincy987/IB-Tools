using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace FoundationProjectionToggle.Services
{
    /// <summary>
    /// Reads RD coordinates typed or pasted by the user. Revit-free (unit tested).
    /// </summary>
    /// <remarks>
    /// <para>Rules:</para>
    /// <list type="bullet">
    /// <item>A decimal comma is accepted ("122285,79").</item>
    /// <item>
    /// "x y [z]" on one line, separated by spaces, tabs or semicolons. A comma only counts as a
    /// separator when the text has no space, tab or semicolon ("122285.79,486175.94"); then the
    /// decimals must use a point.
    /// </item>
    /// <item>
    /// Labels around the numbers are ignored ("X: 122285,790  Y: 486175,940").
    /// </item>
    /// <item>
    /// Units: values inside the RD range are metres. Values that are inside the range only
    /// after dividing by 1000 are millimetres (the old Dynamo input) and are converted to metres.
    /// </item>
    /// </list>
    /// </remarks>
    public static class CoordinateInputParser
    {
        /// <summary>RD New (EPSG:28992) range of the Netherlands incl. margins, in metres.</summary>
        public const double RdMinX = -7000.0;
        public const double RdMaxX = 300000.0;
        public const double RdMinY = 289000.0;
        public const double RdMaxY = 629000.0;

        /// <summary>Longest clipboard text that is still considered as coordinate input.</summary>
        public const int MaxClipboardLength = 200;

        private static readonly Regex NumberWithDecimalComma = new Regex(@"[-+]?\d+(?:[.,]\d+)?", RegexOptions.CultureInvariant);
        private static readonly Regex NumberWithDecimalPoint = new Regex(@"[-+]?\d+(?:\.\d+)?", RegexOptions.CultureInvariant);
        private static readonly Regex SingleNumber = new Regex(@"^\s*([-+]?\d+(?:[.,]\d+)?)\s*$", RegexOptions.CultureInvariant);

        /// <summary>True when (x, y) in metres lies in the RD range of the Netherlands.</summary>
        public static bool IsWithinRd(double x, double y)
        {
            return x >= RdMinX && x <= RdMaxX && y >= RdMinY && y <= RdMaxY;
        }

        /// <summary>
        /// One number from an input field: decimal point or decimal comma, surrounding
        /// whitespace allowed, nothing else. Returns false for empty or invalid text.
        /// </summary>
        public static bool TryParseNumber(string text, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }
            var m = SingleNumber.Match(text);
            if (!m.Success)
            {
                return false;
            }
            return double.TryParse(
                m.Groups[1].Value.Replace(',', '.'),
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out value);
        }

        /// <summary>
        /// The numbers in a line such as "122285.79 486175.94 1.2" (see class remarks for the
        /// separator rules). Never returns null.
        /// </summary>
        public static IList<double> ExtractNumbers(string text)
        {
            var result = new List<double>();
            if (string.IsNullOrWhiteSpace(text))
            {
                return result;
            }

            text = text.Trim();
            bool commaIsSeparator = text.IndexOfAny(new[] { ' ', '\t', ';', '\r', '\n' }) < 0;
            var regex = commaIsSeparator ? NumberWithDecimalPoint : NumberWithDecimalComma;

            foreach (Match m in regex.Matches(text))
            {
                // "N/S" or "x1" style labels: a number glued to a letter belongs to the label.
                if (m.Index > 0 && char.IsLetter(text[m.Index - 1]))
                {
                    continue;
                }
                int end = m.Index + m.Length;
                if (end < text.Length && char.IsLetter(text[end]))
                {
                    continue;
                }

                double v;
                if (double.TryParse(
                        m.Value.Replace(',', '.'),
                        NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                        CultureInfo.InvariantCulture,
                        out v))
                {
                    result.Add(v);
                }
            }
            return result;
        }

        /// <summary>
        /// Reads "x y [z]" (e.g. from the clipboard) into RD metres. Returns null when the text
        /// is not two or three numbers that form an RD position (in m or mm).
        /// </summary>
        public static RdInput TryParseLine(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length > MaxClipboardLength)
            {
                return null;
            }

            var numbers = ExtractNumbers(text);
            if (numbers.Count < 2 || numbers.Count > 3)
            {
                return null;
            }

            return Normalize(numbers[0], numbers[1], numbers.Count == 3 ? numbers[2] : (double?)null);
        }

        /// <summary>
        /// Interprets x, y (and optional z) as metres or millimetres. Null when neither puts
        /// the point inside the RD range.
        /// </summary>
        public static RdInput Normalize(double x, double y, double? z)
        {
            if (IsWithinRd(x, y))
            {
                return new RdInput(x, y, z, false);
            }

            if (IsWithinRd(x / 1000.0, y / 1000.0))
            {
                return new RdInput(x / 1000.0, y / 1000.0, z.HasValue ? z.Value / 1000.0 : (double?)null, true);
            }

            return null;
        }

        /// <summary>Metres with 3 decimals and a point, as shown and copied ("122285.790").</summary>
        public static string FormatMetres(double value)
        {
            return value.ToString("0.000", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>RD position in metres, as read by <see cref="CoordinateInputParser"/>.</summary>
    public sealed class RdInput
    {
        public RdInput(double x, double y, double? z, bool wasMillimetres)
        {
            X = x;
            Y = y;
            Z = z;
            WasMillimetres = wasMillimetres;
        }

        /// <summary>RD X (east), metres.</summary>
        public double X { get; }

        /// <summary>RD Y (north), metres.</summary>
        public double Y { get; }

        /// <summary>NAP height in metres, or null when only x and y were given.</summary>
        public double? Z { get; }

        /// <summary>The input was in millimetres and has been divided by 1000.</summary>
        public bool WasMillimetres { get; }
    }
}
