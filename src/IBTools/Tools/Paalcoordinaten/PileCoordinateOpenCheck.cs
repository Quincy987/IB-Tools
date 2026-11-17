using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using IBTools.Shared;
using IBTools.Tools.Paalcoordinaten.Core;

namespace IBTools.Tools.Paalcoordinaten
{
    /// <summary>
    /// When a model is opened in the UI: checks whether piles carry coordinates that differ from
    /// what the button would write (moved without IB Tools, copied in, never filled) and offers
    /// to update them ("Nu bijwerken" / "Niet nu").
    /// </summary>
    /// <remarks>
    /// <para>
    /// DocumentOpened only remembers the document; the check runs on the first Idling at which
    /// that document is the active UI document. The user then sees the model before the
    /// question, and documents that are opened in the background through the API (links,
    /// batch tools) are never asked about.
    /// </para>
    /// <para>
    /// Asked only when the model is on coordinates, the base point self-test passes and at
    /// least one pile would actually be written. "Niet nu" changes nothing: the question comes
    /// back at the next open.
    /// </para>
    /// <para>
    /// <c>IBTOOLS_PILECHECK=0</c> suppresses the question (the check still runs and is logged),
    /// so that unattended test runs never hang on a modal dialog. build.ps1 sets it for the
    /// Revit it starts unless the caller chose a value.
    /// </para>
    /// </remarks>
    public sealed class PileCoordinateOpenCheck
    {
        /// <summary>Environment variable: "0" = check and log, but never ask.</summary>
        public const string PromptVariable = "IBTOOLS_PILECHECK";

        /// <summary>TaskDialog id, visible in DialogBoxShowing (test harness).</summary>
        public const string DialogId = "IBTools_PileCoordinatesOpenCheck";

        /// <summary>Revit shows add-in task dialogs as "IB Tools - &lt;title&gt;".</summary>
        public const string Title = "Paalcoördinaten";

        private readonly List<Document> _pending = new List<Document>();
        private UIControlledApplication _application;
        private bool _idlingSubscribed;

        public void Register(UIControlledApplication application)
        {
            try
            {
                _application = application;
                application.ControlledApplication.DocumentOpened += OnDocumentOpened;
                application.ControlledApplication.DocumentClosing += OnDocumentClosing;
                Logger.Info("Paalcoördinaten: open check registered (DocumentOpened)");
            }
            catch (Exception ex)
            {
                Logger.Error("Paalcoördinaten: registering the open check failed", ex);
            }
        }

        public void Unregister(UIControlledApplication application)
        {
            try
            {
                application.ControlledApplication.DocumentOpened -= OnDocumentOpened;
                application.ControlledApplication.DocumentClosing -= OnDocumentClosing;
                SetIdling(false);
                _pending.Clear();
            }
            catch (Exception ex)
            {
                Logger.Error("Paalcoördinaten: unregistering the open check failed", ex);
            }
        }

        private void OnDocumentOpened(object sender, DocumentOpenedEventArgs e)
        {
            try
            {
                var doc = e.Document;
                if (doc == null || e.Status != RevitAPIEventStatus.Succeeded)
                {
                    return;
                }
                if (doc.IsFamilyDocument || doc.IsLinked || doc.IsReadOnly)
                {
                    return;
                }
                _pending.Add(doc);
                SetIdling(true);
            }
            catch (Exception ex)
            {
                Logger.Error("Paalcoördinaten: DocumentOpened handler failed", ex);
            }
        }

        private void OnDocumentClosing(object sender, DocumentClosingEventArgs e)
        {
            try
            {
                var index = IndexOf(e.Document);
                if (index >= 0)
                {
                    _pending.RemoveAt(index);
                }
                if (_pending.Count == 0)
                {
                    SetIdling(false);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Paalcoördinaten: DocumentClosing handler failed", ex);
            }
        }

        private void OnIdling(object sender, IdlingEventArgs e)
        {
            try
            {
                var uiapp = sender as UIApplication;
                var active = uiapp?.ActiveUIDocument?.Document;
                if (active == null)
                {
                    return;
                }
                // Forget documents that are gone without a DocumentClosing we saw.
                _pending.RemoveAll(d => !d.IsValidObject);
                var index = IndexOf(active);
                if (index < 0)
                {
                    if (_pending.Count == 0)
                    {
                        SetIdling(false);
                    }
                    return;
                }
                _pending.RemoveAt(index);
                if (_pending.Count == 0)
                {
                    SetIdling(false);
                }
                Check(uiapp, active);
            }
            catch (Exception ex)
            {
                Logger.Error("Paalcoördinaten: open check failed", ex);
            }
        }

        private void Check(UIApplication uiapp, Document doc)
        {
            if (!doc.IsValidObject || doc.IsReadOnly || doc.IsModifiable)
            {
                return; // closed, read-only, or something else has a transaction open
            }

            var evaluation = PileCoordinateService.Evaluate(doc, null, PileScope.WholeModel);
            if (!evaluation.AllowsAutomaticWrite)
            {
                Logger.Info($"Paalcoördinaten open check: '{doc.Title}' {PileCoordinateUpdater.NotWritableReason(evaluation)}: geen vraag.");
                return;
            }

            var check = PileStaleCheck.From(evaluation.Report.Outcomes);
            var prompt = Environment.GetEnvironmentVariable(PromptVariable) != "0";
            Logger.Info($"Paalcoördinaten open check: '{doc.Title}': {check.BuildLogLine()}; " +
                        (!check.ShouldPrompt ? "geen vraag (niets te schrijven)." :
                         prompt ? "vraag getoond." : $"geen vraag ({PromptVariable}=0)."));
            if (!check.ShouldPrompt || !prompt)
            {
                return;
            }

            var dialog = new TaskDialog(Title)
            {
                Id = DialogId,
                MainInstruction = "Paalcoördinaten bijwerken?",
                MainContent = check.BuildMessage(),
                CommonButtons = TaskDialogCommonButtons.None,
                AllowCancellation = true
            };
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Nu bijwerken",
                "Schrijft de juiste RD-coördinaten in X-/Y-coördinaat_funderingspaal, in één stap (één Ctrl+Z).");
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Niet nu",
                "Er verandert niets. Bij de volgende keer openen vraagt IB Tools het opnieuw.");

            var answer = dialog.Show();
            Logger.Info("Paalcoördinaten open check: antwoord " + answer);
            if (answer != TaskDialogResult.CommandLink1)
            {
                return;
            }

            if (!doc.IsValidObject)
            {
                return;
            }
            PileCoordinateService.ApplyInTransaction(doc, evaluation);
            var report = evaluation.Report;
            Logger.Info($"Paalcoördinaten open check: {report.BuildSummary()}");
            ResultDialog.Show(uiapp, "Paalcoördinaten", report.BuildSummary(), report.BuildDetails());
        }

        private int IndexOf(Document doc)
        {
            if (doc == null)
            {
                return -1;
            }
            for (var i = 0; i < _pending.Count; i++)
            {
                var d = _pending[i];
                if (d.IsValidObject && d.Equals(doc))
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>Idling is only subscribed while a document waits for its check.</summary>
        private void SetIdling(bool on)
        {
            if (_application == null || on == _idlingSubscribed)
            {
                return;
            }
            if (on)
            {
                _application.Idling += OnIdling;
            }
            else
            {
                _application.Idling -= OnIdling;
            }
            _idlingSubscribed = on;
        }
    }
}
