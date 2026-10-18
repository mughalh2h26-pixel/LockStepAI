using System;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls;
using LockStepAI.Services;
using System.Threading.Tasks;

namespace LockStepAI.Views
{
    public class ChatListDto
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
    }

    public partial class ChatWindow : Window
    {
        private readonly InferenceService _inference;
        private readonly DatabaseService _db;
        private bool _isProcessing = false;
        private int _currentChatId = 0;

        public ChatWindow(InferenceService inference)
        {
            InitializeComponent();
            _inference = inference;
            _db = new DatabaseService();

            ThemeService.ThemeChanged += ApplyTheme;
            ApplyTheme();

            // Show welcome message on fresh load
            AddMessage("hi, how can i help? i can see whats on you screen, control certain apps on your pc, or do tasks for you.", false);
        }

        private void ApplyTheme()
        {
            Dispatcher.Invoke(() =>
            {
                bool isDark = ThemeService.IsDarkMode();
                MainContainer.Background = new SolidColorBrush(isDark ? Color.FromRgb(10, 10, 10) : Colors.White);
                MainContainer.BorderBrush = new SolidColorBrush(ThemeService.GetBorderColor());

                InputBox.Background = new SolidColorBrush(isDark ? Color.FromRgb(10, 10, 10) : Color.FromRgb(240, 240, 240));
                InputBox.Foreground = ThemeService.GetTextBrush();
                InputBox.CaretBrush = ThemeService.GetTextBrush();
            });
        }

        private void Window_Loaded(object sender, RoutedEventArgs e) => InputBox.Focus();
        private void Hide_Click(object sender, RoutedEventArgs e) => this.Hide();

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            this.Hide();
        }

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

        private void NewChat_Click(object sender, RoutedEventArgs e)
        {
            _currentChatId = 0; // Reset so next message creates a new chat
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
                // Simple prompt window approach (using Microsoft.VisualBasic for InputBox)
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

        private void SendButton_Click(object sender, RoutedEventArgs e) => _ = SendMessageAsync();

        private void InputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift)
            {
                _ = SendMessageAsync();
                e.Handled = true;
            }
        }

        private async Task SendMessageAsync()
        {
            if (_isProcessing) return;
            string text = InputBox.Text.Trim();
            if (string.IsNullOrEmpty(text)) return;

            InputBox.Clear();
            AddMessage(text, true);
            _isProcessing = true;
            InputBox.IsEnabled = false;

            // Auto-create a chat if this is the first message
            if (_currentChatId == 0)
            {
                string title = text.Length > 25 ? text.Substring(0, 25) + "..." : text;
                _currentChatId = _db.CreateNewChat(title);
            }
            _db.SaveMessage(_currentChatId, "user", text);

            var assistantTextBlock = AddEmptyAssistantBubble();
            var sb = new StringBuilder();

            try
            {
                await foreach (var token in _inference.StreamPromptAsync(text))
                {
                    sb.Append(token);
                    assistantTextBlock.Text = sb.ToString();
                    ChatScroll.ScrollToEnd();
                }

                if (sb.Length == 0) assistantTextBlock.Text = "(No response)";
                _db.SaveMessage(_currentChatId, "assistant", sb.ToString());
            }
            catch (Exception ex)
            {
                assistantTextBlock.Text = $"Error: {ex.Message}";
                assistantTextBlock.Foreground = Brushes.IndianRed;
            }
            finally
            {
                _isProcessing = false;
                InputBox.IsEnabled = true;
                ChatScroll.ScrollToEnd();
                InputBox.Focus();
            }
        }

        private void AddMessage(string text, bool isUser)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(isUser ? ThemeService.GetUserBubbleColor() : ThemeService.GetAssistantBubbleColor()),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 5, 0, 5),
                MaxWidth = 280,
                HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                Child = new TextBlock { Text = text, Foreground = ThemeService.GetTextBrush(), TextWrapping = TextWrapping.Wrap, FontSize = 13 }
            };
            MessagePanel.Children.Add(border);
            ChatScroll.ScrollToEnd();
        }

        private TextBlock AddEmptyAssistantBubble()
        {
            var textBlock = new TextBlock { Text = "", Foreground = ThemeService.GetTextBrush(), TextWrapping = TextWrapping.Wrap, FontSize = 13 };
            var border = new Border
            {
                Background = new SolidColorBrush(ThemeService.GetAssistantBubbleColor()),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 5, 0, 5),
                MaxWidth = 280,
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = textBlock
            };
            MessagePanel.Children.Add(border);
            ChatScroll.ScrollToEnd();
            return textBlock;
        }
    }
}