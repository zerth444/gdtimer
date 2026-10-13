using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using WF = System.Windows.Forms;

namespace GDTimer
{
    public class App : Application
    {
        [STAThread]
        public static void Main()
        {
            bool created;
            using (var mutex = new Mutex(true, "GDTimer_SingleInstance", out created))
            {
                if (!created) return;
                new App().Run();
            }
        }

        private Store store;
        private Tray tray;
        private MainWindow window;
        private GdMemory mem = new GdMemory();
        private string lastLevel;
        private int saveCounter;
        private DispatcherTimer timer;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            store = new Store();
            tray = new Tray();
            tray.Open += ShowWindow;
            tray.Exit += ExitApp;

            window = new MainWindow(store);
            window.QuitRequested += ExitApp;

            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += Tick;
            timer.Start();

            ShowWindow();
        }

        private void Tick(object sender, EventArgs e)
        {
            mem.Poll();
            var st = mem.State;
            if (st.Playing)
            {
                store.AddSecond(DateTime.Today);
                if (!string.IsNullOrEmpty(st.LevelName))
                {
                    store.AddLevelSecond(DateTime.Today, st.LevelName);
                    lastLevel = st.LevelName;
                }
                if (++saveCounter % 10 == 0) store.Save();
            }
            window.ReportState(st, lastLevel);
        }

        private void ShowWindow()
        {
            window.Show();
            window.Activate();
        }

