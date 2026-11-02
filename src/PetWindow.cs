using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace DesktopPet
{
    // A borderless, click-through, always-on-top window showing one pet.
    sealed class PetWindow : Form
    {
        const int TickMs = 33;
        const double TopmostEverySeconds = 2;

        readonly Timer timer = new Timer();
        readonly Stopwatch clock = new Stopwatch();
        PetSpec pet;
        Wanderer wanderer;
        double frameClock, topmostClock;
        Anim anim, shownAnim;
        int frame, direction, shownFrame = -1, shownDirection = -1;
        int shownX = int.MinValue, shownY = int.MinValue;

        public PetWindow(PetSpec pet)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            this.pet = pet;
            wanderer = new Wanderer(Roam(), pet.Speed, pet.Has("glide"), pet.Headings == 1);
            anim = pet.Anims[0];
            timer.Interval = TickMs;
            timer.Tick += delegate { Tick(); };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOPMOST
                    | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Draw(true);
            Resume();
        }

        public bool Running { get { return timer.Enabled; } }

        public void Pause()
        {
            timer.Stop();
            clock.Reset();
        }

        public void Resume()
        {
            clock.Restart();
            timer.Start();
        }

        // Swap in a different pet (or the same pet at another size); the caller
        // disposes the old one.
        public void SetPet(PetSpec next)
        {
            pet = next;
            wanderer.Configure(next.Speed, next.Has("glide"), next.Headings == 1);
            anim = next.Find(wanderer.Animation);
            frame = 0;
            direction = next.Direction(wanderer.Heading, wanderer.FacingRight);
            if (IsHandleCreated)
                Draw(true);
        }

        // Where the pet's top-left corner may go: the primary screen minus the taskbar.
        Rectangle Roam()
        {
            Rectangle work = Screen.PrimaryScreen.WorkingArea;
            return new Rectangle(work.Left, work.Top,
                Math.Max(0, work.Width - pet.Width), Math.Max(0, work.Height - pet.Height));
        }

        void Tick()
        {
            double dt = Math.Min(0.25, clock.Elapsed.TotalSeconds);
            clock.Restart();

            wanderer.Step(dt, Roam(), Math.Max(pet.Width, pet.Height));

            // The wanderer picks the animation and how fast it plays. Switching
            // animations keeps the frame position, so swim and glide blend.
            anim = pet.Find(wanderer.Animation);
            frameClock = (frameClock + dt * anim.Fps * wanderer.AnimRate) % anim.Frames;
            frame = (int)frameClock;
            direction = pet.Direction(wanderer.Heading, wanderer.FacingRight);
            Draw(anim != shownAnim || frame != shownFrame || direction != shownDirection);

            topmostClock += dt;
            if (topmostClock >= TopmostEverySeconds)
            {
                topmostClock = 0;
                Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
                    Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            }
        }

        // Repaint only when the picture changes; otherwise just move the window.
        void Draw(bool newFrame)
        {
            int x = (int)Math.Round(wanderer.X), y = (int)Math.Round(wanderer.Y);
            if (newFrame)
            {
                IntPtr dc = pet.Draw(anim, frame, direction);
                Native.PaintLayered(Handle, dc, x, y, pet.Width, pet.Height);
                shownAnim = anim;
                shownFrame = frame;
                shownDirection = direction;
            }
            else if (x != shownX || y != shownY)
            {
                Native.SetWindowPos(Handle, IntPtr.Zero, x, y, 0, 0,
                    Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
            }
            shownX = x;
            shownY = y;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                timer.Dispose();
            base.Dispose(disposing);
        }
    }
}
