using LockStepAI.Services;
using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace LockStepAI.Views
{
    public class ChatListDto
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
    }

    public partial class ChatWindow : Window
    {
        private readonly DatabaseService _db;
        private readonly InferenceService _inference;

        private bool _isProcessing = false;
        private bool _isGenerating = false;
        private int _currentChatId = 0;
        private CancellationTokenSource? _cts;

        public ChatWindow(InferenceService inference)
        {
            InitializeComponent();
            _inference = inference;
            _db = new DatabaseService();
            var version = typeof(ChatWindow).Assembly.GetName().Version ?? new Version(0, 0, 0);
            VersionLabel.Text = $"V{version.Major}.{version.Minor}.{version.Build}";

            ApplyTheme();
            AddMessage("hi, how can i help? i can see what's on your screen, open apps, or do tasks for you.", false);
        }

        private void ApplyTheme()
        {
            Dispatcher.Invoke(() =>
            {
                MainContainer.Background = new SolidColorBrush(Color.FromRgb(0, 0, 0));
                MainContainer.BorderBrush = new SolidColorBrush(ThemeService.GetBorderColor());

                InputBox.Background = new SolidColorBrush(Color.FromRgb(0, 0, 0));
                InputBox.Foreground = ThemeService.GetTextBrush();
                InputBox.CaretBrush = ThemeService.GetTextBrush();
            });
        }

        // --- Window & UI Events ---
        private void Window_Loaded(object sender, RoutedEventArgs e) => InputBox.Focus();
        private void Hide_Click(object sender, RoutedEventArgs e) => this.Hide();

        private void Report_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo("https://docs.google.com/forms/d/e/1FAIpQLSduNuQSK-TfjTHI4nLx8v3-DyihT6RvzWBcahGzE9IlQrcYVA/viewform?usp=publish-editor")
            {
                UseShellExecute = true
            });
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            this.Hide();
        }

        private void SendStopButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isGenerating)
            {
                _cts?.Cancel();
            }
            else
            {
                _ = SendMessageAsync();
            }
        }

        private void InputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift)
            {
                if (!_isGenerating) _ = SendMessageAsync();
                e.Handled = true;
            }
        }

        // --- History Menu Logic ---
        private void Hamburger_Click(object sender, RoutedEventArgs e)
        {
            HistoryMenu.Visibility = HistoryMenu.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            if (HistoryMenu.Visibility == Visibility.Visible) LoadHistory();
        }

        private void LoadHistory()
        {
            ChatHistoryList.Items.Clear();
            foreach (var chat in _db.GetChats())
            {
                ChatHistoryList.Items.Add(new ChatListDto { Id = chat.Id, Title = chat.Title });
            }
        }

        private void NewChat_Click(object? sender, RoutedEventArgs? e)
        {
            _currentChatId = 0;
            MessagePanel.Children.Clear();
            HistoryMenu.Visibility = Visibility.Collapsed;
            AddMessage("New chat started. How can I help?", false);
        }

        private void ChatHistoryList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ChatHistoryList.SelectedItem is ChatListDto selected)
            {
                _currentChatId = selected.Id;
                MessagePanel.Children.Clear();

                var messages = _db.GetMessages(_currentChatId);
                foreach (var msg in messages)
                {
                    AddMessage(msg.Content, msg.Role == "user");
                }
                HistoryMenu.Visibility = Visibility.Collapsed;
            }
        }

        private void RenameChat_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int id)
            {
                string newTitle = Microsoft.VisualBasic.Interaction.InputBox("Enter new chat title:", "Rename Chat", "");
                if (!string.IsNullOrWhiteSpace(newTitle))
                {
                    _db.UpdateChatTitle(id, newTitle);
                    LoadHistory();
                }
            }
        }

        private void DeleteChat_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int id)
            {
                _db.DeleteChat(id);
                if (_currentChatId == id) NewChat_Click(null, null);
                LoadHistory();
            }
        }

        // ==========================================
        // CORE UI ROUTER (No AI logic here!)
        // ==========================================
        private async Task SendMessageAsync()
        {
            if (_isProcessing) return;
            string text = InputBox.Text.Trim();
            if (string.IsNullOrEmpty(text)) return;

            // UI Prep
            InputBox.Clear();
            AddMessage(text, true);
            _isProcessing = true;
            _isGenerating = true;
            InputBox.IsEnabled = false;

            SendStopButton.Content = "\uE71A"; // Stop Icon
            SendStopButton.Background = new SolidColorBrush(Color.FromRgb(255, 65, 54)); // Red
            SendStopButton.Foreground = Brushes.White;

            // Database Prep
            if (_currentChatId == 0)
            {
                string title = text.Length > 25 ? text.Substring(0, 25) + "..." : text;
                _currentChatId = _db.CreateNewChat(title);
            }
            _db.SaveMessage(_currentChatId, "user", text);

            // Generation Prep
            var (assistantTextBox, statsTextBlock) = AddEmptyAssistantBubble();
            var sb = new StringBuilder();
            _cts = new CancellationTokenSource();

            // 🚨 START STOPWATCH
            var stopwatch = Stopwatch.StartNew();
            int tokenCount = 0;

            try
            {
                await foreach (var token in _inference.StreamPromptAsync(text, _cts.Token))
                {
                    sb.Append(token);
                    tokenCount++;
                    assistantTextBox.Text = sb.ToString();
                    ChatScroll.ScrollToEnd();
                }

                _db.SaveMessage(_currentChatId, "assistant", sb.ToString());
            }
            catch (OperationCanceledException)
            {
                assistantTextBox.Text = sb.ToString() + "\n\n[Generation stopped]";
                _db.SaveMessage(_currentChatId, "assistant", assistantTextBox.Text);
            }
            catch (Exception ex)
            {
                this.WindowState = WindowState.Normal;
                this.Activate();
                assistantTextBox.Text = $"Error: {ex.Message}";
                assistantTextBox.Foreground = Brushes.IndianRed;
            }
            finally
            {
                // 🚨 STOP STOPWATCH AND CALCULATE SPEED
                stopwatch.Stop();
                double elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
                if (elapsedSeconds > 0 && tokenCount > 0)
                {
                    double tps = tokenCount / elapsedSeconds;
                    statsTextBlock.Text = $"{tokenCount} tokens • {tps:F1} tok/s • {elapsedSeconds:F1}s";
                    statsTextBlock.Visibility = Visibility.Visible;
                }

                _isProcessing = false;
                _isGenerating = false;
                _cts?.Dispose();
                _cts = null;

                SendStopButton.Content = "\uE724"; // Send Icon
                SendStopButton.Background = Brushes.White;
                SendStopButton.Foreground = Brushes.Black;

                InputBox.IsEnabled = true;
                ChatScroll.ScrollToEnd();
                InputBox.Focus();
            }
        }

        // --- UI Helpers ---
        public void AddSystemWarning(string warningText)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(40, 30, 0)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(255, 165, 0)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 10, 0, 5),
                MaxWidth = 320,
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = new TextBlock
                {
                    Text = warningText,
                    Foreground = new SolidColorBrush(Color.FromRgb(255, 200, 100)),
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 11,
                    FontStyle = FontStyles.Italic
                }
            };
            MessagePanel.Children.Add(border);
            ChatScroll.ScrollToEnd();
        }

        private void AddMessage(string text, bool isUser)
        {
            var textBox = new TextBox
            {
                Text = text,
                Foreground = ThemeService.GetTextBrush(),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                IsReadOnly = true,
                IsTabStop = false,
                Padding = new Thickness(0),
                Margin = new Thickness(0),
                Cursor = Cursors.IBeam
            };

            var border = new Border
            {
                Background = new SolidColorBrush(isUser ? ThemeService.GetUserBubbleColor() : ThemeService.GetAssistantBubbleColor()),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 5, 0, 5),
                MaxWidth = 280,
                HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                Child = textBox
            };

            MessagePanel.Children.Add(border);
            ChatScroll.ScrollToEnd();
        }

        // 🚨 UPDATED: Returns both the TextBox and the Stats TextBlock
        private (TextBox messageBox, TextBlock statsBlock) AddEmptyAssistantBubble()
        {
            var textBox = new TextBox
            {
                Text = "",
                Foreground = ThemeService.GetTextBrush(),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                IsReadOnly = true,
                IsTabStop = false,
                Padding = new Thickness(0),
                Margin = new Thickness(0),
                Cursor = Cursors.IBeam
            };

            var statsBlock = new TextBlock
            {
                Text = "",
                Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
                FontSize = 10,
                Margin = new Thickness(0, 5, 0, 0),
                Visibility = Visibility.Collapsed // Hidden until generation finishes
            };

            var stackPanel = new StackPanel();
            stackPanel.Children.Add(textBox);
            stackPanel.Children.Add(statsBlock);

            var border = new Border
            {
                Background = new SolidColorBrush(ThemeService.GetAssistantBubbleColor()),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 5, 0, 5),
                MaxWidth = 280,
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = stackPanel
            };

            MessagePanel.Children.Add(border);
            ChatScroll.ScrollToEnd();

            return (textBox, statsBlock);
        }
    }
}