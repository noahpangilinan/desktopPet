using System;
using System.Collections.Generic;
using System.Drawing;

namespace DesktopPet
{
    // Moves a pet around the desktop the way an animal would: it keeps a heading
    // and can only turn so fast, so paths curve; a slow wobble makes them meander;
    // screen edges with no neighbouring screen push it back. Pets with a glide
    // animation swim in bursts (a few strong tail beats) followed by coasting,
    // and sometimes rest. Swimmers are moody about the cursor: a swipe always
    // scares them, and when it comes near they're either spooked or hungry,
    // swimming up to nibble it a few times like fish taking food. Pets steer
    // around each other.
    //
    // Side-on pets with land, stand and takeoff animations (birds) also perch:
    // every so often they fly down to the taskbar or the top edge of a window,
    // land, stand around hopping and turning, then take off again. They take
    // off early if the cursor comes close or their window moves or is covered.
    sealed class Wanderer
    {
        public double X, Y;                // top-left of the pet, in screen pixels
        public int Width, Height;
        public double Heading;             // radians, 0 = right, clockwise
        public bool FacingRight = true;    // for side-on pets
        public string Animation;           // null = the pet's first animation
        public double AnimRate = 1;        // multiplier on the animation's fps
        public double OneShot = -1;        // 0..1 through a play-once animation, or -1 to loop
        public bool Ripple;                // true for one step when a swimmer disturbs the water
        public double RippleX, RippleY;    // where, when Ripple is set
        public bool AvoidOthers = true;    // steer around other pets (Activity setting)

        public double CenterX { get { return X + Width / 2.0; } }
        public double CenterY { get { return Y + Height / 2.0; } }
        public bool Grounded { get { return mode != Mode.Fly; } }

        enum Phase { Burst, Glide, Rest }
        enum Mode { Fly, Landing, Perched, Hopping, TakingOff }

        const double MaxTurn = 1.8;        // radians per second while bursting
        const double SideOnPitch = 0.6;    // side-on pets stay within this of level
        const double FastCursor = 1200;    // pixels per second that counts as a swipe

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
        double modeTime, modeLength, startX, startY, endX;
        double flyLeft, perchLeft, restLeft, approachLeft;
        bool approaching;
        long perchId;
        int perchTop;

        // Reactions. A curious swimmer hovers by the cursor and nibbles it.
        double boostLeft, startleCooldown, rippleCooldown, curiousLeft, curiousCooldown;
        bool curious, nibbleIn, cursorWasNear;
        int bites;
        double nibbleWait;

        public Wanderer(World world, PetSpec pet)
        {
            Width = pet.Width;
            Height = pet.Height;
            Rectangle work = world.WorkAreas.Count > 0 ? world.WorkAreas[0] : new Rectangle(0, 0, 800, 600);
            X = work.Left + random.NextDouble() * Math.Max(0, work.Width - Width);
            Y = work.Top + random.NextDouble() * Math.Max(0, work.Height - Height);
            Heading = random.NextDouble() * 2 * Math.PI;
            wobbleA = random.NextDouble() * 10;
            wobbleB = random.NextDouble() * 10;
            Configure(pet);
            PickTarget(world);
        }

        bool CanPerch { get { return landTime > 0 && takeoffTime > 0; } }
        double Size { get { return Math.Max(Width, Height); } }

        public void Configure(PetSpec pet)
        {
            Width = pet.Width;
            Height = pet.Height;
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
            curious = false;
            flyLeft = 6 + random.NextDouble() * 10;
            StartPhase(Phase.Burst);
        }

        static double Seconds(PetSpec pet, string anim)
        {
            Anim a = pet.Find(anim);
            return a.Frames / a.Fps;
        }

        // flock is every pet, including this one.
        public void Step(double dt, World world, IList<Wanderer> flock)
        {
            clock += dt;
            modeTime += dt;
            OneShot = -1;
            AnimRate = 1;
            Ripple = false;
            boostLeft -= dt;
            startleCooldown -= dt;
            rippleCooldown -= dt;

            React(dt, world);
            switch (mode)
            {
                case Mode.Fly: Fly(dt, world, flock); break;
                case Mode.Landing: Land(world); break;
                case Mode.Perched: Perch(dt, world); break;
                case Mode.Hopping: Hop(world); break;
                case Mode.TakingOff: TakeOff(world); break;
            }
        }

