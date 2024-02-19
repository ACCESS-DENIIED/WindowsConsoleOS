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

namespace WindowSelector
{
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

        public MainWindow()
        {
            InitializeComponent();

            this.WindowState = WindowState.Maximized;
            this.Width = SystemParameters.PrimaryScreenWidth;
            this.Height = SystemParameters.PrimaryScreenHeight;

            trayIcon = new NotifyIcon();
            trayIcon.Icon = new System.Drawing.Icon("CT.ico");
            trayIcon.Visible = false;

            ContextMenu trayMenu = new ContextMenu();
            MenuItem quitItem = new MenuItem("Quit");

            // Add click event for quitItem
            quitItem.Click += (sender, e) => System.Windows.Application.Current.Shutdown();

            // Add the quit item to the context menu
            trayMenu.MenuItems.Add(quitItem);

            // Associate the context menu with the tray icon
            trayIcon.ContextMenu = trayMenu;

            // Initialize XInput controller
            controller = new Controller(UserIndex.One);

            var timer = new System.Windows.Threading.DispatcherTimer();
            timer.Tick += (sender, e) =>
            {
                GamepadTick(sender, e);
                RefreshWindowTitlesIfNeeded();
            };
            timer.Interval = TimeSpan.FromMilliseconds(0);
            timer.Start();

            RefreshWindowTitles();
        }

        private DateTime lastRefreshTime = DateTime.MinValue;

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

        private GamepadButtonFlags previousButtons = GamepadButtonFlags.None;

        private void GamepadTick(object sender, EventArgs e)
        {
            if (!controller.IsConnected) return;

            var state = controller.GetState();
            bool l1Pressed = state.Gamepad.Buttons.HasFlag(GamepadButtonFlags.LeftShoulder);
            bool r1Pressed = state.Gamepad.Buttons.HasFlag(GamepadButtonFlags.RightShoulder);
            bool dpadLeftPressed = state.Gamepad.Buttons.HasFlag(GamepadButtonFlags.DPadLeft);

            if (l1Pressed && r1Pressed && dpadLeftPressed)
            {
                this.Dispatcher.Invoke(() =>
                {
                    RestoreWindowFromTray();
                });
            }
            bool navigateUp = state.Gamepad.Buttons.HasFlag(GamepadButtonFlags.DPadUp);
            bool navigateDown = state.Gamepad.Buttons.HasFlag(GamepadButtonFlags.DPadDown);

            if (navigateUp && !previousButtons.HasFlag(GamepadButtonFlags.DPadUp))
            {
                if (WindowListBox.SelectedIndex > 0)
                {
                    WindowListBox.SelectedIndex--;
                }
            }
            else if (navigateDown && !previousButtons.HasFlag(GamepadButtonFlags.DPadDown))
            {
                if (WindowListBox.SelectedIndex < WindowListBox.Items.Count - 1)
                {
                    WindowListBox.SelectedIndex++;
                }
            }

            if (state.Gamepad.Buttons.HasFlag(GamepadButtonFlags.A) && !previousButtons.HasFlag(GamepadButtonFlags.A))
            {
                WindowListBox_SelectionChanged(WindowListBox, null);
                this.Hide();
                trayIcon.Visible = true;
            }

            if (state.Gamepad.Buttons.HasFlag(GamepadButtonFlags.B) && !previousButtons.HasFlag(GamepadButtonFlags.B))
            {
                this.Hide();
                trayIcon.Visible = true;
            }

            previousButtons = state.Gamepad.Buttons;
        }

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

        private void RestoreWindowFromTray()
        {
            ShowTrayIcon(false);
            this.Show();
            this.WindowState = WindowState.Maximized;
            this.Topmost = true;
            this.Topmost = false;

            this.Left = 0;
            this.Top = 0;
            this.Width = SystemParameters.VirtualScreenWidth;
            this.Height = SystemParameters.VirtualScreenHeight;

            this.InvalidateVisual();
        }

        private void RefreshWindowTitles()
        {
            int selectedIndex = WindowListBox.SelectedIndex;
            var processes = GetOpenWindows();
            WindowListBox.ItemsSource = processes.Select(p => new
            {
                Name = GetFriendlyName(p.ProcessName).ToUpper(),
                Process = p
            }).ToList();
            WindowListBox.SelectedIndex = Math.Min(selectedIndex, WindowListBox.Items.Count - 1);
        }

        private string GetFriendlyName(string processName)
        {
            var nameMap = new Dictionary<string, string>
        {
            { "spotify", "Spotify" },
            // Add more mappings if required
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

        #endregion

        private void WindowListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
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

        #endregion

        private void WindowListBox_SelectionChanged_1(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
        
        }
    }
}