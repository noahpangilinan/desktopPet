using System;
using System.Drawing;

namespace DesktopPet
{
    // Moves a pet around a rectangle the way an animal would: it keeps a heading
    // and can only turn so fast, so paths curve; a slow wobble makes them meander;
    // edges push it back toward open space. Pets with a glide animation swim in
    // bursts (a few strong tail beats) followed by coasting, and sometimes rest.
    //
    // Side-on pets with land, stand and takeoff animations (birds) also perch:
    // every so often they fly down to the bottom edge (the top of the taskbar),
    // land, stand around hopping and turning, then take off again.
    sealed class Wanderer
    {
        public double X, Y;                // top-left of the pet, in screen pixels
        public double Heading;             // radians, 0 = right, clockwise
        public bool FacingRight = true;    // for side-on pets
        public string Animation;           // null = the pet's first animation
        public double AnimRate = 1;        // multiplier on the animation's fps
        public double OneShot = -1;        // 0..1 through a play-once animation, or -1 to loop

        enum Phase { Burst, Glide, Rest }
        enum Mode { Fly, Landing, Perched, Hopping, TakingOff }

        const double MaxTurn = 1.8;        // radians per second while bursting
        const double SideOnPitch = 0.6;    // side-on pets stay within this of level

        readonly Random random = new Random();
        double cruise, speed, turnRate, clock;
        double targetX, targetY;
        double wobbleA, wobbleB;
        bool burstGlide, sideOn;
        Phase phase = Phase.Burst;
        double phaseLeft;

        // Perching. A time of 0 means the pet lacks that animation.
        double landTime, hopTime, takeoffTime;
        Mode mode = Mode.Fly;
        double modeTime, startX, startY, endX;
        double flyLeft, perchLeft, restLeft, approachLeft;
        bool approaching;

        public Wanderer(Rectangle area, PetSpec pet)
        {
            X = area.Left + random.NextDouble() * area.Width;
            Y = area.Top + random.NextDouble() * area.Height;
            Heading = random.NextDouble() * 2 * Math.PI;
            wobbleA = random.NextDouble() * 10;
            wobbleB = random.NextDouble() * 10;
            Configure(pet);
            PickTarget(area);
        }

        bool CanPerch { get { return landTime > 0 && takeoffTime > 0; } }

        public void Configure(PetSpec pet)
        {
            cruise = pet.Speed;
            burstGlide = pet.Has("glide");
            sideOn = pet.Headings == 1;
            bool perches = sideOn && pet.Has("land") && pet.Has("stand") && pet.Has("takeoff");
            landTime = perches ? Seconds(pet, "land") : 0;
            takeoffTime = perches ? Seconds(pet, "takeoff") : 0;
            hopTime = perches && pet.Has("hop") ? Seconds(pet, "hop") : 0;

            speed = Math.Min(speed, cruise);
            if (sideOn)
                Heading = Math.Cos(Heading) >= 0 ? 0 : Math.PI;
            mode = Mode.Fly;
            approaching = false;
            flyLeft = 6 + random.NextDouble() * 10;
            StartPhase(Phase.Burst);
        }

        static double Seconds(PetSpec pet, string anim)
        {
            Anim a = pet.Find(anim);
            return a.Frames / a.Fps;
        }

        // area is where the pet's top-left corner may go; size is the pet's size.
        // Its bottom edge is the ground birds land on.
        public void Step(double dt, Rectangle area, int size)
        {
            clock += dt;
            modeTime += dt;
            OneShot = -1;
            AnimRate = 1;
            switch (mode)
            {
                case Mode.Fly: Fly(dt, area, size); break;
                case Mode.Landing: Land(area); break;
                case Mode.Perched: Perch(dt, area, size); break;
                case Mode.Hopping: Hop(area, size); break;
                case Mode.TakingOff: TakeOff(area, size); break;
            }
        }

        void Fly(double dt, Rectangle area, int size)
        {
            // Time to come down? Pick a spot on the ground ahead and head for it.
            if (CanPerch && !approaching)
            {
                flyLeft -= dt;
                if (flyLeft <= 0)
                {
                    approaching = true;
                    approachLeft = 20;
                    double ahead = (FacingRight ? 1 : -1) * area.Width * (0.15 + random.NextDouble() * 0.3);
                    targetX = Math.Max(area.Left + size, Math.Min(area.Right - size, X + ahead));
                }
            }
            if (approaching)
            {
                targetY = area.Bottom;
                approachLeft -= dt;
                if (approachLeft <= 0)
                {
                    approaching = false;   // couldn't get there; try again later
                    flyLeft = 5 + random.NextDouble() * 5;
                    PickTarget(area);
                }
            }

            double dx = targetX - X, dy = targetY - Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);

