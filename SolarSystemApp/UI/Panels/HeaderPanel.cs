using Spectre.Console;
using Spectre.Console.Rendering;

namespace SolarSystemApp.UI.Panels
{
    internal sealed class HeaderPanel : UIPanel
    {
        public HeaderPanel()
        {
            Anchor = PanelAnchor.Top;
            RequestedHeight = 3;
            Visible = true;
        }

        public override IRenderable BuildContent(IGameContext ctx)
        {
            var desc = ctx.SystemDescriptor;
            var sys = ctx.CurrentSystem;
            var title = new Markup(
                $"[bold]System[/]  [grey]({Markup.Escape(sys.Name)})[/]  [teal]{Markup.Escape(desc)}[/]  " +
                $"time={ctx.SimTime:0.0}  x{ctx.TimeScale:0.00}  " +
                $"paused={(ctx.Paused ? "YES" : "NO")}  " +
                $"zoom={ctx.WorldToScreen:0.0}");

            return new Panel(title)
                .Border(BoxBorder.Rounded)
                .Header("CONTROLS", Justify.Left);
        }
    }
}
