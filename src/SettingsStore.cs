using System.Text.Json;

namespace ClickShow;

internal sealed class SettingsStore : IDisposable
{
    internal static readonly string DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClickShow");
    internal static readonly string FilePath = Path.Combine(DirectoryPath, "settings.json");
    private readonly object gate = new();
    private readonly Timer timer;
    private Settings current;
    private bool dirty;
    public Settings Current => Volatile.Read(ref current);
    public event Action<string>? Error;
    public string? LoadError { get; }
    public SettingsStore()
    {
        current = new();
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath));
                if (loaded is null || !loaded.IsValid()) throw new JsonException("配置格式或取值无效。");
                current = loaded;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { LoadError = $"读取设置失败，暂用默认设置：{ex.Message}"; }
        timer = new(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
    }
    public void Set(Settings value) { lock (gate) { Volatile.Write(ref current, value); dirty = true; } timer.Change(350, Timeout.Infinite); }
    public void Flush()
    {
        lock (gate)
        {
            if (!dirty) return;
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                string temporary = FilePath + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temporary, FilePath, true);
                dirty = false;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Error?.Invoke($"保存设置失败：{ex.Message}"); }
        }
    }
    public void Dispose() { timer.Dispose(); Flush(); }
}