        // The cursor. A swipe nearby always startles. Birds won't let it get
        // close. Swimmers pick a mood when it comes near: sometimes spooked,
        // sometimes hungry, coming over to nibble it once it settles.
        void React(double dt, World world)
        {
            if (!world.TrackCursor)
            {
                curious = false;
                return;
            }
            double dx = CenterX - world.CursorX, dy = CenterY - world.CursorY;
            double d = Math.Max(0.01, Math.Sqrt(dx * dx + dy * dy));
            bool swipe = world.CursorSpeed > FastCursor && d < Size * 3;

            if (mode == Mode.Perched || mode == Mode.Hopping)
            {
                if (d < Size * 1.6 || swipe)
                {
                    FacingRight = dx > 0;   // away from the cursor
                    StartTakeoff(true);
                }
                return;
            }
            if (mode != Mode.Fly)
                return;

            curiousCooldown -= dt;
            bool near = d < Size * 2.2;
            bool entered = near && !cursorWasNear;
            cursorWasNear = near;

            if (swipe && startleCooldown <= 0)
            {
                Startle(dx, dy, 0.9);
                return;
            }
            if (!burstGlide)
                return;

            // Spooked this time: the cursor drifting in is enough to send it off.
            if (entered && !curious && world.CursorSpeed > 60 && startleCooldown <= 0 && random.NextDouble() < 0.4)
            {
                Startle(dx, dy, 0.6);
                return;
            }

            if (curious)
            {
                curiousLeft -= dt;
                if (world.CursorSpeed > 300 || curiousLeft <= 0 || d > 900)
                {
                    EndCurious(world, 4);
                    return;
                }
                if (nibbleIn)
                {
                    // Dart in until the mouth touches the cursor.
                    double reach = Size * 0.36;
                    targetX = world.CursorX + dx / d * reach - Width / 2.0;
                    targetY = world.CursorY + dy / d * reach - Height / 2.0;
                }
                else
                {
                    // Hover just out of reach, on the side it came from.
                    double r = Size * 0.8;
                    targetX = world.CursorX + dx / d * r - Width / 2.0;
                    targetY = world.CursorY + dy / d * r - Height / 2.0;
                }
            }
            else if (curiousCooldown <= 0 && world.CursorIdle > 1.0 && d < 700 && d > Size * 0.5
                     && random.NextDouble() < dt * 0.35)
            {
                curious = true;
                curiousLeft = 14;
                bites = 2 + random.Next(3);
                nibbleIn = false;
                nibbleWait = 0.4;
            }
        }

        void Startle(double dx, double dy, double boost)
        {
            startleCooldown = 2;
            curious = false;
            if (approaching)
            {
                approaching = false;
                flyLeft = 4 + random.NextDouble() * 4;
            }
            double away = Math.Atan2(dy, dx);
            targetX = X + Math.Cos(away) * Size * 6;
            targetY = Y + Math.Sin(away) * Size * 6;
            boostLeft = boost;
            if (burstGlide)
            {
                Heading = Wrap(away);   // a sudden dart
                turnRate = 0;
                StartPhase(Phase.Burst);
                MakeRipple();
            }
        }

        void EndCurious(World world, double cooldown)
        {
            curious = false;
            nibbleIn = false;
            curiousCooldown = cooldown;
            PickTarget(world);
            StartPhase(Phase.Glide);
        }

        // Up close to the cursor: hold position facing it, then dart in, bite,
        // and back off, a few times over.
        void Nibble(double dt, World world)
        {
            double face = Math.Atan2(world.CursorY - CenterY, world.CursorX - CenterX);
            double turn = Wrap(face - Heading);
            Heading = Wrap(Heading + Math.Max(-4 * dt, Math.Min(4 * dt, turn)));
            turnRate = 0;

            double rate = nibbleIn ? 9 : 2.5;   // quick in, slower out
            double nx = X + (targetX - X) * Math.Min(1, dt * rate);
            double ny = Y + (targetY - Y) * Math.Min(1, dt * rate);
            speed = Math.Sqrt((nx - X) * (nx - X) + (ny - Y) * (ny - Y)) / Math.Max(dt, 0.001);
            X = nx;
            Y = ny;
            KeepOnScreens(world);
            double left = Math.Sqrt((targetX - X) * (targetX - X) + (targetY - Y) * (targetY - Y));

            if (nibbleIn)
            {
                Animation = "swim";
                AnimRate = 1.6;
                if (left < 3)
                {
                    // Bite: a ring of water where the mouth met the cursor.
                    Ripple = true;
                    RippleX = world.CursorX;
                    RippleY = world.CursorY;
                    rippleCooldown = 0.3;
                    nibbleIn = false;
                    nibbleWait = 0.4 + random.NextDouble() * 0.7;
                    if (--bites <= 0)
                        EndCurious(world, 15 + random.NextDouble() * 20);
                }
            }
            else
            {
                Animation = "glide";
                AnimRate = 0.6;
                if (left < 6)
                {
                    nibbleWait -= dt;
                    if (nibbleWait <= 0 && Math.Abs(turn) < 0.4)
                        nibbleIn = true;
                }
            }
        }

