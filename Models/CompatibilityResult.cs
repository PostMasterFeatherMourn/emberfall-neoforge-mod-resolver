namespace EmberFallModResolver.Models;

public class CompatibilityResult
{
    public DateTime ScannedAtUtc { get; set; }
    public List<LocalMod> InstalledMods { get; set; } = [];
    public List<CandidateVersion> Candidates { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}
