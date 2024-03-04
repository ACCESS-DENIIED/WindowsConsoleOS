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
using System.Windows.Media.Animation;
using NAudio.CoreAudioApi;
using System.Data;
using Windows.Devices.Sms;
using System.Windows.Controls;
using Application = System.Windows.Application;
using NAudio.Wave;
using AudioSwitcher.AudioApi.CoreAudio;
using System.Text;
using MessageBox = System.Windows.MessageBox;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Button = System.Windows.Controls.Button;
using System.Windows.Controls.Primitives;
using Windows.UI.Xaml.Controls;
using Grid = System.Windows.Controls.Grid;
using ComboBox = System.Windows.Forms.ComboBox;
using System.Windows.Media.Effects;
using MaterialDesignColors;
using MaterialDesignThemes.Wpf;

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
                Bitmap resizedBmp = null;
                try
                {
                    Bitmap bmp = MainWindow.PrintWindow(hwnd); // Capture the window image

                    double scaleFactor = 1.5;

                    // Calculate the maximum width based on the screen width and scale factor
                    int maxWidth = (int)(System.Windows.SystemParameters.PrimaryScreenWidth * scaleFactor);
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

    public class AudioDevice
    {
        public string Name { get; set; }
        public Guid Id { get; set; }
        public bool IsInput { get; set; } // True for input devices, false for output
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

    public class SimplifyDeviceNameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var fullName = value as string;
            if (string.IsNullOrEmpty(fullName)) return "";

            // Example logic to trim after the first occurrence of '('
            var simplifiedName = fullName.Split('(')[0].Trim();
            return simplifiedName;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class CompositeConverter : IValueConverter
    {
        public IValueConverter First { get; set; }
        public IValueConverter Second { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var firstResult = First.Convert(value, targetType, parameter, culture);
            return Second.Convert(firstResult, targetType, parameter, culture);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public partial class MainWindow : Window
    {
        private readonly Controller controller;

        private NotifyIcon trayIcon;
        private GamepadButtonFlags previousButtons = GamepadButtonFlags.None;
        private DispatcherTimer gamepadTimer;
        private Gamepad previousGamepadState;
        private bool aButtonPressed = false;

        public MainWindow()
        {
            InitializeComponent();
            InitializeMaterialDesign();
            InitializeGamepadPolling();
            PopulateAudioDevicesAsync();

            // Refresh Window Titles regularly
            refreshTimer = new DispatcherTimer();
            refreshTimer.Interval = TimeSpan.FromSeconds(2);
            refreshTimer.Tick += (sender, e) => RefreshWindowTitlesIfNeeded();
            refreshTimer.Start();

            this.WindowState = WindowState.Maximized;
            this.Width = SystemParameters.PrimaryScreenWidth;
            this.Height = SystemParameters.PrimaryScreenHeight;

            trayIcon = new NotifyIcon();
            trayIcon.Icon = new System.Drawing.Icon("OS.ico");
            trayIcon.Visible = false;

            // Add the application icon to the system tray
            this.Loaded += MainWindow_Loaded;
            // Ensure the tray icon is removed when the application exits
            Application.Current.Exit += Current_Exit;

            System.Windows.Forms.ContextMenu trayMenu = new System.Windows.Forms.ContextMenu();
            System.Windows.Forms.MenuItem quitItem = new System.Windows.Forms.MenuItem("Quit");
            quitItem.Click += (sender, e) => System.Windows.Application.Current.Shutdown();
            trayMenu.MenuItems.Add(quitItem);
            trayIcon.ContextMenu = trayMenu;

            controller = new Controller(UserIndex.One);

            this.MouseDown += new MouseButtonEventHandler(MainWindow_MouseDown);

            RefreshWindowTitles();
        }

        private DispatcherTimer refreshTimer;

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Only make the tray icon visible if it isn't already
            if (!trayIcon.Visible)
            {
                trayIcon.Visible = true;
                this.Hide(); // !debug
            }
        }

        private void Current_Exit(object sender, ExitEventArgs e)
        {
            // Clean up the tray icon when the application exits
            if (trayIcon != null)
            {
                trayIcon.Visible = false;
                trayIcon.Dispose();
            }
        }

        private void InitializeGamepadPolling()
        {
            gamepadTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(50) // Needs more adjustments
            };
            gamepadTimer.Tick += GamepadPollingTick;
            gamepadTimer.Start();
        }

        private void GamepadPollingTick(object sender, EventArgs e)
        {
            if (!controller.IsConnected) return;

            var gamepadState = controller.GetState().Gamepad;
            bool l1Pressed = gamepadState.Buttons.HasFlag(GamepadButtonFlags.LeftShoulder);
            bool r1Pressed = gamepadState.Buttons.HasFlag(GamepadButtonFlags.RightShoulder);
            bool dpadLeftPressed = gamepadState.Buttons.HasFlag(GamepadButtonFlags.DPadLeft);
            bool dpadRightPressed = gamepadState.Buttons.HasFlag(GamepadButtonFlags.DPadRight);
            bool yPressed = gamepadState.Buttons.HasFlag(GamepadButtonFlags.Y);
            bool xPressed = gamepadState.Buttons.HasFlag(GamepadButtonFlags.X);
            bool listboxUp = gamepadState.Buttons.HasFlag(GamepadButtonFlags.DPadUp);
            bool listboxDown = gamepadState.Buttons.HasFlag(GamepadButtonFlags.DPadDown);

            // Check if the current application is the active window
            IntPtr foregroundWindow = GetForegroundWindow();
            GetWindowThreadProcessId(foregroundWindow, out int foregroundProcId);
            Process foregroundProc = Process.GetProcessById(foregroundProcId);
            if (foregroundProc.ProcessName != Process.GetCurrentProcess().ProcessName)
            {
                // Restore window from tray if L1, R1, and DPad Left are pressed
                if (l1Pressed && r1Pressed && dpadLeftPressed)
                {
                    Dispatcher.Invoke(() =>
                    {
                        RestoreWindowFromTray();
                    });
                }
                return;
            }

            // Restore window from tray if L1, R1, and DPad Left are pressed
            if (l1Pressed && r1Pressed && dpadLeftPressed)
            {
                Dispatcher.Invoke(() =>
                {
                    RestoreWindowFromTray();
                });
            }

            if (AudioDevicesPopup.IsOpen)
            {
                // Prevent double dpad input when activating the popup
                if ((DateTime.Now - lastMenuOpenTime).TotalMilliseconds < 150)
                {
                    // Not enough time has passed, ignore the input
                    return;
                }
                // Directly select output or input tab using DPad left and right
                if (dpadRightPressed)
                {
                    AudioDeviceTabs.SelectedIndex = 1; // Input tab selected
                }
                else if (dpadLeftPressed)
                {
                    AudioDeviceTabs.SelectedIndex = 0; // Output tab selected
                }
                else
                {
                    // Handle input specifically for the selected device list (input or output)
                    var selectedTab = AudioDeviceTabs.SelectedItem as TabItem;
                    var listBox = selectedTab.Content as System.Windows.Controls.ListBox;
                    if (listboxUp && listBox.SelectedIndex > 0)
                    {
                        listBox.SelectedIndex--;
                    }
                    else if (listboxDown && listBox.SelectedIndex < listBox.Items.Count - 1)
                    {
                        listBox.SelectedIndex++;
                    }
                    if (gamepadState.Buttons.HasFlag(GamepadButtonFlags.A) && !previousGamepadState.Buttons.HasFlag(GamepadButtonFlags.A))
                    {
                        var selectedDevice = listBox.SelectedItem as AudioDevice;

                        if (selectedDevice != null)
                        {
                            ChangeAudioDeviceToSelected(selectedDevice);
                            HideAudioDevicesPopup();
                        }
                    }
                    else if (gamepadState.Buttons.HasFlag(GamepadButtonFlags.B) && !previousGamepadState.Buttons.HasFlag(GamepadButtonFlags.B))
                    {
                        HideAudioDevicesPopup();
                    }
                }
            }
            else
            {
                // If the fullscreen menu is not visible and the 'B' button is pressed, hide the main window to the tray.
                if (gamepadState.Buttons.HasFlag(GamepadButtonFlags.B) && !previousGamepadState.Buttons.HasFlag(GamepadButtonFlags.B))
                {
                    Dispatcher.Invoke(() =>
                    {
                        this.Hide(); // Hide the main window
                        trayIcon.Visible = true; // Make sure the tray icon is visible
                    });
                }
                if (gamepadState.Buttons.HasFlag(GamepadButtonFlags.A) && !previousGamepadState.Buttons.HasFlag(GamepadButtonFlags.A))
                {
                    aButtonPressed = true;
                    WindowListBox_SelectionChanged(WindowListBox, null);
                    this.Hide();
                    trayIcon.Visible = true;
                }
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

                if (listboxUp && !previousButtons.HasFlag(GamepadButtonFlags.DPadUp))
                {
                    if (WindowListBox.SelectedIndex > 0)
                    {
                        WindowListBox.SelectedIndex--;
                        lastNavigationTime = DateTime.Now; // Update the last navigation time
                    }
                }
                else if (listboxDown && !previousButtons.HasFlag(GamepadButtonFlags.DPadDown))
                {
                    if (WindowListBox.SelectedIndex < WindowListBox.Items.Count - 1)
                    {
                        WindowListBox.SelectedIndex++;
                        lastNavigationTime = DateTime.Now; // Update the last navigation time
                    }
                }
                else if (dpadRightPressed && !previousButtons.HasFlag(GamepadButtonFlags.DPadRight))
                {
                    // show list of audio devices
                    ShowAudioDevicesPopup();

                    lastMenuOpenTime = DateTime.Now;
                }
                else if (gamepadState.Buttons.HasFlag(GamepadButtonFlags.B) && !previousButtons.HasFlag(GamepadButtonFlags.B))
                {
                    if (!isAudioDeviceListVisible)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            // hide the audio devices popup
                            AudioDevicesPopup.IsOpen = false;
                        });
                    }
                    else
                    {
                        this.Hide();
                        trayIcon.Visible = true;
                        HideAudioDevicesPopup();
                    }
                }
            }
            previousGamepadState = gamepadState;
        }

        private DateTime lastMenuOpenTime = DateTime.MinValue;

        private void ShowAudioDevicesPopup()
        {
            // Ensure the popup is closed before adjusting its position, to avoid visual glitches.
            AudioDevicesPopup.IsOpen = false;

            // Calculate the desired position for the popup.
            var windowLocation = this.PointToScreen(new System.Windows.Point(0, 0));
            var windowHeight = this.ActualHeight;
            var windowWidth = this.ActualWidth;

            var popupHeight = PopupContent.ActualHeight;
            var popupWidth = PopupContent.ActualWidth;

            // Set the position to the bottom right of the window.
            AudioDevicesPopup.HorizontalOffset = windowLocation.X + windowWidth - popupWidth - 20;
            AudioDevicesPopup.VerticalOffset = windowLocation.Y + windowHeight - popupHeight - 20;

            ApplyBlurEffectToMainWindowContent(true);

            // Now open the popup.
            AudioDevicesPopup.IsOpen = true;

            // Start the animation, if any.
            var popInStoryboard = FindResource("OpenAudioDevicePopupAnimation") as Storyboard;
            if (popInStoryboard != null)
            {
                Storyboard.SetTarget(popInStoryboard, PopupContent);
                popInStoryboard.Begin();
            }
        }

        private void HideAudioDevicesPopup()
        {

            var popOutStoryboard = FindResource("CloseAudioDevicePopupAnimation") as Storyboard;

            if (popOutStoryboard != null)
            {
                popOutStoryboard.Completed += (s, e) =>
                {
                    AudioDevicesPopup.IsOpen = false;
                };
                Storyboard.SetTarget(popOutStoryboard, PopupContent);
                popOutStoryboard.Begin();
                ApplyBlurEffectToMainWindowContent(false);
            }
            else
            {
                AudioDevicesPopup.IsOpen = false;
            }
        }

        private void ApplyBlurEffectToMainWindowContent(bool apply)
        {
            if (apply)
            {
                var blur = new BlurEffect();
                RootPanel.Effect = blur;

                var animation = new DoubleAnimation
                {
                    From = 0,
                    To = 25, // Target blur radius
                    Duration = TimeSpan.FromSeconds(0.5),
                    FillBehavior = FillBehavior.Stop // Stops the animation at its final value
                };

                animation.Completed += (s, e) => blur.Radius = 25;
                blur.BeginAnimation(BlurEffect.RadiusProperty, animation);
            }
            else
            {
                if (RootPanel.Effect is BlurEffect blur)
                {
                    var animation = new DoubleAnimation
                    {
                        To = 0, // Animate back to no blur
                        Duration = TimeSpan.FromSeconds(0.5),
                    };

                    animation.Completed += (s, e) => RootPanel.Effect = null;
                    blur.BeginAnimation(BlurEffect.RadiusProperty, animation);
                }
            }
        }

        private void InitializeMaterialDesign()
        {
            // Create dummy objects to force the MaterialDesign assemblies to be loaded
            // from this assembly, which causes the MaterialDesign assemblies to be searched
            // relative to this assembly's path. Otherwise, the MaterialDesign assemblies
            // are searched relative to Eclipse's path, so they're not found.
            var card = new Card();
            var hue = new Hue("Dummy", Colors.Black, Colors.White);
        }

        private CustomPopupPlacement[] PopupCustomPlacementMethod(Size popupSize, Size targetSize, Point offset)
        {
            var screen = System.Windows.SystemParameters.WorkArea;
            var rightEdge = screen.Right;
            var popupX = rightEdge - popupSize.Width;
            var popupY = (screen.Height / 2) - (popupSize.Height / 2); // Center vertically

            return new CustomPopupPlacement[] { new CustomPopupPlacement(new System.Windows.Point(popupX, popupY), PopupPrimaryAxis.None) };
        }

        private async Task PopulateAudioDevicesAsync()
        {
            LoadingTextBlock.Visibility = Visibility.Visible;

            // Separate lists for input and output devices
            var outputDeviceList = new List<AudioDevice>();
            var inputDeviceList = new List<AudioDevice>();

            await Task.Run(() =>
            {
                var controller = new CoreAudioController();
                // Fetch output devices
                outputDeviceList = controller.GetPlaybackDevices(AudioSwitcher.AudioApi.DeviceState.Active)
                    .Select(d => new AudioDevice { Name = d.FullName, Id = d.Id, IsInput = false }).ToList();

                // Fetch input devices
                inputDeviceList = controller.GetCaptureDevices(AudioSwitcher.AudioApi.DeviceState.Active)
                    .Select(d => new AudioDevice { Name = d.FullName, Id = d.Id, IsInput = true }).ToList();
            });

            Dispatcher.Invoke(() =>
            {
                AudioOutputDeviceList.ItemsSource = outputDeviceList;
                AudioInputDeviceList.ItemsSource = inputDeviceList;
                shouldUpdateDevices = false; // Reset the flag
                LoadingTextBlock.Visibility = Visibility.Collapsed;
                AudioOutputDeviceList.Visibility = Visibility.Visible; // Make the list visible
            });
        }

        private async Task SetDefaultAudioDeviceAsync(Guid deviceId)
        {
            try
            {
                // Create a new instance of CoreAudioController
                var controller = new AudioSwitcher.AudioApi.CoreAudio.CoreAudioController();
                // Get the device by its Id
                var device = await controller.GetDeviceAsync(deviceId);
                if (device != null)
                {
                    // Set the device as the default playback device
                    await device.SetAsDefaultAsync();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error setting default audio device: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private DateTime lastRefreshTime = DateTime.MinValue;

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

        private void Window_Activated(object sender, EventArgs e)
        {
            this.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(200, 0, 0, 0));
        }

        private bool isAudioDeviceListVisible = false;

        private void RefreshWindowTitlesIfNeeded()
        {
            if ((DateTime.Now - lastRefreshTime).TotalSeconds >= 1)
            {
                RefreshWindowTitles();
                lastRefreshTime = DateTime.Now;
            }
        }

        private DateTime lastNavigationTime = DateTime.MinValue;

        private List<CoreAudioDevice> audioDeviceCache = null;
        private bool shouldUpdateDevices = true;

        public List<MMDevice> GetAudioOutputDevices()
        {
            var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();
            return devices;
        }

        private async void ChangeAudioDeviceToSelected(AudioDevice selectedDevice)
        {
            if (selectedDevice != null)
            {
                try
                {
                    await SetDefaultAudioDeviceAsync(selectedDevice.Id);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error setting default audio device: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
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
            HideAudioDevicesPopup();
        }

        private void RestoreWindowFromTray()
        {

            ShowTrayIcon(false);
            this.Show();
            this.WindowState = WindowState.Maximized;

            // Ensure the window is brought to the foreground
            this.Activate();
            this.Focus();
            this.Topmost = true; // Make the window topmost
            Dispatcher.BeginInvoke(new Action(() =>
            {
                this.Topmost = false; // Revert after a short delay
            }), DispatcherPriority.ApplicationIdle);

            // Set dimensions
            this.Left = 0;
            this.Top = 0;
            this.Width = SystemParameters.VirtualScreenWidth;
            this.Height = SystemParameters.VirtualScreenHeight;

            // Hide the popup, if it's open
            HideAudioDevicesPopup();

            // SetForegroundWindow call
            IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            AllowSetForegroundWindow(Process.GetCurrentProcess().Id);
            SetForegroundWindow(hwnd);
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
            { "ubc", "Ubisoft Connect"  },
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
                // Remove process/window names that are useless to us
                if (!string.IsNullOrEmpty(process.MainWindowTitle) &&
                    !process.ProcessName.Equals("WindowsConsoleOS", StringComparison.OrdinalIgnoreCase) &&
                    !process.ProcessName.Equals("TextInputHost", StringComparison.OrdinalIgnoreCase) &&
                    !process.ProcessName.Equals("Nvidia Overlay", StringComparison.OrdinalIgnoreCase))
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