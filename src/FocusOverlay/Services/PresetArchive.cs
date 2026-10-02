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
        var manifest = new PresetManifest { Format = FormatId, Version = FormatVersion, Name = preset.Name };

        for (var i = 0; i < preset.Pages.Count; i++)
        {
            var page = preset.Pages[i];
            var entryName = $"pages/{i + 1:D2}.md";
            WriteText(archive, entryName, page.Content);
            manifest.Pages.Add(new PresetManifestPage { Title = page.Title, File = entryName });
        }

        var assetsDirectory = StateStore.GetAssetsDirectory(preset);
        if (Directory.Exists(assetsDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(assetsDirectory, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(assetsDirectory, file).Replace(Path.DirectorySeparatorChar, '/');
                archive.CreateEntryFromFile(file, AssetsPrefix + relative, CompressionLevel.Optimal);
            }
        }

        WriteText(archive, ManifestName, JsonSerializer.Serialize(manifest, _options));
    }

    public static Preset Import(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var preset = new Preset { Name = Path.GetFileNameWithoutExtension(path) };

        var manifestEntry = archive.GetEntry(ManifestName);
        if (manifestEntry != null)
        {
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

            if (!string.IsNullOrWhiteSpace(manifest.Name))
            {
                preset.Name = manifest.Name;
            }

            foreach (var page in manifest.Pages)
            {
                var entry = archive.GetEntry(page.File);
                preset.Pages.Add(new OverlayPage
                {
                    Title = string.IsNullOrWhiteSpace(page.Title) ? "Без названия" : page.Title,
                    Content = entry != null ? ReadText(entry) : string.Empty,
                });
            }
        }
        else
        {
            var markdownEntries = archive.Entries
                .Where(entry => entry.FullName.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
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
        }

        ExtractAssets(archive, preset);
        return preset;
    }

    private static void ExtractAssets(ZipArchive archive, Preset preset)
    {
        var assetsDirectory = Path.GetFullPath(StateStore.GetAssetsDirectory(preset));

        foreach (var entry in archive.Entries)
        {
            if (!entry.FullName.StartsWith(AssetsPrefix, StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            var relative = entry.FullName[AssetsPrefix.Length..].Replace('/', Path.DirectorySeparatorChar);
            var target = Path.GetFullPath(Path.Combine(assetsDirectory, relative));
            if (!target.StartsWith(assetsDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, true);
        }
    }

    private static void WriteText(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string ReadText(ZipArchiveEntry entry)
    {
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8, true);
        return reader.ReadToEnd();
    }

    private sealed class PresetManifest
    {
        public string Format { get; set; } = string.Empty;
        public int Version { get; set; }
        public string Name { get; set; } = string.Empty;
        public List<PresetManifestPage> Pages { get; set; } = new();
    }

    private sealed class PresetManifestPage
    {
        public string Title { get; set; } = string.Empty;
        public string File { get; set; } = string.Empty;
    }
}
