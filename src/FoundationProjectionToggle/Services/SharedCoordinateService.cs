using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.Revit.DB;
using FoundationProjectionToggle.Infrastructure;

namespace FoundationProjectionToggle.Services
{
    /// <summary>
    /// Revit side of the coordinate arithmetic: reads the active project location, the project
    /// base point and the survey point, and lets <see cref="SharedCoordinateMath"/> decide the
    /// internal ↔ shared transform. Refuses (IsValid = false) rather than guessing.
    /// </summary>
    public sealed class SharedCoordinateContext
    {
        internal SharedCoordinateContext()
        {
        }

        public string LocationName { get; internal set; }

        /// <summary>Internal → shared (feet); null when refused.</summary>
        public RigidTransform InternalToShared { get; internal set; }

        /// <summary>Shared coordinates of the project base point, metres.</summary>
        public Vec3 BasePointSharedMetres { get; internal set; }

        /// <summary>Shared coordinates of the survey point, metres.</summary>
        public Vec3 SurveyPointSharedMetres { get; internal set; }

        /// <summary>Angle to true north of the active location, degrees.</summary>
        public double AngleDegrees { get; internal set; }

        /// <summary>The checks that were done (one line each), for the report.</summary>
        public List<string> Checks { get; } = new List<string>();

        /// <summary>Why the button refuses (Dutch); null when OK.</summary>
        public string Error { get; internal set; }

        internal ProjectLocation Location { get; set; }

        public bool IsValid => Error == null && InternalToShared != null;

        /// <summary>RD/NAP metres → internal XYZ (feet).</summary>
        public XYZ ToInternal(double xM, double yM, double zM)
        {
            var p = SharedCoordinateMath.SharedMetresToInternal(InternalToShared, xM, yM, zM);
            return new XYZ(p.X, p.Y, p.Z);
        }

        /// <summary>Internal XYZ (feet) → RD/NAP metres.</summary>
        public Vec3 ToSharedMetres(XYZ internalPoint)
        {
            return SharedCoordinateMath.InternalToSharedMetres(InternalToShared, SharedCoordinateService.ToVec(internalPoint));
        }

        /// <summary>
        /// Revit's own answer for the shared position of an internal point
        /// (ProjectLocation.GetProjectPosition), metres; null when unavailable.
        /// </summary>
        public Vec3? RevitSharedMetres(XYZ internalPoint)
        {
            try
            {
                var pos = Location?.GetProjectPosition(internalPoint);
                if (pos == null)
                {
                    return null;
                }
                return new Vec3(pos.EastWest, pos.NorthSouth, pos.Elevation) / SharedCoordinateMath.FeetPerMetre;
            }
            catch (Exception ex)
            {
                Logger.Warn($"GetProjectPosition failed: {ex.Message}");
                return null;
            }
        }
    }

