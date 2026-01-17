using Emoji.Wpf;
using Microsoft.Win32;
using Shared;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace CHAT_WITH_FREND
{
    // Cập nhật Class MessageDisplay
    public class MessageDisplay
    {
        public object Content { get; set; }
        public string Timestamp { get; set; }
        public bool IsMine { get; set; }
        public string Sender { get; set; } // Thêm Sender để hiển thị dạng tab
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
        private void ToggleEmoji_Click(object sender, RoutedEventArgs e)
        {
            EmojiPopup.IsOpen = !EmojiPopup.IsOpen;
            if (EmojiPopup.IsOpen) EmojiPicker.Focus();
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
                picker.Selection = string.Empty;
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

                    // Với file lớn 1GB, cần đọc cẩn thận để tránh lỗi bộ nhớ đệm
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
            bool isMine = sender == _username;
            var emojiBlock = new Emoji.Wpf.TextBlock
            {
                Text = msg,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 15
            };

            // Truyền sender vào property Sender riêng để XAML hiển thị trên tab
            MessagesListBox.Items.Add(new MessageDisplay
            {
                Content = emojiBlock,
                Timestamp = time,
                IsMine = isMine,
                Sender = isMine ? "Bạn" : sender
            });
            ScrollToBottom();
        }

        private void AddImageToUI(string sender, byte[] data, string time)
        {
            bool isMine = sender == _username;
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

                // Không cần add TextBlock tên người gửi vào Panel nữa vì đã có Tab ở trên
                StackPanel panel = new StackPanel();
                var imgControl = new System.Windows.Controls.Image
                {
                    Source = bitmap,
                    MaxWidth = 300
                };
                panel.Children.Add(imgControl);

                MessagesListBox.Items.Add(new MessageDisplay
                {
                    Content = panel,
                    Timestamp = time,
                    IsMine = isMine,
                    Sender = isMine ? "Bạn" : sender
                });
                ScrollToBottom();
            }
            catch { }
        }

        private void AddFileToUI(string sender, string fileName, byte[] data, string time)
        {
            bool isMine = sender == _username;
            StackPanel panel = new StackPanel();

            Button btn = new Button
            {
                Content = $"📂 Tải xuống: {fileName}",
                Background = System.Windows.Media.Brushes.AliceBlue,
                Padding = new Thickness(15, 8, 15, 8),
                Cursor = System.Windows.Input.Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Left
            };

            btn.Click += (s, e) =>
            {
                SaveFileDialog saveDlg = new SaveFileDialog { FileName = fileName };
                if (saveDlg.ShowDialog() == true)
                {
                    try
                    {
                        File.WriteAllBytes(saveDlg.FileName, data);
                        MessageBox.Show("Đã lưu file thành công!");
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Lỗi lưu file: " + ex.Message);
                    }
                }
            };
            panel.Children.Add(btn);

            MessagesListBox.Items.Add(new MessageDisplay
            {
                Content = panel,
                Timestamp = time,
                IsMine = isMine,
                Sender = isMine ? "Bạn" : sender
            });
            ScrollToBottom();
        }

        // --- GỬI DỮ LIỆU ---
        private void SendButton_Click(object sender, RoutedEventArgs e)
        {
            // Gửi tin nhắn text
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

            // Gửi file đính kèm
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

            EmojiPopup.IsOpen = false;
        }

        private void SendPacket(ChatPacket packet)
        {
            try
            {
                // Lưu ý: Serialize file 1GB sang JSON sẽ tốn rất nhiều RAM (khoảng 3-4GB RAM tạm thời).
                // Nếu muốn tối ưu hơn phải viết lại logic gửi Stream thay vì JSON, 
                // nhưng để giữ cấu trúc code cũ thì cách này là nhanh nhất.
                string json = JsonSerializer.Serialize(packet);
                byte[] data = Encoding.UTF8.GetBytes(json);
                byte[] length = BitConverter.GetBytes(data.Length);

                _stream.Write(length, 0, 4);
                _stream.Write(data, 0, data.Length);
                _stream.Flush();
            }
            catch
            {
                MessageBox.Show("Mất kết nối hoặc file quá lớn không đủ bộ nhớ!");
                Close();
            }
        }

        // --- ĐÍNH KÈM FILE/ẢNH (XỬ LÝ ASYNC ĐỂ HIỆN PROGRESS) ---
        private void AttachImage_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dlg = new OpenFileDialog { Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp" };
            if (dlg.ShowDialog() == true) _ = PrepareAttachment(dlg.FileName, PacketType.Image);
        }

        private void AttachFile_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dlg = new OpenFileDialog();
            if (dlg.ShowDialog() == true) _ = PrepareAttachment(dlg.FileName, PacketType.File);
        }

        private async Task PrepareAttachment(string path, PacketType type)
        {
            try
            {
                FileInfo fi = new FileInfo(path);
                // Giới hạn 1GB (1024 * 1024 * 1024)
                if (fi.Length > 1024L * 1024 * 1024)
                {
                    MessageBox.Show("File quá lớn (>1GB).");
                    return;
                }

                // Hiển thị UI Progress
                FileProgressBar.Visibility = Visibility.Visible;
                FileProgressBar.Value = 0;
                PreviewBorder.Visibility = Visibility.Visible;
                PreviewFileName.Text = "Đang đọc file...";

                // Đọc file Async theo từng chunk để cập nhật Progress Bar
                byte[] data = new byte[fi.Length];
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true))
                {
                    long totalBytes = fi.Length;
                    long totalRead = 0;
                    byte[] buffer = new byte[81920]; // Đọc mỗi lần 80KB
                    int read;

                    while ((read = await fs.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        Array.Copy(buffer, 0, data, totalRead, read);
                        totalRead += read;

                        // Cập nhật thanh tiến trình
                        double percent = (double)totalRead / totalBytes * 100;
                        FileProgressBar.Value = percent;
                    }
                }

                _pendingFileData = data;
                _pendingFileName = System.IO.Path.GetFileName(path);
                _pendingType = type;

                // Ẩn Progress Bar khi xong
                FileProgressBar.Visibility = Visibility.Collapsed;

                if (type == PacketType.Image)
                {
                    BitmapImage bitmap = new BitmapImage();
                    using (var mem = new MemoryStream(_pendingFileData))
                    {
                        mem.Position = 0;
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.StreamSource = mem;
                        bitmap.EndInit();
                    }
                    PreviewImage.Source = bitmap;
                    PreviewImage.Visibility = Visibility.Visible;
                    PreviewFileName.Text = "";
                }
                else
                {
                    PreviewImage.Visibility = Visibility.Collapsed;
                    PreviewFileName.Text = $"📄 {_pendingFileName} ({(fi.Length / 1024.0 / 1024.0):F2} MB)";
                }
            }
            catch (OutOfMemoryException)
            {
                MessageBox.Show("Không đủ bộ nhớ RAM để load file này!");
                RemoveAttachment_Click(null, null);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi đọc file: " + ex.Message);
                RemoveAttachment_Click(null, null);
            }
        }

        private void RemoveAttachment_Click(object sender, RoutedEventArgs e)
        {
            _pendingFileData = null;
            _pendingFileName = "";
            PreviewBorder.Visibility = Visibility.Collapsed;
            FileProgressBar.Visibility = Visibility.Collapsed;
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