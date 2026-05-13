namespace EmberFallModResolver.Models;

public class ResolverConfig
{
    public string ModsFolderPath { get; set; } = "mods";
    public string UpdatesFolderPath { get; set; } = "updates";
    public string MinecraftVersion { get; set; } = "1.21.1";
    public string PrimaryLoader { get; set; } = "NeoForge";
    public string ModrinthApiBaseUrl { get; set; } = "https://api.modrinth.com/v2";
    public string CurseForgeApiBaseUrl { get; set; } = "https://api.curseforge.com/v1";
    public string CurseForgeApiKey { get; set; } = string.Empty;
}
