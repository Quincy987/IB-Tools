using System;

namespace FoundationProjectionToggle.Models
{
    /// <summary>
    /// Represents the result of a visibility toggle operation.
    ///
    /// <para>
    /// <b>Design Pattern:</b>
    /// This follows the Result Object pattern (similar to Rust's Result or
    /// functional Either). It encapsulates success/failure without exceptions,
    /// enabling clean handling in the UI layer.
    /// </para>
    ///
    /// <para>
    /// <b>Usage:</b>
    /// <code>
    /// var result = visibilityService.ToggleProjectionLines(uiApp);
    /// if (result.Success)
    /// {
    ///     ShowMessage($"Now {(result.IsNowVisible ? "visible" : "hidden")}");
    /// }
    /// else
    /// {
    ///     ShowError(result.ErrorMessage);
    /// }
    /// </code>
    /// </para>
    /// </summary>
    public class ToggleResult
    {
        /// <summary>
        /// Indicates whether the operation completed successfully.
        /// </summary>
        /// <remarks>
        /// When true, WasVisible and IsNowVisible are guaranteed non-null.
        /// When false, ErrorMessage is guaranteed non-null.
        /// </remarks>
        public bool Success { get; private set; }

        /// <summary>
        /// The visibility state before the toggle (only set on success).
        /// </summary>
        /// <remarks>
        /// True = was visible (not hidden)
        /// False = was hidden
        /// Null = operation failed, state unknown
        /// </remarks>
        public bool? WasVisible { get; private set; }

        /// <summary>
        /// The visibility state after the toggle (only set on success).
        /// </summary>
        /// <remarks>
        /// True = now visible (not hidden)
        /// False = now hidden
        /// Null = operation failed, state unknown
        /// </remarks>
        public bool? IsNowVisible { get; private set; }

        /// <summary>
        /// The name of the view where the toggle was applied.
        /// </summary>
        /// <remarks>
        /// Used for user feedback: "Projection lines now hidden in 'Level 1'"
        /// </remarks>
        public string ViewName { get; private set; }

        /// <summary>
        /// User-friendly error message (only set on failure).
        /// </summary>
        /// <remarks>
        /// This message is safe to show directly to users.
        /// Technical details (stack traces, etc.) are in Exception property.
        /// </remarks>
        public string ErrorMessage { get; private set; }

        /// <summary>
        /// The exception that caused the failure (for logging, not user display).
        /// </summary>
        /// <remarks>
        /// Optional even on failure - some failures are expected (no document open)
        /// and don't have associated exceptions.
        /// </remarks>
        public Exception Exception { get; private set; }

        // Private constructor to enforce factory method usage
        private ToggleResult() { }

        /// <summary>
        /// Factory method for successful toggle operations.
        /// </summary>
        /// <param name="wasVisible">Was the subcategory visible before toggle?</param>
        /// <param name="isNowVisible">Is the subcategory visible after toggle?</param>
        /// <param name="viewName">Name of the affected view.</param>
        /// <returns>A success result with state information.</returns>
        public static ToggleResult Succeed(bool wasVisible, bool isNowVisible, string viewName)
        {
            return new ToggleResult
            {
                Success = true,
                WasVisible = wasVisible,
                IsNowVisible = isNowVisible,
                ViewName = viewName,
                ErrorMessage = null,
                Exception = null
            };
        }

        /// <summary>
        /// Factory method for failed toggle operations.
        /// </summary>
        /// <param name="message">User-friendly error message.</param>
        /// <param name="ex">Optional exception for logging.</param>
        /// <returns>A failure result with error information.</returns>
        public static ToggleResult Fail(string message, Exception ex = null)
        {
            return new ToggleResult
            {
                Success = false,
                WasVisible = null,
                IsNowVisible = null,
                ViewName = null,
                ErrorMessage = message,
                Exception = ex
            };
        }
    }
}
