using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Thetis
{
    /// <summary>
    /// VK Amps Control lives as a child box on the main Thetis console,
    /// the same way Helios DX does, so it stays with the radio window.
    /// </summary>
    public class ucVkAmps : UserControl
    {
        private const string ExeFileName = "VK.Amps.Control-0.1.0-win-x64.exe";

        private const int GWL_STYLE = -16;
        private const int GWL_EXSTYLE = -20;
        private const int WS_CAPTION = 0x00C00000;
        private const int WS_THICKFRAME = 0x00040000;
        private const int WS_POPUP = unchecked((int)0x80000000);
        private const int WS_CHILD = 0x40000000;
        private const int WS_SYSMENU = 0x00080000;
        private const int WS_VISIBLE = 0x10000000;
        private const int WS_CLIPCHILDREN = 0x02000000;
        private const int WS_CLIPSIBLINGS = 0x04000000;
        private const int WS_EX_APPWINDOW = 0x00040000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int GWLP_HWNDPARENT = -8;
        private const int SW_HIDE = 0;
        private const int SW_SHOWNA = 8;
        private const int SWP_NOSIZE = 0x0001;
        private const int SWP_NOMOVE = 0x0002;
        private const int SWP_NOZORDER = 0x0004;
        private const int SWP_NOACTIVATE = 0x0010;
        private const int SWP_FRAMECHANGED = 0x0020;
        private const int SWP_SHOWWINDOW = 0x0040;
        private const int HWND_TOP = 0;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll")]
        private static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
            public int Width { get { return Right - Left; } }
            public int Height { get { return Bottom - Top; } }
        }

        private readonly Console _console;
        private Process _app;
        private IntPtr _appHwnd = IntPtr.Zero;
        private int _savedStyle;
        private int _savedExStyle;
        private bool _embedded;
        private bool _startedByUs;
        private bool _collapsed;
        private Size _expandedSize;
        private bool _dragging;
        private bool _resizing;
        private Point _dragStart;
        private Point _locStart;
        private Size _sizeStart;

        private PanelTS pnlBar;
        private LabelTS lblTitle;
        private ButtonTS btnCollapse;
        private ButtonTS btnClose;
        private ButtonTS btnBrowse;
        private ButtonTS btnLaunch;
        private TextBoxTS txtAppPath;
        private CheckBoxTS chkAutoLaunch;
        private LabelTS lblStatus;
        private PanelTS pnlHost;
        private PanelTS pnlGrab;
        private Timer tmrAttach;
        private Timer tmrWatch;

        public ucVkAmps(Console c)
        {
            _console = c;
            BuildUi();
            Common.DoubleBufferAll(this, true);
            if (_console != null)
                _console.LocationChanged += (s, e) => { if (_embedded) KeepAppInside(); };
        }

        private void BuildUi()
        {
            Name = "ucVkAmps";
            Size = new Size(480, 640);
            MinimumSize = new Size(280, 36);
            BorderStyle = BorderStyle.FixedSingle;
            BackColor = Color.FromArgb(32, 32, 32);

            pnlBar = new PanelTS
            {
                Dock = DockStyle.Top,
                Height = 28,
                BackColor = Color.FromArgb(50, 50, 50),
                Cursor = Cursors.SizeAll
            };
            pnlBar.MouseDown += Title_MouseDown;
            pnlBar.MouseMove += Title_MouseMove;
            pnlBar.MouseUp += Title_MouseUp;

            lblTitle = new LabelTS
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                Text = "  VK3",
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleLeft,
                Cursor = Cursors.SizeAll
            };
            lblTitle.MouseDown += Title_MouseDown;
            lblTitle.MouseMove += Title_MouseMove;
            lblTitle.MouseUp += Title_MouseUp;

            btnCollapse = new ButtonTS
            {
                Text = "–",
                Dock = DockStyle.Right,
                Width = 28,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White,
                BackColor = Color.FromArgb(70, 70, 70)
            };
            btnCollapse.FlatAppearance.BorderSize = 0;
            btnCollapse.Click += (s, e) => ToggleCollapse();

            btnClose = new ButtonTS
            {
                Text = "×",
                Dock = DockStyle.Right,
                Width = 28,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White,
                BackColor = Color.FromArgb(90, 40, 40)
            };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Click += (s, e) => HideBox();

            pnlBar.Controls.Add(lblTitle);
            pnlBar.Controls.Add(btnCollapse);
            pnlBar.Controls.Add(btnClose);

            var pnlTop = new PanelTS
            {
                Dock = DockStyle.Top,
                Height = 58,
                BackColor = Color.FromArgb(40, 40, 40)
            };

            txtAppPath = new TextBoxTS
            {
                Name = "txtVkAmpsPath",
                Location = new Point(6, 6),
                Width = 286,
                Text = DefaultAppPath()
            };

            btnBrowse = new ButtonTS
            {
                Text = "…",
                Location = new Point(296, 4),
                Width = 28,
                Height = 23,
                BackColor = Color.White,
                ForeColor = Color.Black,
                UseVisualStyleBackColor = false
            };
            btnBrowse.Click += BtnBrowse_Click;

            btnLaunch = new ButtonTS
            {
                Text = "Launch",
                Location = new Point(328, 4),
                Width = 90,
                Height = 23,
                BackColor = Color.White,
                ForeColor = Color.Black,
                UseVisualStyleBackColor = false
            };
            btnLaunch.Click += (s, e) => LaunchOrAttach();

            chkAutoLaunch = new CheckBoxTS
            {
                Name = "chkVkAutoLaunch",
                Text = "Auto",
                Checked = false,
                AutoSize = true,
                ForeColor = Color.White,
                Location = new Point(6, 34)
            };

            lblStatus = new LabelTS
            {
                AutoSize = false,
                ForeColor = Color.Gainsboro,
                Location = new Point(70, 34),
                Size = new Size(340, 18),
                Text = "Not connected"
            };

            pnlTop.Controls.Add(txtAppPath);
            pnlTop.Controls.Add(btnBrowse);
            pnlTop.Controls.Add(btnLaunch);
            pnlTop.Controls.Add(chkAutoLaunch);
            pnlTop.Controls.Add(lblStatus);

            pnlGrab = new PanelTS
            {
                Dock = DockStyle.Bottom,
                Height = 10,
                Cursor = Cursors.SizeNWSE,
                BackColor = Color.FromArgb(50, 50, 50)
            };
            pnlGrab.MouseDown += Grab_MouseDown;
            pnlGrab.MouseMove += Grab_MouseMove;
            pnlGrab.MouseUp += Grab_MouseUp;

            pnlHost = new PanelTS
            {
                Name = "pnlVkAmpsHost",
                Dock = DockStyle.Fill,
                BackColor = Color.Black
            };
            pnlHost.Resize += (s, e) => KeepAppInside();

            Controls.Add(pnlHost);
            Controls.Add(pnlGrab);
            Controls.Add(pnlTop);
            Controls.Add(pnlBar);

            tmrAttach = new Timer { Interval = 250 };
            tmrAttach.Tick += TmrAttach_Tick;

            tmrWatch = new Timer { Interval = 100 };
            tmrWatch.Tick += TmrWatch_Tick;
            tmrWatch.Start();
        }

        public void RestoreState()
        {
            if (DB.ds == null) return;
            List<string> vars;
            try { vars = DB.GetVars("VkAmpsBox"); }
            catch { return; }
            if (vars == null) return;
            foreach (string s in vars)
            {
                string[] p = s.Split(new[] { '/' }, 2);
                if (p.Length < 2) continue;
                switch (p[0])
                {
                    case "Left": if (int.TryParse(p[1], out int l)) Left = l; break;
                    case "Top": if (int.TryParse(p[1], out int t)) Top = t; break;
                    case "Width": if (int.TryParse(p[1], out int w)) Width = w; break;
                    case "Height": if (int.TryParse(p[1], out int h)) Height = h; break;
                    case "Path": txtAppPath.Text = p[1]; break;
                    case "Auto": chkAutoLaunch.Checked = p[1] == "True"; break;
                    // Do not restore Visible or auto-launch here. Starting the
                    // app during Thetis init can race the radio connection.
                }
            }
            ClampToConsole();
        }

        public void SaveState()
        {
            if (DB.ds == null) return;
            Size sz = _collapsed ? _expandedSize : Size;
            var a = new List<string>
            {
                "Left/" + Left,
                "Top/" + Top,
                "Width/" + sz.Width,
                "Height/" + sz.Height,
                "Path/" + txtAppPath.Text,
                "Auto/" + chkAutoLaunch.Checked,
                "Visible/" + Visible,
                "Collapsed/" + _collapsed
            };
            DB.SaveVars("VkAmpsBox", a);
        }

        public void ShowBox()
        {
            Visible = true;
            if (_collapsed) ToggleCollapse();
            BringToFront();
            if (CanLaunch && chkAutoLaunch.Checked)
                LaunchOrAttach();
            else
                KeepAppInside();
        }

        public void HideBox()
        {
            Visible = false;
            if (IsWindow(_appHwnd))
                ShowWindow(_appHwnd, SW_HIDE);
            SaveState();
        }

        private bool CanLaunch
        {
            get
            {
                if (_console == null || _console.initializing) return false;
                return _console.PowerOn;
            }
        }

        public void SaveAndCloseHost()
        {
            SaveState();
            tmrWatch.Stop();
            Detach(true);
        }

        private static string DefaultAppPath()
        {
            string downloads = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads", ExeFileName);
            if (File.Exists(downloads)) return downloads;
            string beside = Path.Combine(Application.StartupPath, ExeFileName);
            if (File.Exists(beside)) return beside;
            return downloads;
        }

        private string ProcessName
        {
            get
            {
                string path = (txtAppPath.Text ?? "").Trim();
                string name = Path.GetFileNameWithoutExtension(path);
                if (string.IsNullOrEmpty(name))
                    name = Path.GetFileNameWithoutExtension(ExeFileName);
                return name;
            }
        }

        private void ToggleCollapse()
        {
            if (!_collapsed)
            {
                _expandedSize = Size;
                _collapsed = true;
                Height = pnlBar.Height + 2;
                btnCollapse.Text = "+";
            }
            else
            {
                _collapsed = false;
                Size = _expandedSize.Width > 0 ? _expandedSize : new Size(480, 640);
                btnCollapse.Text = "–";
                KeepAppInside();
            }
            ClampToConsole();
        }

        private void Title_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            _dragging = true;
            _dragStart = PointToScreen(e.Location);
            _locStart = Location;
            BringToFront();
        }

        private void Title_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            Point now = Control.MousePosition;
            Location = new Point(_locStart.X + (now.X - _dragStart.X), _locStart.Y + (now.Y - _dragStart.Y));
            ClampToConsole();
            KeepAppInside();
        }

        private void Title_MouseUp(object sender, MouseEventArgs e)
        {
            _dragging = false;
        }

        private void Grab_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            _resizing = true;
            _dragStart = Control.MousePosition;
            _sizeStart = Size;
        }

        private void Grab_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_resizing) return;
            Point now = Control.MousePosition;
            Width = Math.Max(MinimumSize.Width, _sizeStart.Width + (now.X - _dragStart.X));
            Height = Math.Max(MinimumSize.Height, _sizeStart.Height + (now.Y - _dragStart.Y));
            ClampToConsole();
            KeepAppInside();
        }

        private void Grab_MouseUp(object sender, MouseEventArgs e)
        {
            _resizing = false;
        }

        public void KeepOnConsole()
        {
            ClampToConsole();
            KeepAppInside();
        }

        private void ClampToConsole()
        {
            if (_console == null) return;
            int maxX = Math.Max(0, _console.ClientSize.Width - Width);
            int maxY = Math.Max(0, _console.ClientSize.Height - Height);
            Left = Math.Max(0, Math.Min(Left, maxX));
            Top = Math.Max(0, Math.Min(Top, maxY));
        }

        private void BtnBrowse_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Filter = "VK Amps Control|" + ExeFileName + "|Executables|*.exe|All files|*.*";
                dlg.Title = "Locate " + ExeFileName;
                dlg.FileName = ExeFileName;
                if (File.Exists(txtAppPath.Text))
                    dlg.InitialDirectory = Path.GetDirectoryName(txtAppPath.Text);
                if (dlg.ShowDialog(_console) == DialogResult.OK)
                    txtAppPath.Text = dlg.FileName;
            }
        }

        public void LaunchOrAttach()
        {
            if (_console != null && _console.initializing)
            {
                SetStatus("Wait for the radio, then Launch.");
                return;
            }

            if (_embedded && IsWindow(_appHwnd))
            {
                KeepAppInside();
                SetStatus("VK3 in box.");
                return;
            }

            Process existing = FindAppProcess();
            if (existing != null)
            {
                _app = existing;
                _startedByUs = false;
                BeginAttach();
                return;
            }

            string path = (txtAppPath.Text ?? "").Trim();
            if (!File.Exists(path))
            {
                SetStatus("Set path, then Launch.");
                return;
            }

            try
            {
                var psi = new ProcessStartInfo(path)
                {
                    WorkingDirectory = Path.GetDirectoryName(path) ?? "",
                    UseShellExecute = true
                };
                _app = Process.Start(psi);
                _startedByUs = true;
                BeginAttach();
            }
            catch (Exception ex)
            {
                SetStatus("Start failed: " + ex.Message);
            }
        }

        private void BeginAttach()
        {
            SetStatus("Waiting for VK3…");
            tmrAttach.Tag = Environment.TickCount;
            tmrAttach.Start();
        }

        private void TmrAttach_Tick(object sender, EventArgs e)
        {
            int started = tmrAttach.Tag is int t ? t : Environment.TickCount;
            IntPtr hwnd = FindAppWindow();
            if (hwnd != IntPtr.Zero)
            {
                tmrAttach.Stop();
                AttachWindow(hwnd);
                return;
            }
            if (Environment.TickCount - started > 15000)
            {
                tmrAttach.Stop();
                SetStatus("Window not found — click Launch again.");
            }
        }

        private void TmrWatch_Tick(object sender, EventArgs e)
        {
            if (!_embedded && !tmrAttach.Enabled) return;
            KeepAppInside();
        }

        private void AttachWindow(IntPtr hwnd)
        {
            _appHwnd = hwnd;
            _savedStyle = GetWindowLong(hwnd, GWL_STYLE);
            _savedExStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            _embedded = true;
            MakeChildOfHost(hwnd);
            BringToFront();
            SetStatus("VK3 in box.");
        }

        private void MakeChildOfHost(IntPtr hwnd)
        {
            if (!IsWindow(hwnd)) return;
            if (!pnlHost.IsHandleCreated)
            {
                IntPtr created = pnlHost.Handle;
                if (created == IntPtr.Zero) return;
            }

            // Style must change before SetParent. Electron opens as a popup;
            // leaving WS_POPUP on makes the window stay outside the box.
            int style = GetWindowLong(hwnd, GWL_STYLE);
            style &= ~(WS_POPUP | WS_CAPTION | WS_THICKFRAME | WS_SYSMENU);
            style |= WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS;
            SetWindowLong(hwnd, GWL_STYLE, style);

            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            ex &= ~WS_EX_APPWINDOW;
            ex |= WS_EX_TOOLWINDOW;
            SetWindowLong(hwnd, GWL_EXSTYLE, ex);

            int hostStyle = GetWindowLong(pnlHost.Handle, GWL_STYLE);
            hostStyle |= WS_CLIPCHILDREN | WS_CLIPSIBLINGS;
            SetWindowLong(pnlHost.Handle, GWL_STYLE, hostStyle);

            if (GetParent(hwnd) != pnlHost.Handle)
                SetParent(hwnd, pnlHost.Handle);

            int w = Math.Max(1, pnlHost.ClientSize.Width);
            int h = Math.Max(1, pnlHost.ClientSize.Height);
            MoveWindow(hwnd, 0, 0, w, h, true);
            SetWindowPos(hwnd, (IntPtr)HWND_TOP, 0, 0, w, h, SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_FRAMECHANGED);
            if (!IsWindowVisible(hwnd))
                ShowWindow(hwnd, SW_SHOWNA);
        }

        private void KeepAppInside()
        {
            if (!Visible || _collapsed)
            {
                if (IsWindow(_appHwnd) && IsWindowVisible(_appHwnd))
                    ShowWindow(_appHwnd, SW_HIDE);
                return;
            }

            if (!IsWindow(_appHwnd))
            {
                if (!_embedded) return;
                IntPtr again = FindAppWindow();
                if (again == IntPtr.Zero) return;
                AttachWindow(again);
                return;
            }

            IntPtr popped = FindAppWindow();
            if (popped != IntPtr.Zero && popped != _appHwnd)
            {
                AttachWindow(popped);
                return;
            }

            MakeChildOfHost(_appHwnd);
            if (Parent != null && Parent.Controls.IndexOf(this) != Parent.Controls.Count - 1)
                BringToFront();
        }

        private void FitEmbeddedWindow()
        {
            if (!_embedded || !IsWindow(_appHwnd)) return;
            MakeChildOfHost(_appHwnd);
        }

        private void Detach(bool closingHost)
        {
            tmrAttach.Stop();
            if (_embedded && IsWindow(_appHwnd))
            {
                SetParent(_appHwnd, IntPtr.Zero);
                SetWindowLongPtr(_appHwnd, GWLP_HWNDPARENT, IntPtr.Zero);
                SetWindowLong(_appHwnd, GWL_STYLE, _savedStyle);
                SetWindowLong(_appHwnd, GWL_EXSTYLE, _savedExStyle);
                SetWindowPos(_appHwnd, IntPtr.Zero, 80, 80, 960, 640, SWP_FRAMECHANGED);
            }
            _embedded = false;
            _appHwnd = IntPtr.Zero;

            if (closingHost && _startedByUs && _app != null && !_app.HasExited)
            {
                try { _app.CloseMainWindow(); } catch { }
                try
                {
                    if (!_app.WaitForExit(1500))
                        _app.Kill();
                }
                catch { }
            }

            _app = null;
            _startedByUs = false;
            SetStatus("Not connected");
        }

        private Process FindAppProcess()
        {
            string name = ProcessName;
            foreach (Process p in Process.GetProcesses())
            {
                try
                {
                    if (string.Equals(p.ProcessName, name, StringComparison.OrdinalIgnoreCase))
                        return p;
                }
                catch { }
            }
            return null;
        }

        private IntPtr FindAppWindow()
        {
            var pids = new HashSet<int>();
            if (_app != null)
            {
                try
                {
                    _app.Refresh();
                    if (!_app.HasExited)
                        pids.Add(_app.Id);
                }
                catch { }
            }

            string name = ProcessName;
            foreach (Process p in Process.GetProcesses())
            {
                try
                {
                    if (string.Equals(p.ProcessName, name, StringComparison.OrdinalIgnoreCase)
                        || p.ProcessName.IndexOf("VK.Amps", StringComparison.OrdinalIgnoreCase) >= 0
                        || p.ProcessName.IndexOf("VK Amps", StringComparison.OrdinalIgnoreCase) >= 0)
                        pids.Add(p.Id);
                }
                catch { }
            }
            if (pids.Count == 0) return IntPtr.Zero;

            IntPtr best = IntPtr.Zero;
            int bestArea = 0;
            EnumWindows((h, l) =>
            {
                if (!IsWindowVisible(h)) return true;
                if (GetParent(h) == pnlHost.Handle) return true;
                GetWindowThreadProcessId(h, out uint wpid);
                if (!pids.Contains((int)wpid)) return true;
                if (!GetWindowRect(h, out RECT r)) return true;
                int area = r.Width * r.Height;
                if (r.Width > 80 && r.Height > 80 && area > bestArea)
                {
                    best = h;
                    bestArea = area;
                }
                return true;
            }, IntPtr.Zero);
            return best;
        }

        private void SetStatus(string text)
        {
            if (lblStatus == null) return;
            if (lblStatus.InvokeRequired)
            {
                lblStatus.BeginInvoke(new Action(() => lblStatus.Text = text));
                return;
            }
            lblStatus.Text = text;
        }
    }
}
