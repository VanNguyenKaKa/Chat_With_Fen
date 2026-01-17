using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Shared;

namespace ChatServer
{
    class Program
    {
        private static TcpListener? _server;
        private static Dictionary<string, TcpClient> _connectedClients = new();
        private static readonly object _lock = new();

        // Track file transfers để log
        private static Dictionary<string, (string FileName, string Sender, int TotalChunks, int ReceivedChunks)> _activeTransfers = new();

        static void Main(string[] args)
        {
            _server = new TcpListener(IPAddress.Any, 8888);
            _server.Start();
            Console.WriteLine("========================================");
            Console.WriteLine("SERVER STARTED ON PORT 8888");
            Console.WriteLine("Hỗ trợ file tối đa 2GB với chunked transfer");
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
                    var client = await _server!.AcceptTcpClientAsync();
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
                    byte[] lengthBuffer = new byte[4];
                    int read = await stream.ReadAsync(lengthBuffer, 0, 4);
                    if (read == 0) break;
                    int packetLength = BitConverter.ToInt32(lengthBuffer, 0);

                    byte[] packetBuffer = new byte[packetLength];
                    int totalRead = 0;
                    while (totalRead < packetLength)
                    {
                        int bytesRead = await stream.ReadAsync(packetBuffer, totalRead, packetLength - totalRead);
                        if (bytesRead == 0) break;
                        totalRead += bytesRead;
                    }

                    string json = Encoding.UTF8.GetString(packetBuffer);
                    var packet = JsonSerializer.Deserialize<ChatPacket>(json);

                    if (packet == null) continue;

                    switch (packet.Type)
                    {
                        case PacketType.Login:
                            clientName = packet.Sender;
                            lock (_lock)
                            {
                                _connectedClients[clientName] = client;
                            }
                            Console.WriteLine($"[+] {clientName} joined.");
                            await BroadcastUserList();
                            break;

                        case PacketType.Message:
                        case PacketType.Image:
                        case PacketType.File:
                            Console.WriteLine($"[MSG] {packet.Sender}: {packet.Message ?? "sent a file"}");
                            await BroadcastPacket(packet);
                            break;

                        case PacketType.PrivateMessage:
                            await SendPrivate(packet);
                            break;

                        // ===== CHUNKED FILE TRANSFER - FORWARD TO ALL CLIENTS =====
                        case PacketType.FileStart:
                            Console.WriteLine($"[FILE] {packet.Sender} bắt đầu gửi '{packet.FileName}' ({FormatSize(packet.TotalFileSize)}) - {packet.TotalChunks} chunks");
                            lock (_lock)
                            {
                                _activeTransfers[packet.FileId] = (packet.FileName, packet.Sender, packet.TotalChunks, 0);
                            }
                            // Forward đến tất cả client (hoặc private nếu có target)
                            await ForwardFilePacket(packet);
                            break;

                        case PacketType.FileChunk:
                            // Log progress mỗi 50 chunks
                            lock (_lock)
                            {
                                if (_activeTransfers.TryGetValue(packet.FileId, out var state))
                                {
                                    var newState = (state.FileName, state.Sender, state.TotalChunks, state.Item4 + 1);
                                    _activeTransfers[packet.FileId] = newState;

                                    if (newState.Item4 % 50 == 0 || newState.Item4 == newState.TotalChunks)
                                    {
                                        double progress = (double)newState.Item4 / newState.TotalChunks * 100;
                                        Console.WriteLine($"[FILE] {state.FileName}: {progress:F0}% ({newState.Item4}/{newState.TotalChunks})");
                                    }
                                }
                            }
                            await ForwardFilePacket(packet);
                            break;

                        case PacketType.FileEnd:
                            Console.WriteLine($"[FILE] {packet.Sender} hoàn thành gửi '{packet.FileName}'");
                            lock (_lock)
                            {
                                _activeTransfers.Remove(packet.FileId);
                            }
                            await ForwardFilePacket(packet);
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] {clientName}: {ex.Message}");
            }
            finally
            {
                if (!string.IsNullOrEmpty(clientName))
                {
                    lock (_lock) _connectedClients.Remove(clientName);
                    await BroadcastUserList();
                    Console.WriteLine($"[-] {clientName} disconnected.");
                }
                client.Close();
            }
        }

        // Forward file packets (FileStart, FileChunk, FileEnd)
        static async Task ForwardFilePacket(ChatPacket packet)
        {
            if (string.IsNullOrEmpty(packet.Target) || packet.Target == "ALL")
            {
                // Broadcast đến tất cả (trừ người gửi)
                await BroadcastPacketExceptSender(packet);
            }
            else
            {
                // Private - gửi đến target
                await SendPrivate(packet);
            }
        }

        // Broadcast cũng như SendLargeFileAsync nhưng không gửi lại cho người gửi
        static async Task BroadcastPacketExceptSender(ChatPacket packet)
        {
            List<(string name, TcpClient client)> clients;
            lock (_lock)
            {
                clients = _connectedClients.Select(kv => (kv.Key, kv.Value)).ToList();
            }

            foreach (var (name, client) in clients)
            {
                // Không gửi lại cho người gửi
                if (name != packet.Sender)
                {
                    SendToClient(client, packet);
                }
            }
        }

        static string FormatSize(long bytes)
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

        // --- Các hàm hỗ trợ gửi tin ---
        static async Task BroadcastUserList()
        {
            List<string> userList;
            lock (_lock)
            {
                userList = _connectedClients.Keys.ToList();
            }

            var listPacket = new ChatPacket
            {
                Type = PacketType.UserList,
                Sender = "Server",
                Message = JsonSerializer.Serialize(userList),
                Time = DateTime.Now
            };
            await BroadcastPacket(listPacket);
        }

        static async Task BroadcastPacket(ChatPacket packet)
        {
            List<TcpClient> clients;
            lock (_lock)
            {
                clients = _connectedClients.Values.ToList();
            }

            foreach (var client in clients)
            {
                SendToClient(client, packet);
            }
        }

        static async Task SendPrivate(ChatPacket packet)
        {
            TcpClient? targetClient = null;
            TcpClient? senderClient = null;

            lock (_lock)
            {
                _connectedClients.TryGetValue(packet.Target, out targetClient);
                _connectedClients.TryGetValue(packet.Sender, out senderClient);
            }

            if (targetClient != null)
                SendToClient(targetClient, packet);
            
            // Chỉ gửi lại cho sender nếu là Message/PrivateMessage (không phải file chunks)
            if (senderClient != null && packet.Type == PacketType.PrivateMessage)
                SendToClient(senderClient, packet);
        }

        static void SendToClient(TcpClient client, ChatPacket packet)
        {
            try
            {
                string json = JsonSerializer.Serialize(packet);
                byte[] data = Encoding.UTF8.GetBytes(json);
                byte[] length = BitConverter.GetBytes(data.Length);

                NetworkStream stream = client.GetStream();
                lock (stream) // Thread-safe write
                {
                    stream.Write(length, 0, 4);
                    stream.Write(data, 0, data.Length);
                    stream.Flush();
                }
            }
            catch { }
        }
    }
}