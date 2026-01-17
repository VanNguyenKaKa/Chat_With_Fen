using Emoji.Wpf;
using Microsoft.Win32;
using Shared;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace CHAT_WITH_FREND
{
    public class MessageDisplay
    {
        public object Content { get; set; } = null!;
        public string Timestamp { get; set; } = "";
        public bool IsMine { get; set; }
        public string Sender { get; set; } = "";
    }

    // Class lưu trạng thái nhận file chunked
    public class IncomingFileTransfer
    {
        public string FileId { get; set; } = "";
        public string FileName { get; set; } = "";
        public string Sender { get; set; } = "";
        public int TotalChunks { get; set; }
        public long TotalSize { get; set; }
        public int ReceivedChunks { get; set; }
        public MemoryStream DataStream { get; set; } = new();
        public DateTime StartTime { get; set; } = DateTime.Now;
    }

    public partial class MainWindow : Window
    {
        private TcpClient _client;
        private NetworkStream _stream;
        private string _username;
        private string _targetUser = "ALL";

        private byte[]? _pendingFileData = null;
        private string _pendingFileName = "";
        private PacketType _pendingType = PacketType.Message;

        // Lưu trữ file đang nhận (chunked)
        private ConcurrentDictionary<string, IncomingFileTransfer> _incomingFiles = new();

        // Flag đang gửi file
        private bool _isSendingFile = false;

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
            if (!string.IsNullOrEmpty(picker?.Selection))
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
                        var users = JsonSerializer.Deserialize<List<string>>(packet.Message);
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
                    if (packet.FileData != null)
                        AddImageToUI(packet.Sender, packet.FileData, timeStr);
                    break;

                case PacketType.File:
                    if (packet.FileData != null)
                        AddFileToUI(packet.Sender, packet.FileName, packet.FileData, timeStr);
                    break;

                // ===== CHUNKED FILE TRANSFER =====
                case PacketType.FileStart:
                    HandleIncomingFileStart(packet);
                    break;

                case PacketType.FileChunk:
                    HandleIncomingFileChunk(packet);
                    break;

                case PacketType.FileEnd:
                    HandleIncomingFileEnd(packet, timeStr);
                    break;
            }
        }

        // ===== XỬ LÝ NHẬN FILE CHUNKED =====
        private void HandleIncomingFileStart(ChatPacket packet)
        {
            if (packet.Sender == _username) return;

            var transfer = new IncomingFileTransfer
            {
                FileId = packet.FileId,
                FileName = packet.FileName,
                Sender = packet.Sender,
                TotalChunks = packet.TotalChunks,
                TotalSize = packet.TotalFileSize,
                ReceivedChunks = 0,
                DataStream = new MemoryStream(),
                StartTime = DateTime.Now
            };

            _incomingFiles[packet.FileId] = transfer;

            AddSystemMessage($"📥 {packet.Sender} đang gửi file '{packet.FileName}' ({FormatSize(packet.TotalFileSize)})...");

            FileProgressBar.Visibility = Visibility.Visible;
            FileProgressBar.Value = 0;
            PreviewBorder.Visibility = Visibility.Visible;
            PreviewImage.Visibility = Visibility.Collapsed;
            PreviewFileName.Text = $"📥 Đang nhận: {packet.FileName} (0%)";
        }

        private void HandleIncomingFileChunk(ChatPacket packet)
        {
            if (packet.Sender == _username) return;

            if (_incomingFiles.TryGetValue(packet.FileId, out var transfer))
            {
                if (packet.FileData != null)
                {
                    transfer.DataStream.Write(packet.FileData, 0, packet.FileData.Length);
                    transfer.ReceivedChunks++;

                    double progress = (double)transfer.ReceivedChunks / transfer.TotalChunks * 100;
                    FileProgressBar.Value = progress;
                    PreviewFileName.Text = $"📥 Đang nhận: {transfer.FileName} ({progress:F0}%)";
                }
            }
        }

        private void HandleIncomingFileEnd(ChatPacket packet, string timeStr)
        {
            if (packet.Sender == _username) return;

            if (_incomingFiles.TryRemove(packet.FileId, out var transfer))
            {
                byte[] fileData = transfer.DataStream.ToArray();
                transfer.DataStream.Dispose();

                FileProgressBar.Visibility = Visibility.Collapsed;
                PreviewBorder.Visibility = Visibility.Collapsed;

                string ext = Path.GetExtension(transfer.FileName).ToLower();
                bool isImage = ext is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif";

                if (isImage)
                {
                    AddImageToUI(transfer.Sender, fileData, timeStr);
                }
                else
                {
                    AddFileToUI(transfer.Sender, transfer.FileName, fileData, timeStr);
                }

                var elapsed = DateTime.Now - transfer.StartTime;
                AddSystemMessage($"✅ Đã nhận file '{transfer.FileName}' từ {transfer.Sender} ({FormatSize(fileData.Length)}) trong {elapsed.TotalSeconds:F1}s");
            }
        }

        // ===== GỬI FILE LỚN VỚI CHUNKED TRANSFER =====
        private async Task SendLargeFileAsync(string filePath, PacketType fileType)
        {
            if (_isSendingFile)
            {
                MessageBox.Show("Đang gửi file khác, vui lòng đợi!");
                return;
            }

            FileInfo fileInfo = new FileInfo(filePath);
            if (fileInfo.Length > FileTransferConfig.MaxFileSize)
            {
                MessageBox.Show($"File quá lớn! Giới hạn: {FormatSize(FileTransferConfig.MaxFileSize)}");
                return;
            }

            _isSendingFile = true;
            string fileId = Guid.NewGuid().ToString();
            string fileName = Path.GetFileName(filePath);
            int totalChunks = (int)Math.Ceiling((double)fileInfo.Length / FileTransferConfig.ChunkSize);

            try
            {
                FileProgressBar.Visibility = Visibility.Visible;
                FileProgressBar.Value = 0;
                PreviewBorder.Visibility = Visibility.Visible;
                PreviewImage.Visibility = Visibility.Collapsed;
                PreviewFileName.Text = $"📤 Đang gửi: {fileName} (0%)";

                // 1. Gửi FileStart
                var startPacket = new ChatPacket
                {
                    Type = PacketType.FileStart,
                    Sender = _username,
                    Target = _targetUser,
                    FileName = fileName,
                    FileId = fileId,
                    TotalChunks = totalChunks,
                    TotalFileSize = fileInfo.Length,
                    Time = DateTime.Now
                };
                SendPacket(startPacket);

                // 2. Đọc và gửi từng chunk
                await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    bufferSize: FileTransferConfig.ChunkSize, useAsync: true);

                byte[] buffer = new byte[FileTransferConfig.ChunkSize];
                int chunkIndex = 0;
                int bytesRead;

                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    byte[] chunkData = bytesRead == buffer.Length
                        ? buffer.ToArray()
                        : buffer[..bytesRead];

                    var chunkPacket = new ChatPacket
                    {
                        Type = PacketType.FileChunk,
                        Sender = _username,
                        Target = _targetUser,
                        FileId = fileId,
                        ChunkIndex = chunkIndex,
                        TotalChunks = totalChunks,
                        FileData = chunkData,
                        Time = DateTime.Now
                    };
                    SendPacket(chunkPacket);

                    chunkIndex++;

                    double progress = (double)chunkIndex / totalChunks * 100;
                    FileProgressBar.Value = progress;
                    PreviewFileName.Text = $"📤 Đang gửi: {fileName} ({progress:F0}%)";

                    // Cho phép UI cập nhật
                    await Task.Delay(1);
                }

                // 3. Gửi FileEnd
                var endPacket = new ChatPacket
                {
                    Type = PacketType.FileEnd,
                    Sender = _username,
                    Target = _targetUser,
                    FileId = fileId,
                    FileName = fileName,
                    TotalFileSize = fileInfo.Length,
                    Time = DateTime.Now
                };
                SendPacket(endPacket);

                AddSystemMessage($"✅ Đã gửi file '{fileName}' ({FormatSize(fileInfo.Length)})");

                FileProgressBar.Visibility = Visibility.Collapsed;
                PreviewBorder.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi gửi file: {ex.Message}");
                FileProgressBar.Visibility = Visibility.Collapsed;
                PreviewBorder.Visibility = Visibility.Collapsed;
            }
            finally
            {
                _isSendingFile = false;
            }
        }

        private void AddSystemMessage(string message)
        {
            var textBlock = new System.Windows.Controls.TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                Foreground = System.Windows.Media.Brushes.Gray,
                FontStyle = FontStyles.Italic
            };

            MessagesListBox.Items.Add(new MessageDisplay
            {
                Content = textBlock,
                Timestamp = DateTime.Now.ToString("HH:mm"),
                IsMine = false,
                Sender = "System"
            });
            ScrollToBottom();
        }

        private static string FormatSize(long bytes)
        {
            string[] sizes = ["B", "KB", "MB", "GB"];
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:F2} {sizes[order]}";
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
                Content = $"📂 Tải xuống: {fileName} ({FormatSize(data.Length)})",
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

            // Gửi file nhỏ (legacy - dưới 5MB)
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
                string json = JsonSerializer.Serialize(packet);
                byte[] data = Encoding.UTF8.GetBytes(json);
                byte[] length = BitConverter.GetBytes(data.Length);

                _stream.Write(length, 0, 4);
                _stream.Write(data, 0, data.Length);
                _stream.Flush();
            }
            catch
            {
                MessageBox.Show("Mất kết nối!");
                Close();
            }
        }

        // --- ĐÍNH KÈM FILE/ẢNH ---
        private void AttachImage_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dlg = new OpenFileDialog { Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp;*.gif" };
            if (dlg.ShowDialog() == true)
            {
                FileInfo fi = new FileInfo(dlg.FileName);
                // File > 5MB -> dùng chunked transfer
                if (fi.Length > 5 * 1024 * 1024)
                {
                    _ = SendLargeFileAsync(dlg.FileName, PacketType.Image);
                }
                else
                {
                    _ = PrepareAttachment(dlg.FileName, PacketType.Image);
                }
            }
        }

        private void AttachFile_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dlg = new OpenFileDialog();
            if (dlg.ShowDialog() == true)
            {
                FileInfo fi = new FileInfo(dlg.FileName);
                // File > 5MB -> dùng chunked transfer
                if (fi.Length > 5 * 1024 * 1024)
                {
                    _ = SendLargeFileAsync(dlg.FileName, PacketType.File);
                }
                else
                {
                    _ = PrepareAttachment(dlg.FileName, PacketType.File);
                }
            }
        }

        private async Task PrepareAttachment(string path, PacketType type)
        {
            try
            {
                FileInfo fi = new FileInfo(path);

                FileProgressBar.Visibility = Visibility.Visible;
                FileProgressBar.Value = 0;
                PreviewBorder.Visibility = Visibility.Visible;
                PreviewFileName.Text = "Đang đọc file...";

                byte[] data = new byte[fi.Length];
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true))
                {
                    long totalBytes = fi.Length;
                    long totalRead = 0;
                    byte[] buffer = new byte[81920];
                    int read;

                    while ((read = await fs.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        Array.Copy(buffer, 0, data, totalRead, read);
                        totalRead += read;

                        double percent = (double)totalRead / totalBytes * 100;
                        FileProgressBar.Value = percent;
                    }
                }

                _pendingFileData = data;
                _pendingFileName = Path.GetFileName(path);
                _pendingType = type;

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
                    PreviewFileName.Text = $"📄 {_pendingFileName} ({FormatSize(fi.Length)})";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi đọc file: " + ex.Message);
                RemoveAttachment_Click(null, null);
            }
        }

        private void RemoveAttachment_Click(object? sender, RoutedEventArgs? e)
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
            string? selected = UserListBox.SelectedItem.ToString();
            _targetUser = (selected == "Chat Nhóm") ? "ALL" : selected ?? "ALL";
            ChatTitleText.Text = (selected == "Chat Nhóm") ? "Chat Nhóm" : $"Chat riêng: {selected}";
        }

        private void MessageTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter) SendButton_Click(null!, null!);
        }

        private void ScrollToBottom()
        {
            if (MessagesListBox.Items.Count > 0)
                MessagesListBox.ScrollIntoView(MessagesListBox.Items[MessagesListBox.Items.Count - 1]);
        }
    }
}