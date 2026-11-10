using System.Globalization;
namespace Aholo.Core;

public abstract record SpatialEntity(string Id, int Line);
public sealed record SpatialWall(string Id, int Line, GaussianVector A, GaussianVector B, double Height, double Thickness) : SpatialEntity(Id, Line);
public sealed record SpatialOpening(string Id, int Line, string Type, string WallId, GaussianVector Center, double Width, double Height) : SpatialEntity(Id, Line);
public sealed record SpatialBox(string Id, int Line, string Class, GaussianVector Center, double AngleRadians, GaussianVector Size) : SpatialEntity(Id, Line);
public sealed record SpatialLayout(IReadOnlyList<SpatialEntity> Entities, IReadOnlyList<string> Warnings);
public sealed record SpatialShape(SpatialEntity Entity, string Kind, GaussianVector[] Points);

/// <summary>Official SpatialUnderstanding DSL: metres, right-handed Z-up; no executable expressions.</summary>
public static class SpatialDsl
{
    public static SpatialLayout Read(TextReader reader, CancellationToken ct = default)
    {
        var entities = new List<SpatialEntity>(); var warnings = new List<string>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        int line = 0; string? raw;
        while (true)
        {
            ct.ThrowIfCancellationRequested(); raw = reader.ReadLine(); if (raw is null) break;
            line++; string text = (line == 1 ? raw.TrimStart('\uFEFF') : raw).Trim();
            if (text.Length == 0) continue;
            if (text.EndsWith(';')) text = text[..^1].TrimEnd();
            int equals = text.IndexOf('='), open = text.IndexOf('(', Math.Max(0, equals + 1));
            if (equals <= 0 || open <= equals + 1 || !text.EndsWith(')')) throw Error(line, "Expected id=Type(arguments).");
            string id = text[..equals].Trim(), type = text[(equals + 1)..open].Trim();
            if (type is not ("Wall" or "Door" or "Window" or "Bbox"))
            { warnings.Add(UiText.Format("Line {0}: an unknown spatial entity type was skipped.", line)); continue; }
            if (!SafeId(id)) throw Error(line, "Invalid entity identifier.");
            if (!ids.Add(id)) throw Error(line, "Duplicate entity identifier.");
            string body = text[(open + 1)..^1];
            if (type == "Bbox")
            {
                var numbers = new double[7]; int end = body.Length;
                for (int i = 6; i >= 0; i--)
                {
                    int comma = end > 0 ? body.LastIndexOf(',', end - 1) : -1;
                    if (comma < 0) throw Error(line, "A box needs a class and seven numbers.");
                    numbers[i] = Number(body[(comma + 1)..end], line); end = comma;
                }
                string label = body[..end].Trim();
                if (label.Length == 0) throw Error(line, "The box class is empty.");
                if (numbers[4] <= 0 || numbers[5] <= 0 || numbers[6] <= 0) throw Error(line, "Box dimensions must be positive.");
                entities.Add(new SpatialBox(id, line, label, new(numbers[0], numbers[1], numbers[2]), numbers[3], new(numbers[4], numbers[5], numbers[6])));
                continue;
            }
            string[] fields = body.Split(',');
            if (fields.Length != (type == "Wall" ? 8 : 6)) throw Error(line, "The entity has the wrong number of arguments.");
            if (type == "Wall")
            {
                double[] n = fields.Select(value => Number(value, line)).ToArray();
                if (n[6] <= 0 || n[7] < 0) throw Error(line, "Wall height must be positive and thickness nonnegative.");
                if (n[0] == n[3] && n[1] == n[4]) throw Error(line, "The wall baseline has no horizontal direction.");
                entities.Add(new SpatialWall(id, line, new(n[0], n[1], n[2]), new(n[3], n[4], n[5]), n[6], n[7]));
            }
            else
            {
                string wall = fields[0].Trim(); if (!SafeId(wall)) throw Error(line, "Invalid wall reference.");
                double[] n = fields.Skip(1).Select(value => Number(value, line)).ToArray();
                if (n[3] <= 0 || n[4] <= 0) throw Error(line, "Opening dimensions must be positive.");
                entities.Add(new SpatialOpening(id, line, type, wall, new(n[0], n[1], n[2]), n[3], n[4]));
            }
        }
        var walls = entities.OfType<SpatialWall>().Select(w => w.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var opening in entities.OfType<SpatialOpening>())
        {
            ct.ThrowIfCancellationRequested();
            if (!walls.Contains(opening.WallId)) throw Error(opening.Line, "The opening refers to a missing or non-wall entity.");
        }
        if (entities.Count == 0) throw new InvalidDataException(UiText.Get("The spatial result contains no supported entities."));
        return new(entities, warnings);
    }

    public static SpatialShape[] Shapes(SpatialLayout layout, CancellationToken ct = default)
    {
        var walls = layout.Entities.OfType<SpatialWall>().ToDictionary(w => w.Id, StringComparer.Ordinal);
        var shapes = new List<SpatialShape>();
        foreach (var entity in layout.Entities)
        {
            ct.ThrowIfCancellationRequested(); GaussianVector[] p; string kind;
            if (entity is SpatialWall wall)
            {
                p = new[] { wall.A, wall.B, new(wall.B.X, wall.B.Y, wall.B.Z + wall.Height), new(wall.A.X, wall.A.Y, wall.A.Z + wall.Height) };
                kind = "Wall";
            }
            else if (entity is SpatialOpening opening)
            {
                var wallForOpening = walls[opening.WallId];
                double dx = wallForOpening.B.X - wallForOpening.A.X, dy = wallForOpening.B.Y - wallForOpening.A.Y;
                double largest = Math.Max(Math.Abs(dx), Math.Abs(dy));
                double length = Math.Sqrt((dx / largest) * (dx / largest) + (dy / largest) * (dy / largest));
                dx = dx / largest / length * (opening.Width / 2); dy = dy / largest / length * (opening.Width / 2);
                var c = opening.Center; double z = opening.Height / 2;
                p = new[] { new GaussianVector(c.X - dx, c.Y - dy, c.Z - z), new(c.X + dx, c.Y + dy, c.Z - z), new(c.X + dx, c.Y + dy, c.Z + z), new(c.X - dx, c.Y - dy, c.Z + z) };
                kind = opening.Type;
            }
            else if (entity is SpatialBox box)
            {
                double cos = Math.Cos(box.AngleRadians), sin = Math.Sin(box.AngleRadians);
                var signs = new[] { (-1,-1,-1), (1,-1,-1), (1,1,-1), (-1,1,-1), (-1,-1,1), (1,-1,1), (1,1,1), (-1,1,1) };
                p = signs.Select(s => { double x = s.Item1 * (box.Size.X / 2), y = s.Item2 * (box.Size.Y / 2); return new GaussianVector(box.Center.X + cos * x - sin * y, box.Center.Y + sin * x + cos * y, box.Center.Z + s.Item3 * (box.Size.Z / 2)); }).ToArray();
                kind = "Bbox";
            }
            else throw new InvalidDataException(UiText.Get("Unsupported spatial entity."));
            if (p.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y) || !double.IsFinite(point.Z))) throw Error(entity.Line, "Spatial geometry exceeds the numeric range.");
            shapes.Add(new(entity, kind, p));
        }
        return shapes.ToArray();
    }
    private static bool SafeId(string text) => text.Length > 0 && text.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.');
    private static double Number(string text, int line) => double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number) ? number : throw Error(line, "Expected a finite number.");
    private static InvalidDataException Error(int line, string reason) => new(UiText.Format("Spatial result line {0}: {1}", line, UiText.Get(reason)));
}
