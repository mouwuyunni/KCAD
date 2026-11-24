using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

namespace RuiCAD.UI
{
    public sealed class UtilityCommands
    {
        [CommandMethod("YS")]
        public void Fill() => Drafting.Run(d =>
        {
            var id = d.Pick("选择闭合轮廓或矩形边：");
            var style = d.Choice("样式填充（Wood 木纹 / Diagonal 斜线 / Solid 实心 / Glass 玻璃）",
                "Diagonal", "Wood", "Diagonal", "Solid", "Glass");
            double scale = style == "Solid" ? 1 : d.Distance("填充比例", 10);
            var patterns = new Dictionary<string, string>
            {
                { "Wood", "AR-PARQ1" }, { "Diagonal", "ANSI31" }, { "Solid", "SOLID" }, { "Glass", "ANSI37" }
            };
            d.Write(tr =>
            {
                var entity = (Entity)tr.GetObject(id, OpenMode.ForRead);
                bool closed = entity is Curve curve && curve.Closed;
                var boundary = closed ? new[] { id } : RectSelection.Read(d, tr, id).Edges;
                Vector3d normal = Vector3d.ZAxis.TransformBy(d.ToWorld);
                var hatch = new Hatch { Normal = normal };
                var plane = Matrix3d.WorldToPlane(normal);
                hatch.Elevation = ((Curve)entity).StartPoint.TransformBy(plane).Z;
                var hatchId = d.Add(tr, hatch, entity.Layer, false);
                hatch = (Hatch)tr.GetObject(hatchId, OpenMode.ForWrite);
                hatch.SetHatchPattern(HatchPatternType.PreDefined, patterns[style]);
                if (style != "Solid")
                {
                    hatch.PatternScale = scale;
                    hatch.SetHatchPattern(HatchPatternType.PreDefined, patterns[style]);
                }
                hatch.Associative = true;
                hatch.AppendLoop(HatchLoopTypes.External, new ObjectIdCollection(boundary));
                hatch.EvaluateHatch(true);
            });
        });

        [CommandMethod("BT")]
        public void Thickness() => Drafting.Run(d =>
        {
            var id = d.Pick("选择板件闭合矩形或旧版矩形的一条边：");
            var thickness = d.Distance("新板厚，以板件中心线对称调整", d.Settings()[0]);
            d.Write(tr =>
            {
                var board = RectSelection.Read(d, tr, id);
                var box = board.Box;
                var half = thickness / 2;
                Drafting.Check(thickness < Math.Max(box.Width, box.Height), "板厚必须小于板件长度。");
                var resized = box.Width >= box.Height ? box.WithY(box.CenterY - half, box.CenterY + half)
                    : box.WithX(box.CenterX - half, box.CenterX + half);
                board.Resize(d, tr, resized, Array.Empty<ObjectId>());
            });
        });

