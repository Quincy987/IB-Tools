using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace IBTools.RevitHarness
{
    /// <summary>
    /// Case H: the model that the updater wrote in, opened in a Revit WITHOUT IB Tools (job folder
    /// contains zonder-ibtools.txt). Checks that the IB Tools updater is not registered and logs
    /// every dialog during opening (a missing non-optional updater gives one). Uses only the
    /// Revit API.
    /// </summary>
    public sealed class ZonderIbToolsHarness
    {
        private readonly string _job;
        private readonly List<string> _dialogs = new List<string>();
        private bool _done;

        public ZonderIbToolsHarness(string job)
        {
            _job = job;
        }

        public void OnDialogBoxShowing(object sender, DialogBoxShowingEventArgs e)
        {
            var td = e as TaskDialogShowingEventArgs;
            _dialogs.Add(e.DialogId + (td != null ? " | " + td.Message : ""));
            try
            {
                e.OverrideResult(1);
            }
            catch
            {
                // nothing to do: logged above
            }
        }

        public void OnIdling(object sender, IdlingEventArgs e)
        {
            var uidoc = (sender as UIApplication)?.ActiveUIDocument;
            if (_done || uidoc == null)
            {
                return;
            }
            _done = true;
            var lines = new List<string>();
            try
            {
                var doc = uidoc.Document;
                lines.Add("H. Document: " + doc.PathName);
                // The IB Tools dll may be loaded (the harness dll refers to it), but without its
                // manifest App.OnStartup does not run, so the updater is not registered.
                var ibUpdater = UpdaterRegistry.GetRegisteredUpdaterInfos().Cast<UpdaterInfo>().Any(i => i.UpdaterName.StartsWith("IB Tools"));
                lines.Add((ibUpdater ? "FOUT " : "OK   ") + "H. Revit draait zonder IB Tools-updater");
                foreach (var d in _dialogs) lines.Add("H. Dialoog: " + d);
                var updaterDialogs = _dialogs.Where(d => d.IndexOf("updater", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                lines.Add((updaterDialogs.Count == 0 ? "OK   " : "FOUT ") + "H. Geen melding over een ontbrekende updater bij openen (" + _dialogs.Count + " dialogen in totaal)");
                lines.Add("H. Updaters in deze Revit: " + string.Join("; ", UpdaterRegistry.GetRegisteredUpdaterInfos().Cast<UpdaterInfo>()
                    .Select(i => i.UpdaterName + (i.IsOptional ? " (optioneel)" : ""))));
            }
            catch (Exception ex)
            {
                lines.Add("FOUT H. HARNESS EXCEPTION: " + ex);
            }
            File.AppendAllLines(Path.Combine(_job, "results.txt"), lines.Select(l => DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " " + l), Encoding.UTF8);
            var fails = lines.Count(l => l.StartsWith("FOUT "));
            File.WriteAllText(Path.Combine(_job, "done.txt"), fails == 0 ? "ok" : "fout " + fails);
        }
    }
}