        void Fly(double dt, World world, IList<Wanderer> flock)
        {
            Rectangle work = world.WorkAt(CenterX, CenterY);
            Rectangle area = Roam(work);

            // Time to come down? Pick somewhere to land and head for it.
            if (CanPerch && !approaching)
            {
                flyLeft -= dt;
                if (flyLeft <= 0)
                    ChoosePerch(world);
            }
            if (approaching)
            {
                Surface s = world.Find(perchId, endX, endX + Width);
                approachLeft -= dt;
                if (s == null || Math.Abs(s.Top - perchTop) > 2 || approachLeft <= 0)
                {
                    approaching = false;   // it moved, vanished, or we couldn't get there
                    flyLeft = 4 + random.NextDouble() * 5;
                    PickTarget(world);
                }
                else
                {
                    targetX = endX;
                    targetY = perchTop - Height;
                }
            }

            double dx = targetX - X, dy = targetY - Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);

            if (curious && dist < Size * 0.8)
            {
                Nibble(dt, world);
                return;
            }

            if (approaching)
            {
                // Close enough that easing to a stop over the landing
                // animation starts at roughly cruising speed.
                if (dist <= Math.Max(4, speed * landTime * 0.5))
                {
                    StartLanding();
                    return;
                }
            }
            else if (!curious && dist < Math.Max(Size, cruise * 0.5))
            {
                // Reached the target: head somewhere new, or settle for a while.
                PickTarget(world);
                if (burstGlide && random.NextDouble() < 0.3)
                {
                    StartPhase(Phase.Rest);
                    MakeRipple();
                }
                dx = targetX - X;
                dy = targetY - Y;
                dist = Math.Sqrt(dx * dx + dy * dy);
            }

            phaseLeft -= dt;
            if (burstGlide && phaseLeft <= 0)
                StartPhase(phase == Phase.Burst ? Phase.Glide : Phase.Burst);

            // Speed: steady for flyers; thrust and coast for swimmers; a
            // startled pet briefly goes much faster.
            bool boosted = boostLeft > 0;
            if (boosted)
                speed += (cruise * 2.6 - speed) * Math.Min(1, dt * 6);
            else if (!burstGlide)
                speed += (cruise - speed) * Math.Min(1, dt * 1.5);
            else if (phase == Phase.Burst)
                speed += (cruise * 1.6 - speed) * Math.Min(1, dt * 3);
            else
                speed *= Math.Exp(-dt * (phase == Phase.Rest ? 1.2 : 0.55));

            // Where it wants to go: toward the target, away from screen edges
            // that have no neighbouring screen (except the ground while coming in
            // to land) and away from other pets, plus a slow wobble so it never
            // moves in a dead straight line.
            double wantX = dx / Math.Max(1, dist), wantY = dy / Math.Max(1, dist);
            double margin = Math.Max(100, Size * 2);
            double edge = 0;
            if (!world.OnAnyScreen(work.Left - 2, CenterY))
                edge += Push(X - area.Left, margin, ref wantX, 1);
            if (!world.OnAnyScreen(work.Right + 2, CenterY))
                edge += Push(area.Right - X, margin, ref wantX, -1);
            if (!world.OnAnyScreen(CenterX, work.Top - 2))
                edge += Push(Y - area.Top, margin, ref wantY, 1);
            if (!approaching && !world.OnAnyScreen(CenterX, work.Bottom + 2))
                edge += Push(area.Bottom - Y, margin, ref wantY, -1);
            foreach (Wanderer o in flock)
            {
                if (!AvoidOthers || o == this || o.Grounded)
                    continue;
                double ox = CenterX - o.CenterX, oy = CenterY - o.CenterY;
                double od = Math.Sqrt(ox * ox + oy * oy), reach = (Size + o.Size) * 0.55;
                if (od < reach && od > 0.01)
                {
                    double push = (1 - od / reach) * 1.5;
                    wantX += ox / od * push;
                    wantY += oy / od * push;
                }
            }
            double wobble = 0.6 * Math.Sin(clock * 0.7 + wobbleA) + 0.3 * Math.Sin(clock * 1.9 + wobbleB);
            double calm = Math.Max(0, 1 - edge) * (approaching || curious ? 0.3 : 1);
            double want = Math.Atan2(wantY, wantX) + wobble * calm;
            if (sideOn)
                want = Level(want);