        private void ExitApp()
        {
            window.SavePosition();
            store.Save();
            try { tray.Dispose(); } catch { }
            Shutdown();
        }
    }

    public class Store
    {
        private readonly Dictionary<string, long> seconds = new Dictionary<string, long>();
        private readonly Dictionary<string, Dictionary<string, long>> levels = new Dictionary<string, Dictionary<string, long>>();

        private static string FilePath
        {
            get
            {
                return System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "GDTimer", "daily.txt");
            }
        }

        private static string LevelsFile
        {
            get
            {
                return System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "GDTimer", "levels.txt");
            }
        }

        public Store()
        {
            try
            {
                if (!File.Exists(FilePath)) { }
                else
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var p = line.Split(',');
                    if (p.Length != 2) continue;
                    DateTime date;
                    long val;
                    if (DateTime.TryParseExact(p[0], "yyyy-MM-dd", CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out date)
                        && long.TryParse(p[1], out val)
                        && date >= new DateTime(2020, 1, 1) && date <= DateTime.Today)
                        seconds[p[0]] = val;
                }
            }
            catch { }

            try
            {
                if (!File.Exists(LevelsFile)) return;
                foreach (var line in File.ReadAllLines(LevelsFile))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var p = line.Split('\t');
                    if (p.Length != 3) continue;
                    DateTime date;
                    long val;
                    if (DateTime.TryParseExact(p[0], "yyyy-MM-dd", CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out date)
                        && long.TryParse(p[1], out val) && val >= 0
                        && date >= new DateTime(2020, 1, 1) && date <= DateTime.Today)
                    {
                        var name = p[2];
                        if (string.IsNullOrEmpty(name)) continue;
                        Dictionary<string, long> day;
                        if (!levels.TryGetValue(p[0], out day))
                        {
                            day = new Dictionary<string, long>();
                            levels[p[0]] = day;
                        }
                        long cur;
                        day.TryGetValue(name, out cur);
                        day[name] = cur + val;
                    }
                }
            }
            catch { }
        }

        public void AddSecond(DateTime day)
        {
            var k = day.ToString("yyyy-MM-dd");
            long cur;
            seconds.TryGetValue(k, out cur);
            seconds[k] = cur + 1;
        }

        public void AddLevelSecond(DateTime day, string level)
        {
            if (string.IsNullOrEmpty(level)) return;
            var k = day.ToString("yyyy-MM-dd");
            Dictionary<string, long> dayMap;
            if (!levels.TryGetValue(k, out dayMap))
            {
                dayMap = new Dictionary<string, long>();
                levels[k] = dayMap;
            }
            long cur;
            dayMap.TryGetValue(level, out cur);
            dayMap[level] = cur + 1;
        }

        public Dictionary<string, long> LevelsOn(DateTime day)
        {
            Dictionary<string, long> dayMap;
            if (levels.TryGetValue(day.ToString("yyyy-MM-dd"), out dayMap))
                return dayMap;
            return new Dictionary<string, long>();
        }

        public long Get(DateTime day)
        {
            long v;
            seconds.TryGetValue(day.ToString("yyyy-MM-dd"), out v);
            return v;
        }

        public long SecondsToday()
        {
            return Get(DateTime.Today);
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FilePath));
                File.WriteAllLines(FilePath,
                    seconds.Select(kvp => kvp.Key + "," + kvp.Value).ToArray());
            }
            catch { }

            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(LevelsFile));
                var lines = new List<string>();
                foreach (var day in levels)
                    foreach (var lv in day.Value)
                        if (lv.Value > 0)
                            lines.Add(day.Key + "\t" + lv.Value + "\t" + lv.Key);
                File.WriteAllLines(LevelsFile, lines.ToArray());
            }
            catch { }
        }
    }

    public struct GameState
    {
        public bool Running;
        public bool Playing;
        public bool Paused;
        public string LevelName;
    }

    public class GdMemory
    {
        private const string ProcessName = "GeometryDash";
        private const int GameManagerSingletonRva = 0x6C2ED8;
        private const int PlayLayerOffset = 0x208;
        private const int PlayLayerPausedOffset = 0x39EF;
        private const int LevelNameOffset = 0x158;
        private const string PlayLayerRtti = ".?AVPlayLayer@@";
        private const string GJGameLevelRtti = ".?AVGJGameLevel@@";

        private const int PROCESS_VM_READ = 0x0010;
        private const int PROCESS_QUERY_INFORMATION = 0x0400;

        private IntPtr handle = IntPtr.Zero;
        private int pid;
        private IntPtr moduleBase;
        private bool attached;
        private int levelOffset = -1;

        public GameState State { get; private set; }

        public void Poll()
        {
            var st = new GameState();
            try { PollCore(ref st); } catch { }
            State = st;
        }

        private void PollCore(ref GameState st)
        {
            if (!EnsureAttached()) return;
            st.Running = true;

            ulong gm = ReadU64(Add(moduleBase, GameManagerSingletonRva));
            if (gm == 0) return;

            ulong pl = ReadU64(Add((IntPtr)(long)gm, PlayLayerOffset));
            if (pl == 0) return;
            st.Playing = true;
            st.Paused = ReadU8(Add((IntPtr)(long)pl, PlayLayerPausedOffset)) != 0;

            if (levelOffset < 0)
                levelOffset = FindPointerOffset((IntPtr)(long)pl, 0x2000, GJGameLevelRtti);
            if (levelOffset >= 0)
            {
                ulong lvl = ReadU64(Add((IntPtr)(long)pl, levelOffset));
                if (lvl != 0) st.LevelName = ReadStdString(Add((IntPtr)(long)lvl, LevelNameOffset));
            }
        }

        private bool EnsureAttached()
        {
            if (attached)
            {
                try { if (!Process.GetProcessById(pid).HasExited) return true; } catch { }
                Detach();
            }
            return Attach();
        }

        private bool Attach()
        {
            try
            {
                var procs = Process.GetProcessesByName(ProcessName);
                if (procs.Length == 0) return false;
                var p = procs[0];
                IntPtr b;
                try { b = p.MainModule.BaseAddress; } catch { return false; }
                IntPtr h = OpenProcess(PROCESS_VM_READ | PROCESS_QUERY_INFORMATION, false, p.Id);
                if (h == IntPtr.Zero) return false;
                pid = p.Id;
                handle = h;
                moduleBase = b;
                attached = true;
                levelOffset = -1;
                return true;
            }
            catch { return false; }
        }

        private void Detach()
        {
            try { if (handle != IntPtr.Zero) CloseHandle(handle); } catch { }
            handle = IntPtr.Zero;
            attached = false;
        }

        private int FindPointerOffset(IntPtr obj, int size, string rtti)
        {
            for (int off = 0; off + 8 <= size; off += 8)
            {
                ulong p = ReadU64(Add(obj, off));
                if (p < 0x10000 || p > 0x7FFFFFFFFFFFUL) continue;
                ulong vtable = ReadU64((IntPtr)(long)p);
                if (vtable == 0) continue;
                var name = RttiName((IntPtr)(long)vtable);
                if (name == rtti) return off;
            }
            return -1;
        }

        private string RttiName(IntPtr vtable)
        {
            ulong colVal = ReadU64(Add(vtable, -8));
            if (colVal == 0) return null;
            IntPtr col = (IntPtr)(long)colVal;
            if (ReadU32(col) != 1) return null;
            uint tdRva = ReadU32(Add(col, 0xC));
            if (tdRva == 0) return null;
            return ReadCString(Add(Add(moduleBase, tdRva), 0x10), 64);
        }

        private string ReadStdString(IntPtr s)
        {
            var b = new byte[32];
            if (!ReadRaw(s, b, 32)) return null;
            ulong size = BitConverter.ToUInt64(b, 16);
            ulong cap = BitConverter.ToUInt64(b, 24);
            if (size == 0) return "";
            if (size > 512) size = 512;
            if (cap < 16 || cap > 4096)
                return Encoding.UTF8.GetString(b, 0, (int)size);
            ulong ptr = BitConverter.ToUInt64(b, 0);
            if (ptr == 0) return "";
            return ReadCString((IntPtr)(long)ptr, (int)size);
        }

        private string ReadCString(IntPtr addr, int max)
        {
            var b = new byte[max];
            if (!ReadRaw(addr, b, max)) return null;
            int n = 0;
            while (n < max && b[n] != 0) n++;
            return Encoding.UTF8.GetString(b, 0, n);
        }

        private ulong ReadU64(IntPtr addr)
        {
            var b = new byte[8];
            if (!ReadRaw(addr, b, 8)) return 0;
            return BitConverter.ToUInt64(b, 0);
        }

        private uint ReadU32(IntPtr addr)
        {
            var b = new byte[4];
            if (!ReadRaw(addr, b, 4)) return 0;
            return BitConverter.ToUInt32(b, 0);
        }

        private byte ReadU8(IntPtr addr)
        {
            var b = new byte[1];
            if (!ReadRaw(addr, b, 1)) return 0;
            return b[0];
        }

        private bool ReadRaw(IntPtr addr, byte[] buf, int size)
        {
            if (!attached || handle == IntPtr.Zero) return false;
            IntPtr read;
            return ReadProcessMemory(handle, addr, buf, size, out read) && read.ToInt64() == size;
        }

        private static IntPtr Add(IntPtr p, long off)
        {
            return new IntPtr(p.ToInt64() + off);
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(int access, bool inherit, int pid);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress,
            [Out] byte[] lpBuffer, int dwSize, out IntPtr lpNumberOfBytesRead);
    }

    public class Tray : IDisposable
    {
        private WF.NotifyIcon icon;

        public event Action Open;
        public event Action Exit;

        public Tray()
        {
            icon = new WF.NotifyIcon { Text = "GDTimer", Visible = true };
            try
            {
                icon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(
                    System.Reflection.Assembly.GetExecutingAssembly().Location);
            }
            catch
            {
                icon.Icon = System.Drawing.SystemIcons.Application;
            }
            var menu = new WF.ContextMenuStrip();
            menu.Items.Add("Открыть", null, (s, e) => { if (Open != null) Open(); });
            menu.Items.Add("Выход", null, (s, e) => { if (Exit != null) Exit(); });
            icon.ContextMenuStrip = menu;
            icon.MouseDoubleClick += (s, e) => { if (e.Button == WF.MouseButtons.Left && Open != null) Open(); };
        }

        public void Dispose()
        {
            icon.Visible = false;
            icon.Dispose();
        }
    }

    public class MainWindow : Window
    {
        private const string BgHex = "#0F1115";
        private const string CardBorderHex = "#262B33";
        private const string BorderHex = "#23262C";
        private const string TextHex = "#E7E9ED";
        private const string MutedHex = "#8B929E";
        private const string FaintHex = "#5A606B";
        private const string AccentHex = "#74A4F4";
        private const string GreenHex = "#3FB96F";
        private const string AmberHex = "#E0A44A";
        private const string GreyDotHex = "#3A3E46";
        private static readonly string[] LevelHex = { "#14171C", "#1A2438", "#223554", "#2A4470", "#32589B", "#3A6BC4" };
        private static readonly string[] CellTextHex = { "#6B7280", "#9DB4E0", "#B4C7EA", "#CDDAF2", "#E2EAF8", "#F0F4FB" };

        private static readonly string[] Months = { "Январь", "Февраль", "Март", "Апрель", "Май", "Июнь", "Июль", "Август", "Сентябрь", "Октябрь", "Ноябрь", "Декабрь" };
        private static readonly string[] MonthsGenitive = { "января", "февраля", "марта", "апреля", "мая", "июня", "июля", "августа", "сентября", "октября", "ноября", "декабря" };
        private static readonly string[] Weekdays = { "Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс" };

        private const double WindowWidth = 382;
        private const double ExpandedHeight = 700;
        private const double CollapsedHeight = 132;

        private static Brush B(string hex) { return (Brush)new BrushConverter().ConvertFromString(hex); }

        private readonly Store store;
        private DateTime viewMonth;
        private long lastRenderedMinute = -1;
        private string currentLevel;
        private string lastRenderedLevel;
        private bool collapsed;
        private bool loadingUi;

        private Ellipse statusDot;
        private TextBlock statusText, todayValue, monthLabel;
        private Grid calendarGrid;
        private StackPanel levelsPanel;
        private TextBlock levelsHeader;
        private CheckBox autorunCheck;
        private UIElement collapsiblePart;
        private Button pinButton, collapseButton;
        private Button hotkeyButton;
        private bool recording;
        private int hkMods = 0;
        private int hkVk = 0x77;
        private HwndSource hwndSource;
        private DispatcherTimer hintTimer;

        private const int HOTKEY_ID = 0xB009;
        private const int WM_HOTKEY = 0x0312;
        private const int MOD_ALT = 0x1;
        private const int MOD_CONTROL = 0x2;
        private const int MOD_SHIFT = 0x4;
        private const int MOD_WIN = 0x8;
        private const int MOD_NOREPEAT = 0x4000;

        public event Action QuitRequested;

        public MainWindow(Store store)
        {
            this.store = store;
            viewMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

            Title = "GDTimer";
            Width = WindowWidth;
            Height = ExpandedHeight;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            Background = Brushes.Transparent;

            Resources = (ResourceDictionary)XamlReader.Parse(StylesXaml);

            Content = BuildLayout();
            LoadSettings();
            hotkeyButton.Content = FormatHotkey();

            MouseLeftButtonDown += (s, e) =>
            {
                if (e.ButtonState == MouseButtonState.Pressed)
                {
                    try { DragMove(); } catch { }
                }
            };

            LoadPosition();

            Loaded += (s, e) =>
            {
                loadingUi = true;
                autorunCheck.IsChecked = AutorunEnabled();
                loadingUi = false;
                RefreshAll();
                InstallHotkeyHook();
            };
            Closing += (s, e) => { e.Cancel = true; SavePosition(); Hide(); };
        }

        private UIElement BuildLayout()
        {
            var card = new Border
            {
                CornerRadius = new CornerRadius(16),
                Background = B(BgHex),
                BorderBrush = B(CardBorderHex),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(16),
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    Opacity = 0.5,
                    BlurRadius = 20,
                    ShadowDepth = 2,
                    Direction = 270
                }
            };

            var root = new DockPanel { Margin = new Thickness(18, 14, 18, 16) };

            var footer = new StackPanel();
            var hotkeyRow = new DockPanel { Margin = new Thickness(0, 12, 0, 0), LastChildFill = false };
            hotkeyRow.Children.Add(new TextBlock
            {
                Text = "Горячая клавиша",
                FontSize = 11,
                Foreground = B(MutedHex),
                VerticalAlignment = VerticalAlignment.Center
            });
            hotkeyButton = new Button { VerticalAlignment = VerticalAlignment.Center };
            hotkeyButton.Click += (s, e) => StartRecording();
            DockPanel.SetDock(hotkeyButton, Dock.Right);
            hotkeyRow.Children.Add(hotkeyButton);
            var controls = new DockPanel { Margin = new Thickness(0, 12, 0, 0), LastChildFill = false };
            autorunCheck = new CheckBox
            {
                Content = "Запускать с Windows",
                VerticalAlignment = VerticalAlignment.Center
            };
            autorunCheck.Checked += (s, e) => { if (!loadingUi) SetAutorun(true); };
            autorunCheck.Unchecked += (s, e) => { if (!loadingUi) SetAutorun(false); };
            var quit = new Button
            {
                Content = "Выход",
                VerticalAlignment = VerticalAlignment.Center
            };
            quit.Click += (s, e) => { if (QuitRequested != null) QuitRequested(); };
            controls.Children.Add(autorunCheck);
            DockPanel.SetDock(quit, Dock.Right);
            controls.Children.Add(quit);
            footer.Children.Add(hotkeyRow);
            footer.Children.Add(controls);
            root.Children.Add(footer);
            DockPanel.SetDock(footer, Dock.Bottom);

            var body = new StackPanel();

            var header = new DockPanel { LastChildFill = false };
            var headerButtons = new StackPanel { Orientation = Orientation.Horizontal };
            pinButton = HeaderButton("ПОВЕРХ", ToggleTopmost, AccentHex);
            collapseButton = HeaderButton("—", ToggleCollapse, null);
            var hideButton = HeaderButton("\u2715", (s, e) => Hide(), null);
            headerButtons.Children.Add(pinButton);
            headerButtons.Children.Add(collapseButton);
            headerButtons.Children.Add(hideButton);
            DockPanel.SetDock(headerButtons, Dock.Right);
            header.Children.Add(headerButtons);
            body.Children.Add(header);

            var statusRow = new DockPanel { Margin = new Thickness(2, 14, 2, 0), LastChildFill = false };
            var dotAndText = new StackPanel { Orientation = Orientation.Horizontal };
            statusDot = new Ellipse { Width = 8, Height = 8, Fill = B(GreyDotHex), VerticalAlignment = VerticalAlignment.Center };
            statusText = new TextBlock
            {
                FontSize = 12,
                Foreground = B(MutedHex),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 172
            };
            dotAndText.Children.Add(statusDot);
            dotAndText.Children.Add(statusText);
            statusRow.Children.Add(dotAndText);
            var todayBlock = new StackPanel { Orientation = Orientation.Horizontal };
            DockPanel.SetDock(todayBlock, Dock.Right);
            todayBlock.Children.Add(new TextBlock
            {
                Text = "СЕГОДНЯ",
                FontSize = 9,
                Foreground = B(FaintHex),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });
            todayValue = new TextBlock
            {
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = B(TextHex),
                VerticalAlignment = VerticalAlignment.Center
            };
            todayBlock.Children.Add(todayValue);
            statusRow.Children.Add(todayBlock);
            body.Children.Add(statusRow);

            var collapsible = new StackPanel();

            var navRow = new DockPanel { Margin = new Thickness(2, 14, 2, 0), LastChildFill = false };
            monthLabel = new TextBlock
            {
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = B(TextHex),
                VerticalAlignment = VerticalAlignment.Center
            };
            navRow.Children.Add(monthLabel);
            var nav = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            nav.Children.Add(NavButton("\u2039", () => Go(-1), 28));
            nav.Children.Add(NavButton("\u203A", () => Go(1), 28));
            nav.Children.Add(NavButton("Текущий", () => Go(0), 62));
            DockPanel.SetDock(nav, Dock.Right);
            navRow.Children.Add(nav);
            collapsible.Children.Add(navRow);

            var dowGrid = new UniformGrid { Columns = 7, Margin = new Thickness(0, 12, 0, 0) };
            foreach (var w in Weekdays)
            {
                dowGrid.Children.Add(new TextBlock
                {
                    Text = w,
                    FontSize = 10,
                    Foreground = B(FaintHex),
                    HorizontalAlignment = HorizontalAlignment.Center
                });
            }
            collapsible.Children.Add(dowGrid);

            calendarGrid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            for (int i = 0; i < 7; i++)
                calendarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            collapsible.Children.Add(calendarGrid);

            levelsHeader = new TextBlock
            {
                Text = "Уровни сегодня",
                FontSize = 10,
                Foreground = B(FaintHex),
                Margin = new Thickness(2, 14, 0, 0)
            };
            collapsible.Children.Add(levelsHeader);
            levelsPanel = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            collapsible.Children.Add(levelsPanel);

            collapsiblePart = collapsible;
            body.Children.Add(collapsible);

            root.Children.Add(body);
            card.Child = root;
            return card;
        }

        private Button HeaderButton(string text, RoutedEventHandler onClick, string activeHex)
        {
            var b = new Button
            {
                Content = text,
                Height = 24,
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            if (activeHex != null) b.Foreground = B(activeHex);
            b.Click += onClick;
            return b;
        }

        private Button NavButton(string text, Action action, double width)
        {
            var b = new Button
            {
                Content = text,
                Width = width,
                Height = 24,
                Margin = new Thickness(6, 0, 0, 0),
                FontSize = width > 40 ? 10 : 13
            };
            b.Click += (s, e) => action();
            return b;
        }

        private void ToggleTopmost(object sender, RoutedEventArgs e)
        {
            Topmost = !Topmost;
            pinButton.Foreground = Topmost ? B(AccentHex) : B(MutedHex);
        }

        private void ToggleCollapse(object sender, RoutedEventArgs e)
        {
            collapsed = !collapsed;
            collapsiblePart.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
            collapseButton.Content = collapsed ? "+" : "—";
            FitHeight();
        }

        public void ReportState(GameState st, string lastLevel)
        {
            if (!string.IsNullOrEmpty(lastLevel)) currentLevel = lastLevel;

            if (!st.Running)
            {
                statusDot.Fill = B(GreyDotHex);
                statusText.Text = "Geometry Dash не запущена";
            }
            else if (st.Playing && !st.Paused)
            {
                statusDot.Fill = B(GreenHex);
                statusText.Text = string.IsNullOrEmpty(st.LevelName) ? "В уровне" : "В уровне: " + st.LevelName;
            }
            else if (st.Playing)
            {
                statusDot.Fill = B(AmberHex);
                statusText.Text = string.IsNullOrEmpty(st.LevelName) ? "Пауза" : "Пауза: " + st.LevelName;
            }
            else
            {
                statusDot.Fill = B(GreyDotHex);
                statusText.Text = "В меню";
            }

            var secs = store.SecondsToday();
            todayValue.Text = secs > 0 ? FmtWords(secs) : "нет данных";
            var minute = secs / 60;
            if (minute != lastRenderedMinute || currentLevel != lastRenderedLevel)
            {
                lastRenderedMinute = minute;
                lastRenderedLevel = currentLevel;
                RefreshAll();
            }
        }

        private static string FmtClock(long total)
        {
            var h = total / 3600;
            var m = (total % 3600) / 60;
            var s = total % 60;
            if (h > 0) return string.Format("{0}:{1:00}:{2:00}", h, m, s);
            return string.Format("{0}:{1:00}", m, s);
        }

        private static string FmtWords(long total)
        {
            var h = total / 3600;
            var m = (total % 3600) / 60;
            var s = total % 60;
            if (h > 0) return h + " ч " + m.ToString("00") + " мин";
            if (m > 0) return m + " мин " + s.ToString("00") + " с";
            return s + " с";
        }

        private static string FmtShort(long total)
        {
            var h = total / 3600;
            var m = (total % 3600) / 60;
            if (h > 0) return h + " ч " + m.ToString("00") + " м";
            if (m > 0) return m + " мин";
            return "< 1 мин";
        }

        private void Go(int delta)
        {
            if (delta == 0)
                viewMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            else
                viewMonth = viewMonth.AddMonths(delta);
            RefreshAll();
        }

        public void RefreshAll()
        {
            monthLabel.Text = Months[viewMonth.Month - 1] + " " + viewMonth.Year;
            RenderCalendar();
            UpdateLevels();
            FitHeight();
        }

        private void FitHeight()
        {
            double target;
            if (collapsed)
            {
                target = CollapsedHeight;
            }
            else
            {
                var fe = Content as FrameworkElement;
                if (fe == null) return;
                try
                {
                    fe.Measure(new Size(Width, double.PositiveInfinity));
                    target = Math.Ceiling(fe.DesiredSize.Height);
                }
                catch { return; }
                if (target < CollapsedHeight) return;
            }

            var oldH = Height;
            Height = target;
            if (!double.IsNaN(Top) && oldH > 0 && Math.Abs(oldH - target) > 0.5)
            {
                Top += oldH - target;
                var wa = SystemParameters.WorkArea;
                if (Top < wa.Top) Top = wa.Top;
            }
        }

        private void UpdateLevels()
        {
            levelsPanel.Children.Clear();
            var today = DateTime.Today;
            var list = new List<KeyValuePair<string, long>>();
            foreach (var kv in store.LevelsOn(today))
                if (kv.Value > 0) list.Add(kv);
            list.Sort((a, b) => b.Value.CompareTo(a.Value));

            if (list.Count == 0)
            {
                levelsHeader.Visibility = Visibility.Collapsed;
                levelsPanel.Visibility = Visibility.Collapsed;
                return;
            }
            levelsHeader.Visibility = Visibility.Visible;
            levelsPanel.Visibility = Visibility.Visible;

            int shown = 0;
            foreach (var kv in list)
            {
                if (shown >= 8) break;
                shown++;

                var row = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 1, 0, 1) };
                var name = new TextBlock
                {
                    Text = kv.Key,
                    FontSize = 11.5,
                    Foreground = kv.Key == currentLevel ? B(TextHex) : B(MutedHex),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center
                };
                var time = new TextBlock
                {
                    Text = FmtShort(kv.Value),
                    FontSize = 11.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = kv.Key == currentLevel ? B(AccentHex) : B(TextHex),
                    VerticalAlignment = VerticalAlignment.Center
                };
                DockPanel.SetDock(time, Dock.Right);
                row.Children.Add(name);
                row.Children.Add(time);
                levelsPanel.Children.Add(row);
            }
        }

        private void SetDayTooltip(Border border, DateTime date, long secs)
        {
            var list = new List<KeyValuePair<string, long>>();
            foreach (var kv in store.LevelsOn(date))
                if (kv.Value > 0) list.Add(kv);
            if (list.Count == 0 && secs <= 0) return;
            list.Sort((a, b) => b.Value.CompareTo(a.Value));

            var stack = new StackPanel { MinWidth = 190, MaxWidth = 300 };

            var head = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 0, 0, 7) };
            var dateTxt = new TextBlock
            {
                Text = date.Day + " " + MonthsGenitive[date.Month - 1],
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = B(TextHex),
                VerticalAlignment = VerticalAlignment.Center
            };
            var totTxt = new TextBlock
            {
                Text = FmtWords(secs),
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = B(AccentHex),
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(totTxt, Dock.Right);
            head.Children.Add(dateTxt);
            head.Children.Add(totTxt);
            stack.Children.Add(head);

            stack.Children.Add(new Border { Height = 1, Background = B(BorderHex), Margin = new Thickness(0, 0, 0, 6) });

            foreach (var kv in list)
            {
                var row = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 2, 0, 2) };
                var name = new TextBlock
                {
                    Text = kv.Key,
                    FontSize = 11.5,
                    Foreground = B(MutedHex),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center
                };
                var time = new TextBlock
                {
                    Text = FmtShort(kv.Value),
                    FontSize = 11.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = B(TextHex),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(16, 0, 0, 0)
                };
                DockPanel.SetDock(time, Dock.Right);
                row.Children.Add(name);
                row.Children.Add(time);
                stack.Children.Add(row);
            }

            border.ToolTip = stack;
            ToolTipService.SetInitialShowDelay(border, 150);
            ToolTipService.SetShowDuration(border, 60000);
            ToolTipService.SetPlacement(border, PlacementMode.Mouse);
        }

        private void RenderCalendar()
        {
            calendarGrid.Children.Clear();
            calendarGrid.RowDefinitions.Clear();

            var first = new DateTime(viewMonth.Year, viewMonth.Month, 1);
            int lead = ((int)first.DayOfWeek + 6) % 7;
            int days = DateTime.DaysInMonth(viewMonth.Year, viewMonth.Month);
            var today = DateTime.Today;

            long max = 0;
            for (int d = 1; d <= days; d++)
                max = Math.Max(max, store.Get(new DateTime(viewMonth.Year, viewMonth.Month, d)));

            int rows = (lead + days + 6) / 7;
            for (int r = 0; r < rows; r++)
                calendarGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(66) });

            for (int i = 0; i < lead; i++)
            {
                var empty = new Border { Background = Brushes.Transparent, Margin = new Thickness(3) };
                Grid.SetRow(empty, 0);
                Grid.SetColumn(empty, i);
                calendarGrid.Children.Add(empty);
            }

            for (int d = 1; d <= days; d++)
            {
                var date = new DateTime(viewMonth.Year, viewMonth.Month, d);
                long secs = store.Get(date);
                int slot = lead + d - 1;
                int row = slot / 7;
                int col = slot % 7;

                int idx;
                if (secs <= 0) idx = 0;
                else
                {
                    var m = Math.Max(max, Math.Max(secs, 60));
                    idx = Math.Max(1, Math.Min(5, (int)Math.Floor(Math.Log(Math.Max(secs, 1.0)) / Math.Log((double)m) * 5.0) + 1));
                }

                var border = new Border
                {
                    CornerRadius = new CornerRadius(10),
                    Background = B(LevelHex[idx]),
                    BorderBrush = date == today ? B(AccentHex) : null,
                    BorderThickness = date == today ? new Thickness(1) : new Thickness(0),
                    Margin = new Thickness(3)
                };
                SetDayTooltip(border, date, secs);

                var panel = new Grid();
                panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var num = new TextBlock
                {
                    Text = d.ToString(),
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = idx == 0 ? B(MutedHex) : B(CellTextHex[idx]),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 4, 0, 0)
                };
                Grid.SetRow(num, 0);
                panel.Children.Add(num);

                if (secs > 0)
                {
                    string topLevel = null;
                    long best = -1;
                    foreach (var lv in store.LevelsOn(date))
                        if (lv.Value > best) { best = lv.Value; topLevel = lv.Key; }

                    if (!string.IsNullOrEmpty(topLevel))
                    {
                        var nm = new TextBlock
                        {
                            Text = topLevel,
                            FontSize = 8,
                            Foreground = B(CellTextHex[idx]),
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                            TextAlignment = TextAlignment.Center,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                            VerticalAlignment = VerticalAlignment.Center,
                            Margin = new Thickness(2, 0, 2, 0)
                        };
                        Grid.SetRow(nm, 1);
                        panel.Children.Add(nm);
                    }

                    var t = new TextBlock
                    {
                        Text = FmtShort(secs),
                        FontSize = 9.5,
                        Foreground = B(CellTextHex[idx]),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Bottom,
                        Margin = new Thickness(0, 0, 0, 6)
                    };
                    Grid.SetRow(t, 2);
                    panel.Children.Add(t);
                }

                border.Child = panel;
                Grid.SetRow(border, row);
                Grid.SetColumn(border, col);
                calendarGrid.Children.Add(border);
            }
        }

        private static string PosFile
        {
            get
            {
                return System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "GDTimer", "pos.txt");
            }
        }

        public void SavePosition()
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PosFile));
                File.WriteAllText(PosFile, ((int)Left).ToString() + "," + ((int)Top).ToString());
            }
            catch { }
        }

        private void LoadPosition()
        {
            double left = double.NaN, top = double.NaN;
            try
            {
                if (File.Exists(PosFile))
                {
                    var p = File.ReadAllText(PosFile).Split(',');
                    int l, t;
                    if (p.Length == 2 && int.TryParse(p[0], out l) && int.TryParse(p[1], out t))
                    {
                        left = l;
                        top = t;
                    }
                }
            }
            catch { }

            var area = SystemParameters.WorkArea;
            if (double.IsNaN(left) || double.IsNaN(top)
                || left < 0 || top < 0
                || left + Width > area.Right + 10 || top + Height > area.Bottom + 10)
            {
                left = area.Right - Width - 12;
                top = area.Bottom - Height - 12;
            }
            Left = left;
            Top = top;
        }

        private static bool AutorunEnabled()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                    return k != null && k.GetValue("GDTimer") != null;
            }
            catch { return false; }
        }

        private static void SetAutorun(bool enable)
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                {
                    if (enable)
                        k.SetValue("GDTimer", "\"" + System.Reflection.Assembly.GetExecutingAssembly().Location + "\"");
                    else
                    {
                        try { k.DeleteValue("GDTimer", false); } catch { }
                    }
                }
            }
            catch { }
        }

        private void InstallHotkeyHook()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            hwndSource = HwndSource.FromHwnd(hwnd);
            if (hwndSource != null) hwndSource.AddHook(WndProc);
            PreviewKeyDown += OnPreviewKeyDown;
            if (!RegisterHotKey(hwnd, HOTKEY_ID, (uint)(hkMods | MOD_NOREPEAT), hkVk))
                ShowHint("Сочетание занято");
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                ToggleVisible();
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void ToggleVisible()
        {
            if (IsVisible) Hide();
            else
            {
                Show();
                Activate();
            }
        }

        private void StartRecording()
        {
            if (hintTimer != null) hintTimer.Stop();
            recording = true;
            hotkeyButton.Content = "Нажмите клавишу · Esc — отмена";
            hotkeyButton.Foreground = B(AccentHex);
        }

        private void StopRecording()
        {
            recording = false;
            hotkeyButton.Foreground = B(MutedHex);
            hotkeyButton.Content = FormatHotkey();
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!recording) return;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Escape)
            {
                e.Handled = true;
                StopRecording();
                return;
            }
            if (key == Key.LeftCtrl || key == Key.RightCtrl
                || key == Key.LeftAlt || key == Key.RightAlt
                || key == Key.LeftShift || key == Key.RightShift
                || key == Key.LWin || key == Key.RWin)
                return;

            var mods = Keyboard.Modifiers;
            e.Handled = true;

            int m = 0;
            if ((mods & ModifierKeys.Alt) != 0) m |= MOD_ALT;
            if ((mods & ModifierKeys.Control) != 0) m |= MOD_CONTROL;
            if ((mods & ModifierKeys.Shift) != 0) m |= MOD_SHIFT;
            if ((mods & ModifierKeys.Windows) != 0) m |= MOD_WIN;
            int vk = KeyInterop.VirtualKeyFromKey(key);
            if (vk != 0) ApplyHotkey(m, vk);
        }

        private void ApplyHotkey(int mods, int vk)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            UnregisterHotKey(hwnd, HOTKEY_ID);
            if (RegisterHotKey(hwnd, HOTKEY_ID, (uint)(mods | MOD_NOREPEAT), vk))
            {
                hkMods = mods;
                hkVk = vk;
                SaveSettings();
                StopRecording();
            }
            else
            {
                RegisterHotKey(hwnd, HOTKEY_ID, (uint)(hkMods | MOD_NOREPEAT), hkVk);
                StopRecording();
                ShowHint("Сочетание занято");
            }
        }

        private void ShowHint(string text)
        {
            hotkeyButton.Content = text;
            hotkeyButton.Foreground = B("#E06C6C");
            if (hintTimer == null)
            {
                hintTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.6) };
                hintTimer.Tick += (s, e) =>
                {
                    hintTimer.Stop();
                    if (recording) return;
                    hotkeyButton.Content = FormatHotkey();
                    hotkeyButton.Foreground = B(MutedHex);
                };
            }
            hintTimer.Stop();
            hintTimer.Start();
        }

        private string FormatHotkey()
        {
            var parts = new List<string>();
            if ((hkMods & MOD_CONTROL) != 0) parts.Add("Ctrl");
            if ((hkMods & MOD_ALT) != 0) parts.Add("Alt");
            if ((hkMods & MOD_SHIFT) != 0) parts.Add("Shift");
            if ((hkMods & MOD_WIN) != 0) parts.Add("Win");
            parts.Add(KeyName(hkVk));
            return string.Join(" + ", parts.ToArray());
        }

        private static string KeyName(int vk)
        {
            var n = KeyInterop.KeyFromVirtualKey(vk).ToString();
            if (n.Length == 2 && n[0] == 'D' && n[1] >= '0' && n[1] <= '9') return n[1].ToString();
            if (n.StartsWith("NumPad")) return "Num " + n.Substring(6);
            return n;
        }

        private static string SettingsFile
        {
            get
            {
                return System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "GDTimer", "settings.txt");
            }
        }

        private void SaveSettings()
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SettingsFile));
                File.WriteAllText(SettingsFile, "hotkey=" + hkMods + "," + hkVk);
            }
            catch { }
        }

        private void LoadSettings()
        {
            try
            {
                if (!File.Exists(SettingsFile)) return;
                foreach (var line in File.ReadAllLines(SettingsFile))
                {
                    if (!line.StartsWith("hotkey=")) continue;
                    var p = line.Substring(7).Split(',');
                    int m, v;
                    if (p.Length == 2 && int.TryParse(p[0], out m) && int.TryParse(p[1], out v) && v > 0)
                    {
                        hkMods = m;
                        hkVk = v;
                    }
                }
            }
            catch { }
        }

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, int vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const string StylesXaml = @"<ResourceDictionary
            xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
            xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
            <Style TargetType=""Button"">
                <Setter Property=""Background"" Value=""Transparent""/>
                <Setter Property=""Foreground"" Value=""#8B929E""/>
                <Setter Property=""FontSize"" Value=""10""/>
                <Setter Property=""Padding"" Value=""9,4""/>
                <Setter Property=""Cursor"" Value=""Hand""/>
                <Setter Property=""Template"">
                    <Setter.Value>
                        <ControlTemplate TargetType=""Button"">
                            <Border x:Name=""bd"" Background=""{TemplateBinding Background}"" CornerRadius=""7"" Padding=""{TemplateBinding Padding}"">
                                <ContentPresenter HorizontalAlignment=""Center"" VerticalAlignment=""Center"" Margin=""0,1,0,0""/>
                            </Border>
                            <ControlTemplate.Triggers>
                                <Trigger Property=""IsMouseOver"" Value=""True"">
                                    <Setter TargetName=""bd"" Property=""Background"" Value=""#1E242E""/>
                                    <Setter TargetName=""bd"" Property=""TextElement.Foreground"" Value=""#E7E9ED""/>
                                </Trigger>
                            </ControlTemplate.Triggers>
                        </ControlTemplate>
                    </Setter.Value>
                </Setter>
            </Style>
            <Style TargetType=""CheckBox"">
                <Setter Property=""Foreground"" Value=""#8B929E""/>
                <Setter Property=""FontSize"" Value=""11""/>
                <Setter Property=""Cursor"" Value=""Hand""/>
                <Setter Property=""Template"">
                    <Setter.Value>
                        <ControlTemplate TargetType=""CheckBox"">
                            <StackPanel Orientation=""Horizontal"" Background=""Transparent"">
                                <Grid Width=""15"" Height=""15"" VerticalAlignment=""Center"">
                                    <Border x:Name=""box"" CornerRadius=""4"" Background=""#151920"" BorderBrush=""#2A2F38"" BorderThickness=""1""/>
                                    <Path x:Name=""mark"" Data=""M3 7.5 L6 10.5 L12 3.5"" Stroke=""#74A4F4"" StrokeThickness=""2"" Visibility=""Collapsed""/>
                                </Grid>
                                <ContentPresenter Margin=""8,0,0,0"" VerticalAlignment=""Center"" RecognizesAccessKey=""False""/>
                            </StackPanel>
                            <ControlTemplate.Triggers>
                                <Trigger Property=""IsChecked"" Value=""True"">
                                    <Setter TargetName=""mark"" Property=""Visibility"" Value=""Visible""/>
                                    <Setter TargetName=""box"" Property=""BorderBrush"" Value=""#74A4F4""/>
                                </Trigger>
                                <Trigger Property=""IsMouseOver"" Value=""True"">
                                    <Setter TargetName=""box"" Property=""Background"" Value=""#1C212B""/>
                                    <Setter Property=""Foreground"" Value=""#B9C0CB""/>
                                </Trigger>
                            </ControlTemplate.Triggers>
                        </ControlTemplate>
                    </Setter.Value>
                </Setter>
            </Style>
            <Style TargetType=""ToolTip"">
                <Setter Property=""OverridesDefaultStyle"" Value=""True""/>
                <Setter Property=""Foreground"" Value=""#E7E9ED""/>
                <Setter Property=""Template"">
                    <Setter.Value>
                        <ControlTemplate TargetType=""ToolTip"">
                            <Border Background=""#12151B"" BorderBrush=""#2A2F38"" BorderThickness=""1"" CornerRadius=""10"" Padding=""12,10"">
                                <ContentPresenter/>
                            </Border>
                        </ControlTemplate>
                    </Setter.Value>
                </Setter>
            </Style>
        </ResourceDictionary>";
    }
}
