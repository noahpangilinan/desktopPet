using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DesktopPet
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            // Real pixels, so Windows never blurs the art by scaling the window.
            Native.SetProcessDPIAware();

            bool first;
            using (Mutex single = new Mutex(true, "DesktopPet-SingleInstance", out first))
            {
                if (!first)
                    return;
                Application.EnableVisualStyles();
                try
                {
                    Application.Run(new PetApp());
                }
                catch (Exception e)
                {
                    MessageBox.Show(e.Message, "Desktop Pet", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }

    // Owns the stage and the tray icon, the only way to control the pets,
    // since their windows ignore the mouse.
    sealed class PetApp : ApplicationContext
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunValue = "DesktopPet";
        static readonly int[] Sizes = { 2, 3, 4, 6 };

        readonly string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        readonly string petsDir, settingsPath;
        readonly Dictionary<string, string> settings;
        readonly NotifyIcon tray = new NotifyIcon();
        readonly Stage stage;

        public PetApp()
        {
            petsDir = Path.Combine(baseDir, "pets");
            settingsPath = Path.Combine(baseDir, "settings.ini");
            settings = Ini.Read(settingsPath);

            List<string> names = PetSpec.Available(petsDir);
            if (names.Count == 0)
                throw new FileNotFoundException("No pets found in " + petsDir);

            stage = new Stage(petsDir, Path.Combine(baseDir, "effects"), Scale());
            stage.SetActivity(Ini.GetInt(settings, "activity", Activity.Default));
            stage.ShowOverFullscreen = Flag("overFullscreen");
            foreach (string name in SavedPets(names))
            {
                try { stage.Add(name); }
                catch (Exception) { }   // a broken pet folder shouldn't stop the rest
            }
            if (stage.Pets.Count == 0)
                stage.Add(names[0]);

            tray.Text = "Desktop Pet";
            tray.ContextMenuStrip = new ContextMenuStrip();
            tray.ContextMenuStrip.Opening += delegate { BuildMenu(); };
            UpdateTrayIcon();
            tray.Visible = true;

            if (!Flag("paused"))
                stage.Resume();
        }

        // Pets to show: the saved list ("pets=koi,heron"), the older single
        // "pet=" setting, or the koi.
        List<string> SavedPets(List<string> names)
        {
            string saved;
            if (!settings.TryGetValue("pets", out saved) && !settings.TryGetValue("pet", out saved))
                saved = "";
            List<string> chosen = new List<string>();
            foreach (string part in saved.Split(','))
            {
                string name = part.Trim();
                if (names.Contains(name) && !chosen.Contains(name))
                    chosen.Add(name);
            }
            if (chosen.Count == 0)
                chosen.Add(names.Contains("koi") ? "koi" : names[0]);
            return chosen;
        }

        int Scale()
        {
            return Math.Max(1, Math.Min(8, Ini.GetInt(settings, "scale", 3)));
        }

        bool Flag(string key)
        {
            string v;
            return settings.TryGetValue(key, out v) && v == "true";
        }

        void BuildMenu()
        {
            ContextMenuStrip menu = tray.ContextMenuStrip;
            List<ToolStripItem> old = new List<ToolStripItem>();
            foreach (ToolStripItem item in menu.Items) old.Add(item);
            menu.Items.Clear();
            foreach (ToolStripItem item in old) item.Dispose();

            ToolStripMenuItem pets = new ToolStripMenuItem("Pets");
            foreach (string name in PetSpec.Available(petsDir))
            {
                string chosen = name;
                ToolStripMenuItem item = new ToolStripMenuItem(Title(name), null, delegate { TogglePet(chosen); });
                item.Checked = new List<Pet>(stage.Pets).Exists(p => p.Spec.Name == chosen);
                pets.DropDownItems.Add(item);
            }
            menu.Items.Add(pets);

            ToolStripMenuItem sizes = new ToolStripMenuItem("Size");
            foreach (int size in Sizes)
            {
                int chosen = size;
                ToolStripMenuItem item = new ToolStripMenuItem(size + "x", null, delegate { SetSize(chosen); });
                item.Checked = size == stage.Scale;
                sizes.DropDownItems.Add(item);
            }
            menu.Items.Add(sizes);

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(ActivitySlider(menu));
            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem fullscreen = new ToolStripMenuItem("Show over full-screen apps", null, delegate
            {
                stage.ShowOverFullscreen = !stage.ShowOverFullscreen;
                settings["overFullscreen"] = stage.ShowOverFullscreen ? "true" : "false";
                Save();
            });
            fullscreen.Checked = stage.ShowOverFullscreen;
            menu.Items.Add(fullscreen);

            ToolStripMenuItem pause = new ToolStripMenuItem("Pause", null, delegate { TogglePause(); });
            pause.Checked = !stage.Running;
            menu.Items.Add(pause);

            ToolStripMenuItem startup = new ToolStripMenuItem("Start with Windows", null, delegate { ToggleStartup(); });
            startup.Checked = StartsWithWindows();
            menu.Items.Add(startup);

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Quit", null, delegate { Quit(); }));
        }

        // "Activity: Normal" above a five-step slider, from Calm (lightest on
        // the CPU) to Lively (most aware, smoothest).
        ToolStripItem ActivitySlider(ContextMenuStrip menu)
        {
            Panel panel = new Panel { Width = 190, Height = 58, BackColor = menu.BackColor };
            Label label = new Label { Left = 8, Top = 4, Width = 180, Height = 18, BackColor = menu.BackColor };
            TrackBar slider = new TrackBar
            {
                Left = 2, Top = 22, Width = 184, Height = 32,
                Minimum = 0, Maximum = Activity.Levels.Length - 1,
                SmallChange = 1, LargeChange = 1, TickFrequency = 1,
                Value = Array.IndexOf(Activity.Levels, stage.Activity),
                BackColor = menu.BackColor,
            };
            label.Text = "Activity: " + stage.Activity.Name;
            slider.ValueChanged += delegate
            {
                stage.SetActivity(slider.Value);
                label.Text = "Activity: " + stage.Activity.Name;
                settings["activity"] = slider.Value.ToString();
                Save();
            };
            panel.Controls.Add(label);
            panel.Controls.Add(slider);
            return new ToolStripControlHost(panel) { AutoSize = false, Size = panel.Size };
        }

        static string Title(string name)
        {
            return name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name.Substring(1);
        }

        void TogglePet(string name)
        {
            Pet shown = new List<Pet>(stage.Pets).Find(p => p.Spec.Name == name);
            try
            {
                if (shown == null)
                    stage.Add(name);
                else if (stage.Pets.Count > 1)   // always keep at least one
                {
                    tray.Icon = SystemIcons.Application;   // its icon is about to be freed
                    stage.Remove(name);
                }
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message, "Desktop Pet", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            UpdateTrayIcon();
            SavePets();
        }

        void SetSize(int scale)
        {
            try
            {
                tray.Icon = SystemIcons.Application;
                stage.SetScale(scale);
                settings["scale"] = scale.ToString();
                Save();
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message, "Desktop Pet", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            UpdateTrayIcon();
        }

        void UpdateTrayIcon()
        {
            if (stage.Pets.Count > 0)
                tray.Icon = stage.Pets[0].Spec.TrayIcon;
        }

        void SavePets()
        {
            List<string> names = new List<string>();
            foreach (Pet p in stage.Pets) names.Add(p.Spec.Name);
            settings["pets"] = string.Join(",", names.ToArray());
            settings.Remove("pet");
            Save();
        }

        void TogglePause()
        {
            if (stage.Running) stage.Pause();
            else stage.Resume();
            settings["paused"] = stage.Running ? "false" : "true";
            Save();
        }

        static bool StartsWithWindows()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey))
                return key != null && key.GetValue(RunValue) != null;
        }

        static void ToggleStartup()
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (key.GetValue(RunValue) != null)
                    key.DeleteValue(RunValue);
                else
                    key.SetValue(RunValue, "\"" + Application.ExecutablePath + "\"");
            }
        }

        void Save()
        {
            try { Ini.Write(settingsPath, settings); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        void Quit()
        {
            tray.Visible = false;
            stage.Pause();
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                tray.Dispose();
                stage.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
