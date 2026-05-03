using AsciiEngine;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace SolarSystemApp.UI.Panels
{
    internal sealed class CommandMenuPanel : UIPanel
    {
        public sealed class MenuOption
        {
            public ConsoleKey Key { get; init; }
            public string Label { get; init; } = "";
            public Action<IGameContext>? Execute { get; init; }
            public List<MenuOption>? SubMenu { get; init; }
        }

        private readonly Stack<List<MenuOption>> _menuStack = new Stack<List<MenuOption>>();
        private List<MenuOption> _currentMenu = new List<MenuOption>();

        public Action? OnClose { get; set; }

        public CommandMenuPanel()
        {
            Anchor = PanelAnchor.Center;
            RequestedWidth = 40;
            RequestedHeight = 16;
            Modal = true;
            Visible = false;
        }

        public void Open(List<MenuOption> rootMenu)
        {
            _menuStack.Clear();
            _currentMenu = rootMenu;
        }

        public override bool HandleInput(InputState input, IGameContext ctx)
        {
            if (input.WasPressed(ConsoleKey.Escape))
            {
                if (_menuStack.Count > 0)
                    _currentMenu = _menuStack.Pop();
                else
                    OnClose?.Invoke();
                return true;
            }

            foreach (var opt in _currentMenu)
            {
                if (input.WasPressed(opt.Key))
                {
                    if (opt.SubMenu != null && opt.SubMenu.Count > 0)
                    {
                        _menuStack.Push(_currentMenu);
                        _currentMenu = opt.SubMenu;
                    }
                    else if (opt.Execute != null)
                    {
                        opt.Execute(ctx);
                        OnClose?.Invoke();
                    }
                    return true;
                }
            }

            return true;
        }

        public override IRenderable BuildContent(IGameContext ctx)
        {
            var lines = new List<IRenderable>();

            foreach (var opt in _currentMenu)
            {
                string keyName = opt.Key.ToString();
                if (keyName.StartsWith("Oem"))
                    keyName = keyName.Substring(3);

                string hasSubmenu = opt.SubMenu != null ? " >" : "";
                lines.Add(new Markup($"[yellow][[{Markup.Escape(keyName)}]][/] {Markup.Escape(opt.Label)}{hasSubmenu}"));
            }

            lines.Add(new Text(""));

            string backLabel = _menuStack.Count > 0 ? "Back" : "Close";
            lines.Add(new Markup($"[grey][[Esc]] {backLabel}[/]"));

            return new Panel(new Rows(lines))
                .Border(BoxBorder.Rounded)
                .Header("COMMAND", Justify.Left);
        }
    }
}
