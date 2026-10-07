using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TunnelWatch
{
    // Read-only native text input supports selection, copying and keyboard scrolling.
    // Periodic measurements must not jump the reader back to the first line.
    internal sealed class DiagnosticsBox : TextBox
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wparam, IntPtr lparam);
        internal DiagnosticsBox()
        {
            Multiline = true; ReadOnly = true; WordWrap = true; ScrollBars = ScrollBars.Vertical;
            BorderStyle = BorderStyle.None; Dock = DockStyle.Fill; Margin = new Padding(0); TabStop = true;
            BackColor = UiTheme.Background; ForeColor = UiTheme.Text;
            HideSelection = false;
            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Control && e.KeyCode == Keys.A) { SelectAll(); e.SuppressKeyPress = true; }
            };
        }
        internal void SetContent(string text)
        {
            text = (text ?? "").Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");
            if (Text == text) return;
            int firstLine = IsHandleCreated ? SendMessage(Handle, 0xCE, IntPtr.Zero, IntPtr.Zero).ToInt32() : 0;
            int start = SelectionStart, length = SelectionLength;
            Text = text; Select(Math.Min(start, TextLength), Math.Min(length, Math.Max(0, TextLength - start)));
            if (IsHandleCreated)
            {
                int current = SendMessage(Handle, 0xCE, IntPtr.Zero, IntPtr.Zero).ToInt32();
                SendMessage(Handle, 0xB6, IntPtr.Zero, new IntPtr(firstLine - current));
            }
        }
    }
}
