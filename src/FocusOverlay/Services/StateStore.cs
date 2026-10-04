using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using FocusOverlay.Models;

namespace FocusOverlay.Services;

public static class StateStore
{
    private static readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly string[] _imageExtensions = [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff", ".ico"];

    public static bool IsImageFile(string path) =>
        Array.IndexOf(_imageExtensions, Path.GetExtension(path).ToLowerInvariant()) >= 0;

    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FocusOverlay");

    private static string StatePath => Path.Combine(DataDirectory, "state.json");

    public static string GetAssetsDirectory(Preset preset) =>
        Path.Combine(DataDirectory, "presets", preset.Id, "assets");

    public static AppState Load()
    {
        try
        {
            if (File.Exists(StatePath))
            {
                var state = JsonSerializer.Deserialize<AppState>(File.ReadAllText(StatePath), _options);
                if (state != null)
                {
                    state.Normalize();
                    return state;
                }
            }
        }
        catch (Exception)
        {
            try
            {
                File.Copy(StatePath, Path.Combine(DataDirectory, $"state.broken-{DateTime.Now:yyyyMMdd-HHmmss}.json"), true);
            }
            catch (Exception)
            {
            }
        }

        var fallback = AppState.CreateDefault();
        fallback.Normalize();
        return fallback;
    }

    public static void Save(AppState state)
    {
        Directory.CreateDirectory(DataDirectory);
        var tempPath = StatePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(state, _options));
        File.Move(tempPath, StatePath, true);
    }

    public static void DeleteAssets(Preset preset)
    {
        var directory = Path.GetDirectoryName(GetAssetsDirectory(preset));
        if (directory != null && Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }

    public static string ImportAsset(Preset preset, string sourcePath)
    {
        var directory = GetAssetsDirectory(preset);
        Directory.CreateDirectory(directory);
        var fileName = MakeUniqueFileName(directory, Path.GetFileName(sourcePath));
        File.Copy(sourcePath, Path.Combine(directory, fileName));
        return fileName;
    }

    public static string CreateAssetPath(Preset preset, string desiredName, out string fileName)
    {
        var directory = GetAssetsDirectory(preset);
        Directory.CreateDirectory(directory);
        fileName = MakeUniqueFileName(directory, desiredName);
        return Path.Combine(directory, fileName);
    }

    public static Uri? ResolveAsset(Preset preset, string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        if (Uri.TryCreate(source, UriKind.Absolute, out var absolute)
            && (absolute.IsFile || absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            return absolute;
        }

        var directory = Path.GetFullPath(GetAssetsDirectory(preset));
        var relative = Uri.UnescapeDataString(source).Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(directory, relative));
        if (!fullPath.StartsWith(directory, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (File.Exists(fullPath))
        {
            return new Uri(fullPath);
        }

        var byName = FindAssetByName(directory, Path.GetFileName(fullPath));
        return byName != null ? new Uri(byName) : null;
    }

    private static string? FindAssetByName(string directory, string fileName)
    {
        if (fileName.Length == 0 || !Directory.Exists(directory))
        {
            return null;
        }

        return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .FirstOrDefault(path => string.Equals(Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase));
    }

    private static string MakeUniqueFileName(string directory, string fileName)
    {
        var name = SanitizeFileName(Path.GetFileNameWithoutExtension(fileName));
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var candidate = name + extension;
        var counter = 2;
        while (File.Exists(Path.Combine(directory, candidate)))
        {
            candidate = $"{name}-{counter++}{extension}";
        }

        return candidate;
    }

    public static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Trim().ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalid, chars[i]) >= 0 || char.IsWhiteSpace(chars[i]) || chars[i] is '(' or ')' or '[' or ']')
            {
                chars[i] = '-';
            }
        }

        var result = new string(chars);
        return string.IsNullOrEmpty(result) ? "file" : result;
    }
}
