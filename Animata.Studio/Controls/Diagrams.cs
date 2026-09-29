using Quaternion = System.Numerics.Quaternion;
using Vector3 = System.Numerics.Vector3;
using Avalonia;
using Avalonia.Media;
using Animata.Core.Actuators;
using Animata.Core.Entities;
using Animata.Core.Sensors;
using Animata.Core.WorldObjects;
using Animata.Core.Worlds;
using Animata.Studio.Kit;
using Animata.Studio.Theme;

namespace Animata.Studio.Controls;

/// <summary>Scena z góry w miniaturze (kafel w menu): klocki (z podłogą), cylindry, kule i stwory (wąż — jako łańcuch segmentów).</summary>
public sealed class SceneMiniMap : ThemedControl
{
    private static readonly Color Sky = Color.FromRgb(24, 30, 45);

    public World? World { get; set; }

    public override void Render(DrawingContext aContext)
    {
        var bounds = new Rect(Bounds.Size);
        aContext.DrawRectangle(Ui.Brush(Sky), null, bounds);
        if (World is null || World.Entities.Count == 0)
            return;

        // Zakres: zablokowane klocki (podłogi) i wszystko, co na nich (albo poza nimi) stoi, z małym marginesem.
        var floors = World.Entities.OfType<Box>().Where(aBox => aBox.Locked).ToList();
        var others = World.Entities.Where(aEntity => aEntity is not Box { Locked: true }).ToList();
        var minX = float.PositiveInfinity;
        var maxX = float.NegativeInfinity;
        var minY = float.PositiveInfinity;
        var maxY = float.NegativeInfinity;
        foreach (var floor in floors)
        {
            minX = MathF.Min(minX, floor.Body.Position.X - floor.Size.X / 2);
            maxX = MathF.Max(maxX, floor.Body.Position.X + floor.Size.X / 2);
            minY = MathF.Min(minY, floor.Body.Position.Y - floor.Size.Y / 2);
            maxY = MathF.Max(maxY, floor.Body.Position.Y + floor.Size.Y / 2);
        }
        foreach (var entity in others)
        {
            minX = MathF.Min(minX, entity.Body.Position.X - 1);
            maxX = MathF.Max(maxX, entity.Body.Position.X + 1);
            minY = MathF.Min(minY, entity.Body.Position.Y - 1);
            maxY = MathF.Max(maxY, entity.Body.Position.Y + 1);
        }
        if (!float.IsFinite(minX) || maxX - minX < 1e-3f || maxY - minY < 1e-3f)
            (minX, maxX, minY, maxY) = (-11, 11, -8, 8);
        var scale = Math.Min((bounds.Width - 24) / (maxX - minX), (bounds.Height - 24) / (maxY - minY));
        var offset = new Point(bounds.Width / 2 - (minX + maxX) / 2 * scale, bounds.Height / 2 + (minY + maxY) / 2 * scale);
        Point Map(Vector3 aPosition) => new(offset.X + aPosition.X * scale, offset.Y - aPosition.Y * scale);

        // Klocki: obrócone prostokąty pod wszystkim innym, najpierw zablokowane (podłogi).
        foreach (var slab in floors.Concat(others.OfType<Box>()))
        {
            var heading = Vector3.Transform(Vector3.UnitX, slab.Body.Rotation);
            var angle = Math.Atan2(heading.Y, heading.X);
            var at = Map(slab.Body.Position);
            using (aContext.PushTransform(Matrix.CreateRotation(-angle) * Matrix.CreateTranslation(at.X, at.Y)))
                aContext.DrawRectangle(Ui.Brush(Ui.ColorOf(slab)), null,
                    new Rect(-slab.Size.X / 2 * scale, -slab.Size.Y / 2 * scale, slab.Size.X * scale, slab.Size.Y * scale), 2, 2);
        }

        foreach (var entity in others)
        {
            if (entity is Box)
                continue;
            var center = Map(entity.Body.Position);
            var radius = entity.BoundingRadius * scale;
            var color = Ui.ColorOf(entity);
            switch (entity)
            {
                case ArticulatedCreature body when body.PartPositions.Count > 0:
                    PartSketch.Draw(aContext, body, Map, scale, color, StudioPalette.WithAlpha(color, 0.55), null);
                    break;
                default:
                    aContext.DrawEllipse(Ui.Brush(color), null, center, radius, radius);
                    break;
            }
        }
    }
}