            // Turn toward it, limited and eased. Swimmers turn mostly with
            // their tail, so they turn hardest while bursting.
            double maxTurn = MaxTurn * (burstGlide && phase != Phase.Burst && !boosted ? 0.45 : 1);
            double wantTurn = Math.Max(-maxTurn, Math.Min(maxTurn, Wrap(want - Heading) * 2.5));
            turnRate += (wantTurn - turnRate) * Math.Min(1, dt * 4);
            Heading = Wrap(Heading + turnRate * dt);
            if (burstGlide && phase == Phase.Burst && Math.Abs(turnRate) > 1.3)
                MakeRipple();   // a hard flick of the tail

            X += Math.Cos(Heading) * speed * dt;
            Y += Math.Sin(Heading) * speed * dt;
            KeepOnScreens(world);

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
            else if (phase == Phase.Burst || boosted)
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

        // The box the pet's top-left corner may move in on one work area.
        Rectangle Roam(Rectangle work)
        {
            return new Rectangle(work.Left, work.Top, Math.Max(0, work.Width - Width), Math.Max(0, work.Height - Height));
        }

        // Stay on the screen under the pet's centre, but let it cross into a
        // neighbouring screen.
        void KeepOnScreens(World world)
        {
            Rectangle work = world.WorkAt(CenterX, CenterY);
            Rectangle area = Roam(work);
            double minX = world.OnAnyScreen(work.Left - 2, CenterY) ? work.Left - Width : area.Left;
            double maxX = world.OnAnyScreen(work.Right + 2, CenterY) ? work.Right : area.Right;
            double minY = world.OnAnyScreen(CenterX, work.Top - 2) ? work.Top - Height : area.Top;
            double maxY = world.OnAnyScreen(CenterX, work.Bottom + 2) ? work.Bottom : area.Bottom;
            X = Math.Max(minX, Math.Min(maxX, X));
            Y = Math.Max(minY, Math.Min(maxY, Y));
        }

        // Pick a landing spot: the taskbar of this screen, or (more often) the
        // visible top edge of a nearby window.
        void ChoosePerch(World world)
        {
            Surface ground = world.GroundAt(CenterX, CenterY);
            List<Surface> windows = new List<Surface>();
            foreach (Surface s in world.Surfaces)
            {
                if (s.Ground || s.Right - s.Left < Width * 1.5)
                    continue;
                double mid = (s.Left + s.Right) / 2.0;
                if (Math.Abs(mid - CenterX) > 1400 || s.Top - Height < world.WorkAt(mid, s.Top).Top)
                    continue;
                windows.Add(s);
            }
            Surface pick = windows.Count > 0 && (ground == null || random.NextDouble() < 0.6)
                ? windows[random.Next(windows.Count)] : ground;
            if (pick == null)
            {
                flyLeft = 5;
                return;
            }
            perchId = pick.Id;
            perchTop = pick.Top;
            approaching = true;
            approachLeft = 20;
            double ahead = (FacingRight ? 1 : -1) * (150 + random.NextDouble() * 350);
            endX = Math.Max(pick.Left, Math.Min(pick.Right - Width, X + ahead));
        }

        void StartLanding()
        {
            mode = Mode.Landing;
            modeTime = 0;
            modeLength = landTime;
            approaching = false;
            startX = X;
            startY = Y;
            if (Math.Abs(endX - startX) > 2)
                FacingRight = endX > startX;
        }

        // The perch the pet is on, refreshed; null if it moved, vanished or got
        // covered. The ground just follows the taskbar.
        Surface CurrentPerch(World world)
        {
            Surface s = world.Find(perchId, X, X + Width);
            if (s == null && perchId < 0)
                s = world.GroundAt(CenterX, CenterY);
            if (s == null || (perchId >= 0 && Math.Abs(s.Top - perchTop) > 1))
                return null;
            perchId = s.Id;
            perchTop = s.Top;
            return s;
        }

