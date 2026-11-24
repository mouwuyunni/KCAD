using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

namespace RuiCAD.UI
{
    public sealed class DoorCommands
    {
        [CommandMethod("MB")]
        public void Menu() => Drafting.Run(d =>
        {
            var mode = d.Choice("门板（Plain 普通 / Slim 极简 / Shape 造型 / Arch 半圆 / Slide 移门 / Gap 缩缝）",
                "Plain", "Plain", "Slim", "Shape", "Arch", "Slide", "Gap");
            switch (mode)
            {
                case "Arch": DrawArch(d); break;
                case "Slide": DrawSliding(d); break;
                case "Shape": Shape(d); break;
                case "Gap": AdjustGap(d); break;
                default: DrawHinged(d, mode); break;
            }
        });

        [CommandMethod("6")]
        public void Plain() => Drafting.Run(d => DrawHinged(d, "Plain"));

        [CommandMethod("66")]
        public void Slim() => Drafting.Run(d => DrawHinged(d, "Slim"));

        [CommandMethod("8")]
        public void Shaped() => Drafting.Run(Shape);

        [CommandMethod("BY")]
        public void Arch() => Drafting.Run(DrawArch);

        [CommandMethod("9")]
        public void Sliding() => Drafting.Run(DrawSliding);

        [CommandMethod("MBFX")]
        public void Gap() => Drafting.Run(AdjustGap);

        private static void Shape(Drafting d)
        {
            string style = d.Choice("门型（Plain 平板 / Frame 回字框 / Louver 百叶）",
                "Frame", "Plain", "Frame", "Louver");
            DrawHinged(d, style);
        }

        private static void DrawHinged(Drafting d, string style)
        {
            var opening = d.Opening("门板");
            var settings = d.Settings();
            int count = d.Count("门板扇数", (int)settings[4], 1, 50);
            double gap = d.Distance("门缝", settings[2], true);
            string swing = d.Choice("开启方向（Left 左铰 / Right 右铰 / Pair 成对 / None 无标记）",
                count > 1 ? "Pair" : "Left", "Left", "Right", "Pair", "None");
            double frame = style == "Plain" ? 0 : d.Distance("框边宽度", style == "Slim" ? 22 : 60);
            double pitch = style == "Louver" ? d.Distance("百叶横纹间距", 45) : 0;
            var leaves = Leaves(opening, count, gap);
            d.Write(tr =>
            {
                for (int i = 0; i < count; i++)
                {
                    var box = leaves[i];
                    var group = Guid.NewGuid().ToString("N");
                    var outline = d.Rectangle(tr, box, Drafting.Door);
                    GeometryTags.Set(d, tr, outline, "door", group);
                    if (frame > 0)
                    {
                        var glass = box.Inset(frame);
                        Tag(d, tr, d.Rectangle(tr, glass, Drafting.Door), "decoration", group);
                        if (style == "Slim")
                        {
                            Tag(d, tr, d.Line(tr, glass.At(glass.Left, glass.Bottom), glass.At(glass.Right, glass.Top), Drafting.Door), "decoration", group);
                            Tag(d, tr, d.Line(tr, glass.At(glass.Left, glass.Top), glass.At(glass.Right, glass.Bottom), Drafting.Door), "decoration", group);
                        }
                        if (style == "Louver")
                        {
                            Drafting.Check(glass.Height / pitch <= 500, "百叶横纹过密，请增大间距。");
                            for (double y = glass.Bottom + pitch; y < glass.Top - Drafting.Epsilon; y += pitch)
                                Tag(d, tr, d.Line(tr, glass.At(glass.Left, y), glass.At(glass.Right, y), Drafting.Door), "decoration", group);
                        }
                    }

                    if (swing != "None")
                        AddSwing(d, tr, box, swing == "Left" || (swing == "Pair" && i % 2 == 0), settings[7], group);
                }
            });
        }

        private static void AddSwing(Drafting d, Transaction tr, Box box, bool leftHinge, double handle, string group)
        {
            var margin = Math.Min(box.Width, box.Height) * 0.08;
            double hinge = leftHinge ? box.Left + margin : box.Right - margin;
            double free = leftHinge ? box.Right - margin : box.Left + margin;
            var opening = d.Outline(tr, Drafting.Door, false,
                box.At(hinge, box.Bottom + margin), box.At(free, box.CenterY), box.At(hinge, box.Top - margin));
            Tag(d, tr, opening, "swing", group);
            double handleX = free + (leftHinge ? -margin : margin);
            double half = Math.Min(handle / 2, box.Height / 6);
            Tag(d, tr, d.Line(tr, box.At(handleX, box.CenterY - half), box.At(handleX, box.CenterY + half), Drafting.Door), "swing", group);
        }

        private static Box[] Leaves(Box opening, int count, double gap)
        {
            var width = (opening.Width - (count + 1) * gap) / count;
            return Enumerable.Range(0, count).Select(i =>
            {
                double x = opening.Left + gap + i * (width + gap);
                return new Box(x, opening.Bottom + gap, x + width, opening.Top - gap, opening.Z);
            }).ToArray();
        }

        private static void DrawArch(Drafting d)
        {
            var opening = d.Opening("半圆门型");
            var frame = d.Distance("门框宽度", 35);
            var box = opening.Inset(d.Distance("门板四周缝隙", d.Settings()[2], true));
            var inner = box.Inset(frame);
            Drafting.Check(box.Height >= box.Width / 2 && inner.Height >= inner.Width / 2, "半圆顶要求门板高度至少为宽度的一半。");
            d.Write(tr =>
            {
                AddArch(d, tr, box);
                GeometryTags.Set(d, tr, AddArch(d, tr, inner), "decoration", "");
            });
        }

