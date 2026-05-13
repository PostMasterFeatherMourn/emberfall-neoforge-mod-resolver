namespace EmberFallModResolver.Models;

public class LocalMod
{
    public string FileName { get; set; } = string.Empty;
    public string ModId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Loader { get; set; } = string.Empty;
    public List<string> Dependencies { get; set; } = new();
}
