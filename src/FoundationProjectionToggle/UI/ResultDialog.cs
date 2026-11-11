using System;
using System.Text;
using Autodesk.Revit.UI;
using FoundationProjectionToggle.Infrastructure;

namespace FoundationProjectionToggle.UI
{
    /// <summary>
    /// One call to report the outcome of a command, with "Kopiëren naar klembord".
    /// </summary>
    /// <example>
    /// <code>
    /// ResultDialog.Show(commandData.Application, "IB-worksets",
    ///     "4 worksets gemaakt, 2 hernoemd, 0 bestonden al.",
    ///     string.Join(Environment.NewLine, reportLines));
    /// </code>
    /// </example>
    public static class ResultDialog
    {
        /// <summary>
        /// Shows a modal result window owned by Revit. The details text is selectable and
        /// the copy button copies title + summary + details.
        /// </summary>
        /// <param name="uiapp">commandData.Application (may be null: owner = Revit main window).</param>
        /// <param name="title">Window title, usually the button name.</param>
        /// <param name="summary">One or two sentences: what happened.</param>
        /// <param name="details">Optional multi-line report (per element, per reason...).</param>
        /// <param name="copyImmediately">
        /// Also put the report on the clipboard straight away (diagnostics / error reports).
        /// </param>
        public static void Show(UIApplication uiapp, string title, string summary, string details = null, bool copyImmediately = false)
        {
            try
            {
                var window = new ResultWindow(uiapp, title, summary, details);
                if (copyImmediately)
                {
                    window.Loaded += (s, e) => window.CopyToClipboard();
                }
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                // Never lose the result because the window failed: fall back to a TaskDialog.
                Logger.Error("ResultWindow failed, falling back to TaskDialog", ex);
                ShowTaskDialog(title, summary, details);
            }
        }

        /// <summary>
        /// Native Revit TaskDialog variant, for a short message where a WPF window is too much.
        /// The full text is copied to the clipboard when the user clicks the copy link.
        /// </summary>
        public static void ShowTaskDialog(string title, string summary, string details = null)
        {
            var text = ComposeText(title, summary, details);
            var dialog = new TaskDialog(string.IsNullOrEmpty(title) ? "IB Tools" : title)
            {
                MainInstruction = summary ?? string.Empty,
                ExpandedContent = details ?? string.Empty,
                CommonButtons = TaskDialogCommonButtons.Close,
                DefaultButton = TaskDialogResult.Close
            };
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Kopiëren naar klembord");

            if (dialog.Show() == TaskDialogResult.CommandLink1)
            {
                var copied = ClipboardHelper.TrySetText(text);
                TaskDialog.Show(
                    dialog.Title,
                    copied ? "TEKST GEKOPIEERD NAAR KLEMBORD" : "Kopiëren naar klembord is mislukt.");
            }
        }

        /// <summary>Plain-text form of a result, as copied to the clipboard.</summary>
        public static string ComposeText(string title, string summary, string details)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(title))
            {
                sb.AppendLine(title);
            }
            if (!string.IsNullOrEmpty(summary))
            {
                sb.AppendLine(summary);
            }
            if (!string.IsNullOrEmpty(details))
            {
                sb.AppendLine();
                sb.AppendLine(details);
            }
            return sb.ToString().TrimEnd();
        }
    }
}
