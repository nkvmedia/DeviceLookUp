using System.Text.Json;
using DeviceLookup.Core.Models;

namespace DeviceLookup.Core.Services;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly string _path;
    private readonly ISecretProtector _protector;

    public SettingsStore(string path, ISecretProtector protector)
    {
        _path = path;
        _protector = protector;
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_path)) return new AppSettings();
            var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new AppSettings();
            if (!string.IsNullOrEmpty(s.DellClientSecretProtected))
            {
                try { s.DellClientSecret = _protector.Unprotect(s.DellClientSecretProtected); }
                catch (Exception ex) { AppLogger.Error("Couldn't decrypt the saved Dell secret", ex); }
            }
            s.ConcurrentLookups = Math.Clamp(s.ConcurrentLookups, 1, 10);
            s.CacheDays = Math.Clamp(s.CacheDays, 1, 90);
            return s;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Couldn't read settings, using defaults", ex);
            return new AppSettings();
        }
    }

    public void Save(AppSettings s)
    {
        try
        {
            s.DellClientSecretProtected = string.IsNullOrEmpty(s.DellClientSecret) ? "" : _protector.Protect(s.DellClientSecret);
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(s, Options));
        }
        catch (Exception ex) { AppLogger.Error("Couldn't save settings", ex); }
    }
}