    public static class SharedCoordinateService
    {
        /// <summary>Reads and checks the shared coordinates of <paramref name="doc"/>.</summary>
        public static SharedCoordinateContext Analyse(Document doc)
        {
            var ctx = new SharedCoordinateContext();
            try
            {
                var loc = doc.ActiveProjectLocation;
                if (loc == null)
                {
                    ctx.Error = "Het project heeft geen actieve locatie (Manage › Location).";
                    return ctx;
                }
                ctx.Location = loc;
                ctx.LocationName = loc.Name;

                var p0 = loc.GetProjectPosition(XYZ.Zero);
                var total = loc.GetTotalTransform();
                ctx.AngleDegrees = p0.Angle * 180.0 / Math.PI;

                var bp = BasePoint.GetProjectBasePoint(doc);
                var sp = BasePoint.GetSurveyPoint(doc);
                var refs = new List<ReferencePoint>();
                if (bp != null)
                {
                    refs.Add(new ReferencePoint("projectbasispunt", ToVec(bp.Position), Reported(bp)));
                    ctx.BasePointSharedMetres = Reported(bp) / SharedCoordinateMath.FeetPerMetre;
                }
                if (sp != null)
                {
                    refs.Add(new ReferencePoint("survey point", ToVec(sp.Position), Reported(sp)));
                    ctx.SurveyPointSharedMetres = Reported(sp) / SharedCoordinateMath.FeetPerMetre;
                }
                if (refs.Count < 2)
                {
                    ctx.Error = "Projectbasispunt of survey point niet gevonden; de omrekening is niet te controleren.";
                    return ctx;
                }

                var resolution = SharedCoordinateMath.Resolve(
                    ToTransform(total),
                    new Vec3(p0.EastWest, p0.NorthSouth, p0.Elevation),
                    p0.Angle,
                    refs);
                ctx.Checks.AddRange(resolution.Checks);
                Logger.Info($"Shared coordinates '{loc.Name}': inverse={resolution.UsedInverse}, valid={resolution.IsValid}; " +
                            string.Join(" | ", resolution.Checks));

                if (!resolution.IsValid)
                {
                    ctx.Error = resolution.Error;
                    return ctx;
                }
                ctx.InternalToShared = resolution.InternalToShared;

                // Independent cross-check with Revit's own conversion for a point away from the
                // origin (catches a wrong rotation direction that the reference points cannot).
                var probeInternal = new XYZ(100.0 * SharedCoordinateMath.FeetPerMetre, 50.0 * SharedCoordinateMath.FeetPerMetre, 0);
                var mine = ctx.ToSharedMetres(probeInternal);
                var revit = ctx.RevitSharedMetres(probeInternal);
                var revitOrigin = ctx.RevitSharedMetres(XYZ.Zero);
                if (revit.HasValue && revitOrigin.HasValue && revit.Value.DistanceTo(revitOrigin.Value) > 1.0)
                {
                    double dMm = mine.DistanceTo(revit.Value) * 1000.0;
                    bool ok = dMm <= SharedCoordinateMath.ToleranceMm;
                    ctx.Checks.Add(string.Format(CultureInfo.InvariantCulture,
                        "{0} Revit-controle (GetProjectPosition) op proefpunt: verschil {1:0.000} mm", ok ? "OK " : "FOUT", dMm));
                    if (!ok)
                    {
                        ctx.InternalToShared = null;
                        ctx.Error = "Revit's eigen omrekening wijkt af van de berekende. Niets geplaatst.";
                        return ctx;
                    }
                }
                else
                {
                    ctx.Checks.Add("-   Revit-controle (GetProjectPosition) op proefpunt: niet beschikbaar");
                }

                if (!CoordinateInputParser.IsWithinRd(ctx.BasePointSharedMetres.X, ctx.BasePointSharedMetres.Y))
                {
                    ctx.InternalToShared = null;
                    ctx.Error =
                        "Dit project heeft geen RD-coördinaten: het projectbasispunt staat op E/W " +
                        CoordinateInputParser.FormatMetres(ctx.BasePointSharedMetres.X) + ", N/S " +
                        CoordinateInputParser.FormatMetres(ctx.BasePointSharedMetres.Y) +
                        " m (gedeelde coördinaten). Stel eerst de RD-coördinaten van het project in " +
                        "(basispunt/survey point of Acquire Coordinates).";
                    return ctx;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("SharedCoordinateService.Analyse failed", ex);
                ctx.InternalToShared = null;
                ctx.Error = "De coördinaten van het project konden niet worden gelezen: " + ex.Message;
            }
            return ctx;
        }

        /// <summary>E/W, N/S, Elev as shown on the base point / survey point (feet).</summary>
        private static Vec3 Reported(BasePoint p)
        {
            return new Vec3(
                p.get_Parameter(BuiltInParameter.BASEPOINT_EASTWEST_PARAM).AsDouble(),
                p.get_Parameter(BuiltInParameter.BASEPOINT_NORTHSOUTH_PARAM).AsDouble(),
                p.get_Parameter(BuiltInParameter.BASEPOINT_ELEVATION_PARAM).AsDouble());
        }

        internal static Vec3 ToVec(XYZ p) => new Vec3(p.X, p.Y, p.Z);

        private static RigidTransform ToTransform(Transform t)
        {
            return new RigidTransform(ToVec(t.Origin), ToVec(t.BasisX), ToVec(t.BasisY), ToVec(t.BasisZ));
        }
    }
}
