using System;
using System.Windows.Forms;

namespace DesktopPet
{
    // A borderless, click-through, always-on-top window that shows whatever
    // bitmap it is given. Used for pets and for ripples.
    sealed class PetWindow : Form
    {
        int shownX = int.MinValue, shownY = int.MinValue;

        public PetWindow()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOPMOST
                    | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        // Show the bitmap selected into dc as the window's content, at (x, y).
        public void ShowFrame(IntPtr dc, int x, int y, int width, int height)
        {
            Native.PaintLayered(Handle, dc, x, y, width, height);
            shownX = x;
            shownY = y;
        }

        // Move without repainting; layered windows keep their content.
        public void MoveTo(int x, int y)
        {
            if (x == shownX && y == shownY)
                return;
            Native.SetWindowPos(Handle, IntPtr.Zero, x, y, 0, 0,
                Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
            shownX = x;
            shownY = y;
        }

        // Other always-on-top windows can cover ours; put it back in front.
        public void KeepOnTop()
        {
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        // Directly behind another of our windows (ripples go under their fish).
        public void PlaceBelow(PetWindow other)
        {
            Native.SetWindowPos(Handle, other.Handle, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }
    }
}
