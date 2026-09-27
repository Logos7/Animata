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

/// <summary>Scena z góry w miniaturze (kafel w menu): podłogi, słupki, cele i stwory (wąż — jako łańcuch segmentów).</summary>
public sealed class SceneMiniMap : ThemedControl
{
    private static readonly Color Ground = Color.FromRgb(51, 69, 84);
    private static readonly Color Sky = Color.FromRgb(24, 30, 45);

    public World? World { get; set; }

    public override void Render(DrawingContext aContext)
    {
        var bounds = new Rect(Bounds.Size);
        aContext.DrawRectangle(Ui.Brush(Sky), null, bounds);
        if (World is null || World.Entities.Count == 0)
            return;

        // Zakres: podłogi i wszystko, co na nich (albo poza nimi) stoi, z małym marginesem.
        var floors = World.Entities.OfType<Floor>().ToList();
        var others = World.Entities.Where(aEntity => aEntity is not Floor).ToList();
        var minX = float.PositiveInfinity;
        var maxX = float.NegativeInfinity;
        var minY = float.PositiveInfinity;
        var maxY = float.NegativeInfinity;
        foreach (var floor in floors)
        {
            minX = MathF.Min(minX, floor.Min.X);
            maxX = MathF.Max(maxX, floor.Max.X);
            minY = MathF.Min(minY, floor.Min.Y);
            maxY = MathF.Max(maxY, floor.Max.Y);
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

        foreach (var floor in floors)
            aContext.DrawRectangle(Ui.Brush(Ground), null,
                new Rect(Map(new Vector3(floor.Min.X, floor.Max.Y, 0)), Map(new Vector3(floor.Max.X, floor.Min.Y, 0))), 6, 6);

        foreach (var entity in others)
        {
            var center = Map(entity.Body.Position);
            var radius = entity.BoundingRadius * scale;
            var color = Ui.ColorOf(entity);
            switch (entity)
            {
                case CarCreature car:
                    var heading = Vector3.Transform(Vector3.UnitX, car.Body.Rotation);
                    var yaw = Math.Atan2(heading.Y, heading.X);
                    using (aContext.PushTransform(Matrix.CreateRotation(-yaw) * Matrix.CreateTranslation(center.X, center.Y)))
                        aContext.DrawRectangle(Ui.Brush(color), null,
                            new Rect(-car.Length / 2 * scale, -car.Width / 2 * scale, car.Length * scale, car.Width * scale), 3, 3);
                    break;
                case CylinderCreature:
                    aContext.DrawEllipse(Ui.Brush(color), null, center, radius, radius);
                    break;
                case ArticulatedCreature body:
                {
                    var pen = new Pen(Ui.Brush(color), Math.Max(3, body.BoundingRadius * 2 * scale), lineCap: PenLineCap.Round);
                    for (var index = 1; index < body.PartPositions.Count; index++)
                        aContext.DrawLine(pen, Map(body.PartPositions[index - 1]), Map(body.PartPositions[index]));
                    if (body.PartPositions.Count > 0)
                        aContext.DrawEllipse(Ui.Brush(color), null, Map(body.PartPositions[0]), pen.Thickness * 0.8, pen.Thickness * 0.8);
                    break;
                }
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
        var center = new Point(size.Width * 0.4, size.Height / 2);
        var span = Math.Min(size.Width, size.Height);
        var skin = Math.Max(0.1f, _creature.BoundingRadius);
        var scale = span * 0.2 / skin;
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
        switch (_creature)
        {
            case CarCreature car:
            {
                var length = car.Length * scale;
                var width = car.Width * scale;
                var wheelLength = 0.26 * scale;
                var wheelWidth = 0.1 * scale + 4;
                var steer = _creature.Body.Actuators.OfType<SteeringDriveActuator>().FirstOrDefault()?.SteerAngle ?? 0;
                foreach (var along in new[] { -0.32, 0.32 })
                    foreach (var side in new[] { -1.0, 1.0 })
                    {
                        var wheelCenter = center + new Vector(along * length, side * (width / 2 + wheelWidth / 2));
                        var rotation = along > 0 ? -steer : 0;
                        using (aContext.PushTransform(Matrix.CreateRotation(rotation) * Matrix.CreateTranslation(wheelCenter.X, wheelCenter.Y)))
                            aContext.DrawRectangle(Ui.Brush(along > 0 ? P.Accent : mono), null,
                                new Rect(-wheelLength / 2, -wheelWidth / 2, wheelLength, wheelWidth), 3, 3);
                    }
                aContext.DrawRectangle(Ui.Brush(bodyColor), outline, new Rect(center.X - length / 2, center.Y - width / 2, length, width), 12, 12);
                aContext.DrawEllipse(Ui.Brush(P.Text), null, center + new Vector(length / 2, 0), 7, 7);
                Callout(aContext, center + new Vector(-0.32 * length, width / 2 + wheelWidth), new Point(16, size.Height - 22), "Koła · SteeringDrive");
                break;
            }
            case CylinderCreature cylinder:
            {
                var radius = cylinder.Radius * scale;
                foreach (var side in new[] { -1.0, 1.0 })
                    aContext.DrawRectangle(Ui.Brush(P.Accent), null,
                        new Rect(center.X - radius * 0.2, center.Y + side * radius - 6, radius * 0.4, 12), 4, 4);
                aContext.DrawEllipse(Ui.Brush(bodyColor), outline, center, radius, radius);
                aContext.DrawEllipse(Ui.Brush(P.Text), null, center + new Vector(radius * 1.08, 0), radius * 0.2, radius * 0.2);
                Callout(aContext, center + new Vector(0, radius + 6), new Point(16, size.Height - 22), "Koła · DiskDrive");
                break;
            }
            case ArticulatedCreature body when body.PartPositions.Count > 0:
            {
                // Części w układzie stwora (przód w prawo), skala dopasowana do długości ciała.
                var inverse = Quaternion.Inverse(body.Body.Rotation);
                var local = body.PartPositions.Select(aPosition => Vector3.Transform(aPosition - body.Body.Position, inverse)).ToList();
                var extent = local.Max(aPoint => MathF.Max(MathF.Abs(aPoint.X), MathF.Abs(aPoint.Y))) + body.BoundingRadius * 2;
                var fit = Math.Min(size.Width, size.Height) * 0.38 / Math.Max(0.1f, extent);
                Point ToScreen(Vector3 aPoint) => center + new Vector(aPoint.X * fit, -aPoint.Y * fit);
                var thickness = Math.Max(6, body.BoundingRadius * 2 * fit);
                var pen = new Pen(Ui.Brush(bodyColor), thickness, lineCap: PenLineCap.Round);
                for (var index = 1; index < local.Count; index++)
                    aContext.DrawLine(pen, ToScreen(local[index - 1]), ToScreen(local[index]));
                for (var index = 0; index < local.Count; index++)
                    aContext.DrawEllipse(Ui.Brush(index == 0 ? P.Accent : StudioPalette.WithAlpha(P.Text, 0.35)), null,
                        ToScreen(local[index]), index == 0 ? thickness * 0.45 : 2.5, index == 0 ? thickness * 0.45 : 2.5);
                Callout(aContext, ToScreen(local[^1]), new Point(16, size.Height - 22),
                    $"Kręgosłup · {body.JointCount} stawów (skręt + pochylenie)");
                break;
            }
            default:
                aContext.DrawEllipse(Ui.Brush(bodyColor), outline, center, skin * scale, skin * scale);
                break;
        }

        if (eye is not null)
            Callout(aContext, center + new Vector(skin * scale, 0), new Point(size.Width - 16, 26), "Oko · TargetSensor", TextAnchor.Right);
        if (whiskers is not null)
            Callout(aContext, center + new Vector(Math.Cos(0.5) * (skin * scale + reach * 0.7), -Math.Sin(0.5) * (skin * scale + reach * 0.7)),
                new Point(size.Width - 16, 56), $"Wąsy · RaySensor ×{whiskers.Angles.Count}", TextAnchor.Right);
    }

    private static void Callout(DrawingContext aContext, Point aFrom, Point aLabel, string aText, TextAnchor aAnchor = TextAnchor.Left)
    {
        var width = Draw.Format(aText, 12.5, P.Text, true).Width;
        var lineEnd = aAnchor == TextAnchor.Right ? new Point(aLabel.X - width - 6, aLabel.Y) : new Point(aLabel.X + width + 6, aLabel.Y);
        aContext.DrawLine(Draw.Pen(P.Text3, 1), aFrom, lineEnd);
        Draw.Text(aContext, aText, aLabel, 12.5, P.Text, aAnchor, true);
    }
}