/// <summary>
/// Schemat ciała stwora z góry, w jego układzie (przód w prawo): nadwozie, koła (przednie skręcone jak w aktuatorze),
/// wąsy z ostatniego odczytu (czerwone = trafienie) i kierunek oka do celu. Wąsy są skrócone względem ciała,
/// żeby autko było duże — liczy się proporcja trafienia, nie skala.
/// </summary>
public sealed class BodyDiagram : ThemedControl
{
    private readonly ActiveEntity _creature;
    private readonly World _world;

    public BodyDiagram(ActiveEntity aCreature, World aWorld)
    {
        _creature = aCreature;
        _world = aWorld;
    }

    public override void Render(DrawingContext aContext)
    {
        var size = Bounds.Size;
        var center = new Point(size.Width / 2, size.Height / 2);
        var span = Math.Min(size.Width, size.Height);
        var skin = Math.Max(0.1f, _creature.BoundingRadius);
        // Skala: obrys zajmuje ~20% planszy, a długie ciało (wąż) mieści się w ~75% — wąsy i kierunek oka dochodzą za obrys.
        var extent = _creature is ArticulatedCreature { PartPositions.Count: > 0 } parts ? LocalExtent(parts) : skin;
        var scale = span * 0.2 / Math.Max(skin, extent * 0.55f);
        var reach = span * 0.3;
        var mono = P.Text3;

        // Wąsy.
        var whiskers = _creature.Body.Sensors.OfType<RaySensor>().FirstOrDefault();
        // Przy gęstym wachlarzu (do 25 wąsów) mniejsze kółka trafień, żeby się nie zlewały.
        var blob = whiskers is { Angles.Count: > 7 } ? 7.0 : 14.0;
        if (whiskers is not null)
            for (var ray = 0; ray < whiskers.Angles.Count; ray++)
            {
                var angle = whiskers.Angles[ray];
                var direction = new Vector(Math.Cos(angle), -Math.Sin(angle));
                var start = center + direction * skin * scale;
                var fraction = Math.Clamp(whiskers.LastDistances[ray] / Math.Max(1e-3f, whiskers.Range), 0, 1);
                var hit = fraction < 0.999;
                var end = start + direction * reach * fraction;
                aContext.DrawLine(Draw.Pen(hit ? StudioPalette.Bad : StudioPalette.WithAlpha(mono, 0.6), hit ? 2.5 : 1.5), start, end);
                if (hit)
                    aContext.DrawEllipse(Ui.Brush(StudioPalette.WithAlpha(StudioPalette.Obstacle, 0.6)), null, end + direction * blob, blob, blob);
            }

        // Oko: kierunek do celu.
        var eye = _creature.Body.Sensors.OfType<TargetSensor>().FirstOrDefault();
        if (eye?.TargetId is { } targetId && _world.Find(targetId) is { } target)
        {
            var offset = target.Body.Position - _creature.Body.Position;
            var local = Vector3.Transform(offset, Quaternion.Inverse(_creature.Body.Rotation));
            var direction = local.Length() > 1e-4f ? Vector3.Normalize(local) : Vector3.UnitX;
            var screen = new Vector(direction.X, -direction.Y);
            var from = center + screen * skin * scale;
            var to = center + screen * (skin * scale + reach * 1.25);
            aContext.DrawLine(Draw.Pen(P.Accent, 1.5, new DashStyle([4, 4], 0)), from, to);
            aContext.DrawEllipse(Ui.Brush(StudioPalette.Target), null, to, 12, 12);
            var gap = offset.Length() - _creature.BoundingRadius - target.BoundingRadius;
            Draw.Text(aContext, $"cel · {Ui.F(gap)} m", to + new Vector(0, 26), 12, P.Text2, TextAnchor.Center);
        }

        var bodyColor = Ui.ColorOf(_creature);
        var outline = Draw.Pen(StudioPalette.WithAlpha(P.Text, 0.25), 1.2);
        if (_creature is ArticulatedCreature body && body.PartPositions.Count > 0)
        {
            // Części w układzie stwora (przód w prawo), w tej samej skali co wąsy i oko.
            var inverse = Quaternion.Inverse(body.Body.Rotation);
            Point ToScreen(Vector3 aPoint)
            {
                var local = Vector3.Transform(aPoint - body.Body.Position, inverse);
                return center + new Vector(local.X * scale, -local.Y * scale);
            }
            PartSketch.Draw(aContext, body, ToScreen, scale, bodyColor, mono, outline);
        }
        else
            aContext.DrawEllipse(Ui.Brush(bodyColor), outline, center, skin * scale, skin * scale);
    }

