using AsciiEngine;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace SolarSystemApp.UI.Panels
{
    internal sealed class FleetListPanel : UIPanel
    {
        private int _scrollIndex = 0;

        public FleetListPanel()
        {
            Anchor = PanelAnchor.Left;
            RequestedWidth = 22;
            Visible = false;
            ToggleKey = ConsoleKey.F1;
        }

        public override bool HandleInput(InputState input, IGameContext ctx)
        {
            int totalItems = ctx.CurrentSystem.Ships.Count + ctx.CurrentSystem.Stations.Count;
            if (totalItems == 0) return false;

            if (input.WasPressed(ConsoleKey.UpArrow))
            {
                _scrollIndex = Math.Max(0, _scrollIndex - 1);
                return true;
            }
            if (input.WasPressed(ConsoleKey.DownArrow))
            {
                _scrollIndex = Math.Min(totalItems - 1, _scrollIndex + 1);
                return true;
            }

            return false;
        }

        public override IRenderable BuildContent(IGameContext ctx)
        {
            int totalItems = ctx.CurrentSystem.Ships.Count + ctx.CurrentSystem.Stations.Count;
            if (_scrollIndex >= totalItems) _scrollIndex = Math.Max(0, totalItems - 1);

            var table = new Table();
            table.AddColumn("Name");
            table.AddColumn("Type");
            table.Border(TableBorder.None);
            table.Expand();

            int row = 0;

            foreach (var ship in ctx.CurrentSystem.Ships)
            {
                string name = row == _scrollIndex
                    ? $"[bold yellow]{Markup.Escape(ship.Name)}[/]"
                    : Markup.Escape(ship.Name);
                table.AddRow(new Markup(name), new Text("Ship"));
                row++;
            }

            foreach (var station in ctx.CurrentSystem.Stations)
            {
                string name = row == _scrollIndex
                    ? $"[bold yellow]{Markup.Escape(station.Name)}[/]"
                    : Markup.Escape(station.Name);
                table.AddRow(new Markup(name), new Text("Stn"));
                row++;
            }

            if (row == 0)
                table.AddRow(new Text("(empty)"), new Text(""));

            return new Panel(table)
                .Border(BoxBorder.Rounded)
                .Header("FLEET", Justify.Left);
        }

        public override void OnShow()
        {
            _scrollIndex = 0;
        }
    }
}
