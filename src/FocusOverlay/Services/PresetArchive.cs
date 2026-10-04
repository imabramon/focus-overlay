using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using FocusOverlay.Models;

namespace FocusOverlay.Services;

public static class PresetArchive
{
    public const string FileExtension = ".foverlay";
    public const string DialogFilter = "Пресет оверлея (*.foverlay;*.zip)|*.foverlay;*.zip|Все файлы (*.*)|*.*";

    private const string FormatId = "focus-overlay-preset";
    private const int FormatVersion = 1;
    private const string ManifestName = "manifest.json";
    private const string AssetsPrefix = "assets/";

    private static readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static void Export(Preset preset, string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteTo(archive, preset, string.Empty);
    }

    public static Preset Import(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        if (archive.GetEntry(ManifestName) != null)
        {
            return ReadFrom(archive, string.Empty);
        }

        var preset = new Preset { Name = Path.GetFileNameWithoutExtension(path) };
        var systemEntry = archive.Entries.FirstOrDefault(entry => IsSystemEntry(entry, string.Empty));
        if (systemEntry != null)
        {
            preset.SystemContent = ReadText(systemEntry);
        }

        var markdownEntries = archive.Entries
            .Where(entry => entry.FullName.EndsWith(".md", StringComparison.OrdinalIgnoreCase) && entry != systemEntry)
            .OrderBy(entry => entry.FullName, StringComparer.OrdinalIgnoreCase);

        foreach (var entry in markdownEntries)
        {
            preset.Pages.Add(new OverlayPage
            {
                Title = Path.GetFileNameWithoutExtension(entry.Name),
                Content = ReadText(entry),
            });
        }

        if (preset.Pages.Count == 0)
        {
            throw new InvalidDataException("В архиве нет manifest.json и .md файлов");
        }

        ExtractAssets(archive, preset, string.Empty);
        return preset;
    }

    public static void WriteTo(ZipArchive archive, Preset preset, string prefix)
    {
        var manifest = new PresetManifest { Format = FormatId, Version = FormatVersion, Name = preset.Name };
        if (!string.IsNullOrWhiteSpace(preset.SystemContent))
        {
            WriteText(archive, prefix + Preset.SystemFileName, preset.SystemContent);
            manifest.System = Preset.SystemFileName;
        }

        for (var i = 0; i < preset.Pages.Count; i++)
        {
            var page = preset.Pages[i];
            var entryName = $"pages/{i + 1:D2}.md";
            WriteText(archive, prefix + entryName, page.Content);
            manifest.Pages.Add(new PresetManifestPage { Title = page.Title, File = entryName });
        }

        var assetsDirectory = StateStore.GetAssetsDirectory(preset);
        if (Directory.Exists(assetsDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(assetsDirectory, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(assetsDirectory, file).Replace(Path.DirectorySeparatorChar, '/');
                archive.CreateEntryFromFile(file, prefix + AssetsPrefix + relative, CompressionLevel.Optimal);
            }
        }

        WriteText(archive, prefix + ManifestName, JsonSerializer.Serialize(manifest, _options));
    }

    public static Preset ReadFrom(ZipArchive archive, string prefix)
    {
        var manifestEntry = archive.GetEntry(prefix + ManifestName)
            ?? throw new InvalidDataException($"В архиве нет {prefix}{ManifestName}");
        var manifest = JsonSerializer.Deserialize<PresetManifest>(ReadText(manifestEntry), _options)
            ?? throw new InvalidDataException("Не удалось прочитать manifest.json");

        if (manifest.Format != FormatId)
        {
            throw new InvalidDataException("Архив не является пресетом Focus Overlay");
        }

        if (manifest.Version > FormatVersion)
        {
            throw new InvalidDataException($"Пресет создан более новой версией (формат v{manifest.Version})");
        }

        var preset = new Preset { Name = string.IsNullOrWhiteSpace(manifest.Name) ? "Пресет" : manifest.Name };
        if (!string.IsNullOrWhiteSpace(manifest.System) && archive.GetEntry(prefix + manifest.System) is { } systemEntry)
        {
            preset.SystemContent = ReadText(systemEntry);
        }
        foreach (var page in manifest.Pages)
        {
            var entry = archive.GetEntry(prefix + page.File);
            preset.Pages.Add(new OverlayPage
            {
                Title = string.IsNullOrWhiteSpace(page.Title) ? "Без названия" : page.Title,
                Content = entry != null ? ReadText(entry) : string.Empty,
            });
        }

        ExtractAssets(archive, preset, prefix);
        return preset;
    }

    public static void WriteText(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    public static string ReadText(ZipArchiveEntry entry)
    {
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8, true);
        return reader.ReadToEnd();
    }

    private static bool IsSystemEntry(ZipArchiveEntry entry, string prefix) =>
        string.Equals(entry.FullName, prefix + Preset.SystemFileName, StringComparison.OrdinalIgnoreCase);

    private static void ExtractAssets(ZipArchive archive, Preset preset, string prefix)
    {
        var assetsDirectory = Path.GetFullPath(StateStore.GetAssetsDirectory(preset));
        var entryPrefix = prefix + AssetsPrefix;

        foreach (var entry in archive.Entries)
        {
            if (!entry.FullName.StartsWith(entryPrefix, StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            var relative = entry.FullName[entryPrefix.Length..].Replace('/', Path.DirectorySeparatorChar);
            var target = Path.GetFullPath(Path.Combine(assetsDirectory, relative));
            if (!target.StartsWith(assetsDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, true);
        }
    }

    private sealed class PresetManifest
    {
        public string Format { get; set; } = string.Empty;
        public int Version { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? System { get; set; }
        public List<PresetManifestPage> Pages { get; set; } = new();
    }

    private sealed class PresetManifestPage
    {
        public string Title { get; set; } = string.Empty;
        public string File { get; set; } = string.Empty;
    }
}
