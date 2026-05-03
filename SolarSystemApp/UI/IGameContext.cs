using SolarSystemApp.Rendering;
using SolarSystemApp.World;
using SolarSystemApp.Gameplay;

namespace SolarSystemApp.UI
{
    internal interface IGameContext
    {
        StarSystem CurrentSystem { get; }
        string SystemDescriptor { get; }
        int SystemIndex { get; }

        double SimTime { get; }
        double TimeScale { get; }
        bool Paused { get; }

        double WorldToScreen { get; }
        Camera2D Camera { get; }

        int SelectedIndex { get; }
        int SelectionCount { get; }
        SelectionInfo GetSelection(int index);
        SelectionInfo GetCurrentSelection();

        int ArmedShipIndex { get; }
        long Credits { get; }
        ShipJobs Jobs { get; }

        EventLog Events { get; }

        bool Follow { get; }
        bool FastPan { get; }
    }

    internal struct SelectionInfo
    {
        public string Kind;
        public int Index;
        public int SubIndex;
        public string Label;
        public double WX, WY;

        public double Radius;
        public double A, E;
        public bool HasRings;
        public string Texture;
        public double VX, VY;
        public string Mode;
        public string Job;
        public int JobCompleted;
        public bool HasDetails;
    }
}
