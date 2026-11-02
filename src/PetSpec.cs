using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;

namespace DesktopPet
{
    // One pet, loaded from pets/<name>/pet.ini and sheet.png. Every frame is scaled
    // and converted to a GDI bitmap once, facing right and left, so animating is
    // only a matter of selecting a different bitmap.
    sealed class PetSpec : IDisposable
    {
        public readonly string Name;
        public readonly int Width, Height;   // scaled frame size in screen pixels
        public readonly int FrameCount;
        public readonly double Fps;
        public readonly double Speed;        // screen pixels per second, already scaled
        public readonly Icon TrayIcon;

        readonly IntPtr[] right, left;
        readonly IntPtr memDc;

        PetSpec(string name, Bitmap sheet, int frameWidth, int frameHeight, int frames,
            double fps, double speed, int scale)
        {
            Name = name;
            Width = frameWidth * scale;
            Height = frameHeight * scale;
            FrameCount = frames;
            Fps = fps;
            Speed = speed * scale;
            right = new IntPtr[frames];
            left = new IntPtr[frames];

            for (int i = 0; i < frames; i++)
            {
                using (Bitmap frame = Scale(sheet, new Rectangle(i * frameWidth, 0, frameWidth, frameHeight), scale))
                {
                    right[i] = frame.GetHbitmap(Color.FromArgb(0));
                    frame.RotateFlip(RotateFlipType.RotateNoneFlipX);
                    left[i] = frame.GetHbitmap(Color.FromArgb(0));
                }
            }

            using (Bitmap thumb = Scale(sheet, new Rectangle(0, 0, frameWidth, frameHeight), 1))
            using (Bitmap square = new Bitmap(32, 32, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(square))
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                int side = Math.Max(frameWidth, frameHeight);
                int w = frameWidth * 32 / side, h = frameHeight * 32 / side;
                g.DrawImage(thumb, (32 - w) / 2, (32 - h) / 2, w, h);
                TrayIcon = Icon.FromHandle(square.GetHicon());
            }

            memDc = Native.CreateCompatibleDC(IntPtr.Zero);
        }

        // Select a frame into the shared memory DC and return the DC for painting.
        public IntPtr Select(int frame, bool facingRight)
        {
            Native.SelectObject(memDc, (facingRight ? right : left)[frame]);
            return memDc;
        }

        static Bitmap Scale(Bitmap sheet, Rectangle source, int scale)
        {
            Bitmap result = new Bitmap(source.Width * scale, source.Height * scale, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(result))
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(sheet, new Rectangle(0, 0, result.Width, result.Height), source, GraphicsUnit.Pixel);
            }
            return result;
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
            Dictionary<string, string> ini = Ini.Read(Path.Combine(dir, "pet.ini"));
            int frameWidth = Ini.GetInt(ini, "frameWidth", 32);
            int frameHeight = Ini.GetInt(ini, "frameHeight", 32);
            int frames = Ini.GetInt(ini, "frames", 1);
            double fps = Ini.GetDouble(ini, "fps", 8);
            double speed = Ini.GetDouble(ini, "speed", 50);

            // Read the PNG fully into memory so the file is not kept locked.
            using (MemoryStream bytes = new MemoryStream(File.ReadAllBytes(Path.Combine(dir, "sheet.png"))))
            using (Bitmap sheet = new Bitmap(bytes))
            {
                if (frameWidth <= 0 || frameHeight <= 0 || frames <= 0 ||
                    frameWidth * frames > sheet.Width || frameHeight > sheet.Height)
                    throw new InvalidDataException(name + ": pet.ini frame size does not fit sheet.png");
                return new PetSpec(name, sheet, frameWidth, frameHeight, frames, fps, speed, scale);
            }
        }

        public void Dispose()
        {
            Native.DeleteDC(memDc);
            foreach (IntPtr h in right) Native.DeleteObject(h);
            foreach (IntPtr h in left) Native.DeleteObject(h);
            TrayIcon.Dispose();
        }
    }

    // Minimal key=value reader/writer; ';' and '#' start comments.
    static class Ini
    {
        public static Dictionary<string, string> Read(string path)
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(path))
                return values;
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw;
                int comment = line.IndexOfAny(new[] { ';', '#' });
                if (comment >= 0) line = line.Substring(0, comment);
                int eq = line.IndexOf('=');
                if (eq > 0)
                    values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
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
