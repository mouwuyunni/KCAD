using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Acad = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace RuiCAD.UI
{
    public sealed class DrawingCommands
    {
        [CommandMethod("DXSC")]
        public void FrameMenu() => Drafting.Run(d => Frame(d,
            d.Choice("单线生成", "Point", "Point", "Box") == "Point"));

        [CommandMethod("1")]
        public void PointFrame() => Drafting.Run(d => Frame(d, true));

        [CommandMethod("11")]
        public void BoxFrame() => Drafting.Run(d => Frame(d, false));

        private static void Frame(Drafting d, bool pointMode)
        {
            var box = pointMode ? d.Trace(d.Point("点击闭合线框内部：")) : d.TwoCorners("柜体框架");
            var thickness = d.Distance("框架板厚", d.Settings()[0]);
            var inner = box.Inset(thickness);
            d.Write(tr =>
            {
                if (!pointMode)
                    d.Rectangle(tr, box, Drafting.Cabinet);
                var outline = d.Rectangle(tr, inner, Drafting.Cabinet);
                if (pointMode)
                    GeometryTags.Set(d, tr, outline, "frameInner", "", thickness);
            });
        }

        [CommandMethod("2")]
        public void Shelf() => Drafting.Run(d =>
        {
            var region = d.Opening("层板生成");
            var mode = d.Choice("设置层板底面高度（Pick 点选中心 / Bottom 距底 / Top 距顶）",
                "Pick", "Pick", "Bottom", "Top");
            double thickness = d.Distance("层板厚度", d.Settings()[0]);
            double bottom;
            if (mode == "Pick")
                bottom = d.Point("点选层板中心高度：").Y - thickness / 2;
            else
            {
                double offset = d.Distance(mode == "Bottom" ? "层板底面距空腔底" : "层板顶面距空腔顶",
                    Math.Max(0, (region.Height - thickness) / 2), true);
                bottom = mode == "Bottom" ? region.Bottom + offset : region.Top - offset - thickness;
            }
            Drafting.Check(bottom >= region.Bottom - Drafting.Epsilon && bottom + thickness <= region.Top + Drafting.Epsilon,
                "层板超出空腔。");
            d.Write(tr => d.Rectangle(tr, region.WithY(bottom, bottom + thickness), Drafting.Inside));
        });

        [CommandMethod("3")]
        public void Shelves() => Drafting.Run(d =>
        {
            var box = d.Opening("均分层板");
            var settings = d.Settings();
            int count = d.Count("层板数量", (int)settings[3]);
            double thickness = d.Distance("层板厚度", settings[0]);
            double opening = (box.Height - count * thickness) / (count + 1);
            Drafting.Check(opening > Drafting.Epsilon, "层板数量和厚度超过空腔高度。");
            d.Write(tr =>
            {
                for (int i = 0; i < count; i++)
                {
                    double y = box.Bottom + opening + i * (opening + thickness);
                    d.Rectangle(tr, box.WithY(y, y + thickness), Drafting.Inside);
                }
            });
        });

        [CommandMethod("NC")]
        public void DrawerMenu() => Drafting.Run(d => Drawers(d,
            d.Choice("内抽绘制", "Single", "Single", "Fixed") == "Fixed"));

        [CommandMethod("4")]
        public void SingleDrawer() => Drafting.Run(d => Drawers(d, false));

        [CommandMethod("44")]
        public void FixedDrawers() => Drafting.Run(d => Drawers(d, true));

        private static void Drawers(Drafting d, bool fixedHeight)
        {
            var box = d.Opening("内抽");
            var settings = d.Settings();
            double height = d.Distance("单个内抽高度", fixedHeight ? 160 : settings[6]);
            double gap = settings[2];
            string mode = fixedHeight
                ? d.Choice("排布方向（Up 向上 / Down 向下）", "Up", "Up", "Down")
                : d.Choice("安装位置（Bottom 底部 / Pick 点选抽屉底面）", "Bottom", "Bottom", "Pick");
            double baseY = mode == "Pick" ? d.Point("点选抽屉底面高度：").Y : box.Bottom;
            Drafting.Check(box.Width > 2 * gap && height > gap && baseY >= box.Bottom && baseY + height <= box.Top + Drafting.Epsilon,
                "抽屉尺寸或位置超出空腔。");
            int count = fixedHeight ? d.Count("内抽数量", (int)Math.Min(200, Math.Floor(box.Height / height)), 1,
                (int)Math.Min(200, Math.Floor((box.Height + Drafting.Epsilon) / height))) : 1;
            d.Write(tr =>
            {
                for (int i = 0; i < count; i++)
                {
                    double y = mode == "Down" ? box.Top - (i + 1) * height : baseY + i * height;
                    var opening = new Box(box.Left + gap, y, box.Right - gap, y + height - gap, box.Z);
                    d.Rectangle(tr, opening, Drafting.Inside);
                    double half = Math.Min(settings[7] / 2, opening.Width / 4);
                    d.Line(tr, opening.At(opening.CenterX - half, opening.CenterY),
                        opening.At(opening.CenterX + half, opening.CenterY), Drafting.Hardware);
                }
            });
        }

        [CommandMethod("5")]
        public void OuterDrawer() => Drafting.Run(d =>
        {
            var box = d.Opening("外抽面板");
            var mode = d.Choice("外抽样式（Handleless 免拉手 / Gap 常规留缝）", "Handleless", "Handleless", "Gap");
            var gap = d.Distance("抽面四周缝隙", d.Settings()[2], true);
            var panel = box.Inset(gap);
            if (mode == "Handleless")
                panel = panel.WithY(panel.Bottom, panel.Top - d.Distance("顶部扣手留槽", 15));
            d.Write(tr =>
            {
                d.Rectangle(tr, panel, Drafting.Door);
                if (mode == "Handleless")
                    d.Line(tr, panel.At(panel.Left, box.Top - gap), panel.At(panel.Right, box.Top - gap), Drafting.Guide);
            });
        });

        [LispFunction("KCAD:VERTICAL-BOARD")]
        public void VerticalBoardFromLisp(ResultBuffer arguments) => VerticalBoard();

        [CommandMethod("KCADLB")]
        public void VerticalBoard() => Drafting.Run(d =>
        {
            var box = d.Opening("立板");
            var mode = d.Choice("位置（Center 居中 / Offset 偏移 / Pick 点选）", "Center", "Center", "Offset", "Pick");
            double x = box.CenterX;
            if (mode == "Offset")
                x += d.Distance("相对中心偏移，左负右正", 0, true, true);
            if (mode == "Pick")
                x = d.Point("点选立板中心：").X;
            double half = d.Distance("立板厚度", d.Settings()[0]) / 2;
            Drafting.Check(x - half >= box.Left - Drafting.Epsilon && x + half <= box.Right + Drafting.Epsilon, "立板超出空腔。");
            d.Write(tr => d.Rectangle(tr, box.WithX(x - half, x + half), Drafting.Inside));
        });

        [CommandMethod("BB")]
        public void BackBoard() => Drafting.Run(d =>
        {
            var box = d.Opening("背板");
            var thickness = d.Distance("背板厚度（含左右安装余量）", d.Settings()[1]);
            var back = box.WithX(box.Left + thickness, box.Right - thickness);
            d.Write(tr =>
            {
                var id = d.Rectangle(tr, back, Drafting.Back);
                GeometryTags.Set(d, tr, id, "back", "", thickness);
            });
        });

        [CommandMethod("7")]
        public void Rod() => ForwardLisp("YG");

        [CommandMethod("GB")]
        public void Slats() => ForwardLisp("GS");

        private static void ForwardLisp(string command)
        {
            var document = Acad.DocumentManager.MdiActiveDocument;
            document?.SendStringToExecute(command + "\n", true, false, false);
        }

        [CommandMethod("ZA")]
        public void Obstacle() => Drafting.Run(d =>
        {
            var box = d.TwoCorners("障碍物");
            d.Write(tr =>
            {
                d.Rectangle(tr, box, Drafting.Guide);
                d.Line(tr, box.At(box.Left, box.Bottom), box.At(box.Right, box.Top), Drafting.Guide);
                d.Line(tr, box.At(box.Left, box.Top), box.At(box.Right, box.Bottom), Drafting.Guide);
            });
        });

        [CommandMethod("0")]
        public void DividePoints() => Drafting.Run(d =>
        {
            var a = d.Point("等分起点：");
            var b = d.Point("等分终点：", a);
            Drafting.Check(a.DistanceTo(b) > Drafting.Epsilon, "起点和终点重合。");
            int count = d.Count("等分份数", 3, 2);
            bool ticks = d.Choice("输出形式（Point 点 / Tick 短线）", "Point", "Point", "Tick") == "Tick";
            var tick = new Vector3d();
            if (ticks)
                tick = PlanarNormal(a, b) * d.Distance("辅助短线长度", 20) / 2;
            d.Write(tr =>
            {
                for (int i = 1; i < count; i++)
                {
                    var point = a + (b - a) * ((double)i / count);
                    if (ticks)
                        d.Line(tr, point - tick, point + tick, Drafting.Guide);
                    else
                        d.Add(tr, new DBPoint(point), Drafting.Guide);
                }
            });
        });

        [CommandMethod("DX")]
        public void DivideLines() => Drafting.Run(d =>
        {
            var mode = d.Choice("单线均分（Line 选直线 / Box 框选）", "Line", "Line", "Box");
            Point3d a, b;
            Vector3d offset;
            if (mode == "Line")
            {
                var id = d.Pick("选择基准直线：", true);
                using (var tr = d.Database.TransactionManager.StartOpenCloseTransaction())
                {
                    var line = (Line)tr.GetObject(id, OpenMode.ForRead);
                    a = line.StartPoint.TransformBy(d.ToUcs);
                    b = line.EndPoint.TransformBy(d.ToUcs);
                }
                var normal = PlanarNormal(a, b);
                offset = normal * ((d.Point("指定均分区域的另一侧：") - a).DotProduct(normal));
            }
            else
            {
                var box = d.TwoCorners("单线均分");
                bool horizontal = d.Choice("线条方向（Horizontal 水平 / Vertical 垂直）",
                    "Horizontal", "Horizontal", "Vertical") == "Horizontal";
                a = box.At(box.Left, box.Bottom);
                b = horizontal ? box.At(box.Right, box.Bottom) : box.At(box.Left, box.Top);
                offset = horizontal ? Vector3d.YAxis * box.Height : Vector3d.XAxis * box.Width;
            }
            Drafting.Check(offset.Length > Drafting.Epsilon, "均分区域宽度不足。");
            int count = d.Count("均分份数", 3, 2);
            d.Write(tr =>
            {
                for (int i = 1; i < count; i++)
                {
                    var delta = offset * ((double)i / count);
                    d.Line(tr, a + delta, b + delta, Drafting.Guide);
                }
            });
        });

        [CommandMethod("SX")]
        public void DoubleLine() => Drafting.Run(d =>
        {
            double half = d.Distance("板件厚度", d.Settings()[0]) / 2;
            var points = new List<Point3d> { d.Point("双线起点：") };
            var normals = new List<Vector3d>();
            while (true)
            {
                var last = points[points.Count - 1];
                var result = d.Editor.GetPoint(new PromptPointOptions("\n下一点，回车完成：")
                {
                    BasePoint = last, UseBasePoint = true, AllowNone = true
                });
                if (result.Status == PromptStatus.None)
                    break;
                Drafting.Require(result.Status);
                normals.Add(PlanarNormal(last, result.Value));
                points.Add(result.Value);
            }
            Drafting.Check(normals.Count > 0, "至少需要一个线段。");

            var left = new List<Point3d> { points[0] + normals[0] * half };
            var right = new List<Point3d> { points[0] - normals[0] * half };
            for (int i = 1; i < points.Count - 1; i++)
            {
                var sum = normals[i - 1] + normals[i];
                var scale = 1 + normals[i - 1].DotProduct(normals[i]);
                Drafting.Check(scale >= 0.1, "折角过尖，无法生成厚度一致的双线。");
                var joint = sum * (half / scale);
                left.Add(points[i] + joint);
                right.Add(points[i] - joint);
            }
            var end = points[points.Count - 1];
            left.Add(end + normals[normals.Count - 1] * half);
            right.Add(end - normals[normals.Count - 1] * half);
            right.Reverse();
            left.AddRange(right);
            d.Write(tr => d.Outline(tr, Drafting.Inside, true, left.ToArray()));
        });

        internal static Vector3d PlanarNormal(Point3d a, Point3d b)
        {
            Drafting.Check(Drafting.Near(a.Z, b.Z) && a.DistanceTo(b) > Drafting.Epsilon, "请在同一 UCS 平面指定两个不同的点。");
            return Vector3d.ZAxis.CrossProduct(b - a).GetNormal();
        }
    }
}