        private static ObjectId AddArch(Drafting d, Transaction tr, Box box)
        {
            double shoulder = box.Top - box.Width / 2;
            var outline = Drafting.Shape(true, box.At(box.Left, box.Bottom), box.At(box.Right, box.Bottom),
                box.At(box.Right, shoulder), box.At(box.Left, shoulder));
            outline.SetBulgeAt(2, 1);
            return d.Add(tr, outline, Drafting.Door);
        }

        private static void DrawSliding(Drafting d)
        {
            var box = d.Opening("移门");
            int count = d.Count("移门扇数", 2, 2, 4);
            double overlap = d.Distance("相邻扇搭接宽度", 35);
            double rail = d.Distance("上下轨道高度", 25);
            Drafting.Check(overlap < box.Width / (count + 1), "搭接宽度过大。");
            var panels = box.WithY(box.Bottom + rail, box.Top - rail);
            double width = (box.Width + (count - 1) * overlap) / count;
            d.Write(tr =>
            {
                d.Rectangle(tr, box.WithY(box.Bottom, panels.Bottom), Drafting.Door);
                d.Rectangle(tr, box.WithY(panels.Top, box.Top), Drafting.Door);
                for (int i = 0; i < count; i++)
                {
                    double x = box.Left + i * (width - overlap);
                    var leaf = panels.WithX(x, x + width);
                    d.Rectangle(tr, leaf, Drafting.Door);
                    double half = width * 0.08;
                    var a = leaf.At(leaf.CenterX - half, leaf.CenterY);
                    var b = leaf.At(leaf.CenterX + half, leaf.CenterY);
                    if (i % 2 != 0)
                        (a, b) = (b, a);
                    d.Line(tr, a, b, Drafting.Door);
                    d.Outline(tr, Drafting.Door, false, b + (a - b) * 0.25 + Vector3d.YAxis * half * 0.35,
                        b, b + (a - b) * 0.25 - Vector3d.YAxis * half * 0.35);
                }
            });
        }

        private static void AdjustGap(Drafting d)
        {
            var selected = d.Pick("选择门板矩形轮廓：");
            var mode = d.Choice("门板缝隙（Inset 四周内缩 / Split 分缝）", "Inset", "Inset", "Split");
            var gap = d.Distance(mode == "Inset" ? "每侧内缩量" : "分缝宽度", d.Settings()[2], true);
            int count = mode == "Split" ? d.Count("分成几扇", 2, 2, 50) : 1;
            d.Write(tr =>
            {
                var selection = RectSelection.Read(d, tr, selected);
                var parts = Linked(d, tr, selected);
                if (mode == "Inset")
                {
                    selection.Resize(d, tr, selection.Box.Inset(gap), parts.Select(part => part.Id));
                    return;
                }
                var leaves = Leaves(selection.Box, count, gap);
                foreach (var id in selection.Edges.Concat(parts.Select(part => part.Id)))
                    tr.GetObject(id, OpenMode.ForWrite).Erase();
                foreach (var leaf in leaves)
                    d.Rectangle(tr, leaf, selection.Layer);
            });
        }

        [CommandMethod("MF")]
        public void Flip() => Drafting.Run(d =>
        {
            var mode = d.Choice("翻转门板（Swing 翻转 KCAD 开启线 / Mirror 镜像手选对象）", "Swing", "Swing", "Mirror");
            if (mode == "Mirror")
            {
                MirrorSelection(d);
                return;
            }
            var id = d.Pick("选择带开启标记的 KCAD 门板外轮廓：");
            d.Write(tr =>
            {
                var rect = RectSelection.Read(d, tr, id);
                var parts = Linked(d, tr, id).Where(part => part.Role == "swing").ToArray();
                Drafting.Check(parts.Length > 0, "该门板没有 KCAD 开启标记，可使用 Mirror 镜像。");
                var mirror = Matrix3d.Mirroring(new Plane(rect.Box.At(rect.Box.CenterX, rect.Box.CenterY).TransformBy(d.ToWorld),
                    Vector3d.XAxis.TransformBy(d.ToWorld)));
                foreach (var part in parts)
                    ((Entity)tr.GetObject(part.Id, OpenMode.ForWrite)).TransformBy(mirror);
            });
        });

        private static void MirrorSelection(Drafting draft)
        {
            var selection = draft.Editor.GetSelection();
            Drafting.Require(selection.Status);
            var first = draft.Point("镜像轴第一点：");
            var last = draft.Point("镜像轴第二点：", first);
            var normal = DrawingCommands.PlanarNormal(first, last).TransformBy(draft.ToWorld);
            var mirror = Matrix3d.Mirroring(new Plane(first.TransformBy(draft.ToWorld), normal));
            draft.Write(tr =>
            {
                foreach (var id in selection.Value.GetObjectIds())
                    ((Entity)tr.GetObject(id, OpenMode.ForWrite)).TransformBy(mirror);
            });
        }

        private static (ObjectId Id, string Role)[] Linked(Drafting draft, Transaction tr, ObjectId outline)
        {
            var root = (Entity)tr.GetObject(outline, OpenMode.ForRead);
            var tag = GeometryTags.Get(root);
            if (tag.Role != "door" || tag.Group.Length == 0)
                return Array.Empty<(ObjectId, string)>();

            return draft.Space(tr).OfType<ObjectId>().Where(id => id != outline)
                .Select(id => (Id: id, Tag: GeometryTags.Get((Entity)tr.GetObject(id, OpenMode.ForRead))))
                .Where(part => part.Tag.Group == tag.Group)
                .Select(part => (part.Id, part.Tag.Role)).ToArray();
        }

        private static void Tag(Drafting draft, Transaction tr, ObjectId id, string role, string group) =>
            GeometryTags.Set(draft, tr, id, role, group);
    }
}
