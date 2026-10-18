using System;
using System.Threading.Tasks;
using System.Windows;
using LockStepAI.Services;
using LockStepAI.Views;

namespace LockStepAI
{
    public partial class App : Application
    {
        private InferenceService? _inference;
        private OverlayWindow? _overlay;

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            SQLitePCL.Batteries_V2.Init();

            var splash = new SplashWindow();
            splash.Show();

            _inference = new InferenceService();

            // Start the HTTP Server
            bool ok = await _inference.StartAsync();

            if (!ok)
            {
                splash.Close();
                MessageBox.Show("Failed to start the BitNet inference server.", "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
                return;
            }

            // Warmup the model so the first user message is instant
            await _inference.WarmupAsync();

            try
            {
                // Create overlay (hidden for now, ready for future phases)
                _overlay = new OverlayWindow();
                _overlay.Show();

                var chatWindow = new ChatWindow(_inference);
                var bubbleWindow = new BubbleWindow();

                bubbleWindow.SetChatWindow(chatWindow);

                // Set initial Bubble Position
                var desktop = SystemParameters.WorkArea;
                bubbleWindow.Left = desktop.Right - 100;
                bubbleWindow.Top = desktop.Bottom - 100;

                bubbleWindow.Show();

                // Show the chat window immediately 
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

        protected override void OnExit(ExitEventArgs e)
        {
            _inference?.Dispose();
            _overlay?.Close();
            base.OnExit(e);
        }
    }
}