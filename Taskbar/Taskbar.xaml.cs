using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading; // Added for DispatcherTimer
using SharpDX.XInput;
using Path = System.IO.Path; // Add this to your using directives

namespace WindowsConsoleOS.Taskbar
{
    /// <summary>
    /// Interaction logic for Taskbar.xaml
    /// </summary>
    public partial class Taskbar : Window
    {
        private DispatcherTimer gamepadPollingTimer; // Added for gamepad polling
        private Controller controller = new Controller(UserIndex.One);
        private GamepadButtonFlags desiredButtons = GamepadButtonFlags.LeftShoulder | GamepadButtonFlags.RightShoulder;

        public Taskbar()
        {
            InitializeComponent();
            LoadShortcuts();
            SetupGamepadPolling(); // Added call to setup gamepad polling
        }

        private void ShortcutsListView_Loaded(object sender, RoutedEventArgs e)
        {
            // Debugging purpose: Check if the ListView has items
            var listView = sender as ListView;
            if (listView != null && listView.ItemsSource != null)
            {
                Console.WriteLine("Items loaded: " + listView.Items.Count);
            }
            else
            {
                Console.WriteLine("No items loaded.");
            }
        }

        private void LoadShortcuts()
        {
            string taskbarShortcutsPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + @"\Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar";
            var shortcuts = Directory.GetFiles(taskbarShortcutsPath, "*.lnk").Select(path => new ShortcutModel
            {
                Name = Path.GetFileNameWithoutExtension(path),
                Path = path,
                Icon = GetIcon(path) // Updated to include icon extraction
            }).ToList();

            ShortcutsListView.ItemsSource = shortcuts;
        }

        private ImageSource GetIcon(string path) // Added method for icon extraction
        {
            using (System.Drawing.Icon sysicon = System.Drawing.Icon.ExtractAssociatedIcon(path))
            {
                return Imaging.CreateBitmapSourceFromHIcon(
                    sysicon.Handle,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
            }
        }

        private void SetupGamepadPolling()
        {
            gamepadPollingTimer = new DispatcherTimer();
            gamepadPollingTimer.Interval = TimeSpan.FromMilliseconds(100); // Poll every 100 ms
            gamepadPollingTimer.Tick += GamepadPollingTimer_Tick;
            gamepadPollingTimer.Start();
        }

        private void GamepadPollingTimer_Tick(object sender, EventArgs e)
        {
            if (!controller.IsConnected) return;

            var state = controller.GetState();
            if ((state.Gamepad.Buttons & GamepadButtonFlags.DPadRight) == GamepadButtonFlags.DPadRight)
            {
                MoveSelection(1);
            }
            else if ((state.Gamepad.Buttons & GamepadButtonFlags.DPadLeft) == GamepadButtonFlags.DPadLeft)
            {
                MoveSelection(-1);
            }
            else if ((state.Gamepad.Buttons & GamepadButtonFlags.A) == GamepadButtonFlags.A)
            {
                LaunchSelectedShortcut();
            }
        }

        private void MoveSelection(int direction) // Added method for gamepad navigation
        {
            int newIndex = Math.Max(0, Math.Min(ShortcutsListView.SelectedIndex + direction, ShortcutsListView.Items.Count - 1));
            ShortcutsListView.SelectedIndex = newIndex;
            ShortcutsListView.ScrollIntoView(ShortcutsListView.SelectedItem);
        }

        private void BringApplicationToFront()
        {
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }
            Activate();
            Topmost = true;  // Important
            Topmost = false; // Important
            Focus();
        }

        private void LaunchSelectedShortcut()
        {
            if (ShortcutsListView.SelectedItem is ShortcutModel selectedShortcut)
            {
                Process.Start(selectedShortcut.Path);
            }
        }

        private class ShortcutModel
        {
            public string Name { get; set; }
            public string Path { get; set; }
            public ImageSource Icon { get; set; } // Updated to include Icon property
        }
    }
}
