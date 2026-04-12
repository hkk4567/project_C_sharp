namespace SmartTourGuide.Mobile;

// File này điều phối điều hướng tổng của ứng dụng thông qua Shell.
public partial class AppShell : Shell
{
	public AppShell()
	{
		// Khởi tạo giao diện được khai báo trong AppShell.xaml.
		InitializeComponent();
		// Đăng ký route để điều hướng tới MainPage bằng tên route.
		Routing.RegisterRoute(nameof(MainPage), typeof(MainPage));
	}
}