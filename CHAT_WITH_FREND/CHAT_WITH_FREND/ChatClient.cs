using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Threading;

namespace CHAT_WITH_FREND
{
    public class ChatClient
    {
        private TcpClient? _client;
        private NetworkStream? _stream;
        private bool _isConnected = false;
        private string _serverIP = "127.0.0.1";
        private int _serverPort = 8888;

        public event Action<string>? MessageReceived;

        public bool IsConnected => _isConnected;

        public async Task<bool> ConnectAsync(string serverIP = "127.0.0.1", int serverPort = 8888)
        {
            try
            {
                _serverIP = serverIP;
                _serverPort = serverPort;
                _client = new TcpClient();
                
                // Thêm timeout cho kết nối (5 giây)
                var connectTask = _client.ConnectAsync(_serverIP, _serverPort);
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(5));
                
                var completedTask = await Task.WhenAny(connectTask, timeoutTask);
                if (completedTask == timeoutTask)
                {
                    _client?.Close();
                    throw new TimeoutException();
                }
                
                await connectTask; // Đảm bảo kết nối hoàn tất
                _stream = _client.GetStream();
                _isConnected = true;

                // Bắt đầu lắng nghe tin nhắn
                _ = Task.Run(ListenForMessages);

                return true;
            }
            catch (SocketException ex)
            {
                string errorMessage = ex.ErrorCode switch
                {
                    10061 => $"Không thể kết nối đến server!\n\n" +
                             $"Nguyên nhân: Server chưa được khởi động hoặc không lắng nghe trên {serverIP}:{serverPort}\n\n" +
                             $"Giải pháp:\n" +
                             $"1. Mở terminal/command prompt\n" +
                             $"2. Di chuyển đến thư mục ChatServer\n" +
                             $"3. Chạy lệnh: dotnet run\n" +
                             $"4. Đợi server hiển thị 'Server đang lắng nghe...'\n" +
                             $"5. Sau đó thử kết nối lại từ client này.",
                    _ => $"Lỗi kết nối: {ex.Message}"
                };
                
                MessageBox.Show(errorMessage, "Lỗi kết nối", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                _isConnected = false;
                return false;
            }
            catch (TimeoutException)
            {
                MessageBox.Show($"Kết nối đến server {serverIP}:{serverPort} quá thời gian chờ!\n\n" +
                              $"Vui lòng kiểm tra:\n" +
                              $"1. Server đã được khởi động chưa?\n" +
                              $"2. IP và Port có đúng không?\n" +
                              $"3. Firewall có chặn kết nối không?", 
                    "Lỗi kết nối", MessageBoxButton.OK, MessageBoxImage.Error);
                _isConnected = false;
                return false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Không thể kết nối đến server: {ex.Message}\n\n" +
                              $"Vui lòng đảm bảo server đã được khởi động trước khi kết nối.", 
                    "Lỗi kết nối", MessageBoxButton.OK, MessageBoxImage.Error);
                _isConnected = false;
                return false;
            }
        }

        public void Disconnect()
        {
            _isConnected = false;
            try
            {
                _stream?.Close();
                _client?.Close();
            }
            catch { }
        }

        public async Task SendMessageAsync(string message)
        {
            if (!_isConnected || _stream == null)
            {
                MessageBox.Show("Chưa kết nối đến server!", "Lỗi", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            try
            {
                byte[] data = Encoding.UTF8.GetBytes(message);
                await _stream.WriteAsync(data, 0, data.Length);
                await _stream.FlushAsync();
            }
            catch (Exception ex)
            {
                _isConnected = false;
                Application.Current.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"Không thể gửi tin nhắn: {ex.Message}", "Lỗi", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        }

        private async Task ListenForMessages()
        {
            byte[] buffer = new byte[1024];

            while (_isConnected && _stream != null)
            {
                try
                {
                    int bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0)
                    {
                        // Server đã đóng kết nối
                        _isConnected = false;
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            MessageReceived?.Invoke("Server đã ngắt kết nối.");
                        });
                        break;
                    }

                    string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        MessageReceived?.Invoke(message);
                    });
                }
                catch (Exception ex)
                {
                    if (_isConnected)
                    {
                        _isConnected = false;
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            MessageReceived?.Invoke($"Đã mất kết nối với server: {ex.Message}");
                        });
                    }
                    break;
                }
            }

            _isConnected = false;
        }
    }
}