        [CommandMethod("BE")]
        public void Extend() => Drafting.Run(d =>
        {
            bool asBoard = d.Choice("延伸对象（Line 单线 / Board 矩形板件）", "Line", "Line", "Board") == "Board";
            var objectId = d.Pick(asBoard ? "选择板件矩形边：" : "选择需要延伸的直线：", !asBoard);
            var tip = d.Point("点选要延伸的一端：");
            var borderId = d.Pick("选择目标边界：");
            Drafting.Check(objectId != borderId, "目标边界不能是被延伸对象自身。");
            d.Write(tr =>
            {
                var border = tr.GetObject(borderId, OpenMode.ForRead) as Curve;
                Drafting.Check(border != null, "目标边界必须为直线、圆弧、多段线等曲线对象。");
                if (!asBoard)
                {
                    var line = (Line)tr.GetObject(objectId, OpenMode.ForWrite);
                    bool fromStart = tip.TransformBy(d.ToWorld).DistanceTo(line.StartPoint) < tip.TransformBy(d.ToWorld).DistanceTo(line.EndPoint);
                    var end = fromStart ? line.StartPoint : line.EndPoint;
                    var opposite = fromStart ? line.EndPoint : line.StartPoint;
                    var hit = Extension(end, opposite, border);
                    if (fromStart) line.StartPoint = hit;
                    else line.EndPoint = hit;
                    return;
                }

                var board = RectSelection.Read(d, tr, objectId);
                Drafting.Check(!board.Edges.Contains(borderId), "目标边界不能是板件自身。");
                var box = board.Box;
                bool horizontal = box.Width >= box.Height;
                bool extendHigh = horizontal ? tip.X >= box.CenterX : tip.Y >= box.CenterY;
                var low = horizontal ? box.At(box.Left, box.CenterY) : box.At(box.CenterX, box.Bottom);
                var high = horizontal ? box.At(box.Right, box.CenterY) : box.At(box.CenterX, box.Top);
                var hitUcs = Extension((extendHigh ? high : low).TransformBy(d.ToWorld),
                    (extendHigh ? low : high).TransformBy(d.ToWorld), border).TransformBy(d.ToUcs);
                Box target = horizontal
                    ? box.WithX(extendHigh ? box.Left : hitUcs.X, extendHigh ? hitUcs.X : box.Right)
                    : box.WithY(extendHigh ? box.Bottom : hitUcs.Y, extendHigh ? hitUcs.Y : box.Top);
                board.Resize(d, tr, target, Array.Empty<ObjectId>());
            });
        });

        private static Point3d Extension(Point3d end, Point3d opposite, Curve border)
        {
            var direction = (end - opposite).GetNormal();
            var hits = new Point3dCollection();
            using (var axis = new Line(opposite, end))
                axis.IntersectWith(border, Intersect.ExtendThis, hits, IntPtr.Zero, IntPtr.Zero);

            var options = hits.Cast<Point3d>().Where(point => (point - end).DotProduct(direction) > Drafting.Epsilon)
                .OrderBy(point => point.DistanceTo(end)).ToArray();
            Drafting.Check(options.Length != 0, "延伸方向上没有边界交点。");
            return options[0];
        }

        [CommandMethod("RCML")]
        public void DoorVisibility() => Drafting.Run(d => d.Write(tr =>
        {
            var id = d.Layer(tr, Drafting.Door);
            var layer = (LayerTableRecord)tr.GetObject(id, OpenMode.ForWrite);
            if (!layer.IsOff && d.Database.Clayer == id)
                d.Database.Clayer = d.Layer(tr, Drafting.Cabinet);
            layer.IsOff = !layer.IsOff;
            d.Say(layer.IsOff ? "门板图层已隐藏。" : "门板图层已显示。");
        }));

        [CommandMethod("RCL")]
        public void DrawingLayer() => Drafting.Run(d =>
        {
            using (var transaction = d.Database.TransactionManager.StartOpenCloseTransaction())
            {
                var table = (LayerTable)transaction.GetObject(d.Database.LayerTableId, OpenMode.ForRead);
                var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    Drafting.Cabinet, Drafting.Inside, Drafting.Door, Drafting.Hardware, Drafting.Back,
                    Drafting.Guide, "RC-灯带", "RC-标注", "RC-文字"
                };
                foreach (ObjectId id in table)
                {
                    var layer = (LayerTableRecord)transaction.GetObject(id, OpenMode.ForRead);
                    if (!layer.IsDependent && layer.Name.StartsWith("RC-", StringComparison.OrdinalIgnoreCase))
                        names.Add(layer.Name);
                }

                var layers = names.ToArray();
                for (int i = 0; i < layers.Length; i++)
                    d.Say($"{i + 1}. {layers[i]}");
                int selected = d.Count("当前绘图图层编号", 1, 1, layers.Length) - 1;
                d.Write(tr =>
                {
                    var id = d.Layer(tr, layers[selected]);
                    var layer = (LayerTableRecord)tr.GetObject(id, OpenMode.ForWrite);
                    layer.IsOff = false;
                    layer.IsFrozen = false;
                    d.Database.Clayer = id;
                });
            }
        });

