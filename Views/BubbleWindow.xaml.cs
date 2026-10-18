using System;
using System.Windows;
using System.Windows.Input;

namespace LockStepAI.Views
{
    public partial class BubbleWindow : Window
    {
        private ChatWindow? _chatWindow;
        private Point _startPoint;
        private bool _isDragging = false;

        public BubbleWindow() { InitializeComponent(); }

        public void SetChatWindow(ChatWindow chatWindow) { _chatWindow = chatWindow; }

        private void OpenChat_Click(object sender, RoutedEventArgs e) => ShowChat();
        private void Quit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

        private void ShowChat()
        {
            if (_chatWindow == null) return;
            PositionChatWindow();
            _chatWindow.Show();
            _chatWindow.WindowState = WindowState.Normal;
            _chatWindow.Activate();
        }

        private void ToggleChat()
        {
            if (_chatWindow == null) return;
            if (_chatWindow.IsVisible)
                _chatWindow.Hide();
            else
                ShowChat();
        }

        public void PositionChatWindow()
        {
            if (_chatWindow == null) return;

            // Anchor to the bottom right, above the bubble
            double margin = 10;
            _chatWindow.Left = this.Left - _chatWindow.Width + this.Width;
            _chatWindow.Top = this.Top - _chatWindow.Height - margin;

            // Safety check: if it goes off the top of the screen, put it below the bubble
            if (_chatWindow.Top < 0)
                _chatWindow.Top = this.Top + this.Height + margin;
        }

        private void Bubble_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _startPoint = e.GetPosition(this);
            _isDragging = false;
        }

        private void Bubble_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                Point currentPoint = e.GetPosition(this);
                if (Math.Abs(currentPoint.X - _startPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(currentPoint.Y - _startPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    _isDragging = true;
                    this.DragMove();
                    if (_chatWindow != null && _chatWindow.IsVisible)
                        PositionChatWindow();
                }
            }
        }

        private void Bubble_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // If it was just a click (not a drag), toggle the chat
            if (!_isDragging) ToggleChat();
        }
    }
}