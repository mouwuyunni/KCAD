using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

namespace RuiCAD.UI
{
    public sealed class SectionCommands
    {
        private const string Body = "RC-剖面-柜体";
        private const string Shelf = "RC-剖面-内部";
        private const string Rear = "RC-剖面-背板";
        private const string Front = "RC-剖面-门板";

        [CommandMethod("PM")]
        public void DoorSection() => Drafting.Run(d =>
        {
            var settings = d.Settings();
            double height = 720;
            if (d.Choice("门板剖面（Select 读取门板高度 / Input 输入高度）", "Input", "Select", "Input") == "Select")
            {
                var id = d.Pick("选择现有门板矩形：");
                using (var tr = d.Database.TransactionManager.StartOpenCloseTransaction())
                    height = RectSelection.Read(d, tr, id).Box.Height;
            }
            else
                height = d.Distance("门板高度", height);

            double thickness = d.Distance("门板厚度", settings[0]);
            var origin = d.Point("门板剖面左下角：");
            d.Write(tr =>
            {
                var box = new Box(origin.X, origin.Y, origin.X + thickness, origin.Y + height, origin.Z);
                d.Rectangle(tr, box, Front);
                Label(d, tr, box.At(box.Right + 12, box.CenterY), "门板 " + thickness.ToString("0.##"));
            });
        });

        [CommandMethod("PP")]
        public void CutSection() => Drafting.Run(d =>
        {
            var start = d.Point("剖切线起点：");
            var end = d.Point("剖切线终点（横向或纵向）：", start);
            bool vertical = Drafting.Near(start.X, end.X);
            Drafting.Check(Drafting.Near(start.Z, end.Z) && start.DistanceTo(end) > Drafting.Epsilon &&
                (vertical || Drafting.Near(start.Y, end.Y)), "剖切线必须为同一 UCS 平面的水平或垂直线。");

            var settings = d.Settings();
            double depth = d.Distance("柜体剖面深度", settings[5]);
            double frontThickness = d.Distance("门板剖面厚度", settings[0]);
            var profiles = ReadCut(d, start, end, settings[0], settings[1]);
            Drafting.Check(profiles.Count > 0, "剖切线范围内没有识别到可生成剖面的 RC 结构件。");
            var origin = d.Point("指定剖面图的输出原点：");
            double length = start.DistanceTo(end);
            d.Write(tr =>
            {
                d.Line(tr, start, end, Drafting.Guide);
                foreach (var profile in profiles)
                {
                    double low = profile.Layer == Front ? depth : 0;
                    double high = profile.Layer == Front ? depth + frontThickness :
                        profile.Layer == Rear ? Math.Min(profile.Thickness, depth) : depth;
                    var outline = vertical
                        ? new Box(origin.X + low, origin.Y + profile.Begin, origin.X + high, origin.Y + profile.End, origin.Z)
                        : new Box(origin.X + profile.Begin, origin.Y + low, origin.X + profile.End, origin.Y + high, origin.Z);
                    d.Rectangle(tr, outline, profile.Layer);
                }

                var title = vertical ? origin + Vector3d.YAxis * (length + 28) : origin + Vector3d.YAxis * (depth + frontThickness + 28);
                Label(d, tr, title, vertical ? "柜体纵剖 · 由剖切起点向上" : "柜体横剖 · 由剖切起点向右");
            });
            d.Say($"剖面已生成，识别 {profiles.Count} 个剖切部位。");
        });

