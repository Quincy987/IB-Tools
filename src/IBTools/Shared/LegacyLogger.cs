using System;

// Compatibility shim, NOT for new code: use IBTools.Shared.Logger.
//
// FoundationProjectionToggle.DevTools.dll (an old developer add-in from February 2026, not part of
// this repo, still in the shared %APPDATA%\Autodesk\Revit\Addins\2023 folder on the development PC)
// was compiled against this dll and calls exactly these three methods under their pre-IB Tools
// namespace (read from its metadata: Info(string), Warn(string), Error(string, Exception)). Without
// them it would fail with a TypeLoadException in every Revit 2023 on that PC. Remove this file when
// that add-in is gone.
namespace FoundationProjectionToggle.Infrastructure
{
    [Obsolete("Use IBTools.Shared.Logger. Kept only for FoundationProjectionToggle.DevTools.dll.")]
    public static class Logger
    {
        public static void Info(string message) => IBTools.Shared.Logger.Info(message);

        public static void Warn(string message) => IBTools.Shared.Logger.Warn(message);

        public static void Error(string message, Exception ex = null) => IBTools.Shared.Logger.Error(message, ex);
    }
}
