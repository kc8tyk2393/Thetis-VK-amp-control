using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Thetis
{
    /// <summary>
    /// Lets every console GroupBox/Panel (and Helios) be moved and resized.
    /// Hold Alt and drag a panel. Hold Alt near the bottom-right corner to resize.
    /// Alt+right-click for hide / reset. Positions are stored in the database.
    /// Meter containers keep their own drag chrome; they are unlocked separately.
    /// </summary>
    internal sealed class ConsoleContainerLayout : IMessageFilter
    {
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_MOUSEMOVE = 0x0200;
        private const int Edge = 14;

        private readonly Console _console;
        private readonly Dictionary<string, Rectangle> _defaults = new Dictionary<string, Rectangle>();
        private Control _drag;
        private bool _resize;
        private Point _startMouse;
        private Point _startLoc;
        private Size _startSize;
        private bool _dirty;

        public ConsoleContainerLayout(Console console)
        {
            _console = console;
            CaptureDefaults();
            Load();
            Application.AddMessageFilter(this);
            _console.FormClosing += (s, e) => Save();
        }

        public bool PreFilterMessage(ref Message m)
        {
            if (_console == null || _console.IsDisposed) return false;
            if (!Common.AltlKeyDown && _drag == null) return false;

            if (m.Msg == WM_LBUTTONDOWN)
            {
                Control host = HitContainer();
                if (host == null) return false;
                Point screen = Control.MousePosition;
                Rectangle r = host.RectangleToScreen(host.ClientRectangle);
                bool onCorner = (screen.X >= r.Right - Edge && screen.Y >= r.Bottom - Edge);
                bool onChrome = screen.Y <= r.Top + 18;
                if (!onCorner && !onChrome && OverInteractiveChild(host, screen))
                    return false;
                _resize = onCorner;
                _drag = host;
                _startMouse = screen;
                _startLoc = host.Location;
                _startSize = host.Size;
                host.BringToFront();
                Cursor.Current = _resize ? Cursors.SizeNWSE : Cursors.SizeAll;
                return true;
            }

            if (m.Msg == WM_MOUSEMOVE && _drag != null)
            {
                Point now = Control.MousePosition;
                if (_resize)
                {
                    int w = Math.Max(48, _startSize.Width + (now.X - _startMouse.X));
                    int h = Math.Max(24, _startSize.Height + (now.Y - _startMouse.Y));
                    _drag.Size = new Size(w, h);
                }
                else
                {
                    _drag.Location = new Point(
                        _startLoc.X + (now.X - _startMouse.X),
                        _startLoc.Y + (now.Y - _startMouse.Y));
                }
                Clamp(_drag);
                Cursor.Current = _resize ? Cursors.SizeNWSE : Cursors.SizeAll;
                _dirty = true;
                return true;
            }

            if (m.Msg == WM_LBUTTONUP && _drag != null)
            {
                Clamp(_drag);
                _drag = null;
                _resize = false;
                Save();
                return true;
            }

            if (m.Msg == WM_RBUTTONDOWN)
            {
                Control host = HitContainer();
                if (host == null) return false;
                ShowMenu(host);
                return true;
            }

            return false;
        }

        public void Register(Control c)
        {
            if (c == null || string.IsNullOrEmpty(c.Name)) return;
            if (!_defaults.ContainsKey(c.Name))
                _defaults[c.Name] = new Rectangle(c.Location, c.Size);
            LoadOne(c);
        }

        public void ResetAll()
        {
            foreach (Control c in _console.Controls)
            {
                if (string.IsNullOrEmpty(c.Name)) continue;
                if (_defaults.TryGetValue(c.Name, out Rectangle r))
                {
                    c.Location = r.Location;
                    c.Size = r.Size;
                }
            }

            Display.ResetBlobMaximums(1, true);
            Display.ResetBlobMaximums(2, true);
            Display.ResetSpectrumPeaks(1);
            Display.ResetSpectrumPeaks(2);
            MeterManager.HighlightContainer("");

            _console.Invalidate(true);
            Control pan = _console.Controls["panelDisplay"];
            pan?.Invalidate(true);

            _dirty = true;
            Save();
        }

        private void CaptureDefaults()
        {
            foreach (Control c in _console.Controls)
            {
                if (string.IsNullOrEmpty(c.Name)) continue;
                if (c is MenuStrip || c is StatusStrip) continue;
                _defaults[c.Name] = new Rectangle(c.Location, c.Size);
            }
        }

        private IEnumerable<Control> Enumerate()
        {
            foreach (Control c in _console.Controls)
            {
                if (IsMovable(c)) yield return c;
            }
        }

        private static bool IsMovable(Control c)
        {
            if (c == null || string.IsNullOrEmpty(c.Name)) return false;
            if (c is MenuStrip || c is StatusStrip) return false;
            if (c is ucMeter) return false;
            if (c.Name == "btnHidden") return false;
            if (c is CheckBox || c is TrackBar || c is PictureBox || c is Label || c is NumericUpDown)
                return false;
            return c is GroupBox || c is Panel || c is UserControl;
        }

        private static bool OverInteractiveChild(Control host, Point screen)
        {
            Control c = host;
            while (true)
            {
                Control child = c.GetChildAtPoint(c.PointToClient(screen), GetChildAtPointSkip.Invisible);
                if (child == null) break;
                c = child;
            }
            return c is Button || c is CheckBox || c is RadioButton || c is TrackBar ||
                   c is ComboBox || c is TextBox || c is NumericUpDown || c is ListBox ||
                   c is PictureBox;
        }

        private Control HitContainer()
        {
            if (!_console.IsHandleCreated) return null;
            Point screen = Control.MousePosition;
            if (!_console.RectangleToScreen(_console.ClientRectangle).Contains(screen)) return null;
            Control hit = _console.GetChildAtPoint(_console.PointToClient(screen), GetChildAtPointSkip.Invisible);
            while (hit != null && hit != _console)
            {
                if (hit is ucMeter) return null;
                if (IsMovable(hit) && hit.Parent == _console) return hit;
                hit = hit.Parent;
            }
            return null;
        }

        private void Clamp(Control c)
        {
            int maxX = Math.Max(0, _console.ClientSize.Width - 32);
            int maxY = Math.Max(0, _console.ClientSize.Height - 32);
            int x = Math.Max(-c.Width + 32, Math.Min(c.Left, maxX));
            int y = Math.Max(0, Math.Min(c.Top, maxY));
            c.Location = new Point(x, y);
        }

        private void ShowMenu(Control host)
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("Hide this panel", null, (s, e) => { host.Visible = false; _dirty = true; Save(); });
            menu.Items.Add("Reset this panel", null, (s, e) =>
            {
                if (_defaults.TryGetValue(host.Name, out Rectangle r))
                {
                    host.Location = r.Location;
                    host.Size = r.Size;
                    host.Visible = true;
                    _dirty = true;
                    Save();
                }
            });
            menu.Items.Add("Reset all panels", null, (s, e) => ResetAll());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Alt+drag to move, Alt+drag corner to resize");
            menu.Show(Control.MousePosition);
        }

        private void Load()
        {
            if (DB.ds == null) return;
            List<string> vars = DB.GetVars("ConsoleContainerLayout");
            if (vars == null || vars.Count == 0) return;
            Dictionary<string, string> map = new Dictionary<string, string>();
            foreach (string line in vars)
            {
                int i = line.IndexOf('/');
                if (i <= 0) continue;
                map[line.Substring(0, i)] = line.Substring(i + 1);
            }
            foreach (Control c in Enumerate())
                ApplyMap(c, map);
        }

        private void LoadOne(Control c)
        {
            if (DB.ds == null) return;
            List<string> vars = DB.GetVars("ConsoleContainerLayout");
            if (vars == null) return;
            Dictionary<string, string> map = new Dictionary<string, string>();
            foreach (string line in vars)
            {
                int i = line.IndexOf('/');
                if (i <= 0) continue;
                map[line.Substring(0, i)] = line.Substring(i + 1);
            }
            ApplyMap(c, map);
        }

        private static void ApplyMap(Control c, Dictionary<string, string> map)
        {
            string prefix = c.Name + ".";
            if (map.TryGetValue(prefix + "X", out string xs) && int.TryParse(xs, out int x) &&
                map.TryGetValue(prefix + "Y", out string ys) && int.TryParse(ys, out int y))
                c.Location = new Point(x, y);
            if (map.TryGetValue(prefix + "W", out string ws) && int.TryParse(ws, out int w) &&
                map.TryGetValue(prefix + "H", out string hs) && int.TryParse(hs, out int h) &&
                w >= 48 && h >= 24)
                c.Size = new Size(w, h);
        }

        private void Save()
        {
            if (!_dirty || DB.ds == null) return;
            List<string> a = new List<string>();
            foreach (Control c in Enumerate())
            {
                a.Add(c.Name + ".X/" + c.Left);
                a.Add(c.Name + ".Y/" + c.Top);
                a.Add(c.Name + ".W/" + c.Width);
                a.Add(c.Name + ".H/" + c.Height);
            }
            DB.SaveVars("ConsoleContainerLayout", a);
            _dirty = false;
        }
    }
}
