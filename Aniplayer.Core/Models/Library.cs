namespace Aniplayer.Core.Models;

public class Library
{
    public int Id { get; set; }
    public string Path { get; set; } = string.Empty;
    public string? Label { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
}
