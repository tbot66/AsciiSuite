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

        public InteriorSession(string shipName, InteriorMap map, int spawnX, int spawnY)
        {
            ShipName = string.IsNullOrWhiteSpace(shipName) ? "Ship" : shipName;
            Map = map ?? throw new ArgumentNullException(nameof(map));
            PlayerX = spawnX;
            PlayerY = spawnY;
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
