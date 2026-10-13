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
using System.Windows.Documents;
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
        private const string BgHex = "#0E1110";
        private const string SurfaceHex = "#161C19";
        private const string TrackHex = "#1C2420";
        private const string CardBorderHex = "#242A26";
        private const string BorderHex = "#212724";
        private const string TextHex = "#E8EDE9";
        private const string MutedHex = "#93A09A";
        private const string FaintHex = "#5C6963";
        private const string AccentHex = "#45D07C";
        private const string GreenHex = "#45D07C";
        private const string AmberHex = "#D8A657";
        private const string GreyDotHex = "#3A423D";
        private static readonly string[] LevelHex = { "#181D1A", "#1E2A22", "#254231", "#2E5C3F", "#3A7A50", "#4AA066" };
        private static readonly string[] CellTextHex = { "#6E7A73", "#A7C4B2", "#BCD6C6", "#D2E6D9", "#E6F2EA", "#F3FAF5" };

        private static readonly string[] Months = { "Январь", "Февраль", "Март", "Апрель", "Май", "Июнь", "Июль", "Август", "Сентябрь", "Октябрь", "Ноябрь", "Декабрь" };
        private static readonly string[] MonthsGenitive = { "января", "февраля", "марта", "апреля", "мая", "июня", "июля", "августа", "сентября", "октября", "ноября", "декабря" };
        private static readonly string[] Weekdays = { "Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс" };

        private const double WindowWidth = 382;
        private const double ExpandedHeight = 700;
        private const double FixedHeight = 660;
        private const double CollapsedHeight = 132;

        private static Brush B(string hex) { return (Brush)new BrushConverter().ConvertFromString(hex); }

        private static TextBlock Tabular(TextBlock tb)
        {
            Typography.SetNumeralAlignment(tb, FontNumeralAlignment.Tabular);
            return tb;
        }

        private static FontFamily LoadAppFont()
        {
            try
            {
                var dir = System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),
                    "fonts");
                if (System.IO.Directory.Exists(dir) && System.IO.Directory.GetFiles(dir, "*.ttf").Length > 0)
                {
                    foreach (var fam in Fonts.GetFontFamilies(new Uri(dir + System.IO.Path.DirectorySeparatorChar)))
                    {
                        foreach (var name in fam.FamilyNames.Values)
                        {
                            if (!string.IsNullOrEmpty(name) && name.IndexOf("Comfortaa", StringComparison.OrdinalIgnoreCase) >= 0)
                                return fam;
                        }
                    }
                }
            }
            catch { }
            return new FontFamily("Trebuchet MS");
        }

        private readonly Store store;
        private DateTime viewMonth;
        private long lastRenderedMinute = -1;
        private string currentLevel;
        private string lastRenderedLevel;
        private bool loadingUi;

        private TextBlock statusText, todayValue, monthLabel;
        private Border statusPill;
        private Grid calendarGrid;
        private StackPanel levelsPanel;
        private TextBlock levelsHeader;
        private CheckBox autorunCheck;
        private Button collapseButton;
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
            FontFamily = LoadAppFont();
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
                CornerRadius = new CornerRadius(24),
                Background = new LinearGradientBrush(Color.FromRgb(0x14, 0x19, 0x17), Color.FromRgb(0x09, 0x0C, 0x0B), 90.0),
                BorderBrush = B(CardBorderHex),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(16),
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    Opacity = 0.55,
                    BlurRadius = 28,
                    ShadowDepth = 4,
                    Direction = 270
                }
            };

            var root = new StackPanel { Margin = new Thickness(16, 12, 16, 12) };

            var header = new DockPanel { LastChildFill = false };
            statusPill = new Border
            {
                CornerRadius = new CornerRadius(20),
                Background = B(TrackHex),
                BorderBrush = B(CardBorderHex),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(11, 6, 13, 6),
                VerticalAlignment = VerticalAlignment.Center
            };
            var statusInner = new StackPanel { Orientation = Orientation.Horizontal };
            statusText = new TextBlock
            {
                FontSize = 11,
                Foreground = B(MutedHex),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 200
            };
            statusInner.Children.Add(statusText);
            statusPill.Child = statusInner;
            header.Children.Add(statusPill);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            collapseButton = IconButton("—", (s, e) => Hide());
            var hideButton = IconButton("\u2715", (s, e) => Hide());
            actions.Children.Add(collapseButton);
            actions.Children.Add(hideButton);
            var actionsPill = new Border
            {
                CornerRadius = new CornerRadius(20),
                Background = B(SurfaceHex),
                BorderBrush = B(BorderHex),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(2, 2, 6, 2),
                VerticalAlignment = VerticalAlignment.Center
            };
            actionsPill.Child = actions;
            DockPanel.SetDock(actionsPill, Dock.Right);
            header.Children.Add(actionsPill);
            root.Children.Add(header);

            var todayCard = new Border
            {
                CornerRadius = new CornerRadius(16),
                Background = B(SurfaceHex),
                BorderBrush = B(CardBorderHex),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 10, 0, 0)
            };
            var heroGrid = new Grid();
            var glow = new Ellipse
            {
                Width = 230,
                Height = 110,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -30, 0, 0),
                Opacity = 0.5,
                Fill = new RadialGradientBrush(Color.FromArgb(0x30, 0x45, 0xD0, 0x7C), Color.FromArgb(0x00, 0x45, 0xD0, 0x7C))
            };
            heroGrid.Children.Add(glow);
            var todayStack = new StackPanel();
            todayStack.Children.Add(SectionLabel("ПЛЕЙТАЙМ ЗА СЕГОДНЯ"));
            todayValue = Tabular(new TextBlock
            {
                FontSize = 30,
                FontWeight = FontWeights.Bold,
                Foreground = B(AccentHex),
                Margin = new Thickness(0, 2, 0, 0)
            });
            todayStack.Children.Add(todayValue);
            heroGrid.Children.Add(todayStack);
            todayCard.Child = heroGrid;
            root.Children.Add(todayCard);

            var collapsible = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };

            var navRow = new DockPanel { LastChildFill = false };
            monthLabel = new TextBlock
            {
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = B(TextHex),
                VerticalAlignment = VerticalAlignment.Center
            };
            navRow.Children.Add(monthLabel);
            var nav = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            nav.Children.Add(GhostButton("\u2039", () => Go(-1)));
            nav.Children.Add(GhostButton("\u203A", () => Go(1)));
            var navPill = new Border
            {
                CornerRadius = new CornerRadius(20),
                Background = B(SurfaceHex),
                BorderBrush = B(BorderHex),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(2, 2, 6, 2),
                VerticalAlignment = VerticalAlignment.Center
            };
            navPill.Child = nav;
            DockPanel.SetDock(navPill, Dock.Right);
            navRow.Children.Add(navPill);
            collapsible.Children.Add(navRow);

            var dowGrid = new UniformGrid { Columns = 7, Margin = new Thickness(0, 8, 0, 0) };
            foreach (var w in Weekdays)
                dowGrid.Children.Add(new TextBlock
                {
                    Text = w,
                    FontSize = 9.5,
                    Foreground = B(FaintHex),
                    HorizontalAlignment = HorizontalAlignment.Center
                });
            collapsible.Children.Add(dowGrid);

            var calendarBoard = new Border
            {
                CornerRadius = new CornerRadius(16),
                Background = B(SurfaceHex),
                BorderBrush = B(BorderHex),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 6, 6, 4),
                Margin = new Thickness(0, 6, 0, 0)
            };
            calendarGrid = new Grid();
            for (int i = 0; i < 7; i++)
                calendarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            calendarBoard.Child = calendarGrid;
            collapsible.Children.Add(calendarBoard);

            levelsHeader = SectionLabel("УРОВНИ СЕГОДНЯ");
            levelsHeader.Margin = new Thickness(0, 10, 0, 0);
            collapsible.Children.Add(levelsHeader);
            levelsPanel = new StackPanel();
            var levelsScroll = new ScrollViewer
            {
                Height = 110,
                Margin = new Thickness(0, 4, 0, 0),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            levelsScroll.Content = levelsPanel;
            collapsible.Children.Add(levelsScroll);

            root.Children.Add(collapsible);

            root.Children.Add(Divider(new Thickness(0, 10, 0, 0)));

            var footer = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            var hotkeyRow = new DockPanel { LastChildFill = false };
            hotkeyRow.Children.Add(new TextBlock
            {
                Text = "Горячая клавиша",
                FontSize = 11,
                Foreground = B(MutedHex),
                VerticalAlignment = VerticalAlignment.Center
            });
            hotkeyButton = new Button { VerticalAlignment = VerticalAlignment.Center };
            hotkeyButton.Click += (s, e) => StartRecording();
            var hotkeyChip = new Border
            {
                CornerRadius = new CornerRadius(9),
                Background = B(TrackHex),
                BorderBrush = B(BorderHex),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 2, 8, 2),
                VerticalAlignment = VerticalAlignment.Center
            };
            hotkeyChip.Child = hotkeyButton;
            DockPanel.SetDock(hotkeyChip, Dock.Right);
            hotkeyRow.Children.Add(hotkeyChip);
            footer.Children.Add(hotkeyRow);

            var controls = new DockPanel { Margin = new Thickness(0, 6, 0, 0), LastChildFill = false };
            autorunCheck = new CheckBox
            {
                Content = "Запускать с Windows",
                VerticalAlignment = VerticalAlignment.Center
            };
            autorunCheck.Checked += (s, e) => { if (!loadingUi) SetAutorun(true); };
            autorunCheck.Unchecked += (s, e) => { if (!loadingUi) SetAutorun(false); };
            var quit = new Button { Content = "Выход", VerticalAlignment = VerticalAlignment.Center };
            quit.Click += (s, e) => { if (QuitRequested != null) QuitRequested(); };
            controls.Children.Add(autorunCheck);
            DockPanel.SetDock(quit, Dock.Right);
            controls.Children.Add(quit);
            footer.Children.Add(controls);
            root.Children.Add(footer);

            card.Child = root;
            return card;
        }

        private static TextBlock SectionLabel(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 9.5,
                FontWeight = FontWeights.Medium,
                Foreground = B(FaintHex)
            };
        }

        private static Border Divider(Thickness margin)
        {
            return new Border { Height = 1, Background = B(BorderHex), Margin = margin };
        }

        private Button IconButton(string text, RoutedEventHandler onClick)
        {
            var b = new Button
            {
                Content = text,
                Width = 24,
                Height = 24,
                Padding = new Thickness(0),
                Margin = new Thickness(4, 0, 0, 0),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            b.Click += onClick;
            return b;
        }

        private Button GhostButton(string text, Action action)
        {
            var b = new Button
            {
                Content = text,
                Height = 26,
                Padding = new Thickness(9, 0, 9, 0),
                Margin = new Thickness(4, 0, 0, 0),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            };
            b.Click += (s, e) => action();
            return b;
        }

        public void ReportState(GameState st, string lastLevel)
        {
            if (!string.IsNullOrEmpty(lastLevel)) currentLevel = lastLevel;

            if (!st.Running)
            {
                statusText.Text = "Geometry Dash не запущена";
            }
            else if (st.Playing && !st.Paused)
            {
                var nm = !string.IsNullOrEmpty(st.LevelName) ? st.LevelName : currentLevel;
                statusText.Text = "в " + (string.IsNullOrEmpty(nm) ? "уровне" : nm);
            }
            else if (st.Playing)
            {
                statusText.Text = "Пауза";
            }
            else
            {
                statusText.Text = "В меню";
            }

            var secs = store.SecondsToday();
            todayValue.Text = secs > 0 ? FmtWords(secs) : "0 мин";
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
            Height = FixedHeight;
            var wa = SystemParameters.WorkArea;
            Left = wa.Right - Width - 12;
            Top = wa.Bottom - FixedHeight - 12;
            if (Top < wa.Top) Top = wa.Top;
        }

        private void UpdateLevels()
        {
            levelsPanel.Children.Clear();
            var today = DateTime.Today;
            var list = new List<KeyValuePair<string, long>>();
            foreach (var kv in store.LevelsOn(today))
                if (kv.Value > 0) list.Add(kv);
            list.Sort((a, b) => b.Value.CompareTo(a.Value));

            if (list.Count == 0) return;

            foreach (var kv in list)
            {
                bool cur = kv.Key == currentLevel;

                var row = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 3, 0, 3) };
                var name = new TextBlock
                {
                    Text = kv.Key,
                    FontSize = 12,
                    Foreground = cur ? B(TextHex) : B(MutedHex),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center
                };
                var time = Tabular(new TextBlock
                {
                    Text = FmtShort(kv.Value),
                    FontSize = 11.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = cur ? B(AccentHex) : B(TextHex),
                    VerticalAlignment = VerticalAlignment.Center
                });
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
            var totTxt = Tabular(new TextBlock
            {
                Text = FmtWords(secs),
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = B(AccentHex),
                VerticalAlignment = VerticalAlignment.Center
            });
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
                var time = Tabular(new TextBlock
                {
                    Text = FmtShort(kv.Value),
                    FontSize = 11.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = B(TextHex),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(16, 0, 0, 0)
                });
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

            for (int r = 0; r < 6; r++)
                calendarGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });

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
                    BorderThickness = date == today ? new Thickness(1.25) : new Thickness(0),
                    Margin = new Thickness(3)
                };
                SetDayTooltip(border, date, secs);

                var panel = new Grid();

                var num = new TextBlock
                {
                    Text = d.ToString(),
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = idx == 0 ? B(MutedHex) : B(CellTextHex[idx]),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                panel.Children.Add(num);

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
                                    <Setter TargetName=""bd"" Property=""Background"" Value=""#1B221E""/>
                                    <Setter TargetName=""bd"" Property=""TextElement.Foreground"" Value=""#F3FAF5""/>
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
                                    <Border x:Name=""box"" CornerRadius=""4"" Background=""#171C19"" BorderBrush=""#2A322C"" BorderThickness=""1""/>
                                    <Path x:Name=""mark"" Data=""M3 7.5 L6 10.5 L12 3.5"" Stroke=""#45D07C"" StrokeThickness=""2"" Visibility=""Collapsed""/>
                                </Grid>
                                <ContentPresenter Margin=""8,0,0,0"" VerticalAlignment=""Center"" RecognizesAccessKey=""False""/>
                            </StackPanel>
                            <ControlTemplate.Triggers>
                                <Trigger Property=""IsChecked"" Value=""True"">
                                    <Setter TargetName=""mark"" Property=""Visibility"" Value=""Visible""/>
                                    <Setter TargetName=""box"" Property=""BorderBrush"" Value=""#45D07C""/>
                                </Trigger>
                                <Trigger Property=""IsMouseOver"" Value=""True"">
                                    <Setter TargetName=""box"" Property=""Background"" Value=""#1A211D""/>
                                    <Setter Property=""Foreground"" Value=""#C4CFC8""/>
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
                            <Border Background=""#141816"" BorderBrush=""#262E29"" BorderThickness=""1"" CornerRadius=""10"" Padding=""12,10"">
                                <ContentPresenter/>
                            </Border>
                        </ControlTemplate>
                    </Setter.Value>
                </Setter>
            </Style>
            <Style TargetType=""ScrollBar"">
                <Setter Property=""Background"" Value=""Transparent""/>
                <Setter Property=""Width"" Value=""8""/>
                <Setter Property=""MinWidth"" Value=""8""/>
                <Setter Property=""Template"">
                    <Setter.Value>
                        <ControlTemplate TargetType=""ScrollBar"">
                            <Grid Background=""Transparent"">
                                <Track x:Name=""PART_Track"" IsDirectionReversed=""True"">
                                    <Track.DecreaseRepeatButton>
                                        <RepeatButton Command=""ScrollBar.PageUpCommand"" Background=""Transparent"" BorderThickness=""0"" Focusable=""False"">
                                            <RepeatButton.Template>
                                                <ControlTemplate TargetType=""RepeatButton"">
                                                    <Border Background=""Transparent""/>
                                                </ControlTemplate>
                                            </RepeatButton.Template>
                                        </RepeatButton>
                                    </Track.DecreaseRepeatButton>
                                    <Track.IncreaseRepeatButton>
                                        <RepeatButton Command=""ScrollBar.PageDownCommand"" Background=""Transparent"" BorderThickness=""0"" Focusable=""False"">
                                            <RepeatButton.Template>
                                                <ControlTemplate TargetType=""RepeatButton"">
                                                    <Border Background=""Transparent""/>
                                                </ControlTemplate>
                                            </RepeatButton.Template>
                                        </RepeatButton>
                                    </Track.IncreaseRepeatButton>
                                    <Track.Thumb>
                                        <Thumb MinHeight=""24"">
                                            <Thumb.Template>
                                                <ControlTemplate TargetType=""Thumb"">
                                                    <Border x:Name=""thumb"" CornerRadius=""3"" Width=""5"" HorizontalAlignment=""Center"" Background=""#2E6B45""/>
                                                    <ControlTemplate.Triggers>
                                                        <Trigger Property=""IsMouseOver"" Value=""True"">
                                                            <Setter TargetName=""thumb"" Property=""Background"" Value=""#45D07C""/>
                                                        </Trigger>
                                                        <Trigger Property=""IsDragging"" Value=""True"">
                                                            <Setter TargetName=""thumb"" Property=""Background"" Value=""#45D07C""/>
                                                        </Trigger>
                                                    </ControlTemplate.Triggers>
                                                </ControlTemplate>
                                            </Thumb.Template>
                                        </Thumb>
                                    </Track.Thumb>
                                </Track>
                            </Grid>
                        </ControlTemplate>
                    </Setter.Value>
                </Setter>
                <Style.Triggers>
                    <Trigger Property=""Orientation"" Value=""Horizontal"">
                        <Setter Property=""Width"" Value=""Auto""/>
                        <Setter Property=""Height"" Value=""8""/>
                    </Trigger>
                </Style.Triggers>
            </Style>
        </ResourceDictionary>";
    }
}
