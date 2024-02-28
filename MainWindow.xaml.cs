using SharpDX.XInput;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Linq;
using System.Windows.Media;
using System.Globalization;
using System.Windows.Data;
using System.Drawing;
using System.Windows.Forms;
using System.ComponentModel;
using System.Threading.Tasks;
using System.IO;
using Size = System.Drawing.Size;
using Point = System.Drawing.Point;
using System.Windows.Threading;
using System.Windows.Input;
using System.Threading;
using System.Windows.Media.Imaging;

namespace WindowSelector
{
    public class WindowItem
    {
        public string Name { get; set; }
        public Process Process { get; set; }
        public IntPtr WindowHandle { get; set; }
    }

    public class LazyImageLoader : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is IntPtr hwnd && hwnd != IntPtr.Zero)
            {
                Bitmap resizedBmp = null; // Ensure resizedBmp is declared outside the try block for broader scope
                try
                {
                    Bitmap bmp = MainWindow.PrintWindow(hwnd); // Capture the window image

                    double scaleFactor = 1.5;

                    // Calculate the maximum width based on the screen width and scale factor
                    int maxWidth = (int)(System.Windows.SystemParameters.PrimaryScreenWidth * scaleFactor / 2); // Image is always on the right so divide by 2
                    int maxHeight = (int)(System.Windows.SystemParameters.PrimaryScreenHeight * scaleFactor);

                    double ratioX = (double)maxWidth / bmp.Width;
                    double ratioY = (double)maxHeight / bmp.Height;
                    double ratio = Math.Min(ratioX, ratioY);

                    int newWidth = (int)(bmp.Width * ratio);
                    int newHeight = (int)(bmp.Height * ratio);

                    resizedBmp = new Bitmap(bmp, newWidth, newHeight); // Use the resized bitmap

                    using (MemoryStream memory = new MemoryStream())
                    {
                        resizedBmp.Save(memory, System.Drawing.Imaging.ImageFormat.Jpeg); // Save as JPEG
                        memory.Position = 0;
                        BitmapImage bitmapimage = new BitmapImage();
                        bitmapimage.BeginInit();
                        bitmapimage.StreamSource = memory;
                        bitmapimage.CacheOption = BitmapCacheOption.OnLoad;
                        bitmapimage.EndInit();
                        bitmapimage.Freeze(); // Important for use in another thread
                        return bitmapimage;
                    }
                }
                catch
                {
                    // Handle exceptions or return a default image
                    return DependencyProperty.UnsetValue;
                }
                finally
                {
                    resizedBmp?.Dispose(); // Ensure resizedBmp is disposed of correctly
                }
            }

            return DependencyProperty.UnsetValue; // Return an unset value if conversion is not possible
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    // Convert window titles to uppercase function
    public class StringToUpperConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value.ToString().ToUpper();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public partial class MainWindow : Window
    {
        private readonly Controller controller;
        private List<string> windowTitles;
        private NotifyIcon trayIcon;
        private GamepadButtonFlags previousButtons = GamepadButtonFlags.None;
        private bool aButtonPressed = false;

        public MainWindow()
        {
            InitializeComponent();

            this.WindowState = WindowState.Maximized;
            this.Width = SystemParameters.PrimaryScreenWidth;
            this.Height = SystemParameters.PrimaryScreenHeight;

            trayIcon = new NotifyIcon();
            trayIcon.Icon = new System.Drawing.Icon("OS.ico");
            trayIcon.Visible = false;

            ContextMenu trayMenu = new ContextMenu();
            MenuItem quitItem = new MenuItem("Quit");
            quitItem.Click += (sender, e) => System.Windows.Application.Current.Shutdown();
            trayMenu.MenuItems.Add(quitItem);
            trayIcon.ContextMenu = trayMenu;

            controller = new Controller(UserIndex.One);

            this.MouseDown += new MouseButtonEventHandler(MainWindow_MouseDown);

            this.Loaded += (sender, e) =>
            {
                ShowTrayIcon(true);
                this.Hide(); // !debug
            };

            StartGamepadCheck();

            var timer = new System.Windows.Threading.DispatcherTimer();
            timer.Tick += (sender, e) =>
            {
                GamepadTick(sender, e);
                RefreshWindowTitlesIfNeeded();
            };
            timer.Interval = TimeSpan.FromMilliseconds(100);
            timer.Start();

            RefreshWindowTitles();
        }

        private void StartGamepadCheck()
        {
            Task.Run(async () => await CheckGamepad());
        }

        private DateTime lastRefreshTime = DateTime.MinValue;
        private DateTime lastDpadNavigationTime = DateTime.MinValue;

        private void MainWindow_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // Check if the left mouse button was clicked
            if (e.ButtonState == MouseButtonState.Pressed && e.ChangedButton == MouseButton.Left)
            {
                aButtonPressed = true;
                WindowListBox_SelectionChanged(WindowListBox, null);
                this.Hide();
                trayIcon.Visible = true;
            }
        }

        private async Task CheckGamepad()
        {
            while (true)
            {
                if (controller.IsConnected)
                {
                    var state = controller.GetState();
                    bool l1Pressed = state.Gamepad.Buttons.HasFlag(GamepadButtonFlags.LeftShoulder);
                    bool r1Pressed = state.Gamepad.Buttons.HasFlag(GamepadButtonFlags.RightShoulder);
                    bool dpadLeftPressed = state.Gamepad.Buttons.HasFlag(GamepadButtonFlags.DPadLeft);

                    if (l1Pressed && r1Pressed && dpadLeftPressed)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            RestoreWindowFromTray();
                        },  DispatcherPriority.Input);
                    }
                }
                await Task.Delay(100);
            }
        }

        private void Window_Activated(object sender, EventArgs e)
        {
            this.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(200, 0, 0, 0));
        }

        private void RefreshWindowTitlesIfNeeded()
        {
            if ((DateTime.Now - lastRefreshTime).TotalSeconds >= 1)
            {
                RefreshWindowTitles();
                lastRefreshTime = DateTime.Now;
            }
        }

        private DateTime lastNavigationTime = DateTime.MinValue;
        private TimeSpan navigationCooldown = TimeSpan.FromMilliseconds(100);


        private void GamepadTick(object sender, EventArgs e)
        {
            if (!controller.IsConnected) return;

            var state = controller.GetState();
            bool l1Pressed = state.Gamepad.Buttons.HasFlag(GamepadButtonFlags.LeftShoulder);
            bool r1Pressed = state.Gamepad.Buttons.HasFlag(GamepadButtonFlags.RightShoulder);
            bool dpadLeftPressed = state.Gamepad.Buttons.HasFlag(GamepadButtonFlags.DPadLeft);
            bool yPressed = state.Gamepad.Buttons.HasFlag(GamepadButtonFlags.Y);
            bool xPressed = state.Gamepad.Buttons.HasFlag(GamepadButtonFlags.X);
            bool navigateUp = state.Gamepad.Buttons.HasFlag(GamepadButtonFlags.DPadUp);
            bool navigateDown = state.Gamepad.Buttons.HasFlag(GamepadButtonFlags.DPadDown);

            // Check if the current application is the active window
            IntPtr foregroundWindow = GetForegroundWindow();
            GetWindowThreadProcessId(foregroundWindow, out int foregroundProcId);
            Process foregroundProc = Process.GetProcessById(foregroundProcId);
            if (foregroundProc.ProcessName != Process.GetCurrentProcess().ProcessName)
            {
                // If the current application is not the active window, do not process other inputs
                return;
            }

            GamepadButtonFlags previousButtons = GamepadButtonFlags.None;

            if (yPressed && !previousButtons.HasFlag(GamepadButtonFlags.Y))
            {
                // Minimize the selected window
                if (WindowListBox.SelectedItem != null)
                {
                    dynamic selectedItem = WindowListBox.SelectedItem;
                    var process = selectedItem.Process as Process;
                    if (process != null && process.MainWindowHandle != IntPtr.Zero)
                    {
                        ShowWindow(process.MainWindowHandle, SW_MINIMIZE);
                    }
                }
            }
            else if (xPressed && !previousButtons.HasFlag(GamepadButtonFlags.X))
            {
                // More gracefully close the selected window
                if (WindowListBox.SelectedItem != null)
                {
                    dynamic selectedItem = WindowListBox.SelectedItem;
                    var process = selectedItem.Process as Process;
                    if (process != null)
                    {
                        if (process.MainWindowHandle != IntPtr.Zero)
                        {
                            PostMessage(process.MainWindowHandle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                        }
                        else
                        {
                            try
                            {
                                process.Kill();
                            }
                            catch (Exception ex)
                            {
                                // Handle the exception, log, etc
                            }
                        }
                    }
                }
            }

            // Check if we are within the cooldown period
            if ((DateTime.Now - lastNavigationTime) < navigationCooldown)
            {
                return; // Still in cooldown, ignore navigation input
            }

            if (navigateUp && !previousButtons.HasFlag(GamepadButtonFlags.DPadUp))
            {
                if (WindowListBox.SelectedIndex > 0)
                {
                    WindowListBox.SelectedIndex--;
                    lastNavigationTime = DateTime.Now; // Update the last navigation time
                }
            }
            else if (navigateDown && !previousButtons.HasFlag(GamepadButtonFlags.DPadDown))
            {
                if (WindowListBox.SelectedIndex < WindowListBox.Items.Count - 1)
                {
                    WindowListBox.SelectedIndex++;
                    lastNavigationTime = DateTime.Now; // Update the last navigation time
                }
            }

            if (state.Gamepad.Buttons.HasFlag(GamepadButtonFlags.A) && !previousButtons.HasFlag(GamepadButtonFlags.A))
            {
                aButtonPressed = true;
                WindowListBox_SelectionChanged(WindowListBox, null);
                this.Hide();
                trayIcon.Visible = true;
            }

            if (state.Gamepad.Buttons.HasFlag(GamepadButtonFlags.B) && !previousButtons.HasFlag(GamepadButtonFlags.B))
            {
                this.Hide();
                trayIcon.Visible = true;
            }
        }

        //
        // Credit to the Dev of "Handheld Control Panel" for the following functions and structs. Thank you, kind sir!
        //


        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")]
        public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, int nFlags);

        public static Bitmap PrintWindow(IntPtr hwnd)
        {
            RECT rc;
            GetWindowRect(hwnd, out rc);

            Bitmap bmp = new Bitmap(rc.Width, rc.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            Graphics gfxBmp = Graphics.FromImage(bmp);
            IntPtr hdcBitmap = gfxBmp.GetHdc();

            PrintWindow(hwnd, hdcBitmap, 2);

            gfxBmp.ReleaseHdc(hdcBitmap);
            gfxBmp.Dispose();

            return bmp;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            private int _Left;
            private int _Top;
            private int _Right;
            private int _Bottom;

            public RECT(RECT Rectangle) : this(Rectangle.Left, Rectangle.Top, Rectangle.Right, Rectangle.Bottom)
            {
            }
            public RECT(int Left, int Top, int Right, int Bottom)
            {
                _Left = Left;
                _Top = Top;
                _Right = Right;
                _Bottom = Bottom;
            }

            public int X
            {
                get { return _Left; }
                set { _Left = value; }
            }
            public int Y
            {
                get { return _Top; }
                set { _Top = value; }
            }
            public int Left
            {
                get { return _Left; }
                set { _Left = value; }
            }
            public int Top
            {
                get { return _Top; }
                set { _Top = value; }
            }
            public int Right
            {
                get { return _Right; }
                set { _Right = value; }
            }
            public int Bottom
            {
                get { return _Bottom; }
                set { _Bottom = value; }
            }
            public int Height
            {
                get { return _Bottom - _Top; }
                set { _Bottom = value + _Top; }
            }
            public int Width
            {
                get { return _Right - _Left; }
                set { _Right = value + _Left; }
            }
            public Point Location
            {
                get { return new Point(Left, Top); }
                set
                {
                    _Left = value.X;
                    _Top = value.Y;
                }
            }
            public Size Size
            {
                get { return new Size(Width, Height); }
                set
                {
                    _Right = value.Width + _Left;
                    _Bottom = value.Height + _Top;
                }
            }

            public static implicit operator Rectangle(RECT Rectangle)
            {
                return new Rectangle(Rectangle.Left, Rectangle.Top, Rectangle.Width, Rectangle.Height);
            }
            public static implicit operator RECT(Rectangle Rectangle)
            {
                return new RECT(Rectangle.Left, Rectangle.Top, Rectangle.Right, Rectangle.Bottom);
            }
            public static bool operator ==(RECT Rectangle1, RECT Rectangle2)
            {
                return Rectangle1.Equals(Rectangle2);
            }
            public static bool operator !=(RECT Rectangle1, RECT Rectangle2)
            {
                return !Rectangle1.Equals(Rectangle2);
            }

            public override string ToString()
            {
                return "{Left: " + _Left + "; " + "Top: " + _Top + "; Right: " + _Right + "; Bottom: " + _Bottom + "}";
            }

            public override int GetHashCode()
            {
                return ToString().GetHashCode();
            }

            public bool Equals(RECT Rectangle)
            {
                return Rectangle.Left == _Left && Rectangle.Top == _Top && Rectangle.Right == _Right && Rectangle.Bottom == _Bottom;
            }

            public override bool Equals(object Object)
            {
                if (Object is RECT)
                {
                    return Equals((RECT)Object);
                }
                else if (Object is Rectangle)
                {
                    return Equals(new RECT((Rectangle)Object));
                }

                return false;
            }
        }

        //
        // Again, Thank you, HCP Dev :)
        //

        private void ShowTrayIcon(bool show)
        {
            if (trayIcon != null)
            {
                trayIcon.Visible = show;
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (trayIcon != null)
            {
                trayIcon.Dispose();
            }
            base.OnClosing(e);
        }

        private async void RestoreWindowFromTray()
        {
            ShowTrayIcon(false);
            this.Show();
            this.WindowState = WindowState.Maximized;

            // Ensure the window is brought to the foreground
            this.Activate();
            this.Focus();
            this.Topmost = true; // Make window topmost to ensure it gets focus
            await Task.Delay(100); // Wait a bit for the window to be ready
            this.Topmost = false; // Then set it back to not topmost, so it behaves normally afterwards
            this.Focusable = true; // Ensure the window can be focused after restoration

            this.Left = 0;
            this.Top = 0;
            this.Width = SystemParameters.VirtualScreenWidth;
            this.Height = SystemParameters.VirtualScreenHeight;

            // Use the SetForegroundWindow API to force the window to the foreground
            IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            SetForegroundWindow(hwnd);

            this.InvalidateVisual();
        }

        private void RefreshWindowTitles()
        {
            int selectedIndex = WindowListBox.SelectedIndex;
            var processes = GetOpenWindows();
            WindowListBox.ItemsSource = processes.Select(p =>
            {
                var item = new WindowItem
                {
                    Name = GetFriendlyName(p.ProcessName).ToUpper(),
                    Process = p,
                    WindowHandle = p.MainWindowHandle // Store the handle directly
                };
                return item;
            }).ToList();
            WindowListBox.SelectedIndex = Math.Min(selectedIndex, WindowListBox.Items.Count - 1);
        }

        private string GetFriendlyName(string processName)
        {
            var nameMap = new Dictionary<string, string>
        {
            { "spotify", "Spotify"  },
            { "steam", "Steam"  },
            { "steamwebhelper", "Steam"  },
            { "steamwebhelperupdater", "Steam Updater"  },
            { "steamclient", "Steam"  },
            { "devenv", "Visual Studio"  },
            { "eadesktop", "EA Launcher"  },
            { "playnite.fullscreenapp", "Playnite Fullscreen"  },
            { "playnite.desktopapp", "Playnite Desktop"  },
            // Add more mappings as and when required
        };

            if (nameMap.TryGetValue(processName.ToLower(), out var friendlyName))
            {
                return friendlyName;
            }

            return processName;
        }

        private List<Process> GetOpenWindows()
        {
            var processes = new List<Process>();

            foreach (Process process in Process.GetProcesses())
            {
                // Remove "WindowsConsoleOS" and "TextInputHost" processes from the list
                if (!string.IsNullOrEmpty(process.MainWindowTitle) &&
                    !process.ProcessName.Equals("WindowsConsoleOS", StringComparison.OrdinalIgnoreCase) &&
                    !process.ProcessName.Equals("TextInputHost", StringComparison.OrdinalIgnoreCase))
                {
                    processes.Add(process);
                }
            }

            return processes;
        }

        #region Native Methods

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int processId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
        const uint WM_CLOSE = 0x0010;

        #endregion

        // Function to switch to a specific window
        private void WindowListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (aButtonPressed)
            {
                if (WindowListBox.SelectedIndex >= 0 && WindowListBox.SelectedIndex < WindowListBox.Items.Count)
                {
                    var selectedItem = WindowListBox.SelectedItem as dynamic;
                    var process = selectedItem.Process as Process;
                    if (process != null && process.MainWindowHandle != IntPtr.Zero)
                    {
                        SwitchToThisWindow(process.MainWindowHandle, true);
                    }
                }
                aButtonPressed = false;
            }
        }

        #region Native Methods

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool AllowSetForegroundWindow(int dwProcessId);

        [DllImport("user32.dll")]
        public static extern void SwitchToThisWindow(IntPtr hWnd, bool fAltTab);

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        const int SW_MINIMIZE = 6;

        #endregion
    }
}