using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace CHAT_WITH_FREND
{
    public partial class MainWindow : Window
    {
        private ChatClient? _chatClient;
        private string _clientName;

        public MainWindow()
        {
            InitializeComponent();
            _clientName = $"Client_{Environment.MachineName}_{DateTime.Now:HHmmss}";
            Title = $"Chat Client - {_clientName}";
            
            SendButton.IsEnabled = false;
            MessageTextBox.IsEnabled = false;
            
            InitializeEmojiPanel();
        }

        private void InitializeEmojiPanel()
        {
            // Danh sách emoji phổ biến (không trùng lặp)
            string[] emojis = new string[]
            {
                // Cảm xúc vui
                "😀", "😃", "😄", "😁", "😆", "😅", "😂", "🤣",
                "😊", "😇", "🙂", "🙃", "😉", "😌", "😍", "🥰",
                "😘", "😗", "😙", "😚", "😋", "😛", "😝", "😜",
                "🤪", "🤨", "🧐", "🤓", "😎", "🤩", "🥳", "😏",
                
                // Cảm xúc buồn/tức
                "😒", "😞", "😔", "😟", "😕", "🙁", "😣", "😖",
                "😫", "😩", "🥺", "😢", "😭", "😤", "😠", "😡",
                "🤬", "🤯", "😳", "🥵", "🥶", "😱", "😨", "😰",
                "😥", "😓",
                
                // Cảm xúc khác
                "🤗", "🤔", "🤭", "🤫", "🤥", "😶", "😐", "😑",
                "😬", "🙄", "😯", "😦", "😧", "😮", "😲", "🥱",
                "😴", "🤤", "😪", "😵", "🤐", "🥴", "🤢", "🤮",
                "🤧", "😷", "🤒", "🤕", "🤑", "🤠",
                
                // Trái tim
                "❤️", "🧡", "💛", "💚", "💙", "💜", "🖤", "🤍",
                "💔", "❣️", "💕", "💞", "💓", "💗", "💖", "💘",
                "💝", "💟",
                
                // Tay
                "👍", "👎", "👌", "✌️", "🤞", "🤟", "🤘", "🤙",
                "👏", "🙌", "👐", "🤲", "🤝", "🙏", "✍️", "💪",
                
                // Người
                "👶", "👧", "🧒", "👦", "👩", "🧑", "👨", "👵",
                "🧓", "👴", "🙍", "🙎", "🙅", "🙆", "💁", "🙋",
                "🧏", "🤦", "🤷",
                
                // Hoạt động
                "🚶", "🏃", "💃", "🕺", "👯", "🧘", "🧗", "🤺",
                "🏇", "⛷️", "🏂", "🏌️", "🏄", "🚣", "🏊", "⛹️",
                "🏋️", "🚴", "🚵", "🤸", "🤼", "🤽", "🤾", "🤹",
                "🛀", "🛌",
                
                // Đồ vật/Đồ chơi
                "🎮", "🕹️", "🎰", "🎲", "🃏", "🀄", "🎴", "🎯",
                "🎳", "🎪", "🎭", "🎨", "🎬", "🎤", "🎧", "🎼",
                "🎹", "🥁", "🎷", "🎺", "🎸", "🎻",
                
                // Thể thao
                "⚽", "🏀", "🏈", "⚾", "🎾", "🏐", "🏉", "🎱",
                "🏓", "🏸", "🥅", "🏒", "🏑", "🏏", "⛳", "🏹",
                "🎣", "🥊", "🥋", "🎽", "🛹", "🛷", "⛸️", "🥌",
                "🎿", "🏆", "🥇", "🥈", "🥉",
                
                // Biểu tượng đặc biệt
                "🔥", "💯", "⭐", "🌟", "✨", "💫", "💥", "💢",
                "💤", "💨", "👁️", "👀", "🧠", "🦷", "🦴", "💀",
                "👄", "👅", "👃", "👂",
                
                // Lễ hội
                "🎂", "🎄", "🎁", "🎀", "🎊", "🎉", "🎈"
            };

            // Tạo button cho mỗi emoji
            foreach (string emoji in emojis)
            {
                Button emojiButton = new Button
                {
                    Content = emoji,
                    FontSize = 24,
                    Width = 40,
                    Height = 40,
                    Margin = new Thickness(5),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand,
                    ToolTip = emoji
                };

                emojiButton.Click += (s, e) =>
                {
                    InsertEmoji(emoji);
                };

                emojiButton.MouseEnter += (s, e) =>
                {
                    emojiButton.Background = new SolidColorBrush(Color.FromArgb(30, 0, 0, 0));
                };

                emojiButton.MouseLeave += (s, e) =>
                {
                    emojiButton.Background = Brushes.Transparent;
                };

                EmojiContainer.Children.Add(emojiButton);
            }
        }

        private void EmojiButton_Click(object sender, RoutedEventArgs e)
        {
            // Toggle emoji panel visibility
            if (EmojiPanel.Visibility == Visibility.Visible)
            {
                EmojiPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                EmojiPanel.Visibility = Visibility.Visible;
                // Đảm bảo emoji panel hiển thị phía trên
                EmojiPanel.BringIntoView();
            }
        }

        private void CloseEmojiButton_Click(object sender, RoutedEventArgs e)
        {
            EmojiPanel.Visibility = Visibility.Collapsed;
        }

        private void InsertEmoji(string emoji)
        {
            int caretIndex = MessageTextBox.CaretIndex;
            string text = MessageTextBox.Text;
            MessageTextBox.Text = text.Insert(caretIndex, emoji);
            MessageTextBox.CaretIndex = caretIndex + emoji.Length;
            MessageTextBox.Focus();
            
            // Tự động đóng emoji panel sau khi chọn (tùy chọn - có thể comment nếu muốn giữ panel mở)
            // EmojiPanel.Visibility = Visibility.Collapsed;
        }

        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (_chatClient?.IsConnected == true)
            {
                // Disconnect
                _chatClient.Disconnect();
                _chatClient = null;
                UpdateConnectionStatus(false);
                ConnectButton.Content = "Kết nối";
                SendButton.IsEnabled = false;
                MessageTextBox.IsEnabled = false;
                AddMessage("Đã ngắt kết nối khỏi server.");
            }
            else
            {
                // Connect
                if (!int.TryParse(PortTextBox.Text, out int port))
                {
                    MessageBox.Show("Port không hợp lệ!", "Lỗi", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                _chatClient = new ChatClient();
                _chatClient.MessageReceived += OnMessageReceived;

                bool connected = await _chatClient.ConnectAsync(ServerIPTextBox.Text, port);
                if (connected)
                {
                    UpdateConnectionStatus(true);
                    ConnectButton.Content = "Ngắt kết nối";
                    SendButton.IsEnabled = true;
                    MessageTextBox.IsEnabled = true;
                    AddMessage($"Đã kết nối đến server {ServerIPTextBox.Text}:{port}");
                    MessageTextBox.Focus();
                }
            }
        }

        private void UpdateConnectionStatus(bool isConnected)
        {
            if (isConnected)
            {
                StatusText.Text = "Đã kết nối";
                StatusText.Foreground = new SolidColorBrush(Colors.Green);
            }
            else
            {
                StatusText.Text = "Chưa kết nối";
                StatusText.Foreground = new SolidColorBrush(Colors.Red);
            }
        }

        private void OnMessageReceived(string message)
        {
            AddMessage(message);
            
            // Kiểm tra nếu server đã ngắt kết nối
            if (message.Contains("đã mất kết nối") || message.Contains("đã ngắt kết nối"))
            {
                UpdateConnectionStatus(false);
                ConnectButton.Content = "Kết nối";
                SendButton.IsEnabled = false;
                MessageTextBox.IsEnabled = false;
                _chatClient = null;
            }
        }

        private void AddMessage(string message)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            string formattedMessage = $"[{timestamp}] {message}";
            
            MessagesListBox.Items.Add(formattedMessage);
            
            // Tự động cuộn xuống tin nhắn mới nhất
            if (MessagesListBox.Items.Count > 0)
            {
                MessagesListBox.ScrollIntoView(MessagesListBox.Items[MessagesListBox.Items.Count - 1]);
            }
        }

        private async void SendButton_Click(object sender, RoutedEventArgs e)
        {
            await SendMessage();
        }

        private void MessageTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && SendButton.IsEnabled)
            {
                e.Handled = true;
                _ = SendMessage();
            }
        }

        private async Task SendMessage()
        {
            string message = MessageTextBox.Text.Trim();
            if (string.IsNullOrEmpty(message))
                return;

            if (_chatClient?.IsConnected == true)
            {
                string fullMessage = $"{_clientName}: {message}";
                await _chatClient.SendMessageAsync(fullMessage);
                AddMessage($"Bạn: {message}");
                MessageTextBox.Clear();
                MessageTextBox.Focus();
            }
            else
            {
                MessageBox.Show("Chưa kết nối đến server!", "Lỗi", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _chatClient?.Disconnect();
            base.OnClosed(e);
        }
    }
}