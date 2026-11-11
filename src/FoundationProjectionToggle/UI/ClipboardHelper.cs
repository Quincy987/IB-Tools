using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using FoundationProjectionToggle.Infrastructure;

namespace FoundationProjectionToggle.UI
{
    /// <summary>
    /// Clipboard access that does not throw. The Windows clipboard is regularly held open
    /// briefly by another program (CLIPBRD_E_CANT_OPEN), so this retries a few times.
    /// </summary>
    public static class ClipboardHelper
    {
        /// <summary>Puts <paramref name="text"/> on the clipboard. Returns false if that failed.</summary>
        public static bool TrySetText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            for (int attempt = 1; attempt <= 5; attempt++)
            {
                try
                {
                    Clipboard.SetText(text);
                    return true;
                }
                catch (ExternalException ex)
                {
                    if (attempt == 5)
                    {
                        Logger.Error("Clipboard.SetText failed", ex);
                        return false;
                    }
                    Thread.Sleep(50 * attempt);
                }
                catch (Exception ex)
                {
                    Logger.Error("Clipboard.SetText failed", ex);
                    return false;
                }
            }
            return false;
        }

        /// <summary>Clipboard text, or null if there is none or it cannot be read.</summary>
        public static string TryGetText()
        {
            try
            {
                return Clipboard.ContainsText() ? Clipboard.GetText() : null;
            }
            catch (Exception ex)
            {
                Logger.Warn($"Clipboard.GetText failed: {ex.Message}");
                return null;
            }
        }
    }
}
