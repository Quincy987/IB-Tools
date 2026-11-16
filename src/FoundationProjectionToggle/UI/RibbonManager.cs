using System;
using System.Reflection;
using Autodesk.Revit.UI;
using FoundationProjectionToggle.Infrastructure;

namespace FoundationProjectionToggle.UI
{
    /// <summary>
    /// The shared "IB Tools" ribbon tab: creates the tab and its panels (reusing them if they
    /// already exist) and adds push buttons. Used by every tool; holds no tool-specific state.
    /// </summary>
    /// <remarks>
    /// New buttons: add ONE line in App.OnStartup, e.g.
    /// <code>
    /// RibbonManager.AddPushButton&lt;CreateIbWorksetsCommand&gt;(application, "Model", "IbWorksets",
    ///     "Worksets", "Maakt de IB-standaard worksets aan.",
    ///     iconName: "worksets", availability: typeof(ProjectDocumentAvailability));
    /// </code>
    /// Panels appear in the order they are first requested, so the order of the lines in
    /// App.OnStartup is the order of the panels on the tab.
    /// </remarks>
    public static class RibbonManager
    {
        /// <summary>Name of the shared ribbon tab for all IB buttons.</summary>
        public const string TabName = "IB Tools";

        /// <summary>
        /// Creates the "IB Tools" tab, or reuses it if it already exists (e.g. created by
        /// another IB add-in or a second load of this one).
        /// </summary>
        public static void EnsureTab(UIControlledApplication application)
        {
            try
            {
                application.CreateRibbonTab(TabName);
                Logger.Info($"Created ribbon tab '{TabName}'");
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                Logger.Info($"Ribbon tab '{TabName}' already exists, reusing it");
            }
        }

        /// <summary>
        /// Returns the panel <paramref name="panelName"/> on the IB Tools tab, creating it
        /// (and the tab) if needed. Panels appear in the order they are first requested.
        /// </summary>
        public static RibbonPanel GetOrCreatePanel(UIControlledApplication application, string panelName)
        {
            EnsureTab(application);

            foreach (var existing in application.GetRibbonPanels(TabName))
            {
                if (existing.Name == panelName)
                {
                    return existing;
                }
            }

            var panel = application.CreateRibbonPanel(TabName, panelName);
            Logger.Info($"Created ribbon panel '{panelName}' on tab '{TabName}'");
            return panel;
        }

        /// <summary>
        /// Adds a push button for command <typeparamref name="TCommand"/> to a named panel on
        /// the IB Tools tab. Never throws: a failing button is logged and skipped (returns null),
        /// so one broken button cannot take down the whole tab.
        /// </summary>
        /// <param name="application">Revit UI application (from OnStartup).</param>
        /// <param name="panelName">Panel on the IB Tools tab, created if missing.</param>
        /// <param name="internalName">Unique, stable id (Revit uses it for shortcuts); no spaces.</param>
        /// <param name="text">Button label; use "\n" for a two-line label.</param>
        /// <param name="toolTip">Short tooltip.</param>
        /// <param name="iconName">
        /// Base name of embedded icons: loads "{iconName}-32.png" and "{iconName}-16.png" (see
        /// <see cref="IconLoader"/>). Null or missing files: button without icon.
        /// </param>
        /// <param name="availability">
        /// Optional IExternalCommandAvailability type, e.g. typeof(ProjectDocumentAvailability).
        /// </param>
        /// <param name="longDescription">Optional extended tooltip.</param>
        public static PushButton AddPushButton<TCommand>(
            UIControlledApplication application,
            string panelName,
            string internalName,
            string text,
            string toolTip,
            string iconName = null,
            Type availability = null,
            string longDescription = null)
            where TCommand : IExternalCommand
        {
            try
            {
                if (availability != null && !typeof(IExternalCommandAvailability).IsAssignableFrom(availability))
                {
                    throw new ArgumentException(
                        $"{availability.FullName} does not implement IExternalCommandAvailability",
                        nameof(availability));
                }

                var panel = GetOrCreatePanel(application, panelName);

                var data = new PushButtonData(
                    internalName,
                    text,
                    Assembly.GetExecutingAssembly().Location,
                    typeof(TCommand).FullName)
                {
                    ToolTip = toolTip
                };

                if (!string.IsNullOrEmpty(longDescription))
                {
                    data.LongDescription = longDescription;
                }
                if (availability != null)
                {
                    data.AvailabilityClassName = availability.FullName;
                }
                if (!string.IsNullOrEmpty(iconName))
                {
                    data.LargeImage = IconLoader.Load(iconName + "-32.png");
                    data.Image = IconLoader.Load(iconName + "-16.png");
                }

                var button = panel.AddItem(data) as PushButton;
                Logger.Info($"Added button '{internalName}' to panel '{panelName}'");
                return button;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to add button '{internalName}' to panel '{panelName}'", ex);
                return null;
            }
        }
    }
}
