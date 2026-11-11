using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Autodesk.Revit.UI;

namespace FoundationProjectionToggle.UI
{
    /// <summary>
    /// Base class for every IB Tools WPF window (input forms, result windows).
    /// The window is owned by Revit's main window, so it stays on top of Revit, centres on
    /// it, minimises with it and does not get its own taskbar entry.
    /// </summary>
    /// <remarks>
    /// <para>XAML: use it as the root element.</para>
    /// <code>
    /// &lt;ui:RevitWindow x:Class="FoundationProjectionToggle.UI.MyInputWindow"
    ///     xmlns:ui="clr-namespace:FoundationProjectionToggle.UI" Title="..."&gt;
    /// </code>
    /// <para>
    /// Code-behind: <c>public partial class MyInputWindow : RevitWindow</c>, with a constructor
    /// <c>MyInputWindow(UIApplication uiapp) : base(uiapp) { InitializeComponent(); }</c>.
    /// Show modally from an IExternalCommand with <c>if (win.ShowModal()) { ... }</c>.
    /// </para>
    /// <para>
    /// Revit API calls are only allowed inside the command's Execute, so read everything the
    /// form needs before ShowModal and do the model changes after it returns.
    /// </para>
    /// </remarks>
    public class RevitWindow : Window
    {
        /// <summary>Owned by Revit's main window (resolved from the current process).</summary>
        public RevitWindow() : this(IntPtr.Zero)
        {
        }

        /// <summary>Owned by the Revit main window of <paramref name="uiapp"/>.</summary>
        public RevitWindow(UIApplication uiapp) : this(uiapp != null ? uiapp.MainWindowHandle : IntPtr.Zero)
        {
        }

        /// <summary>Owned by the given window handle (falls back to Revit's main window).</summary>
        public RevitWindow(IntPtr ownerHandle)
        {
            var owner = ownerHandle != IntPtr.Zero ? ownerHandle : GetRevitMainWindowHandle();
            if (owner != IntPtr.Zero)
            {
                new WindowInteropHelper(this).Owner = owner;
            }

            // Defaults; XAML attributes on the derived window override these.
            WindowStartupLocation = owner != IntPtr.Zero
                ? WindowStartupLocation.CenterOwner
                : WindowStartupLocation.CenterScreen;
            ShowInTaskbar = false;
            SizeToContent = SizeToContent.WidthAndHeight;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 12;
            Background = SystemColors.ControlBrush;

            PreviewKeyDown += OnPreviewKeyDown;
        }

        /// <summary>
        /// Shows the window modally. Returns true only when the window set DialogResult = true
        /// (e.g. the OK button); Esc, Cancel and the close box return false.
        /// </summary>
        public bool ShowModal()
        {
            return ShowDialog() == true;
        }

        /// <summary>
        /// Handle of Revit's main window, or IntPtr.Zero if it cannot be determined.
        /// Prefer passing <c>commandData.Application</c> to the constructor when available.
        /// </summary>
        public static IntPtr GetRevitMainWindowHandle()
        {
            try
            {
                using (var process = Process.GetCurrentProcess())
                {
                    return process.MainWindowHandle;
                }
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape)
            {
                return;
            }

            e.Handled = true;
            try
            {
                DialogResult = false; // closes a modal window
            }
            catch (InvalidOperationException)
            {
                Close(); // shown modeless
            }
        }
    }
}
