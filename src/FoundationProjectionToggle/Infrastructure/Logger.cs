using System;
using System.IO;

namespace FoundationProjectionToggle.Infrastructure
{
    /// <summary>
    /// Lightweight file-based logger for debugging production issues.
    ///
    /// <para>
    /// <b>Design Philosophy:</b>
    /// This logger is disabled by default to ensure zero performance overhead
    /// in production. It can be enabled by IT support or developers to diagnose
    /// issues without requiring Visual Studio attachment.
    /// </para>
    ///
    /// <para>
    /// <b>Activation:</b>
    /// Set environment variable PROJECTION_TOGGLE_DEBUG=1 before starting Revit.
    /// Logs are written to %LOCALAPPDATA%\FoundationProjectionToggle\logs\
    /// </para>
    ///
    /// <para>
    /// <b>Thread Safety:</b>
    /// File.AppendAllText is atomic at the OS level for small writes.
    /// For Revit add-ins (single UI thread), this is sufficient.
    /// </para>
    /// </summary>
    public static class Logger
    {
        // Log directory: %LOCALAPPDATA%\FoundationProjectionToggle\logs\
        private static readonly string LogDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FoundationProjectionToggle",
            "logs"
        );

        // Check once at startup, cache result
        // This avoids repeated environment variable lookups
        private static readonly bool IsDebugMode =
            Environment.GetEnvironmentVariable("PROJECTION_TOGGLE_DEBUG") == "1";

        /// <summary>
        /// Log informational message (normal operations).
        /// </summary>
        /// <param name="message">Message to log.</param>
        public static void Info(string message) => Log("INFO", message);

        /// <summary>
        /// Log warning message (unexpected but recoverable situations).
        /// </summary>
        /// <param name="message">Message to log.</param>
        public static void Warn(string message) => Log("WARN", message);

        /// <summary>
        /// Log error message (failures requiring investigation).
        /// </summary>
        /// <param name="message">Message to log.</param>
        /// <param name="ex">Optional exception with stack trace.</param>
        public static void Error(string message, Exception ex = null) =>
            Log("ERROR", ex != null ? $"{message}\n{ex}" : message);

        /// <summary>
        /// Core logging implementation.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This method is intentionally simple and defensive:
        /// - Checks debug mode first (fast path for production)
        /// - Creates directory if needed (first-run scenario)
        /// - Uses daily log files (automatic rotation)
        /// - Catches all exceptions (never crashes add-in)
        /// </para>
        /// </remarks>
        private static void Log(string level, string message)
        {
            // Fast path: if not in debug mode, return immediately
            // This ensures zero overhead in production
            if (!IsDebugMode) return;

            try
            {
                // Ensure log directory exists (first-run scenario)
                Directory.CreateDirectory(LogDir);

                // Daily log file: log_2025-01-22.txt
                var logFile = Path.Combine(LogDir, $"log_{DateTime.Now:yyyy-MM-dd}.txt");

                // Format: [14:32:05.123] [INFO] Message text here
                var line = $"[{DateTime.Now:HH:mm:ss.fff}] [{level}] {message}\n";

                // Append atomically (OS guarantees for small writes)
                File.AppendAllText(logFile, line);
            }
            catch
            {
                // CRITICAL: Logger must NEVER throw exceptions.
                // If logging fails (disk full, permissions, etc.), silently continue.
                // The add-in's core functionality is more important than logging.
            }
        }
    }
}
