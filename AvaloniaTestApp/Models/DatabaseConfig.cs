namespace AvaloniaTestApp.Models;

public class DatabaseConfig
{
    public string Host { get; set; } = "localhost";
    public string Port { get; set; } = "5432";
    public string DatabaseName { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}