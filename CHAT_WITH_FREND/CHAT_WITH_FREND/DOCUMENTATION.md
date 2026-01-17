# TÀI LIỆU KỸ THUẬT - ỨNG DỤNG CHAT SOCKET C#

## Mục Lục
1. [Tổng quan kiến trúc](#tổng-quan-kiến-trúc)
2. [Giao thức giao tiếp](#giao-thức-giao-tiếp)
3. [Cách các máy giao tiếp với nhau](#cách-các-máy-giao-tiếp-với-nhau)
4. [Chi tiết Server](#chi-tiết-server)
5. [Chi tiết Client](#chi-tiết-client)
6. [Luồng hoạt động chi tiết](#luồng-hoạt-động-chi-tiết)
7. [Các hàm chính và cách gọi](#các-hàm-chính-và-cách-gọi)

---

## Tổng quan kiến trúc

Ứng dụng sử dụng mô hình **Client-Server** với giao thức **TCP Socket**:

```
┌─────────────┐         ┌─────────────┐
│   Client 1  │────────▶│             │
└─────────────┘         │   SERVER    │
                        │             │◀────────┐
┌─────────────┐         │  (TCP Socket)│        │
│   Client 2  │────────▶│             │        │
└─────────────┘         └─────────────┘        │
                        ▲                       │
                        │                       │
┌─────────────┐         │                       │
│   Client N  │─────────┘                       │
└─────────────┘                                 │
                                                │
                Tất cả client kết nối đến       │
                server và server phân phối      │
                tin nhắn đến các client khác    │
```

### Cấu trúc Project

```
CHAT_WITH_FREND/
├── ChatServer/                 # Project Server (Console Application)
│   └── Program.cs              # File chính của Server
│
└── CHAT_WITH_FREND/            # Project Client (WPF Application)
    ├── ChatClient.cs           # Class xử lý kết nối Socket
    ├── MainWindow.xaml         # Giao diện người dùng
    └── MainWindow.xaml.cs      # Code-behind của UI
```

---

## Giao thức giao tiếp

### Protocol Stack
- **Transport Layer**: TCP (Transmission Control Protocol)
- **Encoding**: UTF-8 (hỗ trợ tiếng Việt và emoji)
- **Port mặc định**: 8888
- **IP mặc định**: 127.0.0.1 (localhost) hoặc IPAddress.Any (0.0.0.0) cho server

### Format tin nhắn
- **Encoding**: Tin nhắn được chuyển từ string sang byte[] bằng UTF-8
- **Decoding**: Server/Client nhận byte[] và chuyển về string bằng UTF-8
- **Định dạng**: Plain text, không có header/footer đặc biệt

### Ví dụ:
```
Tin nhắn gốc: "Client_ABC: Xin chào! 😀"
    ↓ Encoding UTF-8
Byte array: [67, 108, 105, 101, 110, 116, ...]
    ↓ Gửi qua NetworkStream
    ↓ Nhận và Decoding UTF-8
Tin nhắn nhận: "Client_ABC: Xin chào! 😀"
```

---

## Cách các máy giao tiếp với nhau

### 1. Thiết lập kết nối (Connection Setup)

#### Bước 1: Server khởi động
```
Server (Program.cs - Main)
    ↓
TcpListener.Start() - Lắng nghe trên port 8888
    ↓
AcceptClients() - Chờ client kết nối
```

#### Bước 2: Client kết nối
```
Client (ChatClient.cs - ConnectAsync)
    ↓
TcpClient.ConnectAsync(IP, Port)
    ↓
Tạo NetworkStream từ TcpClient
    ↓
Bắt đầu ListenForMessages() - Lắng nghe tin nhắn từ server
```

#### Kết quả:
- Server có 1 socket listener chờ kết nối
- Mỗi client có 1 socket riêng kết nối đến server
- Server lưu danh sách tất cả client trong `List<TcpClient> _clients`

### 2. Gửi tin nhắn (Sending Message)

#### Client gửi tin nhắn:
```
User nhập tin nhắn trong UI
    ↓
MainWindow.xaml.cs - SendMessage()
    ↓
ChatClient.SendMessageAsync(message)
    ↓
Encoding.UTF8.GetBytes(message) - Chuyển string → byte[]
    ↓
NetworkStream.WriteAsync(data) - Gửi byte[] qua socket
    ↓
NetworkStream.FlushAsync() - Đảm bảo dữ liệu được gửi ngay
```

#### Server nhận tin nhắn:
```
Server (Program.cs - HandleClient)
    ↓
NetworkStream.ReadAsync(buffer) - Đọc byte[] từ socket
    ↓
Encoding.UTF8.GetString(buffer) - Chuyển byte[] → string
    ↓
BroadcastMessageAsync(message, sender) - Phân phối đến các client khác
```

### 3. Phân phối tin nhắn (Broadcasting)

```
Server nhận tin nhắn từ Client A
    ↓
BroadcastMessageAsync(message, sender)
    ↓
Lặp qua danh sách _clients (loại trừ sender)
    ↓
Với mỗi client khác:
    ├─ NetworkStream.WriteAsync(data) - Gửi byte[]
    └─ NetworkStream.FlushAsync()
    ↓
Tất cả client khác nhận được tin nhắn
```

### 4. Nhận tin nhắn (Receiving Message)

```
Server gửi tin nhắn đến Client B
    ↓
Client B - ListenForMessages() đang chạy trong background
    ↓
NetworkStream.ReadAsync(buffer) - Đọc byte[]
    ↓
Encoding.UTF8.GetString(buffer) - Chuyển về string
    ↓
MessageReceived?.Invoke(message) - Trigger event
    ↓
MainWindow.OnMessageReceived() - Cập nhật UI
    ↓
MessagesListBox.Items.Add() - Hiển thị tin nhắn
```

---

## Chi tiết Server

### File: `ChatServer/Program.cs`

### Các biến static:

```csharp
private static TcpListener? _server;              // Server socket listener
private static List<TcpClient> _clients;          // Danh sách tất cả client đã kết nối
private static readonly object _lock;             // Lock object để thread-safe
```

### Hàm Main() - Điểm khởi đầu

**Luồng thực thi:**

```csharp
Main()
    ├─ 1. Tạo TcpListener trên IPAddress.Any:8888
    ├─ 2. _server.Start() - Bắt đầu lắng nghe
    ├─ 3. Hiển thị thông tin IP để chia sẻ
    ├─ 4. Task.Run(AcceptClients) - Chạy async để chờ client kết nối
    └─ 5. Console.ReadLine() - Giữ chương trình chạy
```

**Tại sao IPAddress.Any?**
- `IPAddress.Any` (0.0.0.0) = lắng nghe trên TẤT CẢ network interfaces
- Cho phép client từ mạng khác kết nối (không chỉ localhost)
- `127.0.0.1` chỉ cho phép kết nối từ chính máy đó

### Hàm AcceptClients() - Chấp nhận kết nối mới

**Luồng thực thi:**

```csharp
AcceptClients() [Async - chạy trong background]
    │
    ├─ While (_server != null):
    │   ├─ _server.AcceptTcpClientAsync() - Chờ client kết nối (BLOCKING)
    │   ├─ Khi có client kết nối:
    │   │   ├─ Thêm client vào _clients (có lock để thread-safe)
    │   │   └─ Task.Run(HandleClient) - Xử lý client trong thread riêng
    │   └─ Lặp lại để chờ client tiếp theo
    │
    └─ Nếu server đóng → break
```

**Tại sao cần HandleClient riêng?**
- Mỗi client cần xử lý độc lập
- Không thể block AcceptClients() - nếu block thì không nhận được client mới
- Dùng async/await để xử lý nhiều client đồng thời

### Hàm HandleClient() - Xử lý từng client

**Luồng thực thi:**

```csharp
HandleClient(client) [Async - mỗi client 1 thread]
    │
    ├─ Tạo NetworkStream từ client
    ├─ Tạo buffer 1024 bytes để đọc dữ liệu
    │
    ├─ While (client.Connected):
    │   ├─ stream.ReadAsync(buffer) - Đọc dữ liệu từ client (BLOCKING)
    │   ├─ Nếu bytesRead == 0 → client đã đóng kết nối → break
    │   ├─ Encoding.UTF8.GetString() - Chuyển byte[] → string
    │   ├─ Console.WriteLine() - In ra console
    │   └─ BroadcastMessageAsync() - Gửi đến các client khác
    │
    └─ Finally:
        ├─ Xóa client khỏi _clients (có lock)
        └─ client.Close() - Đóng kết nối
```

**Tại sao dùng buffer 1024 bytes?**
- Mỗi lần ReadAsync chỉ đọc tối đa 1024 bytes
- Nếu tin nhắn > 1024 bytes, cần đọc nhiều lần (trong project này tin nhắn ngắn nên OK)
- Có thể tăng buffer nếu cần

### Hàm BroadcastMessageAsync() - Phân phối tin nhắn

**Luồng thực thi:**

```csharp
BroadcastMessageAsync(message, sender)
    │
    ├─ Encoding.UTF8.GetBytes(message) - Chuyển string → byte[]
    │
    ├─ Lấy danh sách clients (có lock):
    │   └─ clientsToSend = _clients WHERE (c != sender && c.Connected)
    │
    ├─ Với mỗi client trong clientsToSend:
    │   ├─ stream.WriteAsync(data) - Gửi byte[]
    │   ├─ stream.FlushAsync() - Đảm bảo gửi ngay
    │   └─ Nếu lỗi → thêm vào clientsToRemove
    │
    └─ Xóa clients bị lỗi (có lock)
```

**Tại sao cần lock?**
- `_clients` được truy cập từ nhiều thread:
  - Thread AcceptClients: thêm client mới
  - Thread HandleClient: xóa client khi disconnect
  - Thread BroadcastMessageAsync: đọc danh sách client
- `lock (_lock)` đảm bảo chỉ 1 thread truy cập `_clients` tại một thời điểm
- Tránh race condition và crash

**Tại sao không await trong lock?**
- C# không cho phép `await` trong `lock` statement
- Giải pháp: Lấy danh sách clients trong lock, sau đó await bên ngoài lock

---

## Chi tiết Client

### File: `ChatClient.cs`

### Các biến:

```csharp
private TcpClient? _client;              // Socket kết nối đến server
private NetworkStream? _stream;          // Stream để gửi/nhận dữ liệu
private bool _isConnected;               // Trạng thái kết nối
public event Action<string>? MessageReceived;  // Event khi nhận tin nhắn
```

### Hàm ConnectAsync() - Kết nối đến server

**Luồng thực thi:**

```csharp
ConnectAsync(serverIP, serverPort)
    │
    ├─ 1. Tạo TcpClient mới
    ├─ 2. Tạo timeout 5 giây:
    │   ├─ Task.WhenAny(connectTask, timeoutTask)
    │   └─ Nếu timeout → throw TimeoutException
    │
    ├─ 3. _client.ConnectAsync(IP, Port) - Kết nối đến server
    ├─ 4. _stream = _client.GetStream() - Lấy NetworkStream
    ├─ 5. _isConnected = true
    ├─ 6. Task.Run(ListenForMessages) - Bắt đầu lắng nghe tin nhắn (background)
    └─ 7. Return true
```

**Xử lý lỗi:**
- `SocketException (10061)`: Server chưa chạy hoặc không lắng nghe
- `TimeoutException`: Kết nối quá 5 giây
- Các lỗi khác: Hiển thị thông báo chung

### Hàm SendMessageAsync() - Gửi tin nhắn

**Luồng thực thi:**

```csharp
SendMessageAsync(message)
    │
    ├─ 1. Kiểm tra _isConnected và _stream != null
    ├─ 2. Kiểm tra message không rỗng
    ├─ 3. Encoding.UTF8.GetBytes(message) - String → byte[]
    ├─ 4. _stream.WriteAsync(data) - Gửi byte[]
    └─ 5. _stream.FlushAsync() - Đảm bảo gửi ngay
```

**Tại sao cần FlushAsync?**
- WriteAsync có thể buffer dữ liệu
- FlushAsync buộc gửi ngay lập tức qua network
- Đảm bảo tin nhắn được gửi kịp thời

### Hàm ListenForMessages() - Lắng nghe tin nhắn

**Luồng thực thi:**

```csharp
ListenForMessages() [Async - chạy trong background]
    │
    ├─ Tạo buffer 1024 bytes
    │
    ├─ While (_isConnected && _stream != null):
    │   ├─ stream.ReadAsync(buffer) - Đọc dữ liệu (BLOCKING)
    │   ├─ Nếu bytesRead == 0 → server đóng kết nối → break
    │   ├─ Encoding.UTF8.GetString() - Byte[] → string
    │   └─ Application.Current.Dispatcher.Invoke():
    │       └─ MessageReceived?.Invoke(message) - Trigger event (trên UI thread)
    │
    └─ Nếu lỗi → _isConnected = false và thông báo
```

**Tại sao cần Dispatcher.Invoke?**
- ListenForMessages chạy trong background thread
- WPF UI chỉ có thể cập nhật từ UI thread (main thread)
- `Dispatcher.Invoke` đưa code về UI thread để cập nhật UI an toàn

### File: `MainWindow.xaml.cs`

### Hàm ConnectButton_Click() - Xử lý kết nối/ngắt kết nối

**Luồng thực thi:**

```csharp
ConnectButton_Click()
    │
    ├─ Nếu đã kết nối:
    │   ├─ _chatClient.Disconnect()
    │   ├─ Update UI: Disable buttons, update status
    │   └─ Return
    │
    └─ Nếu chưa kết nối:
        ├─ Parse port từ TextBox
        ├─ Tạo ChatClient mới
        ├─ Đăng ký event: _chatClient.MessageReceived += OnMessageReceived
        ├─ await _chatClient.ConnectAsync(IP, Port)
        └─ Nếu thành công:
            ├─ Update UI: Enable buttons, update status
            └─ MessageTextBox.Focus()
```

### Hàm SendMessage() - Gửi tin nhắn từ UI

**Luồng thực thi:**

```csharp
SendMessage()
    │
    ├─ 1. Lấy text từ MessageTextBox
    ├─ 2. Kiểm tra không rỗng
    ├─ 3. Tạo fullMessage = "{clientName}: {message}"
    ├─ 4. await _chatClient.SendMessageAsync(fullMessage) - Gửi đến server
    ├─ 5. AddMessage("Bạn: {message}") - Hiển thị tin nhắn của mình trong UI
    ├─ 6. MessageTextBox.Clear() - Xóa ô nhập
    └─ 7. MessageTextBox.Focus() - Đặt focus lại
```

**Tại sao hiển thị "Bạn: {message}" ngay?**
- Client không nhận lại tin nhắn của chính mình từ server
- Server chỉ broadcast đến các client khác (không gửi lại cho sender)
- Hiển thị ngay để user thấy tin nhắn đã gửi

### Hàm OnMessageReceived() - Xử lý tin nhắn nhận được

**Luồng thực thi:**

```csharp
OnMessageReceived(message)
    │
    ├─ AddMessage(message) - Thêm vào ListBox
    │
    └─ Nếu message chứa "đã mất kết nối" hoặc "đã ngắt kết nối":
        ├─ UpdateConnectionStatus(false)
        ├─ Disable buttons
        └─ _chatClient = null
```

**Khi nào event này được trigger?**
- Từ `ChatClient.ListenForMessages()` khi nhận được tin nhắn từ server
- Được gọi trên UI thread (nhờ Dispatcher.Invoke)

### Hàm AddMessage() - Thêm tin nhắn vào UI

**Luồng thực thi:**

```csharp
AddMessage(message)
    │
    ├─ 1. Tạo timestamp = DateTime.Now.ToString("HH:mm:ss")
    ├─ 2. formattedMessage = "[{timestamp}] {message}"
    ├─ 3. MessagesListBox.Items.Add(formattedMessage)
    └─ 4. MessagesListBox.ScrollIntoView(lastItem) - Tự động cuộn
```

---

## Luồng hoạt động chi tiết

### Kịch bản: Client A gửi tin nhắn "Hello" đến Client B và Client C

```
┌─────────────────────────────────────────────────────────────┐
│ 1. USER A nhập "Hello" và nhấn "Gửi"                        │
└─────────────────────────────────────────────────────────────┘
                          │
                          ▼
┌─────────────────────────────────────────────────────────────┐
│ 2. MainWindow.SendMessage()                                  │
│    - Lấy text: "Hello"                                       │
│    - Tạo: "Client_A: Hello"                                  │
└─────────────────────────────────────────────────────────────┘
                          │
                          ▼
┌─────────────────────────────────────────────────────────────┐
│ 3. ChatClient.SendMessageAsync("Client_A: Hello")           │
│    - Encoding.UTF8.GetBytes() → byte[]                      │
│    - NetworkStream.WriteAsync(byte[])                       │
│    - NetworkStream.FlushAsync()                             │
└─────────────────────────────────────────────────────────────┘
                          │
                          ▼ (Gửi qua TCP Socket)
┌─────────────────────────────────────────────────────────────┐
│ 4. SERVER - HandleClient(Client_A)                           │
│    - NetworkStream.ReadAsync() → byte[]                     │
│    - Encoding.UTF8.GetString() → "Client_A: Hello"          │
│    - Console.WriteLine("Nhận từ Client_A: Client_A: Hello") │
└─────────────────────────────────────────────────────────────┘
                          │
                          ▼
┌─────────────────────────────────────────────────────────────┐
│ 5. SERVER - BroadcastMessageAsync("Client_A: Hello", Client_A)│
│    - Lấy danh sách: [Client_B, Client_C] (loại trừ Client_A)│
│    - Với mỗi client:                                         │
│      ├─ Encoding.UTF8.GetBytes() → byte[]                   │
│      ├─ NetworkStream.WriteAsync(byte[])                    │
│      └─ NetworkStream.FlushAsync()                          │
└─────────────────────────────────────────────────────────────┘
                          │
        ┌─────────────────┴─────────────────┐
        ▼                                   ▼
┌──────────────────┐              ┌──────────────────┐
│ CLIENT B         │              │ CLIENT C         │
│ ListenForMessages│              │ ListenForMessages│
│ - ReadAsync()    │              │ - ReadAsync()    │
│ - GetString()    │              │ - GetString()    │
│ - MessageReceived│              │ - MessageReceived│
│   event          │              │   event          │
└──────────────────┘              └──────────────────┘
        │                                   │
        ▼                                   ▼
┌──────────────────┐              ┌──────────────────┐
│ MainWindow       │              │ MainWindow       │
│ OnMessageReceived│              │ OnMessageReceived│
│ - AddMessage()   │              │ - AddMessage()   │
│ - Hiển thị UI    │              │ - Hiển thị UI    │
└──────────────────┘              └──────────────────┘
```

### Timeline chi tiết:

```
Time    Client A              Server              Client B              Client C
─────────────────────────────────────────────────────────────────────────────
T0      User nhập "Hello"
T1      Click "Gửi"
T2      SendMessage()
T3      SendMessageAsync()
T4      WriteAsync() ─────────────────┐
                                      │
T5                                    ├─► ReadAsync()
T6                                    ├─► HandleClient()
T7                                    ├─► BroadcastMessageAsync()
T8                                    │   ├─► WriteAsync() ───┐
T9                                    │   │                    │
T10                                   │   └─► WriteAsync() ───┼──┐
                                      │                       │  │
T11                                   │                       ▼  ▼
T12                                   │                   ReadAsync()  ReadAsync()
T13                                   │                   MessageReceived
T14                                   │                   OnMessageReceived
T15                                   │                   AddMessage()
T16                                   │                   UI Updated
T17                                   │                              MessageReceived
T18                                   │                              OnMessageReceived
T19                                   │                              AddMessage()
T20                                   │                              UI Updated
```

---

## Các hàm chính và cách gọi

### Server (ChatServer/Program.cs)

| Hàm | Được gọi từ | Mục đích | Thread |
|-----|-------------|----------|--------|
| `Main()` | Entry point | Khởi tạo server, chờ client kết nối | Main thread |
| `AcceptClients()` | `Main()` → `Task.Run()` | Chấp nhận kết nối mới, gọi HandleClient | Background thread |
| `HandleClient(client)` | `AcceptClients()` → `Task.Run()` | Xử lý tin nhắn từ 1 client, gọi BroadcastMessageAsync | Background thread (mỗi client 1 thread) |
| `BroadcastMessageAsync()` | `HandleClient()` | Phân phối tin nhắn đến các client khác | Background thread |

**Cây gọi hàm Server:**

```
Main()
  ├─ TcpListener.Start()
  ├─ Task.Run(AcceptClients) ────────────────┐
  │                                           │
  │   AcceptClients() [Background]           │
  │     ├─ AcceptTcpClientAsync()            │
  │     └─ Task.Run(HandleClient) ───────────┼───┐
  │                                           │   │
  │       HandleClient(client) [Background]  │   │
  │         ├─ ReadAsync()                   │   │
  │         └─ BroadcastMessageAsync() ──────┼───┼───┐
  │                                           │   │   │
  │           BroadcastMessageAsync()        │   │   │
  │             └─ WriteAsync() (cho mỗi     │   │   │
  │                 client khác)             │   │   │
  │                                           │   │   │
  └─ Console.ReadLine() [Block main thread]  │   │   │
                                              ▼   ▼   ▼
                                      (Chạy song song)
```

### Client (ChatClient.cs + MainWindow.xaml.cs)

| Hàm | Được gọi từ | Mục đích | Thread |
|-----|-------------|----------|--------|
| `ConnectAsync()` | `MainWindow.ConnectButton_Click()` | Kết nối đến server, gọi `Task.Run(ListenForMessages)` | UI thread (async) |
| `SendMessageAsync()` | `MainWindow.SendMessage()` | Gửi tin nhắn đến server | UI thread (async) |
| `ListenForMessages()` | `ConnectAsync()` → `Task.Run()` | Lắng nghe tin nhắn, trigger `MessageReceived` event | Background thread |
| `Disconnect()` | `MainWindow.ConnectButton_Click()` (disconnect) | Đóng kết nối | UI thread |
| `ConnectButton_Click()` | User click button | Kết nối/ngắt kết nối | UI thread |
| `SendMessage()` | `SendButton_Click()` hoặc `MessageTextBox_KeyDown()` | Gửi tin nhắn | UI thread (async) |
| `OnMessageReceived()` | `MessageReceived` event | Xử lý tin nhắn nhận được, gọi `AddMessage()` | UI thread (qua Dispatcher) |
| `AddMessage()` | `OnMessageReceived()` hoặc `SendMessage()` | Thêm tin nhắn vào ListBox | UI thread |

**Cây gọi hàm Client:**

```
User Action (Click/Type)
  │
  ├─ ConnectButton_Click() ────────────────────────────┐
  │   └─ ChatClient.ConnectAsync()                     │
  │       └─ Task.Run(ListenForMessages) ──────────────┼───┐
  │                                                     │   │
  ├─ SendButton_Click() ───────────────────────────────┤   │
  │   └─ SendMessage()                                 │   │
  │       └─ ChatClient.SendMessageAsync()             │   │
  │                                                     │   │
  └─ MessageTextBox_KeyDown(Enter)                     │   │
      └─ SendMessage()                                 │   │
          └─ ChatClient.SendMessageAsync()             │   │
                                                       │   │
      ListenForMessages() [Background] ────────────────┘   │
        ├─ ReadAsync()                                    │
        └─ Dispatcher.Invoke()                            │
            └─ MessageReceived?.Invoke() ─────────────────┼───┐
                                                          │   │
              OnMessageReceived() [UI Thread]            │   │
                └─ AddMessage()                          │   │
                    └─ MessagesListBox.Items.Add()       │   │
                                                        │   │
                                                    (Chạy song song)
```

---

## Tóm tắt quan trọng

### Tại sao các máy giao tiếp được với nhau?

1. **TCP Socket là giao thức chuẩn:**
   - TCP đảm bảo dữ liệu được gửi đúng thứ tự và không bị mất
   - Socket là endpoint cho kết nối TCP/IP
   - Mỗi kết nối có địa chỉ IP và Port duy nhất

2. **Server làm trung gian:**
   - Client không giao tiếp trực tiếp với nhau
   - Tất cả client kết nối đến server
   - Server nhận tin nhắn từ client A và gửi đến client B, C, ...

3. **Network Stack:**
   ```
   Application (ChatClient/ChatServer)
       ↓
   TCP Socket (TcpClient/TcpListener)
       ↓
   IP Protocol
       ↓
   Ethernet/WiFi
       ↓
   Physical Network
   ```

4. **Encoding UTF-8:**
   - Cho phép gửi tiếng Việt, emoji, ký tự đặc biệt
   - Đảm bảo tất cả máy hiểu cùng 1 cách

### Điểm quan trọng về Threading

1. **Server:**
   - `AcceptClients()`: 1 thread chờ client mới
   - `HandleClient()`: Mỗi client có 1 thread riêng
   - `BroadcastMessageAsync()`: Chạy trong thread của `HandleClient()`
   - Dùng `lock` để bảo vệ `_clients` list

2. **Client:**
   - `ListenForMessages()`: Background thread để không block UI
   - UI updates: Phải dùng `Dispatcher.Invoke()` để chuyển về UI thread
   - `SendMessageAsync()`: Async để không block UI

### Vì sao cần Async/Await?

- **Không block UI**: User vẫn tương tác được trong lúc chờ network
- **Hiệu quả**: Nhiều client có thể gửi/nhận đồng thời
- **Responsive**: Ứng dụng không bị "đơ" khi network chậm

---

## Kết luận

Ứng dụng Chat Socket này sử dụng mô hình Client-Server với TCP Socket:
- **Server**: Trung gian nhận và phân phối tin nhắn
- **Client**: Gửi tin nhắn đến server và nhận tin nhắn từ server
- **Giao tiếp**: Qua TCP/IP protocol với encoding UTF-8
- **Threading**: Async/await để xử lý nhiều client đồng thời mà không block

File này giải thích chi tiết cách mỗi hàm hoạt động và cách chúng được gọi lẫn nhau.



