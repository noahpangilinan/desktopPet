using System;
using System.Drawing;

namespace DesktopPet
{
    // Moves a pet around a rectangle the way an animal would: it keeps a heading
    // and can only turn so fast, so paths curve; a slow wobble makes them meander;
    // edges push it back toward open space. Pets with a glide animation swim in
    // bursts (a few strong tail beats) followed by coasting, and sometimes rest.
    sealed class Wanderer
    {
        public double X, Y;                // top-left of the pet, in screen pixels
        public double Heading;             // radians, 0 = right, clockwise
        public bool FacingRight = true;    // for side-on pets
        public string Animation;           // null = the pet's first animation
        public double AnimRate = 1;        // multiplier on the animation's fps

        enum Phase { Burst, Glide, Rest }

        const double MaxTurn = 1.8;        // radians per second while bursting
        const double SideOnPitch = 0.6;    // side-on pets stay within this of level

        readonly Random random = new Random();
        double cruise, speed, turnRate, clock;
        double targetX, targetY;
        double wobbleA, wobbleB;
        bool burstGlide, sideOn;
        Phase phase = Phase.Burst;
        double phaseLeft;

        public Wanderer(Rectangle area, double cruise, bool burstGlide, bool sideOn)
        {
            X = area.Left + random.NextDouble() * area.Width;
            Y = area.Top + random.NextDouble() * area.Height;
            Heading = random.NextDouble() * 2 * Math.PI;
            wobbleA = random.NextDouble() * 10;
            wobbleB = random.NextDouble() * 10;
            Configure(cruise, burstGlide, sideOn);
            PickTarget(area);
        }

        public void Configure(double cruise, bool burstGlide, bool sideOn)
        {
            this.cruise = cruise;
            this.burstGlide = burstGlide;
            this.sideOn = sideOn;
            speed = Math.Min(speed, cruise);
            if (sideOn)
                Heading = Math.Cos(Heading) >= 0 ? 0 : Math.PI;
            StartPhase(Phase.Burst);
        }

        // area is where the pet's top-left corner may go; size is the pet's size.
        public void Step(double dt, Rectangle area, int size)
        {
            clock += dt;
            double dx = targetX - X, dy = targetY - Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);

            // Reached the target: head somewhere new, or settle down for a while.
            if (dist < Math.Max(size, cruise * 0.5))
            {
                PickTarget(area);
                if (burstGlide && random.NextDouble() < 0.3)
                    StartPhase(Phase.Rest);
                dx = targetX - X;
                dy = targetY - Y;
                dist = Math.Sqrt(dx * dx + dy * dy);
            }

            phaseLeft -= dt;
            if (burstGlide && phaseLeft <= 0)
                StartPhase(phase == Phase.Burst ? Phase.Glide : Phase.Burst);

            // Speed: steady for flyers; thrust and coast for swimmers.
            if (!burstGlide)
                speed += (cruise - speed) * Math.Min(1, dt * 1.5);
            else if (phase == Phase.Burst)
                speed += (cruise * 1.6 - speed) * Math.Min(1, dt * 3);
            else
                speed *= Math.Exp(-dt * (phase == Phase.Rest ? 1.2 : 0.55));

            // Where it wants to go: toward the target, away from nearby edges,
            // plus a slow wobble so it never moves in a dead straight line.
            double wantX = dx / Math.Max(1, dist), wantY = dy / Math.Max(1, dist);
            double margin = Math.Max(100, size * 2);
            double edge = 0;
            edge += Push(X - area.Left, margin, ref wantX, 1);
            edge += Push(area.Right - X, margin, ref wantX, -1);
            edge += Push(Y - area.Top, margin, ref wantY, 1);
            edge += Push(area.Bottom - Y, margin, ref wantY, -1);
            double wobble = 0.6 * Math.Sin(clock * 0.7 + wobbleA) + 0.3 * Math.Sin(clock * 1.9 + wobbleB);
            double want = Math.Atan2(wantY, wantX) + wobble * Math.Max(0, 1 - edge);
            if (sideOn)
                want = Level(want);

            // Turn toward it, limited and eased. Swimmers turn mostly with
            // their tail, so they turn hardest while bursting.
            double maxTurn = MaxTurn * (burstGlide && phase != Phase.Burst ? 0.45 : 1);
            double wantTurn = Math.Max(-maxTurn, Math.Min(maxTurn, Wrap(want - Heading) * 2.5));
            turnRate += (wantTurn - turnRate) * Math.Min(1, dt * 4);
            Heading = Wrap(Heading + turnRate * dt);

            X += Math.Cos(Heading) * speed * dt;
            Y += Math.Sin(Heading) * speed * dt;
            X = Math.Max(area.Left, Math.Min(area.Right, X));
            Y = Math.Max(area.Top, Math.Min(area.Bottom, Y));

            // Only flip side-on sprites once clearly heading the other way.
            double c = Math.Cos(Heading);
            if (c > 0.15) FacingRight = true;
            else if (c < -0.15) FacingRight = false;

            double pace = Math.Min(1, speed / cruise);
            if (!burstGlide)
            {
                Animation = null;
                AnimRate = 0.35 + 0.65 * pace;
            }
            else if (phase == Phase.Burst)
            {
                Animation = "swim";
                AnimRate = 0.8 + 0.4 * pace;
            }
            else
            {
                Animation = "glide";
                AnimRate = phase == Phase.Rest ? 0.3 : 0.3 + 0.5 * pace;
            }
        }

        void StartPhase(Phase next)
        {
            phase = next;
            if (next == Phase.Burst) phaseLeft = 0.5 + random.NextDouble() * 0.6;
            else if (next == Phase.Glide) phaseLeft = 1.2 + random.NextDouble() * 2.3;
            else phaseLeft = 3 + random.NextDouble() * 5;
        }

        // Adds a push away from an edge closer than margin; returns its strength.
        static double Push(double gap, double margin, ref double axis, int away)
        {
            if (gap >= margin)
                return 0;
            double strength = 1 - Math.Max(0, gap) / margin;
            axis += away * strength * 2;
            return strength;
        }

        // Keep a heading within SideOnPitch of level, on whichever side it points.
        static double Level(double angle)
        {
            angle = Wrap(angle);
            if (Math.Abs(angle) <= Math.PI / 2)
                return Math.Max(-SideOnPitch, Math.Min(SideOnPitch, angle));
            double back = Wrap(angle - Math.PI);
            return Wrap(Math.PI + Math.Max(-SideOnPitch, Math.Min(SideOnPitch, back)));
        }

        static double Wrap(double angle)
        {
            while (angle > Math.PI) angle -= 2 * Math.PI;
            while (angle < -Math.PI) angle += 2 * Math.PI;
            return angle;
        }

        void PickTarget(Rectangle area)
        {
            // A few tries for a target that is a decent distance away. Side-on
            // pets mostly travel sideways.
            for (int i = 0; i < 4; i++)
            {
                targetX = area.Left + random.NextDouble() * area.Width;
                targetY = sideOn
                    ? Y + (random.NextDouble() * 2 - 1) * area.Height * 0.3
                    : area.Top + random.NextDouble() * area.Height;
                targetY = Math.Max(area.Top, Math.Min(area.Bottom, targetY));
                double dx = targetX - X, dy = targetY - Y;
                if (dx * dx + dy * dy > area.Width * area.Width / 16.0)
                    break;
            }
        }
    }
}
