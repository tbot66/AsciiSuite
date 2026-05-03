using Spectre.Console;
using Spectre.Console.Rendering;

namespace SolarSystemApp.UI.Panels
{
    internal sealed class InfoPanel : UIPanel
    {
        public InfoPanel()
        {
            Anchor = PanelAnchor.Right;
            Visible = true;
        }

        public override IRenderable BuildContent(IGameContext ctx)
        {
            var sel = ctx.GetCurrentSelection();

            if (!sel.HasDetails && string.IsNullOrEmpty(sel.Label))
            {
                RequestedWidth = CalcWidth(new[] { ("", "No selection") });
                return new Panel(new Text("No selection"))
                    .Border(BoxBorder.Rounded)
                    .Header("INFO", Justify.Left);
            }

            var armed = ctx.ArmedShipIndex >= 0
                ? ctx.CurrentSystem.Ships.Count > ctx.ArmedShipIndex
                    ? ctx.CurrentSystem.Ships[ctx.ArmedShipIndex].Name
                    : "none"
                : "none";

            var rows = new List<(string Label, string Value)>
            {
                ("Selected", $"{sel.Kind} | {sel.Label}"),
                ("World", $"({sel.WX:0.00},{sel.WY:0.00})"),
                ("Follow", ctx.Follow ? "ON" : "OFF"),
                ("FastPan", ctx.FastPan ? "ON" : "OFF"),
                ("ArmedShip", armed),
                ("Credits", $"{ctx.Credits}"),
            };

            if (sel.Kind == "Ship" && sel.HasDetails)
            {
                rows.Add(("Vel", $"({sel.VX:0.00},{sel.VY:0.00})"));
                rows.Add(("Mode", sel.Mode ?? ""));
                rows.Add(("Job", sel.Job ?? "None"));
                if (sel.Job != null && sel.Job != "None")
                    rows.Add(("Done", $"{sel.JobCompleted}"));
            }
            else if (sel.Kind == "Planet" && sel.HasDetails)
            {
                rows.Add(("Radius", $"{sel.Radius:0.00}"));
                rows.Add(("A / E", $"{sel.A:0.00} / {sel.E:0.00}"));
                rows.Add(("Rings", sel.HasRings ? "YES" : "NO"));
                rows.Add(("Texture", sel.Texture ?? ""));
            }

            RequestedWidth = CalcWidth(rows);

            var grid = new Grid();
            grid.AddColumn(new GridColumn().NoWrap());
            grid.AddColumn(new GridColumn().NoWrap());

            foreach (var (label, value) in rows)
                grid.AddRow(label, value);

            return new Panel(grid)
                .Border(BoxBorder.Rounded)
                .Header("INFO", Justify.Left);
        }

        private static int CalcWidth(IEnumerable<(string Label, string Value)> rows)
        {
            int maxLabel = 0, maxValue = 0;
            foreach (var (label, value) in rows)
            {
                if (label.Length > maxLabel) maxLabel = label.Length;
                if (value.Length > maxValue) maxValue = value.Length;
            }
            // 2 border chars + 2 inner padding + gap between columns
            return maxLabel + maxValue + 7;
        }
    }
}
