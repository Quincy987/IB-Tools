using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FoundationProjectionToggle.Core.Remmingwerk;
using FoundationProjectionToggle.Services;
using UIApplication = Autodesk.Revit.UI.UIApplication;

namespace FoundationProjectionToggle.UI
{
    /// <summary>
    /// Input for "Remmingwerk": side, NAP level, the five family types and the numbers of the
    /// Dynamo script, with a live overview computed by <see cref="RemmingwerkPlanner"/>.
    /// Revit-free apart from the owner window.
    /// </summary>
    public partial class RemmingwerkWindow : RevitWindow
    {
        private readonly IList<RemmingwerkLine> _lines;
        private bool _loaded;

        public RemmingwerkWindow(UIApplication uiapp, string info, IList<RemmingwerkLine> lines,
            IList<LevelOption> levels, int defaultLevel,
            IList<TypeOption> wrijfgording, int wrijfgordingDefault,
            IList<TypeOption> wrijfstijl, int wrijfstijlDefault,
            IList<TypeOption> gording, int gordingDefault,
            IList<TypeOption> stijl, int stijlDefault,
            IList<TypeOption> paal, int paalDefault,
            RemmingwerkSettings settings)
            : base(uiapp)
        {
            InitializeComponent();
            _lines = lines;

            InfoText.Text = info ?? string.Empty;
            Fill(LevelBox, levels, defaultLevel);
            Fill(WrijfgordingBox, wrijfgording, wrijfgordingDefault);
            Fill(WrijfstijlBox, wrijfstijl, wrijfstijlDefault);
            Fill(GordingBox, gording, gordingDefault);
            Fill(StijlBox, stijl, stijlDefault);
            Fill(PaalBox, paal, paalDefault);

            var s = settings ?? new RemmingwerkSettings();
            SideRight.IsChecked = !s.PilesLeft;
            SideLeft.IsChecked = s.PilesLeft;
            RowsBox.Text = s.Rows.ToString(CultureInfo.InvariantCulture);
            RowSpacingBox.Text = Num(s.RowSpacingMm);
            WrijfBreedteBox.Text = Num(s.WrijfBreedteMm);
            WrijfHoogteBox.Text = Num(s.WrijfHoogteMm);
            PileSpacingBox.Text = Num(s.PileSpacingMm);
            PileTipBox.Text = Num(s.PileTipNapMm);
            OverlengthBox.Text = Num(s.PileOverlengthMm);

            Loaded += (sender, e) =>
            {
                _loaded = true;
                UpdateOverview();
                OkButton.Focus();
            };
        }

        /// <summary>Valid after OK.</summary>
        public RemmingwerkSettings Settings { get; private set; }
        public LevelOption Level { get; private set; }
        public TypeOption Wrijfgording { get; private set; }
        public TypeOption Wrijfstijl { get; private set; }
        public TypeOption StalenGording { get; private set; }
        public TypeOption StalenStijl { get; private set; }
        public TypeOption Buispaal { get; private set; }

        private static void Fill<T>(ComboBox box, IList<T> items, int selected)
        {
            foreach (var i in items ?? new List<T>())
            {
                box.Items.Add(i);
            }
            if (selected >= 0 && selected < box.Items.Count)
            {
                box.SelectedIndex = selected;
            }
            else if (box.Items.Count > 0)
            {
                box.SelectedIndex = 0;
            }
        }

        private static string Num(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        private void OnChanged(object sender, RoutedEventArgs e)
        {
            if (_loaded)
            {
                UpdateOverview();
            }
        }

        /// <summary>Reads the form; null + message when something is not a valid number.</summary>
        private RemmingwerkSettings ReadSettings(out string error, out Control field)
        {
            error = null;
            field = null;
            var s = new RemmingwerkSettings { PilesLeft = SideLeft.IsChecked == true };
            double v;
            if (!CoordinateInputParser.TryParseNumber(RowsBox.Text, out v) || v != System.Math.Floor(v)) { error = "Aantal gordingrijen moet een geheel getal zijn."; field = RowsBox; return null; }
            s.Rows = (int)v;
            if (!Read(RowSpacingBox, "h.o.h. gordingen", out v, ref error, ref field)) return null;
            s.RowSpacingMm = v;
            if (!Read(WrijfBreedteBox, "Breedte wrijfgording", out v, ref error, ref field)) return null;
            s.WrijfBreedteMm = v;
            if (!Read(WrijfHoogteBox, "Hoogte wrijfgording", out v, ref error, ref field)) return null;
            s.WrijfHoogteMm = v;
            if (!Read(PileSpacingBox, "h.o.h. buispalen", out v, ref error, ref field)) return null;
            s.PileSpacingMm = v;
            if (!Read(PileTipBox, "O.k. buispaal", out v, ref error, ref field)) return null;
            s.PileTipNapMm = v;
            if (!Read(OverlengthBox, "Overlengte", out v, ref error, ref field)) return null;
            s.PileOverlengthMm = v;
            return s;
        }

        private static bool Read(TextBox box, string what, out double value, ref string error, ref Control field)
        {
            if (CoordinateInputParser.TryParseNumber(box.Text, out value))
            {
                return true;
            }
            error = what + " is geen getal.";
            field = box;
            return false;
        }

        private SteelDimensions Steel()
        {
            return SteelDimensions.FromTypes(GordingBox.SelectedItem as TypeOption, StijlBox.SelectedItem as TypeOption, PaalBox.SelectedItem as TypeOption);
        }

        private RemmingwerkPlan Preview(out string error, out Control field)
        {
            var settings = ReadSettings(out error, out field);
            var level = LevelBox.SelectedItem as LevelOption;
            if (settings == null)
            {
                return null;
            }
            if (level == null)
            {
                error = "Kies het NAP-level.";
                field = LevelBox;
                return null;
            }
            return RemmingwerkPlanner.Plan(_lines, settings, Steel(), level.ElevationMm);
        }

        private void UpdateOverview()
        {
            string error;
            Control field;
            var plan = Preview(out error, out field);
            if (plan == null)
            {
                OverviewText.Text = string.Empty;
                ErrorText.Text = error;
                return;
            }
            if (!plan.IsValid)
            {
                OverviewText.Text = string.Empty;
                ErrorText.Text = string.Join(System.Environment.NewLine, plan.Errors);
                return;
            }
            OverviewText.Text = plan.Overview();
            ErrorText.Text = string.Empty;
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            string error;
            Control field;
            var plan = Preview(out error, out field);
            if (plan == null)
            {
                ErrorText.Text = error;
                field?.Focus();
                (field as TextBox)?.SelectAll();
                return;
            }
            if (!plan.IsValid)
            {
                ErrorText.Text = string.Join(System.Environment.NewLine, plan.Errors);
                return;
            }
            if (WrijfgordingBox.SelectedItem == null || WrijfstijlBox.SelectedItem == null || GordingBox.SelectedItem == null ||
                StijlBox.SelectedItem == null || PaalBox.SelectedItem == null)
            {
                ErrorText.Text = "Kies alle vijf de typen.";
                return;
            }

            Settings = ReadSettings(out error, out field);
            Level = (LevelOption)LevelBox.SelectedItem;
            Wrijfgording = (TypeOption)WrijfgordingBox.SelectedItem;
            Wrijfstijl = (TypeOption)WrijfstijlBox.SelectedItem;
            StalenGording = (TypeOption)GordingBox.SelectedItem;
            StalenStijl = (TypeOption)StijlBox.SelectedItem;
            Buispaal = (TypeOption)PaalBox.SelectedItem;
            DialogResult = true;
        }
    }
}
