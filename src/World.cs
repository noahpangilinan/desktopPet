using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace DesktopPet
{
    // Something a bird can stand on: the bottom of a screen's work area (the top
    // of the taskbar) or a visible stretch of a window's top edge.
    sealed class Surface
    {
        public long Id;            // negative for ground, else the window handle
        public int Left, Right;    // the standing span, in screen pixels
        public int Top;            // y of the line feet rest on
        public bool Ground { get { return Id < 0; } }
    }

    // A snapshot of the desktop the pets live on, refreshed by the stage: the
    // screens, surfaces to perch on, the cursor, and whether a full-screen app
    // is in front. Everything is in physical screen pixels, y down.
    sealed class World
    {
        public readonly List<Rectangle> WorkAreas = new List<Rectangle>();
        public readonly List<Surface> Surfaces = new List<Surface>();
        public double CursorX, CursorY;
        public double CursorSpeed;     // pixels per second, smoothed
        public double CursorIdle;      // seconds the cursor has been still
        public bool FullscreenApp;

        // Set from the Activity level.
        public double WindowsEvery = 0.5;   // seconds between window scans; 0 = taskbar only
        public bool TrackCursor = true;

        const double ScreensEvery = 2, FullscreenEvery = 2;
        const int MinSpan = 100;       // narrower stretches aren't worth landing on

        readonly uint ownProcess = (uint)Process.GetCurrentProcess().Id;
        readonly List<Rectangle> grounds = new List<Rectangle>();
        readonly List<Surface> windowTops = new List<Surface>();
        double screensClock = double.MaxValue, windowsClock = double.MaxValue;
        bool cursorKnown;

        public void Refresh(double dt)
        {
            Native.POINT p;
            if (!TrackCursor)
            {
                cursorKnown = false;
                CursorSpeed = CursorIdle = 0;
            }
            else if (Native.GetCursorPos(out p))
            {
                if (cursorKnown && dt > 0)
                {
                    double dx = p.X - CursorX, dy = p.Y - CursorY;
                    double instant = Math.Sqrt(dx * dx + dy * dy) / dt;
                    CursorSpeed = CursorSpeed * 0.6 + instant * 0.4;
                    CursorIdle = instant < 20 ? CursorIdle + dt : 0;
                }
                CursorX = p.X;
                CursorY = p.Y;
                cursorKnown = true;
            }

            screensClock += dt;
            if (screensClock >= ScreensEvery)
            {
                screensClock = 0;
                WorkAreas.Clear();
                foreach (Screen s in Screen.AllScreens)
                    WorkAreas.Add(s.WorkingArea);
            }

            windowsClock += dt;
            if (windowsClock >= (WindowsEvery > 0 ? WindowsEvery : FullscreenEvery))
            {
                windowsClock = 0;
                if (WindowsEvery > 0)
                    ScanWindows();
                else
                    windowTops.Clear();
                FullscreenApp = IsFullscreen(Native.GetForegroundWindow());
            }

            Surfaces.Clear();
            for (int i = 0; i < WorkAreas.Count; i++)
            {
                Rectangle w = WorkAreas[i];
                Surfaces.Add(new Surface { Id = -(i + 1), Left = w.Left, Right = w.Right, Top = w.Bottom });
            }
            Surfaces.AddRange(windowTops);
        }

        // The surface with this id whose span still covers [left, right], or null.
        public Surface Find(long id, double left, double right)
        {
            foreach (Surface s in Surfaces)
                if (s.Id == id && s.Left <= left + 1 && s.Right >= right - 1)
                    return s;
            return null;
        }

        // The ground (taskbar top) under a point's screen.
        public Surface GroundAt(double x, double y)
        {
            int i = ScreenIndex(x, y);
            return i < 0 ? null : Surfaces[i];
        }

        // The work area containing a point, or the nearest one.
        public Rectangle WorkAt(double x, double y)
        {
            int i = ScreenIndex(x, y);
            return i < 0 ? Screen.PrimaryScreen.WorkingArea : WorkAreas[i];
        }

        int ScreenIndex(double x, double y)
        {
            int best = -1;
            double bestGap = double.MaxValue;
            for (int i = 0; i < WorkAreas.Count; i++)
            {
                Rectangle w = WorkAreas[i];
                double gx = Math.Max(0, Math.Max(w.Left - x, x - w.Right));
                double gy = Math.Max(0, Math.Max(w.Top - y, y - w.Bottom));
                double gap = gx * gx + gy * gy;
                if (gap < bestGap) { bestGap = gap; best = i; }
            }
            return best;
        }

        // True if a point is inside any screen's work area.
        public bool OnAnyScreen(double x, double y)
        {
            foreach (Rectangle w in WorkAreas)
                if (x >= w.Left && x < w.Right && y >= w.Top && y < w.Bottom)
                    return true;
            return false;
        }

        // Walk the windows front to back, collecting their rectangles, then keep
        // the stretches of each window's top edge that nothing in front covers.
        void ScanWindows()
        {
            List<IntPtr> handles = new List<IntPtr>();
            List<Rectangle> rects = new List<Rectangle>();
            List<bool> perchable = new List<bool>();
            StringBuilder cls = new StringBuilder(64);

            Native.EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                if (!Native.IsWindowVisible(h) || Native.IsIconic(h))
                    return true;
                uint pid;
                Native.GetWindowThreadProcessId(h, out pid);
                if (pid == ownProcess)
                    return true;
                int cloaked;
                if (Native.DwmGetWindowAttribute(h, Native.DWMWA_CLOAKED, out cloaked, 4) == 0 && cloaked != 0)
                    return true;   // hidden UWP windows, other virtual desktops
                Native.RECT r;
                if (Native.DwmGetWindowAttribute(h, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out r, Marshal.SizeOf(typeof(Native.RECT))) != 0
                    && !Native.GetWindowRect(h, out r))
                    return true;
                Rectangle rect = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
                if (rect.Width <= 0 || rect.Height <= 0)
                    return true;

                cls.Length = 0;
                Native.GetClassName(h, cls, cls.Capacity);
                string name = cls.ToString();
                bool shell = name == "Progman" || name == "WorkerW" || name == "Shell_TrayWnd"
                    || name == "Shell_SecondaryTrayWnd";
                bool tool = (Native.GetWindowLong(h, Native.GWL_EXSTYLE) & Native.WS_EX_TOOLWINDOW) != 0;

                handles.Add(h);
                rects.Add(rect);
                perchable.Add(!shell && !tool && rect.Width >= MinSpan && rect.Height >= 60);
                return true;
            }, IntPtr.Zero);

            windowTops.Clear();
            for (int i = 0; i < rects.Count; i++)
            {
                if (!perchable[i])
                    continue;
                Rectangle r = rects[i];
                // Needs open space above it on its screen.
                Rectangle work = WorkAt(r.Left + r.Width / 2.0, r.Top + 1);
                if (r.Top < work.Top + 40 || r.Top >= work.Bottom)
                    continue;

                List<int[]> spans = new List<int[]> { new[] { Math.Max(r.Left, work.Left), Math.Min(r.Right, work.Right) } };
                for (int j = 0; j < i && spans.Count > 0; j++)
                {
                    Rectangle f = rects[j];
                    if (f.Top <= r.Top + 1 && f.Bottom > r.Top - 4)
                        spans = Subtract(spans, f.Left, f.Right);
                }
                foreach (int[] s in spans)
                    if (s[1] - s[0] >= MinSpan)
                        windowTops.Add(new Surface { Id = handles[i].ToInt64(), Left = s[0], Right = s[1], Top = r.Top });
            }
        }

        static List<int[]> Subtract(List<int[]> spans, int left, int right)
        {
            List<int[]> result = new List<int[]>();
            foreach (int[] s in spans)
            {
                if (right <= s[0] || left >= s[1]) { result.Add(s); continue; }
                if (left > s[0]) result.Add(new[] { s[0], left });
                if (right < s[1]) result.Add(new[] { right, s[1] });
            }
            return result;
        }

        // A foreground window that exactly covers its monitor: a game, video or
        // presentation. The desktop itself doesn't count.
        bool IsFullscreen(IntPtr h)
        {
            if (h == IntPtr.Zero)
                return false;
            StringBuilder cls = new StringBuilder(64);
            Native.GetClassName(h, cls, cls.Capacity);
            string name = cls.ToString();
            if (name == "Progman" || name == "WorkerW" || name == "Shell_TrayWnd")
                return false;
            uint pid;
            Native.GetWindowThreadProcessId(h, out pid);
            if (pid == ownProcess)
                return false;
            Native.RECT r;
            if (!Native.GetWindowRect(h, out r))
                return false;
            Native.MONITORINFO info = new Native.MONITORINFO { Size = Marshal.SizeOf(typeof(Native.MONITORINFO)) };
            if (!Native.GetMonitorInfo(Native.MonitorFromWindow(h, Native.MONITOR_DEFAULTTONEAREST), ref info))
                return false;
            return r.Left <= info.Monitor.Left && r.Top <= info.Monitor.Top
                && r.Right >= info.Monitor.Right && r.Bottom >= info.Monitor.Bottom;
        }
    }
}
