using System;

namespace SolarSystemApp.Interiors
{
    internal sealed class InteriorSession
    {
        public string ShipName { get; }
        public InteriorMap Map { get; }

        public int PlayerX { get; set; }
        public int PlayerY { get; set; }
        public int CameraX { get; private set; }
        public int CameraY { get; private set; }

        private bool _cameraInitialized;

        public enum InteractionMode
        {
            None,
            Picker,
            NavigationScreen,
            SleepScreen
        }

        public readonly struct InteractionOption
        {
            public readonly int X;
            public readonly int Y;
            public readonly char Glyph;
            public readonly InteriorMap.InteractionKind Kind;

            public InteractionOption(int x, int y, char glyph, InteriorMap.InteractionKind kind)
            {
                X = x;
                Y = y;
                Glyph = glyph;
                Kind = kind;
            }
        }

        private readonly System.Collections.Generic.List<InteractionOption> _interactionOptions =
            new System.Collections.Generic.List<InteractionOption>();

        private InteractionOption _activeInteraction;
        private bool _hasActiveInteraction;

        public InteractionMode CurrentInteractionMode { get; private set; } = InteractionMode.None;

        public System.Collections.Generic.IReadOnlyList<InteractionOption> InteractionOptions => _interactionOptions;

        public bool HasActiveInteraction => _hasActiveInteraction;

        public InteractionOption ActiveInteraction => _activeInteraction;

        public InteriorSession(string shipName, InteriorMap map, int spawnX, int spawnY)
        {
            ShipName = string.IsNullOrWhiteSpace(shipName) ? "Ship" : shipName;
            Map = map ?? throw new ArgumentNullException(nameof(map));
            PlayerX = spawnX;
            PlayerY = spawnY;
        }

        public static string GetInteractionLabel(InteriorMap.InteractionKind kind)
        {
            switch (kind)
            {
                case InteriorMap.InteractionKind.NavigationConsole:
                    return "Navigation Console";
                case InteriorMap.InteractionKind.Bed:
                    return "Bed";
                default:
                    return "Unknown";
            }
        }

        public bool BeginInteractionPicker(int radius)
        {
            _interactionOptions.Clear();
            _hasActiveInteraction = false;
            CurrentInteractionMode = InteractionMode.None;

            int minX = PlayerX - radius;
            int maxX = PlayerX + radius;
            int minY = PlayerY - radius;
            int maxY = PlayerY + radius;

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    if (!Map.InBounds(x, y)) continue;
                    if (x == PlayerX && y == PlayerY) continue;

                    int dx = Math.Abs(x - PlayerX);
                    int dy = Math.Abs(y - PlayerY);
                    if (Math.Max(dx, dy) > radius) continue;

                    char glyph = Map.Get(x, y);
                    if (!InteriorMap.TryGetInteractionKind(glyph, out var kind)) continue;

                    _interactionOptions.Add(new InteractionOption(x, y, glyph, kind));
                }
            }

            if (_interactionOptions.Count == 0) return false;

            CurrentInteractionMode = InteractionMode.Picker;
            return true;
        }

        public bool TrySelectInteraction(int index)
        {
            if (index < 0 || index >= _interactionOptions.Count) return false;

            _activeInteraction = _interactionOptions[index];
            _hasActiveInteraction = true;

            CurrentInteractionMode = _activeInteraction.Kind switch
            {
                InteriorMap.InteractionKind.NavigationConsole => InteractionMode.NavigationScreen,
                InteriorMap.InteractionKind.Bed => InteractionMode.SleepScreen,
                _ => InteractionMode.None
            };

            return CurrentInteractionMode != InteractionMode.None;
        }

        public void CancelInteraction()
        {
            _hasActiveInteraction = false;
            _interactionOptions.Clear();
            CurrentInteractionMode = InteractionMode.None;
        }

        public void UpdateCamera(int viewW, int viewH, int margin = 0)
        {
            if (Map == null) return;

            viewW = Math.Max(1, Math.Min(viewW, Map.W));
            viewH = Math.Max(1, Math.Min(viewH, Map.H));

            if (!_cameraInitialized)
            {
                CameraX = PlayerX - viewW / 2;
                CameraY = PlayerY - viewH / 2;
                _cameraInitialized = true;
            }

            int maxCamX = Math.Max(0, Map.W - viewW);
            int maxCamY = Math.Max(0, Map.H - viewH);

            int minMargin = Math.Min(viewW, viewH) / 4;
            int effectiveMargin = margin > 0 ? margin : Math.Max(2, minMargin);
            effectiveMargin = Math.Min(effectiveMargin, Math.Min(viewW, viewH) / 2);

            if (PlayerX - CameraX < effectiveMargin)
                CameraX = PlayerX - effectiveMargin;
            else if (PlayerX - CameraX >= viewW - effectiveMargin)
                CameraX = PlayerX - (viewW - effectiveMargin - 1);

            if (PlayerY - CameraY < effectiveMargin)
                CameraY = PlayerY - effectiveMargin;
            else if (PlayerY - CameraY >= viewH - effectiveMargin)
                CameraY = PlayerY - (viewH - effectiveMargin - 1);

            CameraX = Math.Max(0, Math.Min(CameraX, maxCamX));
            CameraY = Math.Max(0, Math.Min(CameraY, maxCamY));
        }
    }
}
