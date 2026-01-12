using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Linq;

namespace ChatServer
{
    class Program
    {
        private static TcpListener? _server;
        private static List<TcpClient> _clients = new List<TcpClient>();
        private static readonly object _lock = new object();

        static void Main(string[] args)
        {
            int port = 8888;
            // Lắng nghe trên tất cả các interface (0.0.0.0) để cho phép kết nối từ xa
            IPAddress localAddr = IPAddress.Any;

            _server = new TcpListener(localAddr, port);
            _server.Start();

            Console.WriteLine("========================================");
            Console.WriteLine("   CHAT SERVER ĐÃ KHỞI ĐỘNG");
            Console.WriteLine("========================================");
            Console.WriteLine($"Port: {port}");
            Console.WriteLine($"Đang lắng nghe trên tất cả interfaces...");
            Console.WriteLine();
            
            // Hiển thị các địa chỉ IP local để chia sẻ
            Console.WriteLine("Địa chỉ IP để chia sẻ cho bạn bè:");
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork) // Chỉ hiển thị IPv4
                    {
                        Console.WriteLine($"  - {ip}:{port}");
                    }
                }
            }
            catch
            {
                Console.WriteLine("  - Không thể lấy địa chỉ IP");
            }
            
            Console.WriteLine();
            Console.WriteLine("Lưu ý:");
            Console.WriteLine("1. Để bạn bè kết nối từ xa, họ cần nhập IP của máy này");
            Console.WriteLine("2. Nếu bạn đang dùng router, cần mở port forwarding cho port " + port);
            Console.WriteLine("3. Đảm bảo Windows Firewall cho phép kết nối trên port " + port);
            Console.WriteLine();
            Console.WriteLine("Nhấn Enter để dừng server...");
            Console.WriteLine("========================================");

            // Bắt đầu lắng nghe các kết nối mới
            Task.Run(AcceptClients);

            Console.ReadLine();

            // Đóng tất cả kết nối
            lock (_lock)
            {
                foreach (var client in _clients)
                {
                    client.Close();
                }
            }

            _server.Stop();
            Console.WriteLine("Server đã dừng.");
        }

        static async Task AcceptClients()
        {
            while (_server != null)
            {
                try
                {
                    TcpClient client = await _server.AcceptTcpClientAsync();
                    string clientEndPoint = client.Client.RemoteEndPoint?.ToString() ?? "Unknown";
                    Console.WriteLine($"Client đã kết nối: {clientEndPoint}");
                    Console.WriteLine($"Tổng số client đang kết nối: {_clients.Count + 1}");

                    lock (_lock)
                    {
                        _clients.Add(client);
                    }

                    // Xử lý client trong một task riêng
                    _ = HandleClient(client);
                }
                catch (ObjectDisposedException)
                {
                    // Server đã bị đóng, thoát khỏi vòng lặp
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Lỗi khi chấp nhận client: {ex.Message}");
                }
            }
        }

        static async Task HandleClient(TcpClient client)
        {
            NetworkStream stream = client.GetStream();
            byte[] buffer = new byte[1024];
            string clientEndPoint = client.Client.RemoteEndPoint?.ToString() ?? "Unknown";

            try
            {
                while (client.Connected)
                {
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0)
                        break;

                    string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    Console.WriteLine($"Nhận từ {clientEndPoint}: {message}");

                    // Gửi tin nhắn đến tất cả các client khác
                    await BroadcastMessageAsync(message, client);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi khi xử lý client {clientEndPoint}: {ex.Message}");
            }
            finally
            {
                Console.WriteLine($"Client đã ngắt kết nối: {clientEndPoint}");
                lock (_lock)
                {
                    _clients.Remove(client);
                    Console.WriteLine($"Tổng số client đang kết nối: {_clients.Count}");
                }
                try
                {
                    client.Close();
                }
                catch { }
            }
        }

        static async Task BroadcastMessageAsync(string message, TcpClient sender)
        {
            byte[] data = Encoding.UTF8.GetBytes(message);
            List<TcpClient> clientsToRemove = new List<TcpClient>();
            List<TcpClient> clientsToSend;

            // Lấy danh sách clients cần gửi (ngoài lock)
            lock (_lock)
            {
                clientsToSend = _clients.Where(c => c != sender && c.Connected).ToList();
            }

            // Gửi tin nhắn đến từng client (ngoài lock để có thể await)
            foreach (var client in clientsToSend)
            {
                try
                {
                    NetworkStream stream = client.GetStream();
                    await stream.WriteAsync(data, 0, data.Length);
                    await stream.FlushAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Lỗi khi gửi tin nhắn đến client: {ex.Message}");
                    clientsToRemove.Add(client);
                }
            }

            // Xóa các client bị lỗi
            if (clientsToRemove.Count > 0)
            {
                lock (_lock)
                {
                    foreach (var client in clientsToRemove)
                    {
                        _clients.Remove(client);
                        try
                        {
                            client.Close();
                        }
                        catch { }
                    }
                }
            }
        }
    }
}

