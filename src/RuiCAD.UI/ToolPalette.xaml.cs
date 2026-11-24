using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace RuiCAD.UI
{
    public partial class ToolPalette : UserControl
    {
        public ToolPalette()
        {
            InitializeComponent();

            var drawing = Group("绘图命令", Groups, true);
            Family(drawing, "单线生成-DXSC", "点选模式-1", "两点模式-11");
            Add(drawing, "层板生成-2", "均分层板-3");
            Family(drawing, "内抽绘制-NC", "指定高度-4", "固定高度-44");
            Add(drawing, "外抽绘制-5");
            Family(drawing, "门板绘制-MB", "普通门板-6", "极简门板-66", "门板缝隙-MBFX",
                "任意门型-8", "半圆门型-BY", "移门绘制-9");
            Add(drawing, "两点等分-0", "单线均分-DX", "双线绘制-SX", "门板剖面-PM",
                "拉线剖面-PP", "衣杆绘制-7", "立板绘制-LB", "背板绘制-BB", "样式填充-YS",
                "格栅墙板-GB", "障碍物框-ZA");

            Add(Group("绘图修改", Groups), "智能拉伸-KS", "清理重合线-QL", "门板开启翻转-MF",
                "板件厚度调整-BT", "板件延伸至边界-BE");
            Add(Group("常用命令", Groups), "标准柜-DG", "吊柜-WG", "榻榻米-TTM", "洞洞板-DK",
                "酒格-JG", "挂衣-FF", "灯带-DD", "见光板-XS", "踢脚-JT");
            Add(Group("标注其它", Groups), "一键标注-BZ", "内部分段-NBZ", "连续标注-DBZ", "门板标注-BM",
                "面积-MJ", "板件编号-BH");
            Add(Group("图层其它", Groups), "隐藏或显示门板-RCML", "切换绘图图层-RCL");

            Group("三维其它", Groups).Children.Add(new TextBlock
            {
                Text = "预留",
                Foreground = (Brush)FindResource("MutedBrush"),
                Margin = new Thickness(10, 4, 4, 7)
            });

            Add(Group("其它命令", Groups), "大样放大-FD", "部件清单-WJ", "拆单导出-BOM",
                "详细统计-BOM2", "图框编号-TK", "文字刷-TS", "批量图片-PRIM", "外置图库-KLIB");
            Add(Group("系统相关", Groups), "工艺参数-RC", "命令表-RCH", "版本-RCV");
        }

        private StackPanel Group(string title, Panel parent, bool expanded = false)
        {
            var panel = new StackPanel();
            parent.Children.Add(new Expander
            {
                Style = (Style)FindResource("GroupStyle"),
                Header = new TextBlock { Text = title, FontWeight = FontWeights.SemiBold },
                Content = panel,
                IsExpanded = expanded
            });
            return panel;
        }

        private void Family(StackPanel parent, string title, params string[] entries)
        {
            var header = new DockPanel();
            var toggle = new ToggleButton
            {
                Style = (Style)FindResource("FamilyToggle"),
                IsChecked = true,
                ToolTip = "展开或折叠 " + title
            };
            AutomationProperties.SetName(toggle, (string)toggle.ToolTip);
            DockPanel.SetDock(toggle, Dock.Left);
            header.Children.Add(toggle);
            header.Children.Add(CommandRow(title));
            parent.Children.Add(header);
            var nested = new StackPanel { Margin = new Thickness(23, 0, 0, 4) };
            nested.SetBinding(VisibilityProperty, new Binding(nameof(ToggleButton.IsChecked))
            {
                Source = toggle,
                Converter = (IValueConverter)FindResource("ExpandedVisibility")
            });
            parent.Children.Add(nested);
            Add(nested, entries);
        }

        private void Add(StackPanel panel, params string[] entries)
        {
            foreach (var entry in entries)
                panel.Children.Add(CommandRow(entry));
        }

        private Button CommandRow(string entry)
        {
            var separator = entry.LastIndexOf('-');
            var name = entry.Substring(0, separator);
            var key = entry.Substring(separator + 1);
            var labels = new DockPanel();
            var shortcut = new Border
            {
                Background = (Brush)FindResource("SlotBrush"),
                CornerRadius = (CornerRadius)FindResource("PressedRadius"),
                MinWidth = 27,
                Padding = new Thickness(5, 2, 5, 2),
                Child = new TextBlock
                {
                    Text = key,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 11,
                    Foreground = (Brush)FindResource("AccentBrush"),
                    TextAlignment = TextAlignment.Center
                }
            };
            DockPanel.SetDock(shortcut, Dock.Right);
            labels.Children.Add(shortcut);
            labels.Children.Add(new TextBlock
            {
                Text = name,
                Margin = new Thickness(0, 0, 5, 0),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            var button = new Button
            {
                Style = (Style)FindResource("CommandRow"),
                Content = labels,
                ToolTip = entry
            };
            AutomationProperties.SetName(button, entry);
            button.Click += (sender, args) => Status.Text = Plugin.Dispatch(key);
            return button;
        }
    }
}
