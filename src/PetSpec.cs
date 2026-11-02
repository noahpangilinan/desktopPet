using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;

namespace DesktopPet
{
    // One named animation: a block of rows in the sheet starting at Row. A side-on
    // pet uses one row (mirrored for facing left); a pet with N headings uses N rows.
    sealed class Anim
    {
        public readonly string Name;
        public readonly int Row, Frames;
        public readonly double Fps;

        public Anim(string name, int row, int frames, double fps)
        {
            Name = name;
            Row = row;
            Frames = frames;
            Fps = fps;
        }
    }

    // One pet, loaded from pets/<name>/pet.ini and sheet.png. The sheet stays in
    // memory at its original size; each new frame is stretched into a small
    // buffer only when it changes, so even large sheets cost little memory.
    sealed class PetSpec : IDisposable
    {
        public readonly string Name;
        public readonly int Width, Height;   // scaled frame size in screen pixels
        public readonly int Headings;        // 1 = side-on, mirrored to face left
        public readonly double Speed;        // screen pixels per second, already scaled
        public readonly List<Anim> Anims;
        public readonly Icon TrayIcon;

        readonly int frameWidth, frameHeight, sheetWidth;
        readonly IntPtr sheetBmp, mirrorBmp, frameBmp;
        readonly IntPtr sheetDc, mirrorDc, frameDc;

        PetSpec(string name, Bitmap sheet, int frameWidth, int frameHeight, int headings,
            List<Anim> anims, double speed, int scale)
        {
            Name = name;
            this.frameWidth = frameWidth;
            this.frameHeight = frameHeight;
            sheetWidth = sheet.Width;
            Width = frameWidth * scale;
            Height = frameHeight * scale;
            Headings = headings;
            Anims = anims;
            Speed = speed * scale;

            sheetBmp = sheet.GetHbitmap(Color.FromArgb(0));
            sheetDc = Native.CreateCompatibleDC(IntPtr.Zero);
            Native.SelectObject(sheetDc, sheetBmp);

            if (headings == 1)
            {
                using (Bitmap mirror = (Bitmap)sheet.Clone())
                {
                    mirror.RotateFlip(RotateFlipType.RotateNoneFlipX);
                    mirrorBmp = mirror.GetHbitmap(Color.FromArgb(0));
                }
                mirrorDc = Native.CreateCompatibleDC(IntPtr.Zero);
                Native.SelectObject(mirrorDc, mirrorBmp);
            }

            using (Bitmap buffer = new Bitmap(Width, Height, PixelFormat.Format32bppArgb))
                frameBmp = buffer.GetHbitmap(Color.FromArgb(0));
            frameDc = Native.CreateCompatibleDC(IntPtr.Zero);
            Native.SelectObject(frameDc, frameBmp);
            Native.SetStretchBltMode(frameDc, Native.COLORONCOLOR);

            using (Bitmap square = new Bitmap(32, 32, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(square))
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                int side = Math.Max(frameWidth, frameHeight);
                int w = frameWidth * 32 / side, h = frameHeight * 32 / side;
                Rectangle first = new Rectangle(0, anims[0].Row * frameHeight, frameWidth, frameHeight);
                g.DrawImage(sheet, new Rectangle((32 - w) / 2, (32 - h) / 2, w, h), first, GraphicsUnit.Pixel);
                TrayIcon = Icon.FromHandle(square.GetHicon());
            }
        }

        public bool Has(string anim)
        {
            return Anims.Exists(a => a.Name == anim);
        }

        // The named animation, or the first one if this pet does not have it.
        public Anim Find(string anim)
        {
            return Anims.Find(a => a.Name == anim) ?? Anims[0];
        }

        // Which way to draw a pet heading along angle (radians, 0 = right,
        // clockwise). Side-on pets use 0 = right, 1 = left.
        public int Direction(double angle, bool facingRight)
        {
            if (Headings == 1)
                return facingRight ? 0 : 1;
            int h = (int)Math.Round(angle / (2 * Math.PI / Headings));
            return ((h % Headings) + Headings) % Headings;
        }

        // Stretch one frame into the frame buffer and return its DC for painting.
        public IntPtr Draw(Anim anim, int frame, int direction)
        {
            int sx = frame * frameWidth;
            int sy = anim.Row * frameHeight;
            IntPtr source = sheetDc;
            if (Headings > 1)
                sy += direction * frameHeight;
            else if (direction == 1)
            {
                source = mirrorDc;
                sx = sheetWidth - sx - frameWidth;
            }
            Native.StretchBlt(frameDc, 0, 0, Width, Height, source, sx, sy, frameWidth, frameHeight, Native.SRCCOPY);
            return frameDc;
        }

