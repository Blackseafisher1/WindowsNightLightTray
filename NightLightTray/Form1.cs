using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace NightLightTray
{
    public partial class Form1 : Form
    {
        private const string SettingsRegPath = @"Software\NightLightTray";
        private const int SettingsWidth = 500;
        private const int SettingsHeight = 360;

        private readonly NotifyIcon _notifyIcon;
        private readonly System.Windows.Forms.Timer _pollTimer;
        private ContextMenuStrip _menu;
        private ToolStripMenuItem _alwaysShowItem;
        private bool _alwaysShow;
        private bool _lastThemeDark;

        private IntPtr _settingsHwnd;
        private Thread _hookThread;
        private int _hookThreadId;
        private WinEventProc _winEventProcDelegate;
        private long _lastOpenTicks;
        private long _trackSince;
        private int _idleTicks;
        private readonly System.Windows.Forms.Timer _closeTimer;
        private readonly uint _ourPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;

        private const int OpenDebounceMs = 500;
        private static long NowMs
        {
            get { return System.Diagnostics.Stopwatch.GetTimestamp() * 1000 / System.Diagnostics.Stopwatch.Frequency; }
        }

        public Form1(bool alwaysVisible = false)
        {
            InitializeComponent();
            ShowInTaskbar = false;
            WindowState = FormWindowState.Minimized;
            Opacity = 0;

            _alwaysShow = alwaysVisible || ReadAlwaysShow();
            _lastThemeDark = ThemeManager.IsDark();

            _notifyIcon = new NotifyIcon
            {
                Icon = CreateMoonIcon(),
                Text = "Night Light",
                Visible = false
            };
            _notifyIcon.MouseClick += OnIconClick;
            _notifyIcon.MouseDoubleClick += OnIconDoubleClick;

            BuildMenu();
            _notifyIcon.ContextMenuStrip = _menu;

            _pollTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _pollTimer.Tick += OnPoll;
            _pollTimer.Start();

            FormClosed += (s, e) => StopHookThread();

            _closeTimer = new System.Windows.Forms.Timer { Interval = 300 };
            _closeTimer.Tick += OnCloseWatch;
        }

        private void OnCloseWatch(object sender, EventArgs e)
        {
            IntPtr target = Volatile.Read(ref _settingsHwnd);
            if (target == IntPtr.Zero)
            {
                _closeTimer.Stop();
                return;
            }
            if (NowMs - Volatile.Read(ref _trackSince) < 800)
            {
                return;
            }
            GetWindowThreadProcessId(target, out uint threadId);
            GUITHREADINFO gti;
            gti.cbSize = (uint)Marshal.SizeOf(typeof(GUITHREADINFO));
            gti.flags = 0;
            gti.hwndActive = IntPtr.Zero;
            gti.hwndFocus = IntPtr.Zero;
            gti.hwndCapture = IntPtr.Zero;
            gti.hwndMenuOwner = IntPtr.Zero;
            gti.hwndMoveSize = IntPtr.Zero;
            gti.hwndCaret = IntPtr.Zero;
            gti.rcCaret = new RECT();
            if (GetGUIThreadInfo(threadId, out gti))
            {
                IntPtr active = gti.hwndActive;
                if (active != IntPtr.Zero && (active == target || GetAncestor(active, 2) == target))
                {
                    return;
                }
            }
            CloseSettings(target);
        }

        private void CloseSettings(IntPtr target)
        {
            Interlocked.Exchange(ref _settingsHwnd, IntPtr.Zero);
            _closeTimer.Stop();
            PostMessage(target, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            ScheduleGc();
        }

        private void StopHookThread()
        {
            int id = Interlocked.Exchange(ref _hookThreadId, 0);
            if (id != 0)
            {
                PostThreadMessage((uint)id, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            }
        }

        private void BuildMenu()
        {
            _menu = new ContextMenuStrip();

            _alwaysShowItem = new ToolStripMenuItem("Always show in tray");
            _alwaysShowItem.CheckOnClick = true;
            _alwaysShowItem.Checked = _alwaysShow;
            _alwaysShowItem.CheckedChanged += (s, e) =>
            {
                _alwaysShow = _alwaysShowItem.Checked;
                WriteAlwaysShow(_alwaysShow);
                UpdateIconVisibility();
            };

            ToolStripMenuItem exitItem = new ToolStripMenuItem("Exit");
            exitItem.Click += (s, e) => Application.Exit();

            _menu.Items.Add(_alwaysShowItem);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(exitItem);
            _menu.Font = ThemeManager.UiFont(9f, FontStyle.Regular);
            _menu.Renderer = new ThemedMenuRenderer(_lastThemeDark);
            _menu.Opened += (s, e) =>
            {
                try
                {
                    GetWindowRect(_menu.Handle, out RECT rect);
                    int x = rect.Left;
                    int y = rect.Top - 10;
                    if (y < Screen.PrimaryScreen.WorkingArea.Top)
                    {
                        y = rect.Top;
                    }
                    SetWindowPos(_menu.Handle, HWND_TOPMOST, x, y, 0, 0,
                        SWP_NOSIZE | SWP_NOACTIVATE);
                }
                catch
                {
                }
            };
        }

        private void OnIconClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                TryToggleSettings();
            }
        }

        private void OnIconDoubleClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                TryToggleSettings();
            }
        }

        private void TryToggleSettings()
        {
            long now = NowMs;
            if (now - Volatile.Read(ref _lastOpenTicks) < OpenDebounceMs)
            {
                return;
            }
            Interlocked.Exchange(ref _lastOpenTicks, now);

            IntPtr open = Volatile.Read(ref _settingsHwnd);
            if (open != IntPtr.Zero)
            {
                return;
            }
            OpenNightLightSettings();
        }

        private void OpenNightLightSettings()
        {
            Dictionary<IntPtr, bool> before = SnapshotWindows();
            try
            {
                Process.Start(new ProcessStartInfo("ms-settings:nightlight") { UseShellExecute = true });
            }
            catch
            {
                return;
            }

            Thread watcher = new Thread(() =>
            {
                IntPtr hwnd = WaitForNewWindow(before, 8000);
                if (hwnd == IntPtr.Zero)
                {
                    return;
                }
                if (!IsSettingsProcess(hwnd) || !WaitForWindowReady(hwnd))
                {
                    return;
                }
                PositionAboveTray(hwnd);
                SetSettingsWindow(hwnd);
                BeginInvoke(new Action(() => FocusWindow(hwnd)));
            });
            watcher.IsBackground = true;
            watcher.Start();
        }

        private static IntPtr WaitForNewWindow(Dictionary<IntPtr, bool> before, int timeoutMs)
        {
            long deadline = NowMs + timeoutMs;
            while (NowMs < deadline)
            {
                IntPtr hwnd = FindNewVisibleWindow(before);
                if (hwnd != IntPtr.Zero)
                {
                    return hwnd;
                }
                Thread.Sleep(150);
            }
            return IntPtr.Zero;
        }

        private static bool IsSettingsProcess(IntPtr hwnd)
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0)
            {
                return false;
            }
            try
            {
                using (System.Diagnostics.Process p = System.Diagnostics.Process.GetProcessById((int)pid))
                {
                    string name = p.ProcessName;
                    return name == "SystemSettings" || name == "ApplicationFrameHost";
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool WaitForWindowReady(IntPtr hwnd)
        {
            long seenAt = -1;
            long deadline = NowMs + 4000;
            while (NowMs < deadline)
            {
                if (!IsWindowVisible(hwnd) || !GetWindowRect(hwnd, out RECT rect) || rect.Right - rect.Left <= 0 || rect.Bottom - rect.Top <= 0)
                {
                    seenAt = -1;
                }
                else if (seenAt < 0)
                {
                    seenAt = NowMs;
                }
                else if (NowMs - seenAt > 300)
                {
                    return true;
                }
                Thread.Sleep(50);
            }
            return false;
        }

        private void SetSettingsWindow(IntPtr hwnd)
        {
            EnsureHookThread();
            Interlocked.Exchange(ref _settingsHwnd, hwnd);
            Interlocked.Exchange(ref _trackSince, NowMs);
            _closeTimer.Start();
        }

        private void EnsureHookThread()
        {
            if (_hookThread != null)
            {
                return;
            }
            lock (this)
            {
                if (_hookThread != null)
                {
                    return;
                }
                _hookThread = new Thread(() =>
                {
                _winEventProcDelegate = OnWinEvent;
                IntPtr foregroundHook = SetWinEventHook(
                    EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
                    IntPtr.Zero, _winEventProcDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);
                IntPtr activeHook = SetWinEventHook(
                    EVENT_OBJECT_ACTIVE, EVENT_OBJECT_ACTIVE,
                    IntPtr.Zero, _winEventProcDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);
                IntPtr focusHook = SetWinEventHook(
                    EVENT_OBJECT_FOCUS, EVENT_OBJECT_FOCUS,
                    IntPtr.Zero, _winEventProcDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);
                IntPtr destroyHook = SetWinEventHook(
                    EVENT_OBJECT_DESTROY, EVENT_OBJECT_DESTROY,
                    IntPtr.Zero, _winEventProcDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);
                if (foregroundHook == IntPtr.Zero && activeHook == IntPtr.Zero && focusHook == IntPtr.Zero && destroyHook == IntPtr.Zero)
                {
                    return;
                }
                Interlocked.Exchange(ref _hookThreadId, (int)GetCurrentThreadId());
                try
                {
                    MSG msg;
                    while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
                    {
                        TranslateMessage(ref msg);
                        DispatchMessage(ref msg);
                    }
                }
                finally
                {
                    if (foregroundHook != IntPtr.Zero)
                    {
                        UnhookWinEvent(foregroundHook);
                    }
                    if (activeHook != IntPtr.Zero)
                    {
                        UnhookWinEvent(activeHook);
                    }
                    if (focusHook != IntPtr.Zero)
                    {
                        UnhookWinEvent(focusHook);
                    }
                    if (destroyHook != IntPtr.Zero)
                    {
                        UnhookWinEvent(destroyHook);
                    }
                }
                });
                _hookThread.IsBackground = true;
                _hookThread.Start();
            }
        }

        private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            IntPtr target = Volatile.Read(ref _settingsHwnd);
            if (target == IntPtr.Zero)
            {
                return;
            }

            bool outside = false;
            if (eventType == EVENT_SYSTEM_FOREGROUND)
            {
                IntPtr foreground = GetForegroundWindow();
                outside = foreground != target && GetAncestor(foreground, 2) != target;
            }
            else if (eventType == EVENT_OBJECT_ACTIVE)
            {
                GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid == _ourPid)
                {
                    return;
                }
                IntPtr root = GetAncestor(hwnd, 2);
                outside = root != target && root != IntPtr.Zero && hwnd != target;
            }
            else if (eventType == EVENT_OBJECT_FOCUS)
            {
                IntPtr root = GetAncestor(hwnd, 2);
                outside = root != target && root != IntPtr.Zero && hwnd != target;
            }
            else if (eventType == EVENT_OBJECT_DESTROY && idObject == OBJID_WINDOW)
            {
                if (hwnd == target)
                {
                    Interlocked.Exchange(ref _settingsHwnd, IntPtr.Zero);
                    _closeTimer.Stop();
                    ScheduleGc();
                }
                return;
            }
            else
            {
                return;
            }

            if (!outside)
            {
                return;
            }
            if (NowMs - Volatile.Read(ref _trackSince) < 800)
            {
                return;
            }
            CloseSettings(target);
        }

        private static void ScheduleGc()
        {
            Thread t = new Thread(() =>
            {
                Thread.Sleep(1500);
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                try
                {
                    SetProcessWorkingSetSize(GetCurrentProcess(), (IntPtr)(-1), (IntPtr)(-1));
                }
                catch
                {
                }
            });
            t.IsBackground = true;
            t.Start();
        }

        private static Dictionary<IntPtr, bool> SnapshotWindows()
        {
            Dictionary<IntPtr, bool> windows = new Dictionary<IntPtr, bool>();
            EnumWindows((hwnd, lParam) =>
            {
                if (IsWindowVisible(hwnd))
                {
                    windows[hwnd] = true;
                }
                return true;
            }, IntPtr.Zero);
            return windows;
        }

        private static IntPtr FindNewVisibleWindow(Dictionary<IntPtr, bool> before)
        {
            IntPtr found = IntPtr.Zero;
            EnumWindows((hwnd, lParam) =>
            {
                if (found != IntPtr.Zero)
                {
                    return false;
                }
                if (!IsWindowVisible(hwnd) || before.ContainsKey(hwnd))
                {
                    return true;
                }
                int len = GetWindowTextLength(hwnd);
                if (len <= 0)
                {
                    return true;
                }
                StringBuilder title = new StringBuilder(len + 1);
                GetWindowText(hwnd, title, title.Capacity);
                string t = title.ToString();
                if (t == "NightLightTray" || t == "NightLightTray" || t == "Form1")
                {
                    return true;
                }
                found = hwnd;
                return false;
            }, IntPtr.Zero);
            return found;
        }

        private static void PositionAboveTray(IntPtr hwnd)
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            int x = wa.Right - SettingsWidth - 8;
            int y = wa.Bottom - SettingsHeight - 8;
            MoveWindow(hwnd, x, y, SettingsWidth, SettingsHeight, true);
        }

        private static void FocusWindow(IntPtr hwnd)
        {
            try
            {
                ShowWindow(hwnd, SW_SHOW);
                IntPtr foreground = GetForegroundWindow();
                uint fgThread = GetWindowThreadProcessId(foreground, out _);
                uint curThread = GetCurrentThreadId();
                bool attached = false;
                if (fgThread != 0 && fgThread != curThread)
                {
                    attached = AttachThreadInput(fgThread, curThread, true);
                }
                SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                SetForegroundWindow(hwnd);
                SetActiveWindow(hwnd);
                SetWindowPos(hwnd, HWND_NOTOPMOST, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                if (attached)
                {
                    AttachThreadInput(fgThread, curThread, false);
                }
            }
            catch
            {
            }
        }

        private void OnPoll(object sender, EventArgs e)
        {
            UpdateIconVisibility();

            bool dark = ThemeManager.IsDark();
            if (dark != _lastThemeDark)
            {
                _lastThemeDark = dark;
                _menu.Renderer = new ThemedMenuRenderer(dark);
            }

            if (Volatile.Read(ref _settingsHwnd) != IntPtr.Zero)
            {
                _idleTicks = 0;
                return;
            }
            if (++_idleTicks >= 30)
            {
                _idleTicks = 0;
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                try
                {
                    SetProcessWorkingSetSize(GetCurrentProcess(), (IntPtr)(-1), (IntPtr)(-1));
                }
                catch
                {
                }
            }
        }

        private void UpdateIconVisibility()
        {
            bool enabled = NightLightController.IsAvailable() && NightLightController.GetEnabled();
            bool visible = enabled || _alwaysShow;
            if (_notifyIcon.Visible != visible)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Visible = visible;
            }
            _notifyIcon.Text = enabled ? "Night Light" : "Night Light (off)";
        }

        private static bool ReadAlwaysShow()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(SettingsRegPath))
                {
                    object value = key?.GetValue("AlwaysShow");
                    return value is int i && i != 0;
                }
            }
            catch
            {
                return false;
            }
        }

        private static void WriteAlwaysShow(bool value)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(SettingsRegPath))
                {
                    key.SetValue("AlwaysShow", value ? 1 : 0, RegistryValueKind.DWord);
                }
            }
            catch
            {
            }
        }

        private static Icon CreateMoonIcon()
        {
            using (Bitmap bmp = new Bitmap(16, 16))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    using (Brush b = new SolidBrush(Color.White))
                    {
                        g.FillEllipse(b, 2, 2, 12, 12);
                    }
                    using (Brush cut = new SolidBrush(Color.FromArgb(0, 0, 0)))
                    {
                        g.FillEllipse(cut, 6, 1, 10, 10);
                    }
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool MoveWindow(IntPtr hWnd, int x, int y, int nWidth, int nHeight, bool bRepaint);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool GetGUIThreadInfo(uint idThread, out GUITHREADINFO lpGuiThreadInfo);

        [StructLayout(LayoutKind.Sequential)]
        private struct GUITHREADINFO
        {
            public uint cbSize;
            public uint flags;
            public IntPtr hwndActive;
            public IntPtr hwndFocus;
            public IntPtr hwndCapture;
            public IntPtr hwndMenuOwner;
            public IntPtr hwndMoveSize;
            public IntPtr hwndCaret;
            public RECT rcCaret;
        }

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool SetActiveWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public int ptX;
            public int ptY;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventProc lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        [DllImport("user32.dll")]
        private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll")]
        private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr dwMinimumWorkingSetSize, IntPtr dwMaximumWorkingSetSize);

        private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        private const uint EVENT_OBJECT_ACTIVE = 0x8011;
        private const uint EVENT_OBJECT_FOCUS = 0x8005;
        private const uint EVENT_OBJECT_DESTROY = 0x8001;
        private const int OBJID_WINDOW = 0;
        private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        private const uint WM_QUIT = 0x0012;

        private delegate void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const int SW_SHOW = 5;
        private const uint WM_CLOSE = 0x0010;

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    }

    internal class ThemedMenuRenderer : ToolStripProfessionalRenderer
    {
        private readonly bool _dark;

        public ThemedMenuRenderer(bool dark)
            : base(new ThemedColorTable(dark))
        {
            _dark = dark;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = _dark ? Color.FromArgb(0xF2, 0xF2, 0xF2) : Color.FromArgb(0x1A, 0x1A, 0x1A);
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Color checkColor = _dark ? Color.FromArgb(0x4C, 0xC2, 0xFF) : Color.FromArgb(0x00, 0x67, 0xC0);
            using (Pen pen = new Pen(checkColor, 2f))
            {
                int x = e.ImageRectangle.Left + 3;
                int y = e.ImageRectangle.Top + 3;
                int s = Math.Min(e.ImageRectangle.Width, e.ImageRectangle.Height) - 6;
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                e.Graphics.DrawLine(pen, x, y + s / 2, x + s / 2, y + s);
                e.Graphics.DrawLine(pen, x + s / 2, y + s, x + s, y);
            }
        }
    }

    internal class ThemedColorTable : ProfessionalColorTable
    {
        private readonly bool _dark;

        public ThemedColorTable(bool dark)
        {
            _dark = dark;
        }

        private Color MenuColor { get { return _dark ? Color.FromArgb(0x2B, 0x2B, 0x2B) : Color.White; } }
        private Color HoverColor { get { return _dark ? Color.FromArgb(0x3E, 0x3E, 0x3E) : Color.FromArgb(0xE5, 0xF1, 0xFB); } }
        private Color BorderColor { get { return _dark ? Color.FromArgb(0x44, 0x44, 0x44) : Color.FromArgb(0xE0, 0xE0, 0xE0); } }

        public override Color ToolStripDropDownBackground { get { return MenuColor; } }
        public override Color ImageMarginGradientBegin { get { return MenuColor; } }
        public override Color ImageMarginGradientMiddle { get { return MenuColor; } }
        public override Color ImageMarginGradientEnd { get { return MenuColor; } }
        public override Color MenuBorder { get { return BorderColor; } }
        public override Color MenuItemBorder { get { return HoverColor; } }
        public override Color MenuItemSelected { get { return HoverColor; } }
        public override Color MenuItemSelectedGradientBegin { get { return HoverColor; } }
        public override Color MenuItemSelectedGradientEnd { get { return HoverColor; } }
        public override Color MenuItemPressedGradientBegin { get { return HoverColor; } }
        public override Color MenuItemPressedGradientMiddle { get { return HoverColor; } }
        public override Color MenuItemPressedGradientEnd { get { return HoverColor; } }
        public override Color SeparatorDark { get { return BorderColor; } }
        public override Color SeparatorLight { get { return BorderColor; } }
        public override Color CheckBackground { get { return HoverColor; } }
        public override Color CheckSelectedBackground { get { return HoverColor; } }
        public override Color CheckPressedBackground { get { return HoverColor; } }
    }
}