            if (approaching)
            {
                // Close enough that easing to a stop over the landing
                // animation starts at roughly cruising speed.
                if (dist <= Math.Max(4, speed * landTime * 0.5))
                {
                    StartLanding(area);
                    return;
                }
            }
            else if (dist < Math.Max(size, cruise * 0.5))
            {
                // Reached the target: head somewhere new, or settle down for a while.
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

            // Where it wants to go: toward the target, away from nearby edges
            // (except the ground while coming in to land), plus a slow wobble so
            // it never moves in a dead straight line.
            double wantX = dx / Math.Max(1, dist), wantY = dy / Math.Max(1, dist);
            double margin = Math.Max(100, size * 2);
            double edge = 0;
            edge += Push(X - area.Left, margin, ref wantX, 1);
            edge += Push(area.Right - X, margin, ref wantX, -1);
            edge += Push(Y - area.Top, margin, ref wantY, 1);
            if (!approaching)
                edge += Push(area.Bottom - Y, margin, ref wantY, -1);
            double wobble = 0.6 * Math.Sin(clock * 0.7 + wobbleA) + 0.3 * Math.Sin(clock * 1.9 + wobbleB);
            double want = Math.Atan2(wantY, wantX) + wobble * Math.Max(0, 1 - edge) * (approaching ? 0.3 : 1);
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

        void StartLanding(Rectangle area)
        {
            mode = Mode.Landing;
            modeTime = 0;
            approaching = false;
            startX = X;
            startY = Y;
            endX = targetX;
            if (Math.Abs(endX - startX) > 2)
                FacingRight = endX > startX;
        }

        // Glide down onto the ground, slowing to a stop as the landing plays.
        void Land(Rectangle area)
        {
            double t = Math.Min(1, modeTime / landTime);
            double ease = 1 - (1 - t) * (1 - t);
            X = startX + (endX - startX) * ease;
            Y = startY + (area.Bottom - startY) * ease;
            Animation = "land";
            OneShot = t;
            if (t >= 1)
            {
                mode = Mode.Perched;
                modeTime = 0;
                speed = 0;
                perchLeft = 12 + random.NextDouble() * 25;
                restLeft = 1.5 + random.NextDouble() * 2.5;
            }
        }

        // Stand on the ground; now and then turn around, hop, or take off.
        void Perch(double dt, Rectangle area, int size)
        {
            Y = area.Bottom;
            X = Math.Max(area.Left, Math.Min(area.Right, X));
            Heading = FacingRight ? 0 : Math.PI;
            Animation = "stand";
            perchLeft -= dt;
            restLeft -= dt;
            if (restLeft > 0)
                return;

            bool nearLeft = X - area.Left < size, nearRight = area.Right - X < size;
            if (perchLeft <= 0)
                StartTakeoff();
            else if ((nearLeft && !FacingRight) || (nearRight && FacingRight))
            {
                FacingRight = !FacingRight;   // turn away from the edge
                restLeft = 0.8 + random.NextDouble();
            }
            else if (random.NextDouble() < 0.25)
            {
                FacingRight = !FacingRight;
                restLeft = 0.8 + random.NextDouble() * 1.5;
            }
            else if (hopTime > 0)
            {
                mode = Mode.Hopping;
                modeTime = 0;
                startX = X;
                endX = Math.Max(area.Left, Math.Min(area.Right, X + (FacingRight ? 1 : -1) * size * 0.35));
            }
            else
                restLeft = 2 + random.NextDouble() * 3;
        }

        // A short hop along the ground in a low arc.
        void Hop(Rectangle area, int size)
        {
            double t = Math.Min(1, modeTime / hopTime);
            X = startX + (endX - startX) * t;
            Y = area.Bottom - size * 0.2 * 4 * t * (1 - t);
            Animation = "hop";
            OneShot = t;
            if (t >= 1)
            {
                mode = Mode.Perched;
                modeTime = 0;
                restLeft = 0.6 + random.NextDouble() * 2.4;
            }
        }

        void StartTakeoff()
        {
            mode = Mode.TakingOff;
            modeTime = 0;
            startX = X;
            startY = Y;
        }

        // Spring up and forward, speeding up, then fly on from there.
        void TakeOff(Rectangle area, int size)
        {
            double t = Math.Min(1, modeTime / takeoffTime);
            double dx = (FacingRight ? 1 : -1) * cruise * takeoffTime * 0.4;
            double lift = size * 0.6;
            X = Math.Max(area.Left, Math.Min(area.Right, startX + dx * t * t));
            Y = Math.Max(area.Top, startY - lift * t * t);
            Animation = "takeoff";
            OneShot = t;
            if (t >= 1)
            {
                // Carry on at the speed and angle it left the ground with.
                double vx = 2 * dx / takeoffTime, vy = -2 * lift / takeoffTime;
                Heading = Math.Atan2(vy, vx);
                speed = Math.Min(cruise, Math.Sqrt(vx * vx + vy * vy));
                turnRate = 0;
                mode = Mode.Fly;
                modeTime = 0;
                flyLeft = 10 + random.NextDouble() * 15;
                targetX = area.Left + random.NextDouble() * area.Width;
                targetY = Math.Max(area.Top, Y - area.Height * (0.3 + random.NextDouble() * 0.3));
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
