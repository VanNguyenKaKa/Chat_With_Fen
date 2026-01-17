using Emoji.Wpf; // Namespace cho thư viện Emoji
using Microsoft.Win32;
using Shared;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls; // Namespace cho các control chuẩn WPF
using System.Windows.Media.Imaging;

namespace CHAT_WITH_FREND
{
    public class MessageDisplay
    {
        public object Content { get; set; }
        public string Timestamp { get; set; }
    }

    public partial class MainWindow : Window
    {
        private TcpClient _client;
        private NetworkStream _stream;
        private string _username;
        private string _targetUser = "ALL";

        private byte[] _pendingFileData = null;
        private string _pendingFileName = "";
        private PacketType _pendingType = PacketType.Message;

        public MainWindow(TcpClient client, string username, string serverIP)
        {
            InitializeComponent();

            _client = client;
            _username = username;
            _stream = _client.GetStream();

            CurrentUserText.Text = $"{_username} ({serverIP})";

            SendPacket(new ChatPacket
            {
                Type = PacketType.Login,
                Sender = _username,
                Time = DateTime.Now
            });

            Task.Run(ReceiveMessages);
        }

        // --- XỬ LÝ EMOJI ---
        //private void ToggleEmoji_Click(object sender, RoutedEventArgs e)
        //{
        //    // Đóng/Mở Popup
        //    EmojiPopup.IsOpen = !EmojiPopup.IsOpen;
        //}
        private void ToggleEmoji_Click(object sender, RoutedEventArgs e)
        {
            // Toggle thủ công: nếu đang mở thì đóng, ngược lại mở
            EmojiPopup.IsOpen = !EmojiPopup.IsOpen;

            // Optional: Nếu mở, focus vào Picker để dễ chọn emoji hơn
            if (EmojiPopup.IsOpen)
            {
                EmojiPicker.Focus();
            }
        }

        private void EmojiPicker_SelectionChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            var picker = sender as Emoji.Wpf.Picker;
            if (!string.IsNullOrEmpty(picker.Selection))
            {
                int caret = MessageTextBox.CaretIndex;
                MessageTextBox.Text = MessageTextBox.Text.Insert(caret, picker.Selection);
                MessageTextBox.CaretIndex = caret + picker.Selection.Length;
                MessageTextBox.Focus();

                // Reset lựa chọn
                picker.Selection = string.Empty;

                // Tùy chọn: Đóng popup sau khi chọn (bỏ comment nếu muốn)
                EmojiPopup.IsOpen = false;
            } 
        }

