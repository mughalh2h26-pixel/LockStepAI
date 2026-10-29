using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging; // For the tray icon image
using H.NotifyIcon;                // The tray icon library
using LockStepAI.Services;
using LockStepAI.Views;
using Velopack;
using Velopack.Sources;

namespace LockStepAI
{
    public partial class App : Application
    {
        private const string UpdateRepositoryUrl = "https://github.com/mughalh2h26-pixel/LockStepAI";
        private InferenceService? _inference;
        private OverlayWindow? _overlay;
        private TaskbarIcon? _trayIcon; // 🚨 Tray Icon
        private static Mutex? _mutex;

        [STAThread]
        private static void Main(string[] args)
        {
            VelopackApp.Build().Run();

            var app = new App();
            app.InitializeComponent();
            app.Run();
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            const string mutexName = "Global\\LockStepAI_SingleInstance_Mutex";
            bool createdNew;
            _mutex = new Mutex(true, mutexName, out createdNew);

            if (!createdNew)
            {
                var result = MessageBox.Show("LockStep AI is already running.\n\nClose existing instance and start a new one?", "LockStep AI", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.Yes)
                {
                    var current = Process.GetCurrentProcess();
                    var others = Process.GetProcessesByName(current.ProcessName).Where(p => p.Id != current.Id).ToList();
                    foreach (var p in others) { try { p.Kill(entireProcessTree: true); } catch { } }
                    Thread.Sleep(500);
                }
                else { Shutdown(); return; }
            }

            if (await CheckForUpdatesSilentlyAsync())
            {
                Shutdown();
                return;
            }

            string serverExe = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BitNet", "llama-server.exe");
            ChildProcessTracker.CleanupOrphanedProcesses(serverExe);
            ChildProcessTracker.Initialize();

            SQLitePCL.Batteries_V2.Init();
            var splash = new SplashWindow();
            splash.Show();

            try
            {
                splash.UpdateStatus("Checking AI model...");
                var progress = new Progress<double>(percent =>
                {
                    splash.UpdateProgress(percent);
                    splash.UpdateStatus($"Downloading AI model: {percent:F0}%");
                });
                await ModelDownloader.EnsureModelAsync(progress, CancellationToken.None);
            }
            catch (Exception ex)
            {
                splash.Close();
                MessageBox.Show($"Failed to download the AI model: {ex.Message}", "Setup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
                return;
            }

            splash.UpdateStatus("Loading AI engine...");
            var inference = new InferenceService();
            _inference = inference;
            bool ok = await inference.StartAsync();

            if (!ok)
            {
                splash.Close();
                string logLocation = string.IsNullOrEmpty(inference.StartupLogPath)
                    ? "Startup log could not be created."
                    : $"Full diagnostic log: {inference.StartupLogPath}";
                MessageBox.Show(
                    $"Failed to start the BitNet inference server.{Environment.NewLine}{Environment.NewLine}{inference.StartupDiagnostic}{Environment.NewLine}{Environment.NewLine}{logLocation}",
                    "Startup Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown(1);
                return;
            }

            await inference.WarmupAsync();

            try
            {
                _overlay = new OverlayWindow();
                _overlay.Show();

                var chatWindow = new ChatWindow(inference);
                var bubbleWindow = new BubbleWindow();
                bubbleWindow.SetChatWindow(chatWindow);

                // SYSTEM TRAY 
                _trayIcon = new TaskbarIcon
                {
                    ToolTipText = "LockStep AI",
                    IconSource = new BitmapImage(new Uri("pack://application:,,,/Assets/icon.ico")),
                };

                var contextMenu = new ContextMenu();
                var showItem = new MenuItem { Header = "Open Chat" };
                showItem.Click += (s, args) => { bubbleWindow.ShowChat(); };
                var quitItem = new MenuItem { Header = "Quit" };
                quitItem.Click += (s, args) => Application.Current.Shutdown();

                contextMenu.Items.Add(showItem);
                contextMenu.Items.Add(quitItem);
                _trayIcon.ContextMenu = contextMenu;

                // Double-click tray icon to open chat
                _trayIcon.TrayMouseDoubleClick += (s, args) => { bubbleWindow.ShowChat(); };

                _trayIcon.ForceCreate();

                // Set initial Bubble Position
                var desktop = SystemParameters.WorkArea;
                bubbleWindow.Left = desktop.Right - 100;
                bubbleWindow.Top = desktop.Bottom - 100;

                bubbleWindow.Show();
                bubbleWindow.PositionChatWindow();
                chatWindow.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"UI Initialization Failed: {ex.Message}", "Error");
                Shutdown(1);
            }

            splash.Close();
        }

        private static async Task<bool> CheckForUpdatesSilentlyAsync()
        {
            try
            {
                var manager = new UpdateManager(new GithubSource(UpdateRepositoryUrl, null, false));
                var update = await manager.CheckForUpdatesAsync();
                if (update == null) return false;

                await manager.DownloadUpdatesAsync(update);
                manager.ApplyUpdatesAndRestart(update);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Updater] Update check skipped: {ex.Message}");
                return false;
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _inference?.Dispose();
            ChildProcessTracker.Dispose();
            _overlay?.Close();
            _trayIcon?.Dispose(); // Cleanup tray icon
            _mutex?.ReleaseMutex();
            base.OnExit(e);
        }
    }
}