using System;
using System.Windows;
using Autodesk.Revit.UI;

namespace FoundationProjectionToggle.UI
{
    /// <summary>
    /// Result/report window: a summary line, a selectable details box and a
    /// "Kopiëren naar klembord" button. Use <see cref="ResultDialog.Show"/> rather than
    /// creating it directly.
    /// </summary>
    public partial class ResultWindow : RevitWindow
    {
        private readonly string _clipboardText;

        public ResultWindow(UIApplication uiapp, string title, string summary, string details)
            : base(uiapp)
        {
            InitializeComponent();

            Title = string.IsNullOrEmpty(title) ? "IB Tools" : title;
            SummaryText.Text = summary ?? string.Empty;
            SummaryText.Visibility = string.IsNullOrEmpty(summary) ? Visibility.Collapsed : Visibility.Visible;

            if (string.IsNullOrEmpty(details))
            {
                // Summary only: shrink to fit instead of showing an empty box.
                DetailsBox.Visibility = Visibility.Collapsed;
                SizeToContent = SizeToContent.Height;
                Height = double.NaN;
            }
            else
            {
                DetailsBox.Text = details;
            }

            _clipboardText = ResultDialog.ComposeText(Title, summary, details);
        }

        /// <summary>Text that the copy button puts on the clipboard.</summary>
        public string ClipboardText => _clipboardText;

        /// <summary>Copies the report and shows the confirmation in the window.</summary>
        public void CopyToClipboard()
        {
            StatusText.Text = ClipboardHelper.TrySetText(_clipboardText)
                ? "Gekopieerd naar klembord"
                : "Kopiëren mislukt, probeer opnieuw";
        }

        private void OnCopyClick(object sender, RoutedEventArgs e)
        {
            CopyToClipboard();
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