        // --- NHẬN TIN NHẮN ---
        private async Task ReceiveMessages()
        {
            try
            {
                while (_client.Connected)
                {
                    byte[] lengthBuffer = new byte[4];
                    int read = await _stream.ReadAsync(lengthBuffer, 0, 4);
                    if (read == 0) break;
                    int length = BitConverter.ToInt32(lengthBuffer, 0);

                    byte[] buffer = new byte[length];
                    int totalRead = 0;
                    while (totalRead < length)
                    {
                        int r = await _stream.ReadAsync(buffer, totalRead, length - totalRead);
                        if (r == 0) break;
                        totalRead += r;
                    }

                    string json = Encoding.UTF8.GetString(buffer);
                    try
                    {
                        var packet = JsonSerializer.Deserialize<ChatPacket>(json);
                        if (packet != null)
                        {
                            Dispatcher.Invoke(() => ProcessPacket(packet));
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private void ProcessPacket(ChatPacket packet)
        {
            string timeStr = packet.Time.ToString("HH:mm");

            switch (packet.Type)
            {
                case PacketType.UserList:
                    if (packet.Message != null)
                    {
                        var users = JsonSerializer.Deserialize<List<string>>(packet.Message.ToString());
                        var displayList = new List<string> { "Chat Nhóm" };
                        if (users != null)
                        {
                            displayList.AddRange(users.Where(u => u != _username));
                        }
                        UserListBox.ItemsSource = displayList;
                    }
                    break;

                case PacketType.Message:
                case PacketType.PrivateMessage:
                    AddMessageToUI(packet.Sender, packet.Message, timeStr);
                    break;

                case PacketType.Image:
                    AddImageToUI(packet.Sender, packet.FileData, timeStr);
                    break;

                case PacketType.File:
                    AddFileToUI(packet.Sender, packet.FileName, packet.FileData, timeStr);
                    break;
            }
        }

        // --- UI HELPERS ---
        private void AddMessageToUI(string sender, string msg, string time)
        {
            // Sử dụng Emoji.Wpf.TextBlock để hiển thị icon màu
            var emojiBlock = new Emoji.Wpf.TextBlock();
            emojiBlock.Text = $"{sender}: {msg}";
            emojiBlock.TextWrapping = TextWrapping.Wrap;
            emojiBlock.FontSize = 15;

            MessagesListBox.Items.Add(new MessageDisplay { Content = emojiBlock, Timestamp = time });
            ScrollToBottom();
        }

        private void AddImageToUI(string sender, byte[] data, string time)
        {
            try
            {
                BitmapImage bitmap = new BitmapImage();
                using (var mem = new MemoryStream(data))
                {
                    mem.Position = 0;
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = mem;
                    bitmap.EndInit();
                }

                StackPanel panel = new StackPanel();

                // Sử dụng System.Windows.Controls.TextBlock cho tên người gửi
                var nameBlock = new System.Windows.Controls.TextBlock
                {
                    Text = sender,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 0, 5)
                };
                panel.Children.Add(nameBlock);

                // Sử dụng System.Windows.Controls.Image cho ảnh
                var imgControl = new System.Windows.Controls.Image
                {
                    Source = bitmap,
                    MaxWidth = 300
                };
                panel.Children.Add(imgControl);

                MessagesListBox.Items.Add(new MessageDisplay { Content = panel, Timestamp = time });
                ScrollToBottom();
            }
            catch { }
        }

        private void AddFileToUI(string sender, string fileName, byte[] data, string time)
        {
            StackPanel panel = new StackPanel();

            // Sử dụng System.Windows.Controls.TextBlock
            var nameBlock = new System.Windows.Controls.TextBlock
            {
                Text = sender,
                FontWeight = FontWeights.Bold
            };
            panel.Children.Add(nameBlock);

            Button btn = new Button
            {
                Content = $"📂 Tải xuống: {fileName}",
                Background = System.Windows.Media.Brushes.AliceBlue,
                Padding = new Thickness(15, 8, 15, 8),
                Cursor = System.Windows.Input.Cursors.Hand,
                Margin = new Thickness(0, 5, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left
            };

            btn.Click += (s, e) =>
            {
                SaveFileDialog saveDlg = new SaveFileDialog { FileName = fileName };
                if (saveDlg.ShowDialog() == true)
                {
                    File.WriteAllBytes(saveDlg.FileName, data);
                    MessageBox.Show("Đã lưu file thành công!");
                }
            };
            panel.Children.Add(btn);

            MessagesListBox.Items.Add(new MessageDisplay { Content = panel, Timestamp = time });
            ScrollToBottom();
        }

        // --- GỬI DỮ LIỆU ---
        private void SendButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(MessageTextBox.Text))
            {
                var packet = new ChatPacket
                {
                    Type = _targetUser == "ALL" ? PacketType.Message : PacketType.PrivateMessage,
                    Sender = _username,
                    Target = _targetUser,
                    Message = MessageTextBox.Text,
                    Time = DateTime.Now
                };
                SendPacket(packet);
                MessageTextBox.Text = "";
            }

            if (_pendingFileData != null)
            {
                var packet = new ChatPacket
                {
                    Type = _pendingType,
                    Sender = _username,
                    Target = _targetUser,
                    FileData = _pendingFileData,
                    FileName = _pendingFileName,
                    Time = DateTime.Now
                };
                SendPacket(packet);
                RemoveAttachment_Click(null, null);
            }

            EmojiPopup.IsOpen = false; // Đóng popup khi gửi
        }

        private void SendPacket(ChatPacket packet)
        {
            try
            {
                string json = JsonSerializer.Serialize(packet);
                byte[] data = Encoding.UTF8.GetBytes(json);
                byte[] length = BitConverter.GetBytes(data.Length);

                _stream.Write(length, 0, 4);
                _stream.Write(data, 0, data.Length);
                _stream.Flush();
            }
            catch
            {
                MessageBox.Show("Mất kết nối với Server!");
                Close();
            }
        }

        // --- ĐÍNH KÈM FILE/ẢNH ---
        private void AttachImage_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dlg = new OpenFileDialog { Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp" };
            if (dlg.ShowDialog() == true) PrepareAttachment(dlg.FileName, PacketType.Image);
        }

        private void AttachFile_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dlg = new OpenFileDialog();
            if (dlg.ShowDialog() == true) PrepareAttachment(dlg.FileName, PacketType.File);
        }

        private void PrepareAttachment(string path, PacketType type)
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                if (bytes.Length > 50 * 1024 * 1024) { MessageBox.Show("File > 50MB"); return; }

                _pendingFileData = bytes;
                _pendingFileName = System.IO.Path.GetFileName(path);
                _pendingType = type;

                if (type == PacketType.Image)
                {
                    BitmapImage bitmap = new BitmapImage();
                    using (var mem = new MemoryStream(bytes))
                    {
                        mem.Position = 0; bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = mem; bitmap.EndInit();
                    }
                    PreviewImage.Source = bitmap;
                    PreviewImage.Visibility = Visibility.Visible;
                    PreviewFileName.Text = "";
                }
                else
                {
                    PreviewImage.Visibility = Visibility.Collapsed;
                    PreviewFileName.Text = $"📄 {_pendingFileName}";
                }
                PreviewBorder.Visibility = Visibility.Visible;
            }
            catch (Exception ex) { MessageBox.Show("Lỗi đọc file: " + ex.Message); }
        }

        private void RemoveAttachment_Click(object sender, RoutedEventArgs e)
        {
            _pendingFileData = null;
            _pendingFileName = "";
            PreviewBorder.Visibility = Visibility.Collapsed;
        }

        // --- SỰ KIỆN KHÁC ---
        private void UserListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (UserListBox.SelectedItem == null) return;
            string selected = UserListBox.SelectedItem.ToString();
            _targetUser = (selected == "Chat Nhóm") ? "ALL" : selected;
            ChatTitleText.Text = (selected == "Chat Nhóm") ? "Chat Nhóm" : $"Chat riêng: {selected}";
        }

        private void MessageTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter) SendButton_Click(null, null);
        }

        private void ScrollToBottom()
        {
            if (MessagesListBox.Items.Count > 0)
                MessagesListBox.ScrollIntoView(MessagesListBox.Items[MessagesListBox.Items.Count - 1]);
        }
    }
}

