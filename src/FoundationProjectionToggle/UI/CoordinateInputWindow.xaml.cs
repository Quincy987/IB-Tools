using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using UIApplication = Autodesk.Revit.UI.UIApplication;
using FoundationProjectionToggle.Services;

namespace FoundationProjectionToggle.UI
{
    /// <summary>
    /// Input for "Modelpunt op coördinaat": RD X, RD Y (m), NAP height (m) and the scale type.
    /// Fills itself from the clipboard when that holds "x y [z]". Revit-free apart from the owner.
    /// </summary>
    public partial class CoordinateInputWindow : RevitWindow
    {
        public CoordinateInputWindow(UIApplication uiapp, string info, IList<string> typeNames, string defaultType, RdInput prefill, string prefillSource)
            : base(uiapp)
        {
            InitializeComponent();

            InfoText.Text = info ?? string.Empty;
            foreach (var n in typeNames ?? new List<string>())
            {
                TypeBox.Items.Add(n);
            }
            if (defaultType != null)
            {
                TypeBox.SelectedItem = defaultType;
            }
            if (TypeBox.SelectedIndex < 0 && TypeBox.Items.Count > 0)
            {
                TypeBox.SelectedIndex = 0;
            }

            ZBox.Text = "0.000";
            if (prefill != null)
            {
                Fill(prefill, prefillSource);
            }

            // Pasting "x y z" into any field fills all three.
            DataObject.AddPastingHandler(XBox, OnFieldPasting);
            DataObject.AddPastingHandler(YBox, OnFieldPasting);
            DataObject.AddPastingHandler(ZBox, OnFieldPasting);

            Loaded += (s, e) =>
            {
                XBox.Focus();
                XBox.SelectAll();
            };
        }

        /// <summary>RD X in metres (valid after OK).</summary>
        public double X { get; private set; }

        /// <summary>RD Y in metres (valid after OK).</summary>
        public double Y { get; private set; }

        /// <summary>NAP height in metres (valid after OK).</summary>
        public double Z { get; private set; }

        /// <summary>Chosen family type name (valid after OK).</summary>
        public string TypeName { get; private set; }

        private void Fill(RdInput input, string source)
        {
            XBox.Text = CoordinateInputParser.FormatMetres(input.X);
            YBox.Text = CoordinateInputParser.FormatMetres(input.Y);
            if (input.Z.HasValue)
            {
                ZBox.Text = CoordinateInputParser.FormatMetres(input.Z.Value);
            }
            SourceText.Text = source + (input.WasMillimetres ? " (in mm, omgerekend naar meters)." : ".")
                              + (input.Z.HasValue ? string.Empty : " Hoogte niet opgegeven.");
            ErrorText.Text = string.Empty;
        }

        private void OnFieldPasting(object sender, DataObjectPastingEventArgs e)
        {
            var text = e.SourceDataObject.GetData(DataFormats.UnicodeText) as string;
            var parsed = CoordinateInputParser.TryParseLine(text);
            if (parsed == null)
            {
                return; // one number: normal paste into this field
            }
            e.CancelCommand();
            Fill(parsed, "Geplakt: " + text.Trim());
        }

        private void OnPasteClick(object sender, RoutedEventArgs e)
        {
            var text = ClipboardHelper.TryGetText();
            var parsed = CoordinateInputParser.TryParseLine(text);
            if (parsed == null)
            {
                ErrorText.Text = "Geen RD-coördinaat op het klembord (verwacht \"x y\" of \"x y z\", in m of mm).";
                return;
            }
            Fill(parsed, "Van klembord: " + text.Trim());
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            double x, y, z;
            if (!CoordinateInputParser.TryParseNumber(XBox.Text, out x)) { Fail("RD X is geen getal.", XBox); return; }
            if (!CoordinateInputParser.TryParseNumber(YBox.Text, out y)) { Fail("RD Y is geen getal.", YBox); return; }
            if (string.IsNullOrWhiteSpace(ZBox.Text))
            {
                z = 0;
            }
            else if (!CoordinateInputParser.TryParseNumber(ZBox.Text, out z))
            {
                Fail("Hoogte is geen getal.", ZBox);
                return;
            }

            var normalized = CoordinateInputParser.Normalize(x, y, z);
            if (normalized == null)
            {
                Fail("Dit ligt niet in het RD-bereik van Nederland (X 0–300 km, Y 300–625 km). " +
                     "Controleer of X en Y niet verwisseld zijn.", XBox);
                return;
            }
            if (normalized.WasMillimetres)
            {
                // Show what will be used before placing anything.
                Fill(normalized, "Ingevuld in mm");
                ErrorText.Text = "Omgerekend naar meters. Controleer de waarden en klik nogmaals op Plaatsen.";
                return;
            }
            if (System.Math.Abs(z) > 1000.0)
            {
                Fail("Hoogte moet in meters NAP (tussen -1000 en 1000).", ZBox);
                return;
            }
            if (TypeBox.SelectedItem == null)
            {
                Fail("Kies een schaal.", TypeBox);
                return;
            }

            X = normalized.X;
            Y = normalized.Y;
            Z = z;
            TypeName = (string)TypeBox.SelectedItem;
            DialogResult = true;
        }

        private void Fail(string message, Control focus)
        {
            ErrorText.Text = message;
            focus?.Focus();
            (focus as TextBox)?.SelectAll();
        }
    }
}
