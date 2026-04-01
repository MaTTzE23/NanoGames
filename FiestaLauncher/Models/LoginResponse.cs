namespace FiestaLauncher.Models
{
    public class LoginRequest
    {
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public string RawPassword { get; set; } = "";
    }

    public class LoginResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string Token { get; set; } = "";
        public string AccountId { get; set; } = "";
        public int AccountStatus { get; set; }
    }
}