        // Folder names under petsDir that look like pets, sorted.
        public static List<string> Available(string petsDir)
        {
            List<string> names = new List<string>();
            if (!Directory.Exists(petsDir))
                return names;
            foreach (string dir in Directory.GetDirectories(petsDir))
            {
                if (File.Exists(Path.Combine(dir, "pet.ini")) && File.Exists(Path.Combine(dir, "sheet.png")))
                    names.Add(Path.GetFileName(dir));
            }
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        public static PetSpec Load(string petsDir, string name, int scale)
        {
            string dir = Path.Combine(petsDir, name);
            List<string> sections;
            Dictionary<string, string> ini = Ini.Read(Path.Combine(dir, "pet.ini"), out sections);
            int frameWidth = Ini.GetInt(ini, "frameWidth", 32);
            int frameHeight = Ini.GetInt(ini, "frameHeight", 32);
            int headings = Math.Max(1, Ini.GetInt(ini, "headings", 1));
            double speed = Ini.GetDouble(ini, "speed", 50);

            // Each [section] is an animation. A file without sections is one
            // animation described by top-level frames/fps, starting at row 0.
            List<Anim> anims = new List<Anim>();
            foreach (string section in sections)
            {
                string p = section + ".";
                anims.Add(new Anim(section, Ini.GetInt(ini, p + "row", 0),
                    Ini.GetInt(ini, p + "frames", 1), Ini.GetDouble(ini, p + "fps", 8)));
            }
            if (anims.Count == 0)
                anims.Add(new Anim("default", 0, Ini.GetInt(ini, "frames", 1), Ini.GetDouble(ini, "fps", 8)));

            // Read the PNG fully into memory so the file is not kept locked.
            using (MemoryStream bytes = new MemoryStream(File.ReadAllBytes(Path.Combine(dir, "sheet.png"))))
            using (Bitmap sheet = new Bitmap(bytes))
            {
                if (frameWidth <= 0 || frameHeight <= 0)
                    throw new InvalidDataException(name + ": pet.ini frame size must be positive");
                foreach (Anim a in anims)
                {
                    if (a.Frames <= 0 || a.Row < 0 || a.Fps <= 0 ||
                        a.Frames * frameWidth > sheet.Width || (a.Row + headings) * frameHeight > sheet.Height)
                        throw new InvalidDataException(name + ": animation '" + a.Name + "' does not fit sheet.png");
                }
                return new PetSpec(name, sheet, frameWidth, frameHeight, headings, anims, speed, scale);
            }
        }

        public void Dispose()
        {
            Native.DeleteDC(frameDc);
            Native.DeleteDC(sheetDc);
            Native.DeleteObject(frameBmp);
            Native.DeleteObject(sheetBmp);
            if (mirrorDc != IntPtr.Zero)
            {
                Native.DeleteDC(mirrorDc);
                Native.DeleteObject(mirrorBmp);
            }
            TrayIcon.Dispose();
        }
    }

    // Minimal ini reader/writer; ';' and '#' start comments. Keys inside a
    // [section] are stored as "section.key".
    static class Ini
    {
        public static Dictionary<string, string> Read(string path)
        {
            List<string> sections;
            return Read(path, out sections);
        }

        public static Dictionary<string, string> Read(string path, out List<string> sections)
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            sections = new List<string>();
            if (!File.Exists(path))
                return values;
            string prefix = "";
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw;
                int comment = line.IndexOfAny(new[] { ';', '#' });
                if (comment >= 0) line = line.Substring(0, comment);
                line = line.Trim();
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    string section = line.Substring(1, line.Length - 2).Trim();
                    sections.Add(section);
                    prefix = section + ".";
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq > 0)
                    values[prefix + line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
            return values;
        }

        public static void Write(string path, Dictionary<string, string> values)
        {
            List<string> lines = new List<string>();
            foreach (KeyValuePair<string, string> pair in values)
                lines.Add(pair.Key + "=" + pair.Value);
            File.WriteAllLines(path, lines.ToArray());
        }

        public static int GetInt(Dictionary<string, string> ini, string key, int fallback)
        {
            string s;
            int v;
            return ini.TryGetValue(key, out s) && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : fallback;
        }

        public static double GetDouble(Dictionary<string, string> ini, string key, double fallback)
        {
            string s;
            double v;
            return ini.TryGetValue(key, out s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : fallback;
        }
    }
}
