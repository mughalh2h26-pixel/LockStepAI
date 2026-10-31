using System.Windows;
using System;

namespace LockStepAI.Views
{
    public partial class SplashWindow : Window
    {
        public event EventHandler? CancelRequested;

        public SplashWindow()
        {
            InitializeComponent();
        }

        public void UpdateStatus(string text)
        {
            StatusText.Text = text;
        }

        public void SetCancelEnabled(bool enabled)
        {
            CancelButton.IsEnabled = enabled;
        }

        private void CancelStartup_Click(object sender, RoutedEventArgs e)
        {
            CancelRequested?.Invoke(this, EventArgs.Empty);
            Close();
        }
    }
}