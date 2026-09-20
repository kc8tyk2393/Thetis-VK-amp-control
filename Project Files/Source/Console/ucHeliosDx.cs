using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Thetis
{
    /// <summary>
    /// Helios DX lives as a child box on the main Thetis console so it
    /// cannot fall behind the radio or vanish as a separate window.
    /// </summary>
    public class ucHeliosDx : UserControl
    {
        private const int GWL_STYLE = -16;
        private const int GWL_EXSTYLE = -20;
        private const int WS_CAPTION = 0x00C00000;
        private const int WS_THICKFRAME = 0x00040000;
        private const int WS_POPUP = unchecked((int)0x80000000);
        private const int WS_CHILD = 0x40000000;
        private const int WS_SYSMENU = 0x00080000;
        private const int WS_EX_APPWINDOW = 0x00040000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int SWP_NOSIZE = 0x0001;
        private const int SWP_NOMOVE = 0x0002;
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

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        private delegate bool EnumChildProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumChildProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        private const uint OBJID_CLIENT = 0xFFFFFFFC;

        [DllImport("oleacc.dll")]
        private static extern int AccessibleObjectFromWindow(IntPtr hwnd, uint dwObjectID, ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out object ppvObject);

        private static readonly Guid IID_IAccessible = new Guid("618736E0-3C3D-11CF-810C-00AA00389B71");

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
            public int Width { get { return Right - Left; } }
            public int Height { get { return Bottom - Top; } }
        }

        private readonly Console _console;
        private Process _helios;
        private IntPtr _heliosHwnd = IntPtr.Zero;
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
        private Size _nativeHeliosSize = Size.Empty;

        private PanelTS pnlBar;
        private LabelTS lblTitle;
        private ButtonTS btnCollapse;
        private ButtonTS btnClose;
        private ButtonTS btnBrowse;
        private ButtonTS btnLaunch;
        private TextBoxTS txtHeliosPath;
        private CheckBoxTS chkAutoLaunch;
        private LabelTS lblStatus;
        private PanelTS pnlMeters;
        private LabelTS lblPwrCaption;
        private LabelTS lblPwrValue;
        private LabelTS lblPwrUnit;
        private LabelTS lblSwrCaption;
        private LabelTS lblSwrValue;
        private PanelTS pnlHost;
        private PanelTS pnlGrab;
        private Timer tmrAttach;
        private Timer tmrWatch;

        public ucHeliosDx(Console c)
        {
            _console = c;
            BuildUi();
            Common.DoubleBufferAll(this, true);
        }

        private void BuildUi()
        {
            Name = "ucHeliosDx";
            Size = new Size(380, 590);
            MinimumSize = new Size(260, 36);
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
                Text = "  Helios DX",
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

            txtHeliosPath = new TextBoxTS
            {
                Name = "txtHeliosPath",
                Location = new Point(6, 6),
                Width = 236,
                Text = DefaultHeliosPath()
            };

            btnBrowse = new ButtonTS
            {
                Text = "…",
                Location = new Point(246, 4),
                Width = 28,
                Height = 23
            };
            btnBrowse.Click += BtnBrowse_Click;

            btnLaunch = new ButtonTS
            {
                Text = "Launch",
                Location = new Point(278, 4),
                Width = 90,
                Height = 23
            };
            btnLaunch.Click += (s, e) => LaunchOrAttach();

            chkAutoLaunch = new CheckBoxTS
            {
                Name = "chkAutoLaunch",
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
                Size = new Size(240, 18),
                Text = "Not connected"
            };

            pnlTop.Controls.Add(txtHeliosPath);
            pnlTop.Controls.Add(btnBrowse);
            pnlTop.Controls.Add(btnLaunch);
            pnlTop.Controls.Add(chkAutoLaunch);
            pnlTop.Controls.Add(lblStatus);

            pnlMeters = new PanelTS
            {
                Dock = DockStyle.Top,
                Height = 64,
                BackColor = Color.FromArgb(18, 18, 18)
            };

            lblPwrCaption = new LabelTS
            {
                AutoSize = false,
                Location = new Point(8, 6),
                Size = new Size(44, 18),
                Text = "PWR",
                ForeColor = Color.Gray,
                TextAlign = ContentAlignment.MiddleLeft
            };
            lblPwrValue = new LabelTS
            {
                AutoSize = false,
                Location = new Point(6, 22),
                Size = new Size(118, 36),
                Text = "----",
                Font = new Font("Consolas", 22f, FontStyle.Bold),
                ForeColor = Color.Lime,
                TextAlign = ContentAlignment.MiddleRight
            };
            lblPwrUnit = new LabelTS
            {
                AutoSize = false,
                Location = new Point(126, 30),
                Size = new Size(28, 22),
                Text = "W",
                ForeColor = Color.Gray,
                TextAlign = ContentAlignment.MiddleLeft
            };
            lblSwrCaption = new LabelTS
            {
                AutoSize = false,
                Location = new Point(188, 6),
                Size = new Size(50, 18),
                Text = "SWR",
                ForeColor = Color.Gray,
                TextAlign = ContentAlignment.MiddleLeft
            };
            lblSwrValue = new LabelTS
            {
                AutoSize = false,
                Location = new Point(176, 22),
                Size = new Size(160, 36),
                Text = "--.--",
                Font = new Font("Consolas", 22f, FontStyle.Bold),
                ForeColor = Color.Lime,
                TextAlign = ContentAlignment.MiddleLeft
            };

            pnlMeters.Controls.Add(lblPwrCaption);
            pnlMeters.Controls.Add(lblPwrValue);
            pnlMeters.Controls.Add(lblPwrUnit);
            pnlMeters.Controls.Add(lblSwrCaption);
            pnlMeters.Controls.Add(lblSwrValue);
            pnlMeters.Resize += PnlMeters_Resize;

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
                Name = "pnlHeliosHost",
                Dock = DockStyle.Fill,
                BackColor = Color.Black
            };
            pnlHost.Resize += (s, e) => FitEmbeddedWindow();

            Controls.Add(pnlHost);
            Controls.Add(pnlGrab);
            Controls.Add(pnlMeters);
            Controls.Add(pnlTop);
            Controls.Add(pnlBar);

            tmrAttach = new Timer { Interval = 250 };
            tmrAttach.Tick += TmrAttach_Tick;

            tmrWatch = new Timer { Interval = 250 };
            tmrWatch.Tick += TmrWatch_Tick;
            tmrWatch.Start();
        }

        public void RestoreState()
        {
            if (DB.ds == null) return;
            List<string> vars;
            try { vars = DB.GetVars("HeliosDxBox"); }
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
                    case "Path": txtHeliosPath.Text = p[1]; break;
                    case "Auto": chkAutoLaunch.Checked = p[1] == "True"; break;
                    // Do not restore Visible or auto-launch here. Starting Helios
                    // during Thetis init steals the ANAN/Hermes Ethernet port.
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
                "Path/" + txtHeliosPath.Text,
                "Auto/" + chkAutoLaunch.Checked,
                "Visible/" + Visible,
                "Collapsed/" + _collapsed
            };
            DB.SaveVars("HeliosDxBox", a);
        }

        public void ShowBox()
        {
            Visible = true;
            if (_collapsed) ToggleCollapse();
            BringToFront();
            if (CanLaunchHelios && chkAutoLaunch.Checked)
                LaunchOrAttach();
            else
                KeepHeliosInside();
        }

        public void HideBox()
        {
            Visible = false;
            SaveState();
        }

        private bool CanLaunchHelios
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

        private static string DefaultHeliosPath()
        {
            string downloads = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads", "Helios DX.exe");
            if (File.Exists(downloads)) return downloads;
            string beside = Path.Combine(Application.StartupPath, "Helios DX.exe");
            if (File.Exists(beside)) return beside;
            return downloads;
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
                Size = _expandedSize.Width > 0 ? _expandedSize : new Size(380, 520);
                btnCollapse.Text = "–";
                FitEmbeddedWindow();
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
            KeepHeliosInside();
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
            FitEmbeddedWindow();
        }

        private void Grab_MouseUp(object sender, MouseEventArgs e)
        {
            _resizing = false;
        }

        public void KeepOnConsole()
        {
            ClampToConsole();
            KeepHeliosInside();
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
                dlg.Filter = "Helios DX|Helios DX.exe|Executables|*.exe|All files|*.*";
                dlg.Title = "Locate Helios DX.exe";
                dlg.FileName = "Helios DX.exe";
                if (File.Exists(txtHeliosPath.Text))
                    dlg.InitialDirectory = Path.GetDirectoryName(txtHeliosPath.Text);
                if (dlg.ShowDialog(_console) == DialogResult.OK)
                    txtHeliosPath.Text = dlg.FileName;
            }
        }

        public void LaunchOrAttach()
        {
            if (_console != null && _console.initializing)
            {
                SetStatus("Wait for the radio, then Launch.");
                return;
            }

            if (_embedded && IsWindow(_heliosHwnd))
            {
                KeepHeliosInside();
                SetStatus("Helios DX in box.");
                return;
            }

            Process existing = FindHeliosProcess();
            if (existing != null)
            {
                _helios = existing;
                _startedByUs = false;
                BeginAttach();
                return;
            }

            string path = (txtHeliosPath.Text ?? "").Trim();
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
                _helios = Process.Start(psi);
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
            SetStatus("Waiting for Helios…");
            tmrAttach.Tag = Environment.TickCount;
            tmrAttach.Start();
        }

        private void TmrAttach_Tick(object sender, EventArgs e)
        {
            int started = tmrAttach.Tag is int t ? t : Environment.TickCount;
            IntPtr hwnd = FindHeliosWindow();
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
            if (!Visible || _collapsed) return;
            KeepHeliosInside();
            RefreshHeliosMeters();
        }

        private void PnlMeters_Resize(object sender, EventArgs e)
        {
            int mid = pnlMeters.ClientSize.Width / 2;
            lblSwrCaption.Left = mid + 8;
            lblSwrValue.Left = mid + 4;
        }

        private static string GetCaption(IntPtr hwnd)
        {
            int n = GetWindowTextLength(hwnd);
            if (n <= 0) return "";
            var sb = new StringBuilder(n + 1);
            GetWindowText(hwnd, sb, sb.Capacity);
            return sb.ToString().Trim();
        }

        private static string GetAccName(IntPtr hwnd)
        {
            try
            {
                Guid iid = IID_IAccessible;
                if (AccessibleObjectFromWindow(hwnd, OBJID_CLIENT, ref iid, out object obj) == 0 && obj != null)
                {
                    dynamic acc = obj;
                    object name = acc.get_accName(0);
                    string s = (name ?? "").ToString().Trim();
                    if (!string.IsNullOrEmpty(s)) return s;
                }
            }
            catch { }
            return GetCaption(hwnd);
        }

        private void TryReadHeliosCaptions(out string pwr, out string swr)
        {
            pwr = null;
            swr = null;
            var kids = new List<Tuple<IntPtr, IntPtr, string, RECT>>();
            EnumChildWindows(_heliosHwnd, (h, l) =>
            {
                string text = GetAccName(h);
                GetWindowRect(h, out RECT r);
                kids.Add(Tuple.Create(h, GetParent(h), text, r));
                return true;
            }, IntPtr.Zero);

            foreach (var k in kids)
            {
                if (!string.Equals(k.Item3, "SWR", StringComparison.OrdinalIgnoreCase))
                    continue;
                foreach (var s in kids)
                {
                    if (s.Item2 != k.Item2) continue;
                    if (Regex.IsMatch(s.Item3, @"^\d+\.\d{2}$"))
                    {
                        swr = s.Item3;
                        break;
                    }
                }
            }

            RECT outputRect = default;
            IntPtr outputParent = IntPtr.Zero;
            bool foundOutput = false;
            foreach (var k in kids)
            {
                if (!string.Equals(k.Item3, "Output", StringComparison.OrdinalIgnoreCase))
                    continue;
                outputRect = k.Item4;
                outputParent = k.Item2;
                foundOutput = true;
                break;
            }
            if (!foundOutput) return;
            int best = int.MaxValue;
            foreach (var s in kids)
            {
                if (s.Item2 != outputParent) continue;
                if (!Regex.IsMatch(s.Item3, @"^\d+$")) continue;
                int dx = s.Item4.Left - outputRect.Right;
                int dy = Math.Abs(s.Item4.Top - outputRect.Top);
                if (dx < -24 || dx > 120 || dy > 48) continue;
                if (dx < best)
                {
                    best = dx;
                    pwr = s.Item3;
                }
            }
        }

        private void RefreshHeliosMeters()
        {
            if (!_embedded || !IsWindow(_heliosHwnd))
            {
                SetMeterTexts("----", "--.--", Color.Gray, Color.Gray);
                return;
            }

            TryReadHeliosCaptions(out string pwr, out string swr);

            Color pwrColor = Color.Lime;
            Color swrColor = Color.Lime;
            if (string.IsNullOrEmpty(pwr))
            {
                pwr = "----";
                pwrColor = Color.Gray;
            }
            else if (pwr == "0")
            {
                pwrColor = Color.Gray;
            }

            if (string.IsNullOrEmpty(swr))
            {
                swr = "--.--";
                swrColor = Color.Gray;
            }
            else if (double.TryParse(swr, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double sv))
            {
                if (sv >= 2.0) swrColor = Color.OrangeRed;
                else if (sv >= 1.5) swrColor = Color.Gold;
                else swrColor = Color.Lime;
            }

            SetMeterTexts(pwr, swr, pwrColor, swrColor);
        }

        private void SetMeterTexts(string pwr, string swr, Color pwrColor, Color swrColor)
        {
            if (lblPwrValue.Text != pwr) lblPwrValue.Text = pwr;
            if (lblSwrValue.Text != swr) lblSwrValue.Text = swr;
            if (lblPwrValue.ForeColor != pwrColor) lblPwrValue.ForeColor = pwrColor;
            if (lblSwrValue.ForeColor != swrColor) lblSwrValue.ForeColor = swrColor;
        }

        private void AttachWindow(IntPtr hwnd)
        {
            if (GetWindowRect(hwnd, out RECT r) && r.Width > 32 && r.Height > 32)
                _nativeHeliosSize = new Size(r.Width, r.Height);
            else
                _nativeHeliosSize = new Size(320, 420);

            _heliosHwnd = hwnd;
            _savedStyle = GetWindowLong(hwnd, GWL_STYLE);
            _savedExStyle = GetWindowLong(hwnd, GWL_EXSTYLE);

            ShowWindow(hwnd, 5);
            SetParent(hwnd, pnlHost.Handle);

            int style = GetWindowLong(hwnd, GWL_STYLE);
            style &= ~(WS_CAPTION | WS_THICKFRAME | WS_SYSMENU | WS_POPUP);
            style |= WS_CHILD;
            SetWindowLong(hwnd, GWL_STYLE, style);

            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            ex &= ~WS_EX_APPWINDOW;
            ex |= WS_EX_TOOLWINDOW;
            SetWindowLong(hwnd, GWL_EXSTYLE, ex);
            SetWindowPos(hwnd, (IntPtr)HWND_TOP, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_FRAMECHANGED | SWP_SHOWWINDOW);

            _embedded = true;
            KeepHeliosInside();
            SetStatus("Helios DX in box.");
        }

        private void KeepHeliosInside()
        {
            if (!IsWindow(_heliosHwnd) || !pnlHost.IsHandleCreated) return;

            IntPtr parent = GetParent(_heliosHwnd);
            if (parent != pnlHost.Handle)
            {
                SetParent(_heliosHwnd, pnlHost.Handle);
                int style = GetWindowLong(_heliosHwnd, GWL_STYLE);
                style &= ~(WS_CAPTION | WS_THICKFRAME | WS_SYSMENU | WS_POPUP);
                style |= WS_CHILD;
                SetWindowLong(_heliosHwnd, GWL_STYLE, style);
                _embedded = true;
            }

            FitEmbeddedWindow();
        }

        private void FitEmbeddedWindow()
        {
            if (!_embedded || !IsWindow(_heliosHwnd) || !pnlHost.IsHandleCreated) return;
            Rectangle dest = LetterboxDest(pnlHost.ClientSize);
            MoveWindow(_heliosHwnd, dest.X, dest.Y, dest.Width, dest.Height, true);
        }

        private Rectangle LetterboxDest(Size box)
        {
            Size src = _nativeHeliosSize.Width > 0 ? _nativeHeliosSize : new Size(320, 400);
            int bw = Math.Max(1, box.Width);
            int bh = Math.Max(1, box.Height);
            float scale = Math.Min((float)bw / src.Width, (float)bh / src.Height);
            int w = Math.Max(1, (int)Math.Round(src.Width * scale));
            int h = Math.Max(1, (int)Math.Round(src.Height * scale));
            return new Rectangle((bw - w) / 2, (bh - h) / 2, w, h);
        }

        private void Detach(bool closingHost)
        {
            tmrAttach.Stop();
            if (_embedded && IsWindow(_heliosHwnd))
            {
                SetParent(_heliosHwnd, IntPtr.Zero);
                SetWindowLong(_heliosHwnd, GWL_STYLE, _savedStyle);
                SetWindowLong(_heliosHwnd, GWL_EXSTYLE, _savedExStyle);
                SetWindowPos(_heliosHwnd, IntPtr.Zero, 80, 80, 640, 400, SWP_FRAMECHANGED);
            }
            _embedded = false;
            _heliosHwnd = IntPtr.Zero;

            if (closingHost && _startedByUs && _helios != null && !_helios.HasExited)
            {
                try { _helios.CloseMainWindow(); } catch { }
                try
                {
                    if (!_helios.WaitForExit(1500))
                        _helios.Kill();
                }
                catch { }
            }

            _helios = null;
            _startedByUs = false;
            SetStatus("Not connected");
        }

        private static Process FindHeliosProcess()
        {
            foreach (Process p in Process.GetProcesses())
            {
                try
                {
                    if (string.Equals(p.ProcessName, "Helios DX", StringComparison.OrdinalIgnoreCase))
                        return p;
                }
                catch { }
            }
            return null;
        }

        private IntPtr FindHeliosWindow()
        {
            if (_helios == null) _helios = FindHeliosProcess();
            if (_helios == null) return IntPtr.Zero;
            try { _helios.Refresh(); } catch { return IntPtr.Zero; }
            if (!_helios.HasExited && _helios.MainWindowHandle != IntPtr.Zero)
                return _helios.MainWindowHandle;
            return IntPtr.Zero;
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
