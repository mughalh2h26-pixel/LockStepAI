using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace LockStepAI.Views
{
    public partial class OverlayWindow : Window
    {
        // Win32 extended styles for click-through
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int nValue);

        public OverlayWindow()
        {
            InitializeComponent();
            Loaded += OverlayWindow_Loaded;

            // Span the entire virtual screen (all monitors)
            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;
        }

        private void OverlayWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Make the window click-through via Win32 extended styles
            var hwnd = new WindowInteropHelper(this).Handle;
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            exStyle |= WS_EX_TRANSPARENT   // Clicks pass through to apps below
                     | WS_EX_LAYERED       // Required for transparency
                     | WS_EX_NOACTIVATE    // Never steal focus from user
                     | WS_EX_TOOLWINDOW;   // Hide from Alt+Tab
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);
        }

        // ─────────────────────────────────────────
        //  PUBLIC API — Called by your Agent logic
        // ─────────────────────────────────────────

        /// <summary>Highlight a UI element region (in screen pixels).</summary>
        public void HighlightElement(double x, double y, double width, double height,
                                     Color? color = null, int durationMs = 1500)
        {
            Dispatcher.Invoke(() =>
            {
                var c = color ?? Color.FromRgb(0, 200, 255); // Cyan glow

                var rect = new Rectangle
                {
                    Width = width,
                    Height = height,
                    Stroke = new SolidColorBrush(c),
                    StrokeThickness = 3,
                    Fill = new SolidColorBrush(Color.FromArgb(40, c.R, c.G, c.B)),
                    RadiusX = 6,
                    RadiusY = 6,
                    IsHitTestVisible = false,
                    Opacity = 0
                };

                // Adjust for virtual screen offset if multi-monitor
                Canvas.SetLeft(rect, x - Left);
                Canvas.SetTop(rect, y - Top);
                OverlayCanvas.Children.Add(rect);

                // Fade in -> hold -> fade out -> remove
                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));
                var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(300))
                {
                    BeginTime = TimeSpan.FromMilliseconds(durationMs)
                };
                fadeOut.Completed += (s, e) => OverlayCanvas.Children.Remove(rect);

                rect.BeginAnimation(OpacityProperty, fadeIn);
                rect.BeginAnimation(OpacityProperty, fadeOut);
            });
        }

        /// <summary>Show a ripple where a click happened (screen coordinates).</summary>
        public void ShowClickRipple(double screenX, double screenY,
                                    Color? color = null, int size = 60)
        {
            Dispatcher.Invoke(() =>
            {
                var c = color ?? Color.FromRgb(0, 200, 255);

                var ring = new Ellipse
                {
                    Width = 0,
                    Height = 0,
                    Stroke = new SolidColorBrush(c),
                    StrokeThickness = 4,
                    IsHitTestVisible = false
                };

                Canvas.SetLeft(ring, screenX - Left);
                Canvas.SetTop(ring, screenY - Top);
                OverlayCanvas.Children.Add(ring);

                // Animate size and opacity
                var sizeAnim = new DoubleAnimation(0, size, TimeSpan.FromMilliseconds(450))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                var fadeAnim = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(450))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                fadeAnim.Completed += (s, e) => OverlayCanvas.Children.Remove(ring);

                ring.BeginAnimation(WidthProperty, sizeAnim);
                ring.BeginAnimation(HeightProperty, sizeAnim);
                ring.BeginAnimation(OpacityProperty, fadeAnim);

                // Keep the ring centered on the click point as it grows
                ring.SizeChanged += (s, e) =>
                {
                    Canvas.SetLeft(ring, screenX - Left - ring.Width / 2);
                    Canvas.SetTop(ring, screenY - Top - ring.Height / 2);
                };
            });
        }

        /// <summary>Show a small text label near the action point.</summary>
        public void ShowLabel(double screenX, double screenY, string text, int durationMs = 1500)
        {
            Dispatcher.Invoke(() =>
            {
                var label = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(220, 20, 20, 20)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0, 200, 255)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8, 4, 8, 4),
                    IsHitTestVisible = false,
                    Child = new TextBlock
                    {
                        Text = text,
                        Foreground = Brushes.White,
                        FontSize = 12,
                        FontFamily = new FontFamily("Segoe UI")
                    }
                };

                Canvas.SetLeft(label, screenX - Left + 16);
                Canvas.SetTop(label, screenY - Top + 16);
                OverlayCanvas.Children.Add(label);

                var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300))
                {
                    BeginTime = TimeSpan.FromMilliseconds(durationMs)
                };
                fadeOut.Completed += (s, e) => OverlayCanvas.Children.Remove(label);
                label.BeginAnimation(OpacityProperty, fadeOut);
            });
        }

        /// <summary>Clear all drawings.</summary>
        public void ClearAll()
        {
            Dispatcher.Invoke(() => OverlayCanvas.Children.Clear());
        }
    }
}