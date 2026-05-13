using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using EmberFallModResolver.Models;

namespace EmberFallModResolver;

public class Program
{
    private const int CurseForgeMinecraftGameId = 432;
    private const int CurseForgeNeoForgeLoaderType = 6;
    private static readonly Regex DependencySectionRegex = new(@"\[\[dependencies\.[^\]]+\]\](?<block>[\s\S]*?)(?=\r?\n\[\[|$)", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex DependencyModIdRegex = new(@"modId\s*=\s*""([^""]+)""", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static async Task Main(string[] args)
    {
        var workingDirectory = args.Length > 0
            ? Path.GetFullPath(args[0])
            : Directory.GetCurrentDirectory();

        var configPath = Path.Combine(workingDirectory, "resolver_config.json");
        var config = await LoadConfigAsync(configPath);
        var environmentApiKey = Environment.GetEnvironmentVariable("CURSEFORGE_API_KEY");
        if (!string.IsNullOrWhiteSpace(environmentApiKey))
        {
            config.CurseForgeApiKey = environmentApiKey;
        }

        var modsFolder = Path.GetFullPath(Path.Combine(workingDirectory, config.ModsFolderPath));
        var updatesFolder = Path.GetFullPath(Path.Combine(workingDirectory, config.UpdatesFolderPath));
        Directory.CreateDirectory(updatesFolder);

        Console.WriteLine("EmberFall Mod Resolver");
        Console.WriteLine($"Working directory: {workingDirectory}");
        Console.WriteLine($"Mods folder: {modsFolder}");
        Console.WriteLine($"Updates folder: {updatesFolder}");
        Console.WriteLine("Safety: active mods are read-only; no automatic delete/replace operations are performed.");

        var installedMods = ScanLocalMods(modsFolder).ToList();
        var result = new CompatibilityResult
        {
            ScannedAtUtc = DateTime.UtcNow,
            InstalledMods = installedMods
        };
        var curseForgeWarningAdded = false;

        using var httpClient = new HttpClient();
        foreach (var mod in installedMods)
        {
            var modrinthCandidate = await QueryModrinthAsync(httpClient, mod, config);
            if (modrinthCandidate is not null)
            {
                result.Candidates.Add(modrinthCandidate);
            }

            var curseForgeCandidate = await QueryCurseForgeAsync(httpClient, mod, config);
            if (curseForgeCandidate is not null)
            {
                result.Candidates.Add(curseForgeCandidate);
            }
            else if (!curseForgeWarningAdded && string.IsNullOrWhiteSpace(config.CurseForgeApiKey))
            {
                result.Warnings.Add("CurseForge lookup skipped because CURSEFORGE_API_KEY was not set.");
                curseForgeWarningAdded = true;
            }
        }

        var reportPath = Path.Combine(updatesFolder, $"compatibility_report_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(result, JsonOptions));
        StageCandidateMetadataFiles(result.Candidates, updatesFolder);

        Console.WriteLine($"Installed mods scanned: {installedMods.Count}");
        Console.WriteLine($"Compatibility candidates found: {result.Candidates.Count}");
        Console.WriteLine($"Report generated: {reportPath}");
    }

    private static IEnumerable<LocalMod> ScanLocalMods(string modsFolder)
    {
        if (!Directory.Exists(modsFolder))
        {
            Console.WriteLine("Mods folder does not exist. Create it and add .jar files to scan.");
            return [];
        }

        var localMods = new List<LocalMod>();
        foreach (var filePath in Directory.EnumerateFiles(modsFolder, "*.jar", SearchOption.TopDirectoryOnly))
        {
            localMods.Add(ReadModMetadata(filePath));
        }

        return localMods;
    }

    private static LocalMod ReadModMetadata(string jarPath)
    {
        var fileName = Path.GetFileName(jarPath);
        try
        {
            using var archive = ZipFile.OpenRead(jarPath);

            var neoForgeEntry = archive.GetEntry("META-INF/neoforge.mods.toml") ?? archive.GetEntry("META-INF/mods.toml");
            if (neoForgeEntry is not null)
            {
                using var stream = neoForgeEntry.Open();
                using var reader = new StreamReader(stream, Encoding.UTF8);
                var toml = reader.ReadToEnd();

                var modId = ExtractTomlValue(toml, "modId") ?? Path.GetFileNameWithoutExtension(fileName);
                return new LocalMod
                {
                    FileName = fileName,
                    Loader = "NeoForge",
                    ModId = modId,
                    DisplayName = ExtractTomlValue(toml, "displayName") ?? Path.GetFileNameWithoutExtension(fileName),
                    Version = ExtractTomlValue(toml, "version") ?? "unknown",
                    Dependencies = ExtractTomlDependencies(toml, modId)
                };
            }

            var fabricEntry = archive.GetEntry("fabric.mod.json");
            if (fabricEntry is not null)
            {
                using var stream = fabricEntry.Open();
                using var document = JsonDocument.Parse(stream);
                var root = document.RootElement;

                var dependencies = new List<string>();
                if (root.TryGetProperty("depends", out var dependsElement) && dependsElement.ValueKind == JsonValueKind.Object)
                {
                    dependencies.AddRange(dependsElement.EnumerateObject().Select(x => x.Name));
                }

                return new LocalMod
                {
                    FileName = fileName,
                    Loader = "Fabric",
                    ModId = root.TryGetProperty("id", out var id) ? id.GetString() ?? Path.GetFileNameWithoutExtension(fileName) : Path.GetFileNameWithoutExtension(fileName),
                    DisplayName = root.TryGetProperty("name", out var name) ? name.GetString() ?? Path.GetFileNameWithoutExtension(fileName) : Path.GetFileNameWithoutExtension(fileName),
                    Version = root.TryGetProperty("version", out var version) ? version.GetString() ?? "unknown" : "unknown",
                    Dependencies = dependencies
                };
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or JsonException)
        {
            Console.WriteLine($"Failed to read metadata from {fileName}: {ex.Message}");
        }

        return new LocalMod
        {
            FileName = fileName,
            Loader = "Unknown",
            ModId = Path.GetFileNameWithoutExtension(fileName),
            DisplayName = Path.GetFileNameWithoutExtension(fileName),
            Version = "unknown"
        };
    }

    private static string? ExtractTomlValue(string content, string key)
    {
        var regex = new Regex($@"{Regex.Escape(key)}\s*=\s*""([^""]+)""", RegexOptions.Multiline);
        var match = regex.Match(content);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static List<string> ExtractTomlDependencies(string content, string modId)
    {
        var dependencySectionMatches = DependencySectionRegex.Matches(content);
        var values = dependencySectionMatches
            .SelectMany(section => DependencyModIdRegex.Matches(section.Groups["block"].Value)
                .Select(match => match.Groups[1].Value))
            .Where(value => !string.Equals(value, "minecraft", StringComparison.OrdinalIgnoreCase))
            .Where(value => !string.Equals(value, modId, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return values;
    }

    private static async Task<CandidateVersion?> QueryModrinthAsync(HttpClient httpClient, LocalMod mod, ResolverConfig config)
    {
        try
        {
            var loaderParameter = Uri.EscapeDataString(JsonSerializer.Serialize(new[] { config.PrimaryLoader.ToLowerInvariant() }));
            var gameVersionParameter = Uri.EscapeDataString(JsonSerializer.Serialize(new[] { config.MinecraftVersion }));
            var url = $"{config.ModrinthApiBaseUrl}/project/{mod.ModId}/version?loaders={loaderParameter}&game_versions={gameVersionParameter}";
            using var response = await httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var stream = await response.Content.ReadAsStreamAsync();
            using var document = await JsonDocument.ParseAsync(stream);
            if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() == 0)
            {
                return null;
            }

            var latest = document.RootElement[0];
            string? downloadUrl = null;
            if (latest.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array && files.GetArrayLength() > 0)
            {
                downloadUrl = files[0].TryGetProperty("url", out var urlElement) ? urlElement.GetString() : null;
            }

            return new CandidateVersion
            {
                Source = "Modrinth",
                ModId = mod.ModId,
                DisplayName = mod.DisplayName,
                Version = latest.TryGetProperty("version_number", out var versionNumber) ? versionNumber.GetString() ?? "unknown" : "unknown",
                DownloadUrl = downloadUrl,
                Compatible = true
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            Console.WriteLine($"Modrinth query failed for {mod.ModId}: {ex.Message}");
            return null;
        }
    }

    private static async Task<CandidateVersion?> QueryCurseForgeAsync(HttpClient httpClient, LocalMod mod, ResolverConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.CurseForgeApiKey))
        {
            return null;
        }

        try
        {
            var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{config.CurseForgeApiBaseUrl}/mods/search?gameId={CurseForgeMinecraftGameId}&slug={Uri.EscapeDataString(mod.ModId)}&gameVersion={Uri.EscapeDataString(config.MinecraftVersion)}&modLoaderType={CurseForgeNeoForgeLoaderType}");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Add("x-api-key", config.CurseForgeApiKey);

            using var response = await httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var stream = await response.Content.ReadAsStreamAsync();
            using var document = await JsonDocument.ParseAsync(stream);
            if (!document.RootElement.TryGetProperty("data", out var data) ||
                data.ValueKind != JsonValueKind.Array ||
                data.GetArrayLength() == 0)
            {
                return null;
            }

            var first = data[0];
            string? latestFileName = null;
            string? latestDownloadUrl = null;
            if (first.TryGetProperty("latestFiles", out var files) && files.ValueKind == JsonValueKind.Array && files.GetArrayLength() > 0)
            {
                var latest = files[0];
                latestFileName = latest.TryGetProperty("displayName", out var displayName) ? displayName.GetString() : null;
                latestDownloadUrl = latest.TryGetProperty("downloadUrl", out var downloadUrl) ? downloadUrl.GetString() : null;
            }

            return new CandidateVersion
            {
                Source = "CurseForge",
                ModId = mod.ModId,
                DisplayName = first.TryGetProperty("name", out var name) ? name.GetString() ?? mod.DisplayName : mod.DisplayName,
                Version = latestFileName ?? "unknown",
                DownloadUrl = latestDownloadUrl,
                Compatible = true
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            Console.WriteLine($"CurseForge query failed for {mod.ModId}: {ex.Message}");
            return null;
        }
    }

    private static async Task<ResolverConfig> LoadConfigAsync(string configPath)
    {
        if (!File.Exists(configPath))
        {
            return new ResolverConfig();
        }

        try
        {
            var json = await File.ReadAllTextAsync(configPath);
            return JsonSerializer.Deserialize<ResolverConfig>(json, JsonOptions) ?? new ResolverConfig();
        }
        catch (JsonException)
        {
            Console.WriteLine("resolver_config.json is invalid JSON. Falling back to default configuration.");
            return new ResolverConfig();
        }
    }

    private static void StageCandidateMetadataFiles(IEnumerable<CandidateVersion> candidates, string updatesFolder)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars().ToHashSet();
        foreach (var candidate in candidates.Where(x => !string.IsNullOrWhiteSpace(x.DownloadUrl)))
        {
            var safeName = new string($"{candidate.ModId}_{candidate.Source}".Select(character => invalidCharacters.Contains(character) ? '_' : character).ToArray());
            var candidatePath = Path.Combine(updatesFolder, $"{safeName}.candidate.txt");
            var content = new StringBuilder()
                .AppendLine($"source={candidate.Source}")
                .AppendLine($"modId={candidate.ModId}")
                .AppendLine($"displayName={candidate.DisplayName}")
                .AppendLine($"version={candidate.Version}")
                .AppendLine($"downloadUrl={candidate.DownloadUrl}")
                .ToString();
            File.WriteAllText(candidatePath, content);
        }
    }
}
