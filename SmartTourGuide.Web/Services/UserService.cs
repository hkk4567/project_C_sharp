using System.Net.Http.Json;
using System.Text.Json;

namespace SmartTourGuide.Web.Services // Kiểm tra lại namespace của bạn nhé
{
    // Lớp chứa dữ liệu trả về từ API
    public class UserDto
    {
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
    }

    public interface IUserService
    {
        Task<UserDto?> GetUserByUsernameAsync(string username);
    }

    public class UserService : IUserService
    {
        private readonly HttpClient _http;
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        public UserService(HttpClient http)
        {
            _http = http;
        }

        public async Task<UserDto?> GetUserByUsernameAsync(string username)
        {
            try 
            {
                // Gọi đến API lấy thông tin User theo Username
                // Lưu ý: Hãy đảm bảo Backend của bạn đã có Route: api/users/{username}
                var response = await _http.GetAsync($"api/users/by-username/{username}");
                
                if (!response.IsSuccessStatusCode)
                    return null;

                var content = await response.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<dynamic>(content, JsonOptions);
                
                // Parse JSON manually để handle camelCase -> PascalCase
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;
                
                return new UserDto
                {
                    FullName = root.GetProperty("fullName").GetString() ?? string.Empty,
                    Email = root.GetProperty("email").GetString() ?? string.Empty
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching user: {ex.Message}");
                return null;
            }
        }
    }
}