using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Acad = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace RuiCAD.UI
{
    internal sealed class Drafting
    {
        internal const string Cabinet = "RC-柜体";
        internal const string Inside = "RC-内部";
        internal const string Door = "RC-门板";
        internal const string Hardware = "RC-五金";
        internal const string Back = "RC-背板";
        internal const string Guide = "RC-辅助";
        internal const double Epsilon = 0.0001;

        internal readonly Editor Editor;
        internal readonly Database Database;
        internal readonly Matrix3d ToWorld;
        internal readonly Matrix3d ToUcs;

        private Drafting(Document document)
        {
            Editor = document.Editor;
            Database = document.Database;
            ToWorld = Editor.CurrentUserCoordinateSystem;
            ToUcs = ToWorld.Inverse();
        }

        internal static void Run(Action<Drafting> action)
        {
            var document = Acad.DocumentManager.MdiActiveDocument;
            if (document == null)
                return;
            try
            {
                action(new Drafting(document));
            }
            catch (OperationCanceledException)
            {
                // Esc cancels the whole command before the transaction commits.
            }
            catch (InvalidOperationException error)
            {
                document.Editor.WriteMessage("\n[KCAD] " + error.Message);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception error)
            {
                document.Editor.WriteMessage("\n[KCAD] AutoCAD 无法完成操作：" + error.Message);
            }
            catch (System.IO.IOException error)
            {
                document.Editor.WriteMessage("\n[KCAD] 文件操作失败：" + error.Message);
            }
            catch (UnauthorizedAccessException error)
            {
                document.Editor.WriteMessage("\n[KCAD] 无权访问文件：" + error.Message);
            }
        }

        internal void Say(string message) => Editor.WriteMessage("\n[KCAD] " + message);

        internal double[] Settings()
        {
            var defaults = new double[] { 18, 9, 2, 3, 2, 600, 200, 128 };
            using (var args = new ResultBuffer(new TypedValue((int)LispDataType.Text, "kcad:settings")))
            using (var result = TryInvoke(args))
            {
                if (result == null)
                    return defaults;

                var values = result.AsArray().Where(value => value.Value is double || value.Value is short || value.Value is int)
                    .Select(value => Convert.ToDouble(value.Value)).ToArray();
                if (values.Length != defaults.Length)
                    return defaults;
                for (int i = 0; i < defaults.Length; i++)
                {
                    if (values[i] > 0 || (i == 2 && values[i] == 0))
                        defaults[i] = values[i];
                }
            }
            return defaults;
        }

        private static ResultBuffer TryInvoke(ResultBuffer arguments)
        {
            try { return Acad.Invoke(arguments); }
            catch (Autodesk.AutoCAD.Runtime.Exception) { return null; }
        }

        internal string Choice(string message, string defaultKey, params string[] keys)
        {
            var options = new PromptKeywordOptions("\n" + message);
            foreach (var key in keys)
                options.Keywords.Add(key);
            options.Keywords.Default = defaultKey;
            options.AllowNone = true;
            var result = Editor.GetKeywords(options);
            if (result.Status == PromptStatus.None)
                return defaultKey;
            Require(result.Status);
            return result.StringResult;
        }

        internal double Distance(string label, double fallback, bool allowZero = false, bool allowNegative = false)
        {
            var result = Editor.GetDouble(new PromptDoubleOptions("\n" + label)
            {
                DefaultValue = fallback, UseDefaultValue = true, AllowNone = true,
                AllowZero = allowZero, AllowNegative = allowNegative
            });
            if (result.Status == PromptStatus.None)
                return fallback;
            Require(result.Status);
            return result.Value;
        }

        internal int Count(string label, int fallback, int min = 1, int max = 200)
        {
            var result = Editor.GetInteger(new PromptIntegerOptions("\n" + label)
            {
                LowerLimit = min, UpperLimit = max,
                DefaultValue = Math.Max(min, Math.Min(max, fallback)),
                UseDefaultValue = true, AllowNone = true, AllowZero = min == 0, AllowNegative = false
            });
            if (result.Status == PromptStatus.None)
                return Math.Max(min, Math.Min(max, fallback));
            Require(result.Status);
            return result.Value;
        }

        internal Point3d Point(string message, Point3d? from = null)
        {
            var options = new PromptPointOptions("\n" + message);
            if (from.HasValue)
            {
                options.BasePoint = from.Value;
                options.UseBasePoint = true;
            }
            var result = Editor.GetPoint(options);
            Require(result.Status);
            return result.Value;
        }

        internal ObjectId Pick(string message, bool onlyLine = false)
        {
            var options = new PromptEntityOptions("\n" + message);
            if (onlyLine)
            {
                options.SetRejectMessage("\n请选择直线。");
                options.AddAllowedClass(typeof(Line), true);
            }
            var result = Editor.GetEntity(options);
            Require(result.Status);
            return result.ObjectId;
        }

        internal Box Opening(string label)
        {
            var options = new PromptPointOptions("\n" + label + "，点选空腔或选择 [框选(Box)]：");
            options.Keywords.Add("Box");
            options.AppendKeywordsToMessage = false;
            var result = Editor.GetPoint(options);
            if (result.Status == PromptStatus.Keyword)
                return TwoCorners(label);
            Require(result.Status);
            return Trace(result.Value);
        }

        internal Box TwoCorners(string label)
        {
            var first = Point(label + "，第一角点：");
            var result = Editor.GetCorner("\n第二角点：", first);
            Require(result.Status);
            return new Box(first, result.Value);
        }

        internal Box Trace(Point3d seed)
        {
            var objects = Editor.TraceBoundary(seed, true);
            try
            {
                if (objects != null && objects.Count == 1 && objects[0] is Polyline polyline &&
                    TryBox(polyline, ToUcs, out var box) && box.Contains(seed))
                    return box;

                throw new InvalidOperationException("未识别到无孤岛的闭合矩形空腔。请让轮廓完整显示在当前视图，并检查断点；也可输入 Box 框选。");
            }
            finally
            {
                if (objects != null)
                    foreach (DBObject item in objects)
                        item.Dispose();
            }
        }

        internal static bool TryBox(Polyline polyline, Matrix3d toUcs, out Box box)
        {
            box = default;
            if (!polyline.Closed || polyline.NumberOfVertices < 4)
                return false;
            var points = Enumerable.Range(0, polyline.NumberOfVertices)
                .Select(i => polyline.GetPoint3dAt(i).TransformBy(toUcs)).ToArray();
            var min = new Point3d(points.Min(p => p.X), points.Min(p => p.Y), points[0].Z);
            var max = new Point3d(points.Max(p => p.X), points.Max(p => p.Y), points[0].Z);
            if (max.X - min.X <= Epsilon || max.Y - min.Y <= Epsilon)
                return false;
            box = new Box(min, max);
            for (int i = 0; i < points.Length; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % points.Length];
                if (!Near(a.Z, box.Z) || !Near(polyline.GetBulgeAt(i), 0))
                    return false;
                bool vertical = Near(a.X, b.X) && (Near(a.X, box.Left) || Near(a.X, box.Right));
                bool horizontal = Near(a.Y, b.Y) && (Near(a.Y, box.Bottom) || Near(a.Y, box.Top));
                if (!vertical && !horizontal)
                    return false;
            }
            return Math.Abs(polyline.Area - box.Width * box.Height) <= Epsilon * (box.Width + box.Height);
        }

        internal static bool Near(double a, double b) => Math.Abs(a - b) <= Epsilon;
        internal static void Check(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        internal static void Require(PromptStatus status)
        {
            if (status != PromptStatus.OK)
                throw new OperationCanceledException();
        }

        internal void Write(Action<Transaction> draw)
        {
            using (var transaction = Database.TransactionManager.StartTransaction())
            {
                draw(transaction);
                transaction.Commit();
            }
        }

        internal BlockTableRecord Space(Transaction transaction, OpenMode mode = OpenMode.ForRead) =>
            (BlockTableRecord)transaction.GetObject(Database.CurrentSpaceId, mode);

        internal ObjectId Layer(Transaction transaction, string name)
        {
            var table = (LayerTable)transaction.GetObject(Database.LayerTableId, OpenMode.ForRead);
            if (table.Has(name))
                return table[name];
            table.UpgradeOpen();
            short color = name == Inside ? (short)1 : name == Door ? (short)3 :
                name == Hardware ? (short)4 : name == Guide || name == Back ? (short)8 : (short)7;
            using (var layer = new LayerTableRecord { Name = name, Color = Color.FromColorIndex(ColorMethod.ByAci, color) })
            {
                var id = table.Add(layer);
                transaction.AddNewlyCreatedDBObject(layer, true);
                return id;
            }
        }

        internal ObjectId Add(Transaction transaction, Entity entity, string layer, bool fromUcs = true)
        {
            using (entity)
            {
                entity.SetDatabaseDefaults(Database);
                entity.LayerId = Layer(transaction, layer);
                if (fromUcs)
                    entity.TransformBy(ToWorld);
                var id = Space(transaction, OpenMode.ForWrite).AppendEntity(entity);
                transaction.AddNewlyCreatedDBObject(entity, true);
                return id;
            }
        }

        internal ObjectId Line(Transaction transaction, Point3d a, Point3d b, string layer) =>
            Add(transaction, new Line(a, b), layer);

        internal ObjectId Outline(Transaction transaction, string layer, bool closed, params Point3d[] points) =>
            Add(transaction, Shape(closed, points), layer);

        internal static Polyline Shape(bool closed, params Point3d[] points)
        {
            var outline = new Polyline(points.Length) { Closed = closed, Elevation = points[0].Z };
            for (int i = 0; i < points.Length; i++)
                outline.AddVertexAt(i, new Point2d(points[i].X, points[i].Y), 0, 0, 0);
            return outline;
        }

        internal ObjectId Rectangle(Transaction transaction, Box box, string layer) =>
            Outline(transaction, layer, true, box.At(box.Left, box.Bottom), box.At(box.Right, box.Bottom),
                box.At(box.Right, box.Top), box.At(box.Left, box.Top));
    }

    internal readonly struct Box
    {
        internal readonly double Left, Bottom, Right, Top, Z;
        internal double Width => Right - Left;
        internal double Height => Top - Bottom;
        internal double CenterX => (Left + Right) / 2;
        internal double CenterY => (Bottom + Top) / 2;

        internal Box(Point3d first, Point3d second)
            : this(Math.Min(first.X, second.X), Math.Min(first.Y, second.Y),
                Math.Max(first.X, second.X), Math.Max(first.Y, second.Y), first.Z) { }

        internal Box(double left, double bottom, double right, double top, double z)
        {
            Drafting.Check(right - left > Drafting.Epsilon && top - bottom > Drafting.Epsilon, "区域太小，或厚度、缝隙超出区域。");
            Left = left;
            Bottom = bottom;
            Right = right;
            Top = top;
            Z = z;
        }

        internal Point3d At(double x, double y) => new Point3d(x, y, Z);
        internal Box Inset(double distance) => new Box(Left + distance, Bottom + distance, Right - distance, Top - distance, Z);
        internal Box WithX(double left, double right) => new Box(left, Bottom, right, Top, Z);
        internal Box WithY(double bottom, double top) => new Box(Left, bottom, Right, top, Z);
        internal bool Contains(Point3d point) => point.X > Left && point.X < Right && point.Y > Bottom && point.Y < Top;
    }
}
