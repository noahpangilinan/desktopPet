using System;
using System.Collections.Generic;
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

    // Owns the pet window and the tray icon, the only way to control the pet,
    // since the window itself ignores the mouse.
    sealed class PetApp : ApplicationContext
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunValue = "DesktopPet";
        static readonly int[] Sizes = { 2, 3, 4, 6 };

        readonly string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        readonly string petsDir, settingsPath;
        readonly Dictionary<string, string> settings;
        readonly NotifyIcon tray = new NotifyIcon();
        readonly PetWindow window;
        PetSpec pet;

        public PetApp()
        {
            petsDir = Path.Combine(baseDir, "pets");
            settingsPath = Path.Combine(baseDir, "settings.ini");
            settings = Ini.Read(settingsPath);

            List<string> names = PetSpec.Available(petsDir);
            if (names.Count == 0)
                throw new FileNotFoundException("No pets found in " + petsDir);
            string name;
            if (!settings.TryGetValue("pet", out name) || !names.Contains(name))
                name = names.Contains("koi") ? "koi" : names[0];
            pet = PetSpec.Load(petsDir, name, Scale());

            window = new PetWindow(pet);
            tray.Icon = pet.TrayIcon;
            tray.Text = "Desktop Pet";
            tray.ContextMenuStrip = new ContextMenuStrip();
            tray.ContextMenuStrip.Opening += delegate { BuildMenu(); };
            tray.Visible = true;

            window.Show();
            if (settings.ContainsKey("paused") && settings["paused"] == "true")
                window.Pause();
        }

        int Scale()
        {
            int scale = Ini.GetInt(settings, "scale", 3);
            return Math.Max(1, Math.Min(8, scale));
        }

        void BuildMenu()
        {
            ContextMenuStrip menu = tray.ContextMenuStrip;
            menu.Items.Clear();

            ToolStripMenuItem pets = new ToolStripMenuItem("Pet");
            foreach (string name in PetSpec.Available(petsDir))
            {
                string chosen = name;
                ToolStripMenuItem item = new ToolStripMenuItem(Title(name), null, delegate { Switch(chosen, Scale()); });
                item.Checked = name == pet.Name;
                pets.DropDownItems.Add(item);
            }
            menu.Items.Add(pets);

            ToolStripMenuItem sizes = new ToolStripMenuItem("Size");
            foreach (int size in Sizes)
            {
                int chosen = size;
                ToolStripMenuItem item = new ToolStripMenuItem(size + "x", null, delegate { Switch(pet.Name, chosen); });
                item.Checked = size == Scale();
                sizes.DropDownItems.Add(item);
            }
            menu.Items.Add(sizes);

            ToolStripMenuItem pause = new ToolStripMenuItem("Pause", null, delegate { TogglePause(); });
            pause.Checked = !window.Running;
            menu.Items.Add(pause);

            ToolStripMenuItem startup = new ToolStripMenuItem("Start with Windows", null, delegate { ToggleStartup(); });
            startup.Checked = StartsWithWindows();
            menu.Items.Add(startup);

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Quit", null, delegate { Quit(); }));
        }

        static string Title(string name)
        {
            return name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name.Substring(1);
        }

        void Switch(string name, int scale)
        {
            PetSpec next;
            try
            {
                next = PetSpec.Load(petsDir, name, scale);
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message, "Desktop Pet", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            PetSpec old = pet;
            pet = next;
            window.SetPet(next);
            tray.Icon = next.TrayIcon;
            old.Dispose();

            settings["pet"] = name;
            settings["scale"] = scale.ToString();
            Save();
        }

        void TogglePause()
        {
            if (window.Running) window.Pause();
            else window.Resume();
            settings["paused"] = window.Running ? "false" : "true";
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
            window.Close();
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                tray.Dispose();
                window.Dispose();
                pet.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
