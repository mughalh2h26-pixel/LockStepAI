using System.Windows;

namespace LockStepAI.Views
{
    public partial class SplashWindow : Window
    {
        public SplashWindow()
        {
            InitializeComponent();
        }

        public void UpdateStatus(string text)
        {
            StatusText.Text = text;
        }

        public void UpdateProgress(double percent) => DownloadProgress.Value = Math.Clamp(percent, 0, 100);
    }
}