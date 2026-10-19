// C# | UI/BallOverlayForm.cs | .NET Framework 4.8 x64 | Non-activating, click-through external ball annotation.
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using BlueLockV2.Core;
using BlueLockV2.Diagnostics;

namespace BlueLockV2.UI
{
    internal sealed class BallOverlayForm : Form
    {
        GameState state;
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);
        public BallOverlayForm()
        {
            Text = "BlueLock — antena da bola";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.Magenta;
            TransparencyKey = BackColor;
            DoubleBuffered = true;
            StartPosition = FormStartPosition.Manual;
        }

        protected override bool ShowWithoutActivation
        {
            get
            {
                return true;
            }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var parameters = base.CreateParams;
                // WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE
                parameters.ExStyle |= 0x80000 | 0x20 | 0x80 | 0x08000000;
                return parameters;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // Prevent self-observation by capture APIs on supported Windows 10+ compositors.
            try
            {
                SetWindowDisplayAffinity(Handle, 0x11);
            }
            catch (EntryPointNotFoundException)
            {
            }
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x84) // WM_NCHITTEST: pass mouse events to the game.
            {
                message.Result = new IntPtr(-1);
                return;
            }

            if (message.Msg == 0x21) // WM_MOUSEACTIVATE: never activate the overlay.
            {
                message.Result = new IntPtr(3);
                return;
            }

            base.WndProc(ref message);
        }

        public void Present(GameState snapshot, Rectangle clientBounds)
        {
            bool changed = !Object.ReferenceEquals(state, snapshot) || Bounds != clientBounds;
            state = snapshot;
            if (Bounds != clientBounds)
                Bounds = clientBounds;
            if (!Visible)
            {
                Show();
                changed = true;
            }

            if (changed)
                Invalidate();
        }

        public void Clear()
        {
            state = null;
            if (Visible)
                Hide();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            BallAnnotation.Draw(e.Graphics, state, ClientSize, new Vec(), 1, 1);
        }
    }
}
