namespace OrbitFund.Models;

public class LoginResponse
{
    public string Token { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}