        private static List<Span> ReadCut(Drafting d, Point3d start, Point3d end, double boardThickness, double backThickness)
        {
            var result = new List<Span>();
            var cabinetHits = new List<double>();
            var oldBorders = new List<double>();
            var oldShelves = new List<double>();
            var oldDoors = new List<double>();
            var worldStart = start.TransformBy(d.ToWorld);
            var worldEnd = end.TransformBy(d.ToWorld);
            var direction = (worldEnd - worldStart).GetNormal();
            double length = worldStart.DistanceTo(worldEnd);
            using (var cut = new Line(worldStart, worldEnd))
            using (var tr = d.Database.TransactionManager.StartOpenCloseTransaction())
            {
                foreach (ObjectId id in d.Space(tr))
                {
                    var entity = tr.GetObject(id, OpenMode.ForRead) as Curve;
                    if (entity == null)
                        continue;
                    var layer = entity.Layer;
                    if (layer != Drafting.Cabinet && layer != Drafting.Inside && layer != Drafting.Door && layer != Drafting.Back)
                        continue;
                    var tag = GeometryTags.Get(entity);
                    if (tag.Role == "swing" || tag.Role == "decoration")
                        continue;
                    if (!entity.Closed && !(entity is Line))
                        continue;
                    var points = new Point3dCollection();
                    cut.IntersectWith(entity, Intersect.ExtendThis, points, IntPtr.Zero, IntPtr.Zero);
                    var hits = Distinct(points.Cast<Point3d>().Select(point => (point - worldStart).DotProduct(direction)));
                    if (hits.Length == 0)
                        continue;

                    if (layer == Drafting.Cabinet)
                    {
                        if (entity.Closed)
                        {
                            if (tag.Role == "frameInner" && hits.Length == 2 && tag.Value > 0)
                            {
                                Add(result, hits[0] - tag.Value, hits[0], Body, length);
                                Add(result, hits[1], hits[1] + tag.Value, Body, length);
                            }
                            else
                                cabinetHits.AddRange(hits);
                        }
                        else if (hits.Length == 1)
                            oldBorders.Add(hits[0]);
                    }
                    else if (entity.Closed)
                    {
                        var target = layer == Drafting.Inside ? Shelf : layer == Drafting.Door ? Front : Rear;
                        var thickness = tag.Role == "back" && tag.Value > 0 ? tag.Value : backThickness;
                        Pair(result, hits, target, length, thickness);
                    }
                    else if (hits.Length == 1 && layer == Drafting.Inside)
                        oldShelves.Add(hits[0]);
                    else if (hits.Length == 1 && layer == Drafting.Door)
                        oldDoors.Add(hits[0]);
                }
            }

            Pair(result, Distinct(cabinetHits), Body, length);
            Pair(result, Distinct(oldDoors), Front, length);
            AddLegacyBoards(result, oldBorders, oldShelves, length, boardThickness);
            return result.OrderBy(span => span.Begin).ThenBy(span => span.Layer).ToList();
        }

        private static void AddLegacyBoards(List<Span> result, List<double> borders, List<double> shelves, double length, double thickness)
        {
            var hits = Distinct(borders.Concat(shelves));
            for (int i = 0; i < hits.Length; i++)
            {
                double begin = hits[i];
                bool cabinet = borders.Any(value => Drafting.Near(value, begin));
                if (i + 1 < hits.Length)
                {
                    double end = hits[i + 1];
                    double distance = end - begin;
                    if (distance >= thickness * 0.5 && distance <= thickness * 1.5)
                    {
                        bool nextIsCabinet = borders.Any(value => Drafting.Near(value, end));
                        Add(result, begin, end, cabinet || nextIsCabinet ? Body : Shelf, length);
                        i++;
                        continue;
                    }
                }
                if (!cabinet)
                    Add(result, begin - thickness / 2, begin + thickness / 2, Shelf, length);
            }
        }

        private static void Pair(List<Span> result, double[] hits, string layer, double length, double thickness = 0)
        {
            for (int i = 1; i < hits.Length; i += 2)
                Add(result, hits[i - 1], hits[i], layer, length, thickness);
        }

        private static void Add(List<Span> result, double start, double end, string layer, double length, double thickness = 0)
        {
            start = Math.Max(0, start);
            end = Math.Min(length, end);
            if (end > start + Drafting.Epsilon &&
                !result.Any(item => item.Layer == layer && Drafting.Near(item.Begin, start) && Drafting.Near(item.End, end)))
                result.Add(new Span(start, end, layer, thickness));
        }

        private static double[] Distinct(IEnumerable<double> source)
        {
            var result = new List<double>();
            foreach (var value in source.OrderBy(value => value))
            {
                if (result.Count == 0 || !Drafting.Near(result[result.Count - 1], value))
                    result.Add(value);
            }
            return result.ToArray();
        }

        private static void Label(Drafting d, Transaction tr, Point3d location, string text) =>
            d.Add(tr, new DBText { Position = location, Height = 20, TextString = text }, "RC-文字");

        private readonly struct Span
        {
            public readonly double Begin, End, Thickness;
            public readonly string Layer;

            public Span(double begin, double end, string layer, double thickness)
            {
                Begin = begin;
                End = end;
                Layer = layer;
                Thickness = thickness;
            }
        }
    }
}
