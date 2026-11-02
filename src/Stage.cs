using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Forms;

namespace DesktopPet
{
    // Runs every pet on one timer: refreshes the world snapshot, steps and
    // draws each pet, spawns ripples under swimmers, and hides everything while
    // a full-screen app is in front (unless told not to).
    sealed class Stage : IDisposable
    {
        const double TopmostEverySeconds = 2;
        const int MaxRipples = 6;

        public readonly World World = new World();
        public bool ShowOverFullscreen;

        readonly string petsDir, effectsDir;
        readonly List<Pet> pets = new List<Pet>();
        readonly List<Wanderer> flock = new List<Wanderer>();
        readonly List<Ripple> ripples = new List<Ripple>();
        readonly Timer timer = new Timer();
        readonly Stopwatch clock = new Stopwatch();
        PetSpec rippleSpec;
        Activity activity = Activity.Levels[Activity.Default];
        int scale;
        double topmostClock;
        bool hidden;

        sealed class Ripple
        {
            public readonly PetWindow Window = new PetWindow();
            public double Age = -1;    // seconds since it started, or -1 when idle
            public int X, Y, Frame = -1;
        }

        public Stage(string petsDir, string effectsDir, int scale)
        {
            this.petsDir = petsDir;
            this.effectsDir = effectsDir;
            this.scale = scale;
            World.Refresh(0);
            LoadRipple();
            timer.Tick += delegate { Tick(); };
            SetActivity(Activity.Default);
        }

        public Activity Activity { get { return activity; } }

        // Apply an Activity level: update rate and which awareness runs.
        public void SetActivity(int level)
        {
            activity = Activity.Levels[Math.Max(0, Math.Min(Activity.Levels.Length - 1, level))];
            timer.Interval = activity.TickMs;
            World.WindowsEvery = activity.WindowsEvery;
            World.TrackCursor = activity.Cursor;
            foreach (Wanderer w in flock)
                w.AvoidOthers = activity.AvoidOthers;
        }

        public IList<Pet> Pets { get { return pets.AsReadOnly(); } }
        public bool Running { get { return timer.Enabled; } }
        public int Scale { get { return scale; } }

        public void Add(string name)
        {
            Pet pet = new Pet(PetSpec.Load(petsDir, name, scale), World);
            pet.Wanderer.AvoidOthers = activity.AvoidOthers;
            pets.Add(pet);
            flock.Add(pet.Wanderer);
            if (!hidden)
                pet.Show();
        }

        public void Remove(string name)
        {
            Pet pet = pets.Find(p => p.Spec.Name == name);
            if (pet == null)
                return;
            pets.Remove(pet);
            flock.Remove(pet.Wanderer);
            pet.Dispose();
        }

        public void SetScale(int next)
        {
            List<PetSpec> specs = new List<PetSpec>();
            foreach (Pet p in pets)
                specs.Add(PetSpec.Load(petsDir, p.Spec.Name, next));   // load all first, so a bad file changes nothing
            scale = next;
            for (int i = 0; i < pets.Count; i++)
                pets[i].SetSpec(specs[i]);
            LoadRipple();
        }

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

        void LoadRipple()
        {
            if (rippleSpec != null)
                rippleSpec.Dispose();
            rippleSpec = null;
            try { rippleSpec = PetSpec.Load(effectsDir, "ripple", scale); }
            catch (Exception) { }   // ripples are optional
        }

        void Tick()
        {
            double dt = Math.Min(0.25, clock.Elapsed.TotalSeconds);
            clock.Restart();
            World.Refresh(dt);

            bool hide = World.FullscreenApp && !ShowOverFullscreen;
            if (hide != hidden)
            {
                hidden = hide;
                foreach (Pet p in pets)
                {
                    if (hide) p.Hide();
                    else p.Show();
                }
                foreach (Ripple r in ripples)
                {
                    r.Window.Hide();
                    r.Age = -1;
                }
            }
            if (hidden)
                return;

            foreach (Pet p in pets)
            {
                p.Tick(dt, World, flock);
                if (p.Wanderer.Ripple && activity.Ripples)
                    SpawnRipple(p);
            }
            TickRipples(dt);

            topmostClock += dt;
            if (topmostClock >= TopmostEverySeconds)
            {
                topmostClock = 0;
                foreach (Pet p in pets)
                    p.Window.KeepOnTop();
            }
        }

        void SpawnRipple(Pet pet)
        {
            if (rippleSpec == null)
                return;
            Ripple r = ripples.Find(x => x.Age < 0);
            if (r == null)
            {
                if (ripples.Count >= MaxRipples)
                    return;
                r = new Ripple();
                ripples.Add(r);
            }
            r.Age = 0;
            r.Frame = -1;
            r.X = (int)Math.Round(pet.Wanderer.RippleX - rippleSpec.Width / 2.0);
            r.Y = (int)Math.Round(pet.Wanderer.RippleY - rippleSpec.Height / 2.0);
            r.Window.Show();
            r.Window.PlaceBelow(pet.Window);
        }

        void TickRipples(double dt)
        {
            if (rippleSpec == null)
                return;
            Anim anim = rippleSpec.Anims[0];
            foreach (Ripple r in ripples)
            {
                if (r.Age < 0)
                    continue;
                r.Age += dt;
                int frame = (int)(r.Age * anim.Fps);
                if (frame >= anim.Frames)
                {
                    r.Window.Hide();
                    r.Age = -1;
                }
                else if (frame != r.Frame)
                {
                    r.Window.ShowFrame(rippleSpec.Draw(anim, frame, 0), r.X, r.Y, rippleSpec.Width, rippleSpec.Height);
                    r.Frame = frame;
                }
            }
        }

        public void Dispose()
        {
            timer.Dispose();
            foreach (Pet p in pets) p.Dispose();
            foreach (Ripple r in ripples) r.Window.Dispose();
            if (rippleSpec != null) rippleSpec.Dispose();
        }
    }
}
