namespace EmberFallModResolver.Models;

public class CandidateVersion
{
    public string Source { get; set; } = string.Empty;
    public string ModId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string? DownloadUrl { get; set; }
    public bool Compatible { get; set; }
    public string? Notes { get; set; }
}