        // Glide down onto the perch, slowing to a stop as the landing plays.
        void Land(World world)
        {
            Surface s = world.Find(perchId, endX, endX + Width);
            if (s == null || Math.Abs(s.Top - perchTop) > 2)
            {
                // The window went away under us: pull up and fly on.
                mode = Mode.Fly;
                Heading = FacingRight ? -0.5 : Math.PI + 0.5;
                speed = cruise * 0.6;
                flyLeft = 4 + random.NextDouble() * 4;
                PickTarget(world);
                return;
            }
            perchTop = s.Top;
            double t = Math.Min(1, modeTime / modeLength);
            double ease = 1 - (1 - t) * (1 - t);
            X = startX + (endX - startX) * ease;
            Y = startY + (perchTop - Height - startY) * ease;
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

        // Stand on the perch; now and then turn around, hop, or take off.
        void Perch(double dt, World world)
        {
            Surface s = CurrentPerch(world);
            if (s == null)
            {
                StartTakeoff(true);   // startled: the window moved or was covered
                return;
            }
            Y = s.Top - Height;
            X = Math.Max(s.Left, Math.Min(s.Right - Width, X));
            Heading = FacingRight ? 0 : Math.PI;
            Animation = "stand";
            perchLeft -= dt;
            restLeft -= dt;
            if (restLeft > 0)
                return;

            bool nearLeft = X - s.Left < Width, nearRight = s.Right - (X + Width) < Width;
            if (perchLeft <= 0)
                StartTakeoff(false);
            else if ((nearLeft && !FacingRight) || (nearRight && FacingRight))
            {
                FacingRight = !FacingRight;   // turn away from the end
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
                modeLength = hopTime;
                startX = X;
                endX = Math.Max(s.Left, Math.Min(s.Right - Width, X + (FacingRight ? 1 : -1) * Width * 0.35));
            }
            else
                restLeft = 2 + random.NextDouble() * 3;
        }

        // A short hop along the perch in a low arc.
        void Hop(World world)
        {
            Surface s = CurrentPerch(world);
            if (s == null)
            {
                StartTakeoff(true);
                return;
            }
            double t = Math.Min(1, modeTime / modeLength);
            X = startX + (endX - startX) * t;
            Y = s.Top - Height - Size * 0.2 * 4 * t * (1 - t);
            Animation = "hop";
            OneShot = t;
            if (t >= 1)
            {
                mode = Mode.Perched;
                modeTime = 0;
                restLeft = 0.6 + random.NextDouble() * 2.4;
            }
        }

        // Startled takeoffs are quicker.
        void StartTakeoff(bool startled)
        {
            mode = Mode.TakingOff;
            modeTime = 0;
            modeLength = takeoffTime / (startled ? 1.7 : 1);
            startX = X;
            startY = Y;
        }

        // Spring up and forward, speeding up, then fly on from there.
        void TakeOff(World world)
        {
            double t = Math.Min(1, modeTime / modeLength);
            double dx = (FacingRight ? 1 : -1) * cruise * modeLength * 0.4;
            double lift = Size * 0.6;
            X = startX + dx * t * t;
            Y = startY - lift * t * t;
            KeepOnScreens(world);
            Animation = "takeoff";
            OneShot = t;
            if (t >= 1)
            {
                // Carry on at the speed and angle it left the perch with.
                double vx = 2 * dx / modeLength, vy = -2 * lift / modeLength;
                Heading = Math.Atan2(vy, vx);
                speed = Math.Min(cruise * 1.5, Math.Sqrt(vx * vx + vy * vy));
                turnRate = 0;
                mode = Mode.Fly;
                modeTime = 0;
                flyLeft = 10 + random.NextDouble() * 15;
                PickTarget(world);
                Rectangle work = world.WorkAt(CenterX, CenterY);
                targetY = Math.Max(work.Top, Y - work.Height * (0.3 + random.NextDouble() * 0.3));
            }
        }

        void MakeRipple()
        {
            if (burstGlide && rippleCooldown <= 0)
            {
                Ripple = true;
                RippleX = CenterX;
                RippleY = CenterY;
                rippleCooldown = 1.2;
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

        // A new place to head for: usually somewhere on this screen, sometimes
        // on another one. Side-on pets mostly travel sideways.
        void PickTarget(World world)
        {
            Rectangle work = world.WorkAt(CenterX, CenterY);
            if (world.WorkAreas.Count > 1 && random.NextDouble() < 0.3)
                work = world.WorkAreas[random.Next(world.WorkAreas.Count)];
            for (int i = 0; i < 4; i++)
            {
                targetX = work.Left + random.NextDouble() * Math.Max(0, work.Width - Width);
                targetY = sideOn
                    ? Y + (random.NextDouble() * 2 - 1) * work.Height * 0.3
                    : work.Top + random.NextDouble() * Math.Max(0, work.Height - Height);
                targetY = Math.Max(work.Top, Math.Min(work.Bottom - Height, targetY));
                double dx = targetX - X, dy = targetY - Y;
                if (dx * dx + dy * dy > work.Width * work.Width / 16.0)
                    break;
            }
        }
    }
}
