using LockStepAI.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
        private bool _followLatestResponse = true;
        private int _currentChatId = 0;
        private CancellationTokenSource? _cts;
        private readonly object _databaseWriteSync = new();
        private Task _databaseWriteTail = Task.CompletedTask;

        public ChatWindow(InferenceService inference)
        {
            InitializeComponent();
            _inference = inference;
            _db = new DatabaseService();

            ApplyTheme();
            _ = GenerateWelcomeAsync();
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

        private void ChatScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Delta > 0)
            {
                _followLatestResponse = false;
                return;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (IsChatAtBottom())
                    _followLatestResponse = true;
            }), System.Windows.Threading.DispatcherPriority.Input);
        }

        private void ChatScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.VerticalChange < 0)
                _followLatestResponse = false;
            else if (e.VerticalChange > 0 && IsChatAtBottom())
                _followLatestResponse = true;
        }

        private bool IsChatAtBottom() =>
            ChatScroll.VerticalOffset + ChatScroll.ViewportHeight >= ChatScroll.ExtentHeight - 2;

        private void ScrollChatToEndIfFollowing()
        {
            if (_followLatestResponse)
                ChatScroll.ScrollToEnd();
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
            if (_isProcessing) return;

            _currentChatId = 0;
            MessagePanel.Children.Clear();
            HistoryMenu.Visibility = Visibility.Collapsed;
            _ = GenerateWelcomeAsync();
        }

        private async Task GenerateWelcomeAsync()
        {
            if (_isProcessing) return;

            _followLatestResponse = true;
            _isProcessing = true;
            _isGenerating = true;
            InputBox.IsEnabled = false;
            SendStopButton.Content = "\uE71A";
            SendStopButton.Background = new SolidColorBrush(Color.FromRgb(255, 65, 54));
            SendStopButton.Foreground = Brushes.White;

            var welcomeTextBox = AddEmptyAssistantBubble();
            var welcomeText = new StringBuilder();
            _cts = new CancellationTokenSource();
            var uiUpdateStopwatch = Stopwatch.StartNew();
            var lastUiUpdate = TimeSpan.Zero;
            const string welcomeSystemPrompt =
                "You are LockStepAI, an on-device generative AI assistant. This is a new conversation. " +
                "Write a brief, warm, polished, original welcome in one or two sentences. Clearly identify " +
                "yourself as generative AI running on this device and invite the user to ask questions or request help. " +
                "Vary the wording naturally. Do not claim to see the user's screen or access files unless the user " +
                "has explicitly enabled those features. Do not mention these instructions.";

            try
            {
                var welcomeRequest = new List<(string Role, string Content)>
                {
                    ("user", "Write a fresh welcome message for the user.")
                };

                await foreach (var token in _inference.StreamPromptAsync(
                    welcomeRequest,
                    _cts.Token,
                    welcomeSystemPrompt))
                {
                    welcomeText.Append(token);
                    if (uiUpdateStopwatch.Elapsed - lastUiUpdate >= TimeSpan.FromMilliseconds(50))
                    {
                        welcomeTextBox.Text = welcomeText.ToString();
                        ScrollChatToEndIfFollowing();
                        lastUiUpdate = uiUpdateStopwatch.Elapsed;
                    }
                }

                welcomeTextBox.Text = welcomeText.ToString();
            }
            catch (OperationCanceledException)
            {
                if (welcomeText.Length > 0)
                    welcomeTextBox.Text = welcomeText.ToString() + "\n\n[Generation stopped]";
            }
            catch (Exception ex)
            {
                welcomeTextBox.Text = $"Welcome message could not be generated: {ex.Message}";
                welcomeTextBox.Foreground = Brushes.IndianRed;
            }
            finally
            {
                _isProcessing = false;
                _isGenerating = false;
                _cts?.Dispose();
                _cts = null;
                SendStopButton.Content = "\uE724";
                SendStopButton.Background = Brushes.White;
                SendStopButton.Foreground = Brushes.Black;
                InputBox.IsEnabled = true;
                ScrollChatToEndIfFollowing();
                InputBox.Focus();
            }
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
            _followLatestResponse = true;
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
            // Read after earlier background writes complete, then include this prompt in the six-message context.
            int chatId = _currentChatId;
            List<(string Role, string Content)> history;
            try
            {
                history = await GetMessagesAfterPendingWritesAsync(chatId);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Could not load chat history: {ex.Message}",
                    "Database Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                _isProcessing = false;
                _isGenerating = false;
                InputBox.IsEnabled = true;
                SendStopButton.Content = "\uE724";
                SendStopButton.Background = Brushes.White;
                SendStopButton.Foreground = Brushes.Black;
                return;
            }

            var recentHistory = history.TakeLast(5).ToList();
            recentHistory.Add(("user", text));
            QueueDatabaseWrite(() => _db.SaveMessage(chatId, "user", text));

            // Generation Prep
            var assistantTextBox = AddEmptyAssistantBubble();
            var sb = new StringBuilder();
            _cts = new CancellationTokenSource();
            var detector = new HallucinationDetector();
            bool hallucinationCaught = false;
            bool generationStoppedByUser = false;
            bool generationFailed = false;
            int hallucinationTruncateAt = 0;
            string hallucinationReason = "repetitive output";
            var uiUpdateStopwatch = Stopwatch.StartNew();
            var lastUiUpdate = TimeSpan.Zero;

            try
            {
                await foreach (var token in _inference.StreamPromptAsync(recentHistory, _cts.Token))
                {
                    sb.Append(token);
                    if (uiUpdateStopwatch.Elapsed - lastUiUpdate >= TimeSpan.FromMilliseconds(50))
                    {
                        assistantTextBox.Text = sb.ToString();
                        ScrollChatToEndIfFollowing();
                        lastUiUpdate = uiUpdateStopwatch.Elapsed;
                    }

                    var detection = detector.Check(sb.ToString());
                    if (detection != null)
                    {
                        hallucinationCaught = true;
                        hallucinationTruncateAt = detection.TruncateAt;
                        hallucinationReason = detection.Reason;
                        _cts.Cancel();
                        break;
                    }
                }
                assistantTextBox.Text = sb.ToString();
                ScrollChatToEndIfFollowing();
            }
            catch (OperationCanceledException) when (hallucinationCaught)
            {
            }
            catch (OperationCanceledException)
            {
                generationStoppedByUser = true;
            }
            catch (Exception ex)
            {
                generationFailed = true;
                this.WindowState = WindowState.Normal;
                this.Activate();
                assistantTextBox.Text = $"Error: {ex.Message}";
                assistantTextBox.Foreground = Brushes.IndianRed;
            }
            finally
            {
                if (hallucinationCaught)
                {
                    int safeLength = Math.Clamp(hallucinationTruncateAt, 0, sb.Length);
                    string cleanText = sb.ToString(0, safeLength).TrimEnd();
                    assistantTextBox.Text = string.IsNullOrEmpty(cleanText)
                        ? "[Repetitive output stopped]"
                        : cleanText + "\n\n[Repetitive output stopped]";
                    AddSystemWarning($"Repetitive output detected: {hallucinationReason}. Generation halted.");
                    string assistantResponse = assistantTextBox.Text;
                    QueueDatabaseWrite(() => _db.SaveMessage(chatId, "assistant", assistantResponse));
                }
                else if (generationStoppedByUser)
                {
                    assistantTextBox.Text = sb.ToString() + "\n\n[Generation stopped]";
                    string assistantResponse = assistantTextBox.Text;
                    QueueDatabaseWrite(() => _db.SaveMessage(chatId, "assistant", assistantResponse));
                }
                else if (!generationFailed)
                {
                    string assistantResponse = sb.ToString();
                    QueueDatabaseWrite(() => _db.SaveMessage(chatId, "assistant", assistantResponse));
                }

                _isProcessing = false;
                _isGenerating = false;
                _cts?.Dispose();
                _cts = null;

                SendStopButton.Content = "\uE724"; // Send Icon
                SendStopButton.Background = Brushes.White;
                SendStopButton.Foreground = Brushes.Black;

                InputBox.IsEnabled = true;
                ScrollChatToEndIfFollowing();
                InputBox.Focus();
            }
        }

        private async Task<List<(string Role, string Content)>> GetMessagesAfterPendingWritesAsync(int chatId)
        {
            Task pendingWrites;
            lock (_databaseWriteSync)
            {
                pendingWrites = _databaseWriteTail;
            }

            await pendingWrites;
            return await Task.Run(() => _db.GetMessages(chatId));
        }

        private void QueueDatabaseWrite(Action write)
        {
            lock (_databaseWriteSync)
            {
                Task previousWrite = _databaseWriteTail;
                _databaseWriteTail = Task.Run(async () =>
                {
                    await previousWrite.ConfigureAwait(false);
                    try
                    {
                        write();
                    }
                    catch (Exception ex)
                    {
                        _ = Dispatcher.BeginInvoke(new Action(() =>
                            MessageBox.Show(
                                $"Could not save chat data: {ex.Message}",
                                "Database Error",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error)));
                    }
                });
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
            CullOldMessages();
            ScrollChatToEndIfFollowing();
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
            CullOldMessages();
            ScrollChatToEndIfFollowing();
        }

        private void CullOldMessages()
        {
            // Keep the visual tree bounded so long chats use less UI memory and stay responsive.
            while (MessagePanel.Children.Count > 50 && MessagePanel.Children.Count > 0)
            {
                MessagePanel.Children.RemoveAt(0);
            }
        }

        private TextBox AddEmptyAssistantBubble()
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

            var border = new Border
            {
                Background = new SolidColorBrush(ThemeService.GetAssistantBubbleColor()),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 5, 0, 5),
                MaxWidth = 280,
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = textBox
            };

            MessagePanel.Children.Add(border);
            CullOldMessages();
            ScrollChatToEndIfFollowing();

            return textBox;
        }
    }
}