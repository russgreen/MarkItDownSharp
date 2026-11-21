using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MarkItDownSharp.Presentation;

/// <summary>Protects API keys before they are written to disk.</summary>
public interface ISecretProtector
{
    /// <returns>The protected text, or <see langword="null"/> when the secret cannot be stored safely.</returns>
    string? Protect(string secret);

    string? Unprotect(string protectedSecret);
}

/// <summary>Uses Windows DPAPI scoped to the current user. On other platforms secrets are not persisted.</summary>
public sealed class DpapiSecretProtector : ISecretProtector
{
    public string? Protect(string secret) =>
        OperatingSystem.IsWindows() ? ProtectWindows(secret) : null;

    public string? Unprotect(string protectedSecret)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            return UnprotectWindows(protectedSecret);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            // Written by another user or machine: the key must be entered again.
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    private static string ProtectWindows(string secret) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), null, DataProtectionScope.CurrentUser));

    [SupportedOSPlatform("windows")]
    private static string UnprotectWindows(string protectedSecret) =>
        Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(protectedSecret), null, DataProtectionScope.CurrentUser));
}

public sealed class SettingsStore
{
    public const string ImageDescriptionKey = "imageDescription";
    public const string TranscriptionKey = "transcription";
    public const string DocumentIntelligenceKey = "documentIntelligence";
    public const string ContentUnderstandingKey = "contentUnderstanding";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _path;
    private readonly ISecretProtector _protector;
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    public SettingsStore(AppPaths? paths = null, ISecretProtector? protector = null)
    {
        _path = (paths ?? AppPaths.Default).SettingsFile;
        _protector = protector ?? new DpapiSecretProtector();
    }

    public async Task<AppSettings> LoadAsync()
    {
        if (!File.Exists(_path))
        {
            return new AppSettings();
        }

        AppSettings settings;
        await using (var stream = File.OpenRead(_path))
        {
            settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions) ?? new AppSettings();
        }

        var c = settings.Converters;
        c.ImageDescription.ApiKey = Unprotect(settings, ImageDescriptionKey);
        c.Transcription.ApiKey = Unprotect(settings, TranscriptionKey);
        c.DocumentIntelligence.ApiKey = Unprotect(settings, DocumentIntelligenceKey);
        c.ContentUnderstanding.ApiKey = Unprotect(settings, ContentUnderstandingKey);
        return settings;
    }

    public async Task SaveAsync(AppSettings settings)
    {
        var c = settings.Converters;
        var secrets = new Dictionary<string, string>();
        Protect(secrets, ImageDescriptionKey, c.ImageDescription.ApiKey);
        Protect(secrets, TranscriptionKey, c.Transcription.ApiKey);
        Protect(secrets, DocumentIntelligenceKey, c.DocumentIntelligence.ApiKey);
        Protect(secrets, ContentUnderstandingKey, c.ContentUnderstanding.ApiKey);
        settings.ProtectedSecrets = secrets;

        // Several callers can save at once (a toggle and "Save settings"); they share one temporary file.
        await _saveGate.WaitAsync();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporaryPath = _path + ".tmp";
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, settings, JsonOptions);
            }

            File.Move(temporaryPath, _path, true);
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private void Protect(Dictionary<string, string> secrets, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value) && _protector.Protect(value) is { } protectedValue)
        {
            secrets[name] = protectedValue;
        }
    }

    private string? Unprotect(AppSettings settings, string name) =>
        settings.ProtectedSecrets.TryGetValue(name, out var value) ? _protector.Unprotect(value) : null;
}
