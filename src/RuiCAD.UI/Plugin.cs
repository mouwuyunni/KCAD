using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using Acad = Autodesk.AutoCAD.ApplicationServices.Core.Application;

[assembly: ExtensionApplication(typeof(RuiCAD.UI.Plugin))]
[assembly: CommandClass(typeof(RuiCAD.UI.Plugin))]
[assembly: CommandClass(typeof(RuiCAD.UI.DrawingCommands))]
[assembly: CommandClass(typeof(RuiCAD.UI.DoorCommands))]
[assembly: CommandClass(typeof(RuiCAD.UI.UtilityCommands))]
[assembly: CommandClass(typeof(RuiCAD.UI.SectionCommands))]

namespace RuiCAD.UI
{
    public sealed class Plugin : IExtensionApplication
    {
        private sealed class LispState
        {
            public bool Queued;
        }

        private static readonly ConditionalWeakTable<Document, LispState> Lisp = new ConditionalWeakTable<Document, LispState>();
        internal static readonly string Root = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        private static PaletteSet palette;
        private static bool openOnIdle;

        public void Initialize()
        {
            using (var process = Process.GetCurrentProcess())
                openOnIdle = !string.Equals(process.ProcessName, "accoreconsole", StringComparison.OrdinalIgnoreCase);
            Acad.Idle += OnIdle;
        }

        public void Terminate()
        {
            Acad.Idle -= OnIdle;
        }

        private static void OnIdle(object sender, EventArgs args)
        {
            var document = Acad.DocumentManager.MdiActiveDocument;
            if (document == null || !document.Editor.IsQuiescent)
                return;

            LoadLisp(document);
            if (openOnIdle)
            {
                openOnIdle = false;
                ShowPalette();
            }
        }

        private static void LoadLisp(Document document)
        {
            var state = Lisp.GetValue(document, _ => new LispState());
            if (state.Queued)
                return;
            state.Queued = true;

            var init = Path.Combine(Root, "init.lsp");
            if (!File.Exists(init))
            {
                document.Editor.WriteMessage("\n[KCAD] 加载文件缺失。请将 DLL 与 init.lsp、kcad-bridge.lsp 放在同一目录。");
                return;
            }

            var root = Root.Replace('\\', '/');
            document.SendStringToExecute(
                $"(progn (if (not (and *kcad-loaded-root* (= (strcase *kcad-loaded-root*) (strcase \"{root}\")))) (progn (setq *rc-root* \"{root}\") (load \"{root}/init.lsp\"))) (princ))\n",
                true, false, false);
        }

        [CommandMethod("KCAD")]
        public static void ShowPalette()
        {
            if (palette == null)
            {
                palette = new PaletteSet("KCAD · 全屋定制", new Guid("ef521e3e-dc5c-4e64-9f83-07ad986e9ad0"))
                {
                    Style = PaletteSetStyles.ShowCloseButton | PaletteSetStyles.ShowAutoHideButton,
                    DockEnabled = DockSides.Left | DockSides.Right,
                    MinimumSize = new System.Drawing.Size(222, 250),
                    Size = new System.Drawing.Size(284, 690),
                    KeepFocus = false
                };
                palette.AddVisual("工具", new ToolPalette());
                palette.Visible = true;
                palette.Dock = DockSides.Left;
            }

            palette.Visible = true;
        }

        [CommandMethod("KCADRELOAD")]
        public static void Reload()
        {
            var document = Acad.DocumentManager.MdiActiveDocument;
            if (document == null)
                return;
            Lisp.Remove(document);
            document.SendStringToExecute("(setq *kcad-loaded-root* nil)\n", true, false, false);
            LoadLisp(document);
            ShowPalette();
        }

        [LispFunction("KCAD:VERSION")]
        public static string Version(ResultBuffer arguments) => Assembly.GetExecutingAssembly().GetName().Version.ToString(3);

        internal static string Dispatch(string command)
        {
            var document = Acad.DocumentManager.MdiActiveDocument;
            if (document == null)
                return "请先打开一张图纸";

            if (!document.Editor.IsQuiescent)
            {
                document.Window.Focus();
                return "请先结束当前 CAD 命令";
            }

            LoadLisp(document);
            document.Window.Focus();
            document.SendStringToExecute(command + "\n", true, false, false);
            return "已发送 " + command;
        }
    }
}