        [CommandMethod("KLIB")]
        public void InsertLibrary() => Drafting.Run(d =>
        {
            var directory = Path.Combine(Plugin.Root, "library");
            string[] files;
            try
            {
                files = Directory.Exists(directory)
                    ? Directory.GetFiles(directory, "*.dwg").OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase).ToArray()
                    : Array.Empty<string>();
            }
            catch (IOException error)
            {
                throw new InvalidOperationException("无法读取图库：" + error.Message);
            }
            Drafting.Check(files.Length > 0, "图库暂无 DWG。请放入 " + directory);
            for (int i = 0; i < files.Length; i++)
                d.Say($"{i + 1}. {Path.GetFileName(files[i])}");
            string file = files[d.Count("图库文件编号", 1, 1, files.Length) - 1];
            var point = d.Point("指定图块插入点：");
            double scale = d.Distance("插入比例", 1);
            string basename = Path.GetFileNameWithoutExtension(file);
            var name = "KCAD_" + new string(basename.Take(100).Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
            d.Write(tr =>
            {
                var table = (BlockTable)tr.GetObject(d.Database.BlockTableId, OpenMode.ForRead);
                string uniqueName = name;
                for (int i = 2; table.Has(uniqueName); i++)
                    uniqueName = name + "_" + i;
                ObjectId definition;
                using (var source = new Database(false, true))
                {
                    source.ReadDwgFile(file, FileOpenMode.OpenForReadAndAllShare, false, null);
                    source.CloseInput(true);
                    definition = d.Database.Insert(uniqueName, source, true);
                }
                var reference = new BlockReference(point, definition) { ScaleFactors = new Scale3d(scale) };
                d.Add(tr, reference, Drafting.Cabinet);
            });
        });

        [LispFunction("KCAD:ATTACH-IMAGES")]
        public void AttachImagesFromLisp(ResultBuffer arguments) => AttachImages();

        [CommandMethod("KCADPRIM")]
        public void AttachImages() => Drafting.Run(d =>
        {
            while (true)
            {
                var options = new PromptStringOptions("\n输入图片完整路径，回车结束：") { AllowSpaces = true };
                var result = d.Editor.GetString(options);
                if (result.Status == PromptStatus.None)
                    return;
                Drafting.Require(result.Status);
                string path = result.StringResult.Trim().Trim('"');
                if (path.Length == 0)
                    return;
                if (!File.Exists(path))
                {
                    d.Say("未找到图片：" + path);
                    continue;
                }
                var point = d.Point("指定图片插入点：");
                double width = d.Distance("图片显示宽度", 600);
                d.Write(tr => AttachImage(d, tr, path, point, width));
            }
        });

        private static void AttachImage(Drafting d, Transaction tr, string path, Point3d point, double width)
        {
            var dictionaryId = RasterImageDef.GetImageDictionary(d.Database);
            if (dictionaryId.IsNull)
                dictionaryId = RasterImageDef.CreateImageDictionary(d.Database);
            var images = (DBDictionary)tr.GetObject(dictionaryId, OpenMode.ForWrite);
            using (var definition = new RasterImageDef { SourceFileName = path })
            {
                definition.Load();
                var size = definition.Size;
                Drafting.Check(size.X > 0 && size.Y > 0, "无法读取图片尺寸。");
                var definitionId = images.SetAt("KCAD_" + Guid.NewGuid().ToString("N"), definition);
                tr.AddNewlyCreatedDBObject(definition, true);
                var image = new RasterImage
                {
                    ImageDefId = definitionId,
                    Orientation = new CoordinateSystem3d(point.TransformBy(d.ToWorld),
                        (Vector3d.XAxis * width).TransformBy(d.ToWorld),
                        (Vector3d.YAxis * (width * size.Y / size.X)).TransformBy(d.ToWorld))
                };
                var imageId = d.Add(tr, image, Drafting.Guide, false);
                image = (RasterImage)tr.GetObject(imageId, OpenMode.ForWrite);
                image.ShowImage = true;
                RasterImage.EnableReactors(true);
                image.AssociateRasterDef(definition);
            }
        }
    }
}
