using Spectre.Console;
using Spectre.Console.Rendering;

namespace SolarSystemApp.UI.Panels
{
    internal sealed class EventLogPanel : UIPanel
    {
        public EventLogPanel()
        {
            Anchor = PanelAnchor.Bottom;
            RequestedHeight = 7;
            Visible = true;
        }

        public override IRenderable BuildContent(IGameContext ctx)
        {
            int maxLines = Bounds.H - 2;
            if (maxLines < 1) maxLines = 1;

            var events = new List<string>();
            foreach (var e in ctx.Events.GetNewestFirst(maxLines))
                events.Add(e);

            events.Reverse();

            var lines = new List<IRenderable>();
            foreach (var e in events)
                lines.Add(new Markup($"[grey]{Markup.Escape(e)}[/]"));

            while (lines.Count < maxLines)
                lines.Insert(0, new Text(""));

            return new Panel(new Rows(lines))
                .Border(BoxBorder.Rounded)
                .Header("LOG", Justify.Left);
        }
    }
}
