using System;
using System.Collections.Generic;

namespace DesktopPet
{
    // One animal on screen: its sprites, its movement, and its window.
    sealed class Pet : IDisposable
    {
        public PetSpec Spec { get; private set; }
        public readonly Wanderer Wanderer;
        public readonly PetWindow Window = new PetWindow();

        double frameClock;
        Anim shownAnim;
        int shownFrame = -1, shownDirection = -1;

        public Pet(PetSpec spec, World world)
        {
            Spec = spec;
            Wanderer = new Wanderer(world, spec);
        }

        public void Show()
        {
            Window.Show();
            Draw(0, true);
        }

        public void Hide()
        {
            Window.Hide();
        }

        // Swap in new sprites (another size); disposes the old ones.
        public void SetSpec(PetSpec next)
        {
            PetSpec old = Spec;
            Spec = next;
            Wanderer.Configure(next);
            old.Dispose();
            if (Window.Visible)
                Draw(0, true);
        }

        public void Tick(double dt, World world, IList<Wanderer> flock)
        {
            Wanderer.Step(dt, world, flock);
            Draw(dt, false);
        }

        // Pick the frame the wanderer asks for; repaint only when the picture
        // changes, otherwise just move the window.
        void Draw(double dt, bool force)
        {
            Anim anim = Spec.Find(Wanderer.Animation);
            int frame;
            if (Wanderer.OneShot >= 0)
                frame = Math.Min(anim.Frames - 1, (int)(Wanderer.OneShot * anim.Frames));
            else
            {
                // Switching animations keeps the frame position, so swim and glide blend.
                frameClock = (frameClock + dt * anim.Fps * Wanderer.AnimRate) % anim.Frames;
                frame = Math.Min(anim.Frames - 1, (int)frameClock);
            }
            int direction = Spec.Direction(Wanderer.Heading, Wanderer.FacingRight);
            int x = (int)Math.Round(Wanderer.X), y = (int)Math.Round(Wanderer.Y);

            if (force || anim != shownAnim || frame != shownFrame || direction != shownDirection)
            {
                Window.ShowFrame(Spec.Draw(anim, frame, direction), x, y, Spec.Width, Spec.Height);
                shownAnim = anim;
                shownFrame = frame;
                shownDirection = direction;
            }
            else
                Window.MoveTo(x, y);
        }

        public void Dispose()
        {
            Window.Dispose();
            Spec.Dispose();
        }
    }
}
