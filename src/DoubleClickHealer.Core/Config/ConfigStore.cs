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
    private long _saveVersion;
    private FileSystemWatcher? _watcher;
    private Timer? _debounce;
    private bool _disposed;

    public ConfigStore(string? path = null)
    {
        _path = path ?? DefaultPath;
        _current = Read();

        if (RecoveredFromCorruptFile)
        {
            // 损坏文件已备份，立即写入默认配置，避免下次启动重复告警。
            try
            {
                WriteAtomic(JsonSerializer.Serialize(_current, JsonOptions));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LastError = ex.Message;
            }
        }
    }

    public static string DefaultPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DoubleClickHealer",
        "config.json");

    public string Path => _path;

    public AppConfig Current => _current;

    public string? LastError { get; private set; }

    /// <summary>启动时发现配置文件结构损坏，并已备份为 .bad-时间戳。</summary>
    public bool RecoveredFromCorruptFile { get; private set; }

    /// <summary>配置文件被外部修改并成功读取后触发。</summary>
    public event Action<AppConfig>? Changed;

    /// <summary>首次启动时确保配置文件存在，不覆盖已有文件。</summary>
    public void EnsureFile()
    {
        if (File.Exists(_path)) return;
        Save(_current);
    }

    /// <summary>写入配置。失败时记录 LastError 并返回 false，不抛异常。</summary>
    public bool Save(AppConfig config)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        string json = JsonSerializer.Serialize(config, JsonOptions);

        string? directory = System.IO.Path.GetDirectoryName(_path);
        try
        {
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            lock (_gate)
            {
                LastError = ex.Message;
            }

            return false;
        }

        Exception? lastError = null;

        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                WriteAtomic(json);

                lock (_gate)
                {
                    _current = config;
                    _lastSavedJson = json;
                    _saveVersion++;
                    LastError = null;
                }

                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lastError = ex;
                if (attempt < 2) Thread.Sleep(50);
            }
        }

        lock (_gate)
        {
            LastError = lastError?.Message;
        }

        return false;
    }

    public void StartWatching()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_watcher != null) return;

        string? directory = System.IO.Path.GetDirectoryName(_path);
        if (string.IsNullOrEmpty(directory)) return;

        try
        {
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _watcher?.Dispose();
            _watcher = null;

            lock (_gate)
            {
                LastError = ex.Message;
            }
        }
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
            lock (_gate)
            {
                LastError = ex.Message;
            }

            return;
        }

        long version;
        lock (_gate)
        {
            if (_disposed) return;
            if (json == _lastSavedJson) return; // 自己刚写入的内容，无需重载。
            version = _saveVersion;
        }

        AppConfig config;
        try
        {
            var parsed = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
            if (parsed == null) return;
            config = AppConfig.Migrate(parsed);
        }
        catch (Exception ex)
        {
            // 外部编辑写入了非法内容：保留当前配置，等待下一次修改。
            lock (_gate)
            {
                LastError = ex.Message;
            }

            return;
        }

        lock (_gate)
        {
            // 反序列化期间用户保存过配置，本次外部修改已过期。
            if (_disposed || _saveVersion != version) return;
            _current = config;
            LastError = null;
        }

        try
        {
            Changed?.Invoke(config);
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                LastError = ex.Message;
            }
        }
    }

    private AppConfig Read()
    {
        try
        {
            if (!File.Exists(_path)) return new AppConfig();
            string json = File.ReadAllText(_path);
            var parsed = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
            return AppConfig.Migrate(parsed);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 暂时性 IO 错误：保留原文件，不备份也不覆盖。
            LastError = ex.Message;
            return new AppConfig();
        }
        catch (Exception ex)
        {
            // 结构损坏（JSON 语法、null 元素、类型不匹配）：备份后使用默认配置。
            LastError = ex.Message;
            RecoveredFromCorruptFile = BackupBrokenFile();
            return new AppConfig();
        }
    }

    private bool BackupBrokenFile()
    {
        try
        {
            if (!File.Exists(_path)) return false;

            string backup = _path + ".bad-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            File.Move(_path, backup, overwrite: false);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void WriteAtomic(string json)
    {
        string temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, json);
            File.Move(temp, _path, overwrite: true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
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
