using System.Text.Json;

namespace MarkItDownSharp.Desktop;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MarkItDownSharp",
        "settings.json");

    public async Task<AppSettings> LoadAsync()
    {
        if (!File.Exists(_path))
        {
            return new AppSettings();
        }

        await using var stream = File.OpenRead(_path);
        return await JsonSerializer.DeserializeAsync<AppSettings>(stream) ?? new AppSettings();
    }

    public async Task SaveAsync(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporaryPath = _path + ".tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, settings, JsonOptions);
        }

        File.Move(temporaryPath, _path, true);
    }
}
