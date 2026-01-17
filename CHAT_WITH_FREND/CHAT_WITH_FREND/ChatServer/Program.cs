using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Shared; // Đảm bảo đã tham chiếu project Shared

namespace ChatServer
{
    class Program
    {
        private static TcpListener? _server;
        private static Dictionary<string, TcpClient> _connectedClients = new Dictionary<string, TcpClient>();
        private static readonly object _lock = new object();

        static void Main(string[] args)
        {
            _server = new TcpListener(IPAddress.Any, 8888);
            _server.Start();
            Console.WriteLine("========================================");
            Console.WriteLine("SERVER STARTED ON PORT 8888");
            Console.WriteLine("========================================");

            Task.Run(AcceptClients);
            Console.ReadLine();
        }

        static async Task AcceptClients()
        {
            while (true)
            {
                try
                {
                    var client = await _server.AcceptTcpClientAsync();
                    _ = HandleClient(client);
                }
                catch { }
            }
        }

        static async Task HandleClient(TcpClient client)
        {
            NetworkStream stream = client.GetStream();
            string clientName = "";

            try
            {
                while (client.Connected)
                {
                    // 1. Đọc 4 bytes đầu tiên (Độ dài gói tin)
                    byte[] lengthBuffer = new byte[4];
                    int read = await stream.ReadAsync(lengthBuffer, 0, 4);
                    if (read == 0) break; // Client ngắt kết nối
                    int packetLength = BitConverter.ToInt32(lengthBuffer, 0);

                    // 2. Đọc nội dung gói tin dựa trên độ dài
                    byte[] packetBuffer = new byte[packetLength];
                    int totalRead = 0;
                    while (totalRead < packetLength)
                    {
                        int bytesRead = await stream.ReadAsync(packetBuffer, totalRead, packetLength - totalRead);
                        if (bytesRead == 0) break;
                        totalRead += bytesRead;
                    }

                    // 3. Xử lý JSON
                    string json = Encoding.UTF8.GetString(packetBuffer);
                    var packet = JsonSerializer.Deserialize<ChatPacket>(json);

                    if (packet == null) continue;

                    switch (packet.Type)
                    {
                        case PacketType.Login:
                            clientName = packet.Sender;
                            lock (_lock)
                            {
                                if (_connectedClients.ContainsKey(clientName))
                                    _connectedClients[clientName] = client;
                                else
                                    _connectedClients.Add(clientName, client);
                            }
                            Console.WriteLine($"{clientName} joined.");
                            await BroadcastUserList();
                            break;

                        case PacketType.Message:
                        case PacketType.Image:
                        case PacketType.File:
                            Console.WriteLine($"{packet.Sender}: {packet.Message ?? "sent a file"}");
                            await BroadcastPacket(packet);
                            break;

                        case PacketType.PrivateMessage:
                            await SendPrivate(packet);
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error with client {clientName}: {ex.Message}");
            }
            finally
            {
                if (!string.IsNullOrEmpty(clientName))
                {
                    lock (_lock) _connectedClients.Remove(clientName);
                    await BroadcastUserList();
                    Console.WriteLine($"{clientName} disconnected.");
                }
                client.Close();
            }
        }

        // --- Các hàm hỗ trợ gửi tin ---
        static async Task BroadcastUserList()
        {
            var listPacket = new ChatPacket
            {
                Type = PacketType.UserList,
                Sender = "Server",
                Message = JsonSerializer.Serialize(_connectedClients.Keys.ToList()),
                Time = DateTime.Now
            };
            await BroadcastPacket(listPacket);
        }

        static async Task BroadcastPacket(ChatPacket packet)
        {
            lock (_lock)
            {
                foreach (var client in _connectedClients.Values)
                {
                    SendToClient(client, packet);
                }
            }
        }

        static async Task SendPrivate(ChatPacket packet)
        {
            lock (_lock)
            {
                if (_connectedClients.TryGetValue(packet.Target, out TcpClient targetClient))
                {
                    SendToClient(targetClient, packet);
                }
                // Gửi lại cho người gửi để hiện lên màn hình của họ
                if (_connectedClients.TryGetValue(packet.Sender, out TcpClient senderClient))
                {
                    SendToClient(senderClient, packet);
                }
            }
        }

        static void SendToClient(TcpClient client, ChatPacket packet)
        {
            try
            {
                string json = JsonSerializer.Serialize(packet);
                byte[] data = Encoding.UTF8.GetBytes(json);
                byte[] length = BitConverter.GetBytes(data.Length);

                NetworkStream stream = client.GetStream();
                stream.Write(length, 0, 4); // Gửi độ dài trước
                stream.Write(data, 0, data.Length); // Gửi nội dung sau
                stream.Flush();
            }
            catch { }
        }
    }
}