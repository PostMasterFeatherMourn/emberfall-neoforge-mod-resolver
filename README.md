# EmberFall Mod Resolver

Private local Minecraft NeoForge 1.21.1 modpack compatibility resolver for EmberFall.

## Purpose

This project is a **local compatibility-reporting and staging tool** for personal modpack maintenance.  
It is **not** a launcher, public redistribution platform, mirror, or hosting service for mod files.

## What it does

- Scans a local `mods` folder for `.jar` files
- Reads NeoForge (`META-INF/neoforge.mods.toml` / `META-INF/mods.toml`) and Fabric (`fabric.mod.json`) metadata
- Identifies installed mod IDs, names, versions, loaders, and dependencies
- Queries metadata from Modrinth and CurseForge (when API key is available)
- Generates a compatibility report JSON in the local `updates` folder
- Stages candidate metadata files in the local `updates` folder only

## Safety guarantees

- Never modifies or deletes files in the active mods folder automatically
- Reads CurseForge API keys from the `CURSEFORGE_API_KEY` environment variable
- Keeps `curseForgeApiKey` blank in the example config
- Never prints API keys to console or reports
- Respects author distribution settings and API rules

## Quick start

1. Copy the example config:
   - `cp resolver_config.example.json resolver_config.json`
2. Keep `curseForgeApiKey` blank in `resolver_config.json`
3. Optionally set the environment variable for CurseForge:
   - Linux/macOS: `export CURSEFORGE_API_KEY="your-key"`
   - PowerShell: `$env:CURSEFORGE_API_KEY="your-key"`
4. Place mod jars in your configured mods folder (default: `mods`)
5. Run:
   - `dotnet run`

Report and staged candidate metadata are written to the configured updates folder (default: `updates`).
