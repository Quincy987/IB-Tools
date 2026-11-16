using System;
using System.Reflection;
using System.Windows.Media.Imaging;

namespace IBTools.Shared
{
    /// <summary>
    /// Loads ribbon icons embedded in this assembly (Tools\&lt;Tool&gt;\Resources\icons\*.png, embedded as
    /// "IBTools.icons.&lt;file&gt;" whatever folder they are in, see the csproj).
    /// </summary>
    public static class IconLoader
    {
        private const string ResourcePrefix = "IBTools.icons.";

        /// <summary>
        /// Loads one embedded icon, e.g. <c>Load("projection-on-32.png")</c>.
        /// Returns null (and logs) if the resource is missing, so a missing icon never
        /// breaks add-in startup.
        /// </summary>
        public static BitmapImage Load(string fileName)
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var resourceName = ResourcePrefix + fileName;

                using (var stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        Logger.Warn($"Icon resource not found: {resourceName}");
                        return null;
                    }

                    var image = new BitmapImage();
                    image.BeginInit();
                    image.StreamSource = stream;
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.EndInit();
                    image.Freeze();
                    return image;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to load icon: {fileName}", ex);
                return null;
            }
        }
    }
}
