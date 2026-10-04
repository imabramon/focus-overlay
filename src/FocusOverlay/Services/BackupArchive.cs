using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using FocusOverlay.Models;
using FocusOverlay.Properties;

namespace FocusOverlay.Services;

public sealed record BackupContents(string SettingsJson, IReadOnlyList<Preset> Presets, int ActivePresetIndex, int ActivePageIndex);

public static class BackupArchive
{
    public const string FileExtension = ".foverlay-backup";
    public static string DialogFilter => Strings.BackupDialogFilter;

    private const string FormatId = "focus-overlay-backup";
    private const int FormatVersion = 1;
    private const string ManifestName = "manifest.json";
    private const string SettingsName = "settings.json";

    private static readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static void Export(AppState state, string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        var manifest = new BackupManifest
        {
            Format = FormatId,
            Version = FormatVersion,
            ActivePresetIndex = state.Settings.ActivePresetIndex,
            ActivePageIndex = state.Settings.ActivePageIndex,
        };

        for (var i = 0; i < state.Presets.Count; i++)
        {
            var prefix = $"presets/{i + 1:D2}/";
            PresetArchive.WriteTo(archive, state.Presets[i], prefix);
            manifest.Presets.Add(prefix);
        }

        PresetArchive.WriteText(archive, SettingsName, SettingsArchive.ToJson(state.Settings));
        PresetArchive.WriteText(archive, ManifestName, JsonSerializer.Serialize(manifest, _options));
    }

    public static BackupContents Read(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var manifestEntry = archive.GetEntry(ManifestName)
            ?? throw new InvalidDataException(Strings.BackupNotBackup);
        var manifest = JsonSerializer.Deserialize<BackupManifest>(PresetArchive.ReadText(manifestEntry), _options)
            ?? throw new InvalidDataException(Strings.ArchiveCannotReadManifest);

        if (manifest.Format != FormatId)
        {
            throw new InvalidDataException(Strings.BackupNotBackup);
        }

        if (manifest.Version > FormatVersion)
        {
            throw new InvalidDataException(string.Format(Strings.BackupNewerVersion, manifest.Version));
        }

        var settingsEntry = archive.GetEntry(SettingsName)
            ?? throw new InvalidDataException(Strings.BackupNoSettings);
        var settingsJson = PresetArchive.ReadText(settingsEntry);

        var presets = new List<Preset>();
        try
        {
            foreach (var prefix in manifest.Presets)
            {
                presets.Add(PresetArchive.ReadFrom(archive, prefix));
            }
        }
        catch (Exception)
        {
            foreach (var preset in presets)
            {
                StateStore.DeleteAssets(preset);
            }

            throw;
        }

        return new BackupContents(settingsJson, presets, manifest.ActivePresetIndex, manifest.ActivePageIndex);
    }

    private sealed class BackupManifest
    {
        public string Format { get; set; } = string.Empty;
        public int Version { get; set; }
        public int ActivePresetIndex { get; set; }
        public int ActivePageIndex { get; set; }
        public List<string> Presets { get; set; } = new();
    }
}
