namespace DesktopPet
{
    // How much the pets notice and how often they update: the Activity slider.
    // Lower levels tick less often and skip the costlier awareness, so CPU use
    // can be traded against liveliness.
    sealed class Activity
    {
        public readonly string Name;
        public readonly int TickMs;            // update interval
        public readonly double WindowsEvery;   // seconds between window scans; 0 = taskbar only
        public readonly bool Cursor;           // react to the mouse
        public readonly bool Ripples;          // water rings under swimmers
        public readonly bool AvoidOthers;      // pets steer around each other

        public const int Default = 2;
        public static readonly Activity[] Levels =
        {
            new Activity("Calm", 83, 0, false, false, false),      // ~12 fps, just wanders
            new Activity("Relaxed", 50, 2.0, false, true, true),   // 20 fps
            new Activity("Normal", 33, 0.5, true, true, true),     // 30 fps, everything
            new Activity("Alert", 25, 0.3, true, true, true),      // 40 fps
            new Activity("Lively", 16, 0.15, true, true, true),    // ~60 fps
        };

        Activity(string name, int tickMs, double windowsEvery, bool cursor, bool ripples, bool avoidOthers)
        {
            Name = name;
            TickMs = tickMs;
            WindowsEvery = windowsEvery;
            Cursor = cursor;
            Ripples = ripples;
            AvoidOthers = avoidOthers;
        }
    }
}
