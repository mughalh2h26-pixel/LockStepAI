using System;
using System.Diagnostics;
using System.Windows;

namespace LockStepAI.Views
{
    public partial class AboutWindow : Window
    {
        private const string ReportUrl = "https://docs.google.com/forms/d/e/1FAIpQLSduNuQSK-TfjTHI4nLx8v3-DyihT6RvzWBcahGzE9IlQrcYVA/viewform?usp=publish-editor";

        public AboutWindow()
        {
            InitializeComponent();

            var version = typeof(AboutWindow).Assembly.GetName().Version ?? new Version(0, 0, 0);
            VersionLabel.Text = $"Version {version.Major}.{version.Minor}.{version.Build}";
        }

        private void Report_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo(ReportUrl) { UseShellExecute = true });
        }
    }
}
