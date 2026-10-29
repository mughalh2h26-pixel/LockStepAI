using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace LockStepAI.Views
{
    public partial class BubbleWindow : Window
    {
        private ChatWindow? _chatWindow;
        private Point _startPoint;
        private bool _isDragging = false;

        public BubbleWindow()
        {
            InitializeComponent();
        }

        public void SetChatWindow(ChatWindow chatWindow) { _chatWindow = chatWindow; }

        // Make this public so the Tray Icon can call it
        public void ShowChat()
        {
            if (_chatWindow == null) return;
            PositionChatWindow();
            _chatWindow.Show();
            _chatWindow.WindowState = WindowState.Normal;
            _chatWindow.Activate();
        }

        private void OpenChat_Click(object sender, RoutedEventArgs e) => ShowChat();
        private void Quit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

        private void ToggleChat()
        {
            if (_chatWindow == null) return;
            if (_chatWindow.IsVisible) _chatWindow.Hide();
            else ShowChat();
        }

        public void PositionChatWindow()
        {
            if (_chatWindow == null) return;

            double margin = 10;
            _chatWindow.Left = this.Left - _chatWindow.Width + this.Width;
            _chatWindow.Top = this.Top - _chatWindow.Height - margin;

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
                    this.DragMove(); // DragMove blocks until mouse is released

                    // After dragging, move the chat window with it
                    if (_chatWindow != null && _chatWindow.IsVisible)
                        PositionChatWindow();
                }
            }
        }

        private void Bubble_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // 🚨 Clamp to screen when dragging finishes
            ClampToScreen();

            if (!_isDragging) ToggleChat();
        }

        private void ClampToScreen()
        {
            // Get the virtual screen bounds (covers all monitors)
            double minLeft = SystemParameters.VirtualScreenLeft;
            double minTop = SystemParameters.VirtualScreenTop;
            double maxLeft = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - this.Width;
            double maxTop = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - this.Height;

            // Snap the window back if it's out of bounds
            if (this.Left < minLeft) this.Left = minLeft;
            if (this.Top < minTop) this.Top = minTop;
            if (this.Left > maxLeft) this.Left = maxLeft;
            if (this.Top > maxTop) this.Top = maxTop;

            // Update chat position if it's open
            if (_chatWindow != null && _chatWindow.IsVisible)
                PositionChatWindow();
        }
    }
}