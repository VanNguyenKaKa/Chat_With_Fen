using System.Windows;

namespace CHAT_WITH_FREND
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            try
            {
                // 1. Mở màn hình đăng nhập
                LoginWindow loginWindow = new LoginWindow();
                bool? result = loginWindow.ShowDialog();

                // 2. Nếu đăng nhập thành công (kết quả trả về true và Client không null)
                if (result == true && loginWindow.ConnectedClient != null)
                {
                    // Truyền Client đã kết nối sang MainWindow
                    MainWindow mainWindow = new MainWindow(
                        loginWindow.ConnectedClient,
                        loginWindow.Username,
                        loginWindow.ServerIP
                    );

                    // Đặt MainWindow làm cửa sổ chính
                    Application.Current.MainWindow = mainWindow;

                    // Chuyển chế độ Shutdown: Khi tắt MainWindow thì tắt App
                    this.ShutdownMode = ShutdownMode.OnMainWindowClose;

                    mainWindow.Show();
                }
                else
                {
                    // Người dùng tắt form login hoặc lỗi -> Tắt app thủ công
                    Shutdown();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi khởi chạy ứng dụng: {ex.Message}");
                Shutdown();
            }
        }
    }
}