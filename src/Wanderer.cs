using System;
using System.Drawing;

namespace DesktopPet
{
    // Moves a point around a rectangle: glide toward a random target, ease in and
    // out, sometimes idle for a moment, then pick the next target.
    sealed class Wanderer
    {
        public double X, Y;               // top-left of the pet, in screen pixels
        public bool FacingRight = true;
        public double Pace { get { return Math.Min(1, Math.Sqrt(vx * vx + vy * vy) / cruise); } }

        readonly Random random = new Random();
        double vx, vy;
        double targetX, targetY;
        double cruise;
        double idleLeft;

        public Wanderer(Rectangle area, double speed)
        {
            cruise = speed;
            X = area.Left + random.NextDouble() * Math.Max(0, area.Width);
            Y = area.Top + random.NextDouble() * Math.Max(0, area.Height);
            PickTarget(area);
        }

        public void SetSpeed(double speed) { cruise = speed; }

        // area is where the pet's top-left corner may go.
        public void Step(double dt, Rectangle area)
        {
            double dx = targetX - X, dy = targetY - Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            double wantX = 0, wantY = 0;

            if (idleLeft > 0)
            {
                idleLeft -= dt;
                if (idleLeft <= 0)
                    PickTarget(area);
            }
            else if (dist < cruise * 0.25)
            {
                if (random.NextDouble() < 0.35)
                    idleLeft = 1.5 + random.NextDouble() * 3;
                else
                    PickTarget(area);
            }
            else
            {
                // Slow down over the last stretch instead of overshooting.
                double speed = cruise * Math.Min(1, dist / (cruise * 1.5));
                wantX = dx / dist * speed;
                wantY = dy / dist * speed;
            }

            // Ease toward the wanted velocity; this also limits how sharply it turns.
            double ease = Math.Min(1, dt * 1.2);
            vx += (wantX - vx) * ease;
            vy += (wantY - vy) * ease;
            X += vx * dt;
            Y += vy * dt;

            // Stay inside the area even if the screen layout changes.
            X = Math.Max(area.Left, Math.Min(area.Right, X));
            Y = Math.Max(area.Top, Math.Min(area.Bottom, Y));

            // Only turn around once it is clearly moving the other way.
            double flip = cruise * 0.15;
            if (vx > flip) FacingRight = true;
            else if (vx < -flip) FacingRight = false;
        }

        void PickTarget(Rectangle area)
        {
            // Mostly sideways trips: the sprites are drawn side-on.
            targetX = area.Left + random.NextDouble() * area.Width;
            double reach = area.Height * 0.3;
            targetY = Y + (random.NextDouble() * 2 - 1) * reach;
            targetY = Math.Max(area.Top, Math.Min(area.Bottom, targetY));
        }
    }
}
