using System.Text.Json;
using System.Text.Json.Serialization;

namespace DoubleClickHealer.Core.Config;

/// <summary>
/// 配置文件的读写与热加载。写入采用临时文件 + 替换，避免读到写了一半的 JSON。
/// </summary>
public sealed class ConfigStore : IDisposable
{
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(400);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly object _gate = new();

    private AppConfig _current;
    private string? _lastSavedJson;
    private FileSystemWatcher? _watcher;
    private Timer? _debounce;
    private bool _disposed;

    public ConfigStore(string? path = null)
    {
        _path = path ?? DefaultPath;
        _current = Read();
    }

    public static string DefaultPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DoubleClickHealer",
        "config.json");

    public string Path => _path;

    public AppConfig Current => _current;

    public string? LastError { get; private set; }

    /// <summary>配置文件被外部修改并成功读取后触发。</summary>
    public event Action<AppConfig>? Changed;

    /// <summary>首次启动时确保配置文件存在，不覆盖已有文件。</summary>
    public void EnsureFile()
    {
        if (File.Exists(_path)) return;
        Save(_current);
    }

    public void Save(AppConfig config)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        string? directory = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        string json = JsonSerializer.Serialize(config, JsonOptions);
        string temp = _path + ".tmp";

        File.WriteAllText(temp, json);
        File.Move(temp, _path, overwrite: true);

        lock (_gate)
        {
            _current = config;
            _lastSavedJson = json;
            LastError = null;
        }
    }

    public void StartWatching()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_watcher != null) return;

        string? directory = System.IO.Path.GetDirectoryName(_path);
        if (string.IsNullOrEmpty(directory)) return;

        Directory.CreateDirectory(directory);

        _watcher = new FileSystemWatcher(directory, System.IO.Path.GetFileName(_path))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
        };
        _watcher.Changed += OnFileChanged;
        _watcher.Created += OnFileChanged;
        _watcher.Renamed += OnFileChanged;
        _watcher.EnableRaisingEvents = true;
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _debounce?.Dispose();
            _debounce = new Timer(_ => Reload(), null, DebounceDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private void Reload()
    {
        string json;
        try
        {
            json = File.ReadAllText(_path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LastError = ex.Message;
            return;
        }

        lock (_gate)
        {
            if (_disposed) return;
            if (json == _lastSavedJson) return; // 自己刚写入的内容，无需重载。
        }

        AppConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            LastError = ex.Message;
            return;
        }

        if (config == null) return;

        lock (_gate)
        {
            if (_disposed) return;
            _current = config;
            LastError = null;
        }

        Changed?.Invoke(config);
    }

    private AppConfig Read()
    {
        try
        {
            if (!File.Exists(_path)) return new AppConfig();
            string json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            LastError = ex.Message;
            return new AppConfig();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _debounce?.Dispose();
            _debounce = null;
        }

        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnFileChanged;
            _watcher.Created -= OnFileChanged;
            _watcher.Renamed -= OnFileChanged;
            _watcher.Dispose();
            _watcher = null;
        }
    }
}
