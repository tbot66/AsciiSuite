using AsciiEngine;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace SolarSystemApp.UI.Settings
{
    internal sealed class SettingsPanel : UI.UIPanel
    {
        private enum Category { Display, Camera, Simulation, Theme }

        private static readonly Category[] Categories =
            { Category.Display, Category.Camera, Category.Simulation, Category.Theme };

        private int _categoryIndex = 0;
        private int _optionIndex = 0;

        public GameSettings Settings { get; set; } = new GameSettings();
        public Action? OnClose { get; set; }
        public Action<GameSettings>? OnSettingsChanged { get; set; }

        public SettingsPanel()
        {
            Anchor = PanelAnchor.Center;
            RequestedWidth = 50;
            RequestedHeight = 22;
            Modal = true;
            Visible = false;
            ToggleKey = ConsoleKey.F10;
        }

        private Category CurrentCategory => Categories[_categoryIndex];

        private int OptionCount => CurrentCategory switch
        {
            Category.Display => 8,
            Category.Camera => 4,
            Category.Simulation => 2,
            Category.Theme => 2,
            _ => 0
        };

        public override bool HandleInput(InputState input, IGameContext ctx)
        {
            if (input.WasPressed(ConsoleKey.Escape))
            {
                SettingsManager.Save(Settings);
                OnClose?.Invoke();
                return true;
            }

            if (input.WasPressed(ConsoleKey.Tab))
            {
                _categoryIndex = (_categoryIndex + 1) % Categories.Length;
                _optionIndex = 0;
                return true;
            }

            int count = OptionCount;
            if (input.WasPressed(ConsoleKey.UpArrow))
            {
                _optionIndex = Math.Max(0, _optionIndex - 1);
                return true;
            }
            if (input.WasPressed(ConsoleKey.DownArrow))
            {
                _optionIndex = Math.Min(count - 1, _optionIndex + 1);
                return true;
            }

            bool enter = input.WasPressed(ConsoleKey.Enter);
            bool left = input.WasPressed(ConsoleKey.LeftArrow);
            bool right = input.WasPressed(ConsoleKey.RightArrow);

            if (enter || left || right)
            {
                int dir = right ? 1 : (left ? -1 : 1);
                ApplyChange(dir);
                OnSettingsChanged?.Invoke(Settings);
                return true;
            }

            return true; // modal
        }

        private void ApplyChange(int dir)
        {
            switch (CurrentCategory)
            {
                case Category.Display:
                    switch (_optionIndex)
                    {
                        case 0: Settings.ShowOrbits = !Settings.ShowOrbits; break;
                        case 1: Settings.ShowLabels = !Settings.ShowLabels; break;
                        case 2: Settings.ShowStarfield = !Settings.ShowStarfield; break;
                        case 3: Settings.ShowBelts = !Settings.ShowBelts; break;
                        case 4: Settings.ShowRings = !Settings.ShowRings; break;
                        case 5: Settings.ShowDebris = !Settings.ShowDebris; break;
                        case 6: Settings.EnableBloom = !Settings.EnableBloom; break;
                        case 7: Settings.EnableLensFlare = !Settings.EnableLensFlare; break;
                    }
                    break;

                case Category.Camera:
                    switch (_optionIndex)
                    {
                        case 0: Settings.ZoomSpeed = CycleEnum(Settings.ZoomSpeed, dir); break;
                        case 1: Settings.PanSpeed = CycleEnum(Settings.PanSpeed, dir); break;
                        case 2: Settings.CameraSmoothing = CycleEnum(Settings.CameraSmoothing, dir); break;
                        case 3:
                            var presets = GameSettings.OrbitYScalePresets;
                            int idx = Array.IndexOf(presets, Settings.OrbitYScale);
                            if (idx < 0) idx = 2;
                            idx = (idx + dir + presets.Length) % presets.Length;
                            Settings.OrbitYScale = presets[idx];
                            break;
                    }
                    break;

                case Category.Simulation:
                    switch (_optionIndex)
                    {
                        case 0:
                            var tsPresets = GameSettings.TimeScalePresets;
                            int tsIdx = Array.IndexOf(tsPresets, Settings.DefaultTimeScale);
                            if (tsIdx < 0) tsIdx = 0;
                            tsIdx = (tsIdx + dir + tsPresets.Length) % tsPresets.Length;
                            Settings.DefaultTimeScale = tsPresets[tsIdx];
                            break;
                        case 1: Settings.PauseOnSystemEntry = !Settings.PauseOnSystemEntry; break;
                    }
                    break;

                case Category.Theme:
                    switch (_optionIndex)
                    {
                        case 0: Settings.ColorTheme = CycleEnum(Settings.ColorTheme, dir); break;
                        case 1: Settings.BorderStyle = CycleEnum(Settings.BorderStyle, dir); break;
                    }
                    break;
            }
        }

        private static T CycleEnum<T>(T current, int dir) where T : struct, Enum
        {
            var values = Enum.GetValues<T>();
            int idx = Array.IndexOf(values, current);
            if (idx < 0) idx = 0;
            idx = (idx + dir + values.Length) % values.Length;
            return values[idx];
        }

        public override IRenderable BuildContent(IGameContext ctx)
        {
            var lines = new List<IRenderable>();

            var tabs = new List<string>();
            for (int i = 0; i < Categories.Length; i++)
            {
                string name = Categories[i].ToString();
                tabs.Add(i == _categoryIndex ? $"[bold yellow] {name} [/]" : $"[grey] {name} [/]");
            }
            lines.Add(new Markup(string.Join("  ", tabs)));
            lines.Add(new Rule().RuleStyle(Style.Parse("grey")));

            switch (CurrentCategory)
            {
                case Category.Display:
                    AddToggle(lines, 0, "Orbit lines", Settings.ShowOrbits);
                    AddToggle(lines, 1, "Labels", Settings.ShowLabels);
                    AddToggle(lines, 2, "Starfield", Settings.ShowStarfield);
                    AddToggle(lines, 3, "Asteroid belts", Settings.ShowBelts);
                    AddToggle(lines, 4, "Rings", Settings.ShowRings);
                    AddToggle(lines, 5, "Debris", Settings.ShowDebris);
                    AddToggle(lines, 6, "Bloom effect", Settings.EnableBloom);
                    AddToggle(lines, 7, "Lens flare", Settings.EnableLensFlare);
                    break;

                case Category.Camera:
                    AddValue(lines, 0, "Zoom Speed", Settings.ZoomSpeed.ToString());
                    AddValue(lines, 1, "Pan Speed", Settings.PanSpeed.ToString());
                    AddValue(lines, 2, "Smoothing", Settings.CameraSmoothing.ToString());
                    AddValue(lines, 3, "Orbit Squash", $"{Settings.OrbitYScale:0.00}");
                    break;

                case Category.Simulation:
                    AddValue(lines, 0, "Default Time Scale", $"{Settings.DefaultTimeScale}x");
                    AddToggle(lines, 1, "Pause on System Entry", Settings.PauseOnSystemEntry);
                    break;

                case Category.Theme:
                    AddValue(lines, 0, "Color Palette", Settings.ColorTheme.ToString());
                    AddValue(lines, 1, "Border Style", Settings.BorderStyle.ToString());
                    break;
            }

            lines.Add(new Text(""));
            lines.Add(new Rule().RuleStyle(Style.Parse("grey")));
            lines.Add(new Markup("[grey]↑↓ Navigate  Enter/←→ Change  Tab Category  Esc Close[/]"));

            return new Panel(new Rows(lines))
                .Border(GameSettings.GetBoxBorder(Settings.BorderStyle))
                .Header("SETTINGS", Justify.Left);
        }

        private void AddToggle(List<IRenderable> lines, int index, string label, bool value)
        {
            string check = value ? "x" : " ";
            string style = index == _optionIndex ? "[bold yellow]" : "[white]";
            lines.Add(new Markup($"  {style}[[{check}]] {Markup.Escape(label)}[/]"));
        }

        private void AddValue(List<IRenderable> lines, int index, string label, string value)
        {
            string style = index == _optionIndex ? "[bold yellow]" : "[white]";
            string arrows = index == _optionIndex ? "◄ " : "  ";
            string arrowsR = index == _optionIndex ? " ►" : "  ";
            lines.Add(new Markup($"  {style}{Markup.Escape(label)}: {arrows}{Markup.Escape(value)}{arrowsR}[/]"));
        }

        public override void OnShow()
        {
            _categoryIndex = 0;
            _optionIndex = 0;
        }
    }
}
