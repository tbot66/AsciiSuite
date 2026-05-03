using System;

namespace SolarSystemApp.Interiors
{
    internal sealed class InteriorSession
    {
        public string ShipName { get; }
        public InteriorMap Map { get; }

        public int PlayerX { get; set; }
        public int PlayerY { get; set; }

        public InteriorSession(string shipName, InteriorMap map, int spawnX, int spawnY)
        {
            ShipName = string.IsNullOrWhiteSpace(shipName) ? "Ship" : shipName;
            Map = map ?? throw new ArgumentNullException(nameof(map));
            PlayerX = spawnX;
            PlayerY = spawnY;
        }
    }
}
