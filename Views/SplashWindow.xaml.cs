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
    }
}