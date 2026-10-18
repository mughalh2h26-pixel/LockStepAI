using System.Windows;

namespace LockStepAI.Views
{
    public partial class SplashWindow : Window
    {
        public SplashWindow()
        {
            InitializeComponent();
        }

        // We are keeping this method so App.xaml.cs doesn't crash when it tries to update the status.
        // It just does nothing now since there is no text to update.
        public void UpdateStatus(string text)
        {
            // Do nothing, we want a clean image-only splash screen
        }

        private void TextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {

        }
    }
}