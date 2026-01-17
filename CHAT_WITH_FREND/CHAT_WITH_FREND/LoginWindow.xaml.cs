using System.Net.Sockets;
using System.Windows;
using System.Windows.Input;

namespace CHAT_WITH_FREND
{
    public partial class LoginWindow : Window
    {
        // Properties để truyền sang MainWindow
        public TcpClient? ConnectedClient { get; private set; }
        public string Username { get; private set; } = string.Empty;
        public string ServerIP { get; private set; } = string.Empty;

        public LoginWindow()
        {
            InitializeComponent();
        }

        // Kéo thả cửa sổ
        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            // 1. Reset trạng thái
            ErrorText.Text = "";

            // 2. Validate Input
            string name = NameInput.Text.Trim();
            string ip = IpInput.Text.Trim();

            if (string.IsNullOrEmpty(name))
            {
                ErrorText.Text = "Vui lòng nhập tên hiển thị!";
                return;
            }
            if (!int.TryParse(PortInput.Text, out int port))
            {
                ErrorText.Text = "Port phải là số!";
                return;
            }

            // 3. Khóa UI và hiện Loading
            SetLoadingState(true);

            try
            {
                // Tạo mới TcpClient cho mỗi lần thử kết nối
                // (QUAN TRỌNG: Không được dùng lại biến TcpClient cũ đã bị Dispose/Lỗi)
                var tempClient = new TcpClient();

                // Tạo task kết nối với Timeout 3 giây
                var connectTask = tempClient.ConnectAsync(ip, port);
                var timeoutTask = Task.Delay(3000);

                var completedTask = await Task.WhenAny(connectTask, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    // Quá thời gian chờ (Timeout)
                    tempClient.Dispose(); // Hủy kết nối đang treo
                    throw new TimeoutException("Không tìm thấy Server. Vui lòng kiểm tra IP/Port.");
                }

                // Chờ connectTask hoàn tất để chắc chắn không có lỗi socket khác
                await connectTask;

                if (tempClient.Connected)
                {
                    // === KẾT NỐI THÀNH CÔNG ===
                    ConnectedClient = tempClient;
                    Username = name;
                    ServerIP = ip;

                    // Lúc này mới đóng Window và báo thành công cho App.xaml.cs
                    DialogResult = true;
                    Close();
                }
            }
            catch (Exception ex)
            {
                // === KẾT NỐI THẤT BẠI ===
                // Giữ nguyên Window, chỉ hiện thông báo lỗi
                string msg = ex.Message;
                if (ex is SocketException) msg = "Không thể kết nối đến Server.";

                ErrorText.Text = $"Lỗi: {msg}";

                // Mở lại UI để người dùng sửa IP/Port
                SetLoadingState(false);
            }
        }

        private void SetLoadingState(bool isLoading)
        {
            if (isLoading)
            {
                LoadingBar.Visibility = Visibility.Visible;
                BtnConnect.IsEnabled = false;
                BtnConnect.Content = "ĐANG KẾT NỐI...";
                NameInput.IsEnabled = false;
                IpInput.IsEnabled = false;
                PortInput.IsEnabled = false;
            }
            else
            {
                LoadingBar.Visibility = Visibility.Collapsed;
                BtnConnect.IsEnabled = true;
                BtnConnect.Content = "THAM GIA NGAY";
                NameInput.IsEnabled = true;
                IpInput.IsEnabled = true;
                PortInput.IsEnabled = true;
            }
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
    }
}