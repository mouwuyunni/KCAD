using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace RuiCAD.UI
{
    internal sealed class RectSelection
    {
        internal readonly Box Box;
        internal readonly ObjectId[] Edges;
        internal readonly string Layer;

        private RectSelection(Box box, string layer, params ObjectId[] edges)
        {
            Box = box;
            Edges = edges;
            Layer = layer;
        }

        internal static RectSelection Read(Drafting draft, Transaction tr, ObjectId selected)
        {
            var entity = (Entity)tr.GetObject(selected, OpenMode.ForRead);
            if (entity is Polyline polyline && Drafting.TryBox(polyline, draft.ToUcs, out var box))
                return new RectSelection(box, entity.Layer, selected);

            if (entity is Line seed)
            {
                var a = seed.StartPoint.TransformBy(draft.ToUcs);
                var b = seed.EndPoint.TransformBy(draft.ToUcs);
                bool horizontal = Drafting.Near(a.Y, b.Y) && !Drafting.Near(a.X, b.X);
                bool vertical = Drafting.Near(a.X, b.X) && !Drafting.Near(a.Y, b.Y);
                if (horizontal || vertical)
                {
                    var matches = draft.Space(tr).OfType<ObjectId>().Where(id => id != selected)
                        .Select(id => tr.GetObject(id, OpenMode.ForRead) as Line)
                        .Where(line => line != null && line.LayerId == seed.LayerId)
                        .Select(line => (Id: line.ObjectId, A: line.StartPoint.TransformBy(draft.ToUcs),
                            B: line.EndPoint.TransformBy(draft.ToUcs))).ToArray();
                    var corners = new List<RectSelection>();
                    foreach (var side in matches)
                    {
                        Point3d c;
                        if (Same(side.A, b))
                            c = side.B;
                        else if (Same(side.B, b))
                            c = side.A;
                        else
                            continue;

                        if (!Drafting.Near(a.Z, c.Z) ||
                            (horizontal && (!Drafting.Near(b.X, c.X) || Drafting.Near(a.Y, c.Y))) ||
                            (vertical && (!Drafting.Near(b.Y, c.Y) || Drafting.Near(a.X, c.X))))
                            continue;

                        var other = horizontal ? new Point3d(a.X, c.Y, a.Z) : new Point3d(c.X, a.Y, a.Z);
                        var opposite = Find(matches, c, other);
                        var last = Find(matches, other, a);
                        if (!opposite.IsNull && !last.IsNull)
                            corners.Add(new RectSelection(new Box(a, c), seed.Layer, selected, side.Id, opposite, last));
                    }
                    if (corners.Count == 1)
                        return corners[0];
                }
            }

            throw new InvalidOperationException("请选择闭合矩形多段线，或由四条同图层直线构成的独立矩形边。");
        }

        private static ObjectId Find(IEnumerable<(ObjectId Id, Point3d A, Point3d B)> lines, Point3d a, Point3d b)
        {
            foreach (var line in lines)
            {
                if ((Same(line.A, a) && Same(line.B, b)) || (Same(line.B, a) && Same(line.A, b)))
                    return line.Id;
            }
            return ObjectId.Null;
        }

        private static bool Same(Point3d a, Point3d b) => a.DistanceTo(b) <= Drafting.Epsilon;

        internal void Resize(Drafting draft, Transaction tr, Box target, IEnumerable<ObjectId> extra)
        {
            Point3d Map(Point3d world)
            {
                var p = world.TransformBy(draft.ToUcs);
                return target.At(target.Left + (p.X - Box.Left) * target.Width / Box.Width,
                    target.Bottom + (p.Y - Box.Bottom) * target.Height / Box.Height).TransformBy(draft.ToWorld);
            }

            foreach (var id in Edges.Concat(extra))
            {
                var entity = tr.GetObject(id, OpenMode.ForWrite);
                if (entity is Line line)
                {
                    line.StartPoint = Map(line.StartPoint);
                    line.EndPoint = Map(line.EndPoint);
                }
                if (entity is Polyline polyline)
                {
                    var plane = Matrix3d.WorldToPlane(polyline.Normal);
                    for (int i = 0; i < polyline.NumberOfVertices; i++)
                    {
                        var p = Map(polyline.GetPoint3dAt(i)).TransformBy(plane);
                        polyline.SetPointAt(i, new Point2d(p.X, p.Y));
                    }
                }
            }
        }
    }
}