    /// <summary>Największy zasięg części od środka stwora w jego płaszczyźnie (z grubością części).</summary>
    private static float LocalExtent(ArticulatedCreature aCreature)
    {
        var inverse = Quaternion.Inverse(aCreature.Body.Rotation);
        var extent = 0f;
        for (var part = 0; part < aCreature.PartPositions.Count && part < aCreature.Plan.Parts.Count; part++)
        {
            var local = Vector3.Transform(aCreature.PartPositions[part] - aCreature.Body.Position, inverse);
            var plan = aCreature.Plan.Parts[part];
            var reach = plan.Shape switch
            {
                Core.Bodies.PartShape.Box => new Vector2(plan.Size.X, plan.Size.Y).Length() / 2,
                Core.Bodies.PartShape.Capsule => plan.Size.X + plan.Size.Y / 2,
                _ => plan.Size.X
            };
            extent = MathF.Max(extent, new Vector2(local.X, local.Y).Length() + reach);
        }
        return extent;
    }
}

/// <summary>
/// Stwór z góry, część po części, w kształtach z planu ciała: klocek — prostokąt, kapsuła — gruba linia, walec — koło (oś
/// pionowa) albo prostokąt (oś pozioma, np. koło pojazdu — z prawdziwym skrętem), kula — koło. Najpierw części najniższe.
/// Koła pojazdu (dzieci stawów kół) mają osobny kolor. Ten sam rysunek dla każdego stwora z części — nowy nie potrzebuje kodu.
/// </summary>
public static class PartSketch
{
    /// <param name="aMap">Punkt świata → punkt na ekranie.</param>
    /// <param name="aScale">Pikseli na metr.</param>
    public static void Draw(DrawingContext aContext, ArticulatedCreature aCreature, Func<Vector3, Point> aMap, double aScale,
        Color aBody, Color aWheel, IPen? aOutline)
    {
        var plan = aCreature.Plan;
        var count = Math.Min(plan.Parts.Count, aCreature.PartPositions.Count);
        var wheels = plan.Joints.Where(aJoint => aJoint.Kind == Core.Bodies.JointKind.Wheel).Select(aJoint => aJoint.Child).ToHashSet();
        var bodyBrush = Ui.Brush(aBody);
        var wheelBrush = Ui.Brush(aWheel);
        foreach (var index in Enumerable.Range(0, count).OrderBy(aIndex => aCreature.PartPositions[aIndex].Z))
        {
            var part = plan.Parts[index];
            var position = aCreature.PartPositions[index];
            var orientation = aCreature.PartOrientations[index];
            var brush = wheels.Contains(part.Name) ? wheelBrush : bodyBrush;
            var radius = part.Size.X * aScale;
            switch (part.Shape)
            {
                case Core.Bodies.PartShape.Capsule:
                {
                    var axis = Vector3.Transform(Vector3.UnitX, orientation) * (part.Size.Y / 2);
                    var pen = new Pen(brush, Math.Max(2, 2 * radius), lineCap: PenLineCap.Round);
                    aContext.DrawLine(pen, aMap(position - axis), aMap(position + axis));
                    break;
                }
                case Core.Bodies.PartShape.Box:
                    Quad(aContext, brush, aOutline, aMap, position,
                        Vector3.Transform(Vector3.UnitX, orientation) * (part.Size.X / 2),
                        Vector3.Transform(Vector3.UnitY, orientation) * (part.Size.Y / 2));
                    break;
                case Core.Bodies.PartShape.Cylinder:
                {
                    var axis = Vector3.Transform(Vector3.UnitY, orientation);
                    if (new Vector2(axis.X, axis.Y).Length() < 0.3f)
                    {
                        aContext.DrawEllipse(brush, aOutline, aMap(position), radius, radius);
                        break;
                    }
                    var across = Vector3.Normalize(Vector3.Cross(axis, Vector3.UnitZ)) * part.Size.X;
                    Quad(aContext, brush, null, aMap, position, axis * (part.Size.Y / 2), across);
                    break;
                }
                default:
                    aContext.DrawEllipse(brush, aOutline, aMap(position), radius, radius);
                    break;
            }
        }
    }

    /// <summary>Równoległobok pozycja ± a ± b (w świecie), zrzutowany na ekran.</summary>
    private static void Quad(DrawingContext aContext, IBrush aBrush, IPen? aOutline, Func<Vector3, Point> aMap, Vector3 aCenter, Vector3 aA, Vector3 aB)
    {
        var geometry = new StreamGeometry();
        using (var figure = geometry.Open())
        {
            figure.BeginFigure(aMap(aCenter + aA + aB), true);
            figure.LineTo(aMap(aCenter + aA - aB));
            figure.LineTo(aMap(aCenter - aA - aB));
            figure.LineTo(aMap(aCenter - aA + aB));
            figure.EndFigure(true);
        }
        aContext.DrawGeometry(aBrush, aOutline, geometry);
    }
}
