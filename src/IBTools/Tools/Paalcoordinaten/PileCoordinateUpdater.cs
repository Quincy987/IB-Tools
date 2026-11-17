using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using IBTools.Shared;

namespace IBTools.Tools.Paalcoordinaten
{
    /// <summary>
    /// Keeps X-/Y-coördinaat_funderingspaal up to date without the button: when a pile is moved,
    /// copied or placed, Revit calls <see cref="Execute"/> inside that same transaction and the
    /// new values are written there (one Ctrl+Z undoes both). A change of the coordinate system
    /// (project base point, survey point, project location) updates every pile.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only in a model on coordinates whose base point self-test passes: otherwise nothing is
    /// written automatically (one log line per document). The button keeps working there.
    /// </para>
    /// <para>
    /// Registered as an OPTIONAL updater: colleagues without IB Tools open the same models
    /// without the "third-party updater is missing" warning.
    /// </para>
    /// <para>
    /// Writing a parameter is no geometry change, so the updater does not trigger itself.
    /// Never throws into Revit (Revit would show an error and offer to switch the updater off).
    /// </para>
    /// </remarks>
    public sealed class PileCoordinateUpdater : IUpdater
    {
        /// <summary>Fixed id of this updater (with the add-in id it forms the UpdaterId).</summary>
        public static readonly Guid UpdaterGuid = new Guid("6C3B7E1A-4F25-4D8B-9A61-2E7F0C5D8B34");

        /// <summary>Id of the registered updater, or null (e.g. for DisableUpdater in tests).</summary>
        public static UpdaterId RegisteredId { get; private set; }

        private readonly UpdaterId _id;

        /// <summary>Documents for which "not on coordinates" was logged already (one line per document).</summary>
        private readonly HashSet<string> _notWritableLogged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public PileCoordinateUpdater(AddInId addInId)
        {
            _id = new UpdaterId(addInId, UpdaterGuid);
        }

        /// <summary>
        /// Registers the updater and its triggers. Returns false (and logs) when that fails, so
        /// the add-in keeps loading without the automatic update.
        /// </summary>
        public bool Register()
        {
            try
            {
                if (UpdaterRegistry.IsUpdaterRegistered(_id))
                {
                    return true;
                }
                UpdaterRegistry.RegisterUpdater(this, true); // optional: no warning without IB Tools

                var foundations = new ElementCategoryFilter(BuiltInCategory.OST_StructuralFoundation);
                UpdaterRegistry.AddTrigger(_id, foundations, Element.GetChangeTypeGeometry());
                UpdaterRegistry.AddTrigger(_id, foundations, Element.GetChangeTypeElementAddition());

                UpdaterRegistry.AddTrigger(_id, CoordinateSystemFilter(), Element.GetChangeTypeAny());

                RegisteredId = _id;
                Logger.Info("Paalcoördinaten: updater registered (optional)");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("Paalcoördinaten: registering the updater failed; piles are only updated by the button", ex);
                return false;
            }
        }

        public void Unregister()
        {
            try
            {
                if (UpdaterRegistry.IsUpdaterRegistered(_id))
                {
                    UpdaterRegistry.UnregisterUpdater(_id);
                }
                RegisteredId = null;
            }
            catch (Exception ex)
            {
                Logger.Error("Paalcoördinaten: unregistering the updater failed", ex);
            }
        }

        /// <summary>Project base point, survey point and project location: the coordinate system.</summary>
        private static ElementFilter CoordinateSystemFilter()
        {
            return new LogicalOrFilter(new List<ElementFilter>
            {
                new ElementCategoryFilter(BuiltInCategory.OST_ProjectBasePoint),
                new ElementCategoryFilter(BuiltInCategory.OST_SharedBasePoint),
                new ElementClassFilter(typeof(ProjectLocation))
            });
        }

        public void Execute(UpdaterData data)
        {
            try
            {
                var doc = data.GetDocument();
                if (doc == null || doc.IsFamilyDocument)
                {
                    return;
                }

                var modified = data.GetModifiedElementIds();
                var added = data.GetAddedElementIds();
                var coordinateSystemChanged = modified.Concat(added).Any(id => IsCoordinateSystemElement(doc, id));

                PileCoordinateEvaluation evaluation;
                string what;
                if (coordinateSystemChanged)
                {
                    evaluation = PileCoordinateService.Evaluate(doc, null, PileScope.WholeModel);
                    what = "coördinatenstelsel gewijzigd, hele model";
                }
                else
                {
                    // Instances only: loading a pile family adds types of this category as well.
                    var piles = modified.Concat(added).Distinct()
                        .Where(id => !(doc.GetElement(id) is ElementType))
                        .ToList();
                    if (piles.Count == 0)
                    {
                        return;
                    }
                    evaluation = PileCoordinateService.Evaluate(doc, piles, PileScope.Changed);
                    what = piles.Count == 1 ? "1 fundering gewijzigd" : piles.Count + " funderingen gewijzigd";
                }

                var key = DocumentKey(doc);
                if (!evaluation.AllowsAutomaticWrite)
                {
                    if (_notWritableLogged.Add(key))
                    {
                        Logger.Info($"Paalcoördinaten updater: '{doc.Title}' {NotWritableReason(evaluation)}: niets automatisch geschreven (knop werkt wel).");
                    }
                    return;
                }
                _notWritableLogged.Remove(key);

                if (evaluation.PendingCount == 0)
                {
                    Logger.Debug($"Paalcoördinaten updater: '{doc.Title}' {what}; niets te schrijven ({evaluation.Report.BuildSummary()})");
                    return;
                }

                PileCoordinateService.ApplyWithoutTransaction(evaluation);
                Logger.Info($"Paalcoördinaten updater: '{doc.Title}' {what}; {evaluation.PendingCount} geschreven: " +
                            string.Join(", ", evaluation.PendingIds) + " (" + evaluation.Report.BuildSummary() + ")");
            }
            catch (Exception ex)
            {
                // Never let Revit see an exception from an updater.
                Logger.Error("Paalcoördinaten updater failed", ex);
            }
        }

        internal static string NotWritableReason(PileCoordinateEvaluation evaluation)
        {
            return evaluation.SelfTestPassed ? "staat niet op coördinaten" : "zelftest projectbasispunt mislukt";
        }

        internal static string DocumentKey(Document doc)
        {
            var path = doc.PathName;
            return string.IsNullOrEmpty(path) ? "title:" + doc.Title : path;
        }

        private static bool IsCoordinateSystemElement(Document doc, ElementId id)
        {
            var e = doc.GetElement(id);
            if (e == null)
            {
                return false;
            }
            if (e is ProjectLocation || e is BasePoint)
            {
                return true;
            }
            var cat = e.Category;
            if (cat == null)
            {
                return false;
            }
#if REVIT2024 || REVIT2025_OR_GREATER
            var catId = cat.Id.Value;
#else
            var catId = cat.Id.IntegerValue;
#endif
            return catId == (int)BuiltInCategory.OST_ProjectBasePoint || catId == (int)BuiltInCategory.OST_SharedBasePoint;
        }

        public UpdaterId GetUpdaterId() => _id;

        public ChangePriority GetChangePriority() => ChangePriority.Structure;

        public string GetUpdaterName() => "IB Tools: paalcoördinaten bijwerken";

        public string GetAdditionalInformation() =>
            "Schrijft de RD-coördinaten van verplaatste, gekopieerde en nieuwe palen in X-/Y-coördinaat_funderingspaal " +
            "(IB Tools, Ingenieursbureau Gemeente Amsterdam). Optioneel: zonder IB Tools opent het model gewoon.";
    }
}
