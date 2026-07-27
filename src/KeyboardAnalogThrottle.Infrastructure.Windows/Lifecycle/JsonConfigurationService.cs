using System.Text.Json;
using System.Text.Json.Serialization;
using KeyboardAnalogThrottle.Core.Abstractions;
using KeyboardAnalogThrottle.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace KeyboardAnalogThrottle.Infrastructure.Windows.Lifecycle;

/// <summary>
/// Persists application settings as JSON and only publishes a replacement configuration after it has been fully parsed and validated.
/// </summary>
public sealed class JsonConfigurationService : IConfigurationService, IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _configurationPath;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _currentGate = new();
    private readonly object _watcherGate = new();
    private readonly FileSystemWatcher _watcher;
    private readonly ILogger<JsonConfigurationService> _logger;
    private AppConfiguration _current = AppConfiguration.CreateDefault();
    private CancellationTokenSource? _reloadDebounce;
    private int _disposed;

    public JsonConfigurationService()
        : this(GetDefaultConfigurationPath(), logger: null)
    {
    }

    public JsonConfigurationService(string configurationPath, ILogger<JsonConfigurationService>? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationPath);
        _configurationPath = Path.GetFullPath(configurationPath);
        _logger = logger ?? NullLogger<JsonConfigurationService>.Instance;
        var directory = Path.GetDirectoryName(_configurationPath)
            ?? throw new ArgumentException("The configuration path must include a directory.", nameof(configurationPath));
        Directory.CreateDirectory(directory);

        _watcher = new FileSystemWatcher(directory, Path.GetFileName(_configurationPath))
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };
        _watcher.Changed += OnConfigurationFileChanged;
        _watcher.Created += OnConfigurationFileChanged;
        _watcher.Deleted += OnConfigurationFileChanged;
        _watcher.Renamed += OnConfigurationFileRenamed;
    }

    public static string GetDefaultConfigurationPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KeyboardAnalogThrottle",
        "config.json");

    public AppConfiguration Current
    {
        get
        {
            lock (_currentGate)
            {
                return _current;
            }
        }
    }

    public event Func<AppConfiguration, CancellationToken, Task>? ConfigurationChanged;

    public async Task<ConfigurationReloadResult> ReloadAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            AppConfiguration candidate;
            if (File.Exists(_configurationPath))
            {
                var read = await ReadConfigurationAsync(cancellationToken).ConfigureAwait(false);
                if (read.Error is not null)
                {
                    return ConfigurationReloadResult.Failure([read.Error]);
                }

                candidate = read.Configuration!;
            }
            else
            {
                candidate = AppConfiguration.CreateDefault();
                if (!await WriteAtomicallyAsync(candidate, replaceExisting: false, cancellationToken: cancellationToken).ConfigureAwait(false))
                {
                    var read = await ReadConfigurationAsync(cancellationToken).ConfigureAwait(false);
                    if (read.Error is not null)
                    {
                        return ConfigurationReloadResult.Failure([read.Error]);
                    }

                    candidate = read.Configuration!;
                }
            }

            var errors = ConfigurationValidator.Validate(candidate);
            if (errors.Count != 0)
            {
                return ConfigurationReloadResult.Failure(errors);
            }

            try
            {
                await PublishConfigurationAsync(candidate, cancellationToken).ConfigureAwait(false);
                return ConfigurationReloadResult.Success;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Configuration reload could not be applied.");
                return ConfigurationReloadResult.Failure([
                    new ConfigurationValidationError("$", $"Configuration could not be applied: {exception.Message}")
                ]);
            }
        }
        catch (IOException exception)
        {
            return ConfigurationReloadResult.Failure([new ConfigurationValidationError("$", $"Configuration file could not be read: {exception.Message}")]);
        }
        catch (UnauthorizedAccessException exception)
        {
            return ConfigurationReloadResult.Failure([new ConfigurationValidationError("$", $"Configuration file could not be read: {exception.Message}")]);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task SaveAsync(AppConfiguration configuration, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(configuration);
        var errors = ConfigurationValidator.Validate(configuration);
        if (errors.Count != 0)
        {
            throw new ArgumentException(string.Join(Environment.NewLine, errors.Select(static error => $"{error.PropertyName}: {error.Message}")), nameof(configuration));
        }

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await WriteAtomicallyAsync(configuration, replaceExisting: true, cancellationToken: cancellationToken).ConfigureAwait(false);
            await PublishConfigurationAsync(configuration, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _watcher.EnableRaisingEvents = false;
        _watcher.Changed -= OnConfigurationFileChanged;
        _watcher.Created -= OnConfigurationFileChanged;
        _watcher.Deleted -= OnConfigurationFileChanged;
        _watcher.Renamed -= OnConfigurationFileRenamed;
        _watcher.Dispose();
        lock (_watcherGate)
        {
            _reloadDebounce?.Cancel();
        }
    }

    private async Task<(AppConfiguration? Configuration, ConfigurationValidationError? Error)> ReadConfigurationAsync(CancellationToken cancellationToken)
    {
        try
        {
            var json = await File.ReadAllTextAsync(_configurationPath, cancellationToken).ConfigureAwait(false);
            var configuration = JsonSerializer.Deserialize<AppConfiguration>(json, SerializerOptions);
            return configuration is null
                ? (null, new ConfigurationValidationError("$", "Configuration JSON must contain an object."))
                : (configuration, null);
        }
        catch (JsonException exception)
        {
            return (null, new ConfigurationValidationError("$", $"Configuration JSON is invalid: {exception.Message}"));
        }
    }

    private async Task<bool> WriteAtomicallyAsync(
        AppConfiguration configuration,
        bool replaceExisting,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_configurationPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(_configurationPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, configuration, SerializerOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            if (!replaceExisting)
            {
                try
                {
                    File.Move(temporaryPath, _configurationPath, overwrite: false);
                    return true;
                }
                catch (IOException) when (File.Exists(_configurationPath))
                {
                    return false;
                }
            }

            if (File.Exists(_configurationPath))
            {
                File.Replace(temporaryPath, _configurationPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, _configurationPath);
            }

            return true;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private async Task PublishConfigurationAsync(AppConfiguration configuration, CancellationToken cancellationToken)
    {
        var handlers = ConfigurationChanged;
        if (handlers is not null)
        {
            foreach (Func<AppConfiguration, CancellationToken, Task> handler in handlers.GetInvocationList())
            {
                await handler(configuration, cancellationToken).ConfigureAwait(false);
            }
        }

        lock (_currentGate)
        {
            _current = configuration;
        }
    }

    private void OnConfigurationFileChanged(object sender, FileSystemEventArgs eventArgs) => ScheduleReload();

    private void OnConfigurationFileRenamed(object sender, RenamedEventArgs eventArgs) => ScheduleReload();

    private void ScheduleReload()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        var next = new CancellationTokenSource();
        CancellationTokenSource? previous;
        lock (_watcherGate)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                next.Dispose();
                return;
            }

            previous = _reloadDebounce;
            _reloadDebounce = next;
        }

        previous?.Cancel();
        _ = ReloadAfterDebounceAsync(next);
    }

    private async Task ReloadAfterDebounceAsync(CancellationTokenSource debounce)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), debounce.Token).ConfigureAwait(false);
            if (Volatile.Read(ref _disposed) == 0)
            {
                var result = await ReloadAsync(CancellationToken.None).ConfigureAwait(false);
                if (!result.IsSuccess)
                {
                    foreach (var error in result.Errors)
                    {
                        _logger.LogError(
                            "Configuration watcher reload failed for {PropertyName}: {Message}",
                            error.PropertyName,
                            error.Message);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (debounce.IsCancellationRequested)
        {
            // A newer file event or disposal superseded this reload request.
        }
        catch (ObjectDisposedException)
        {
            // Disposal may race a file-system notification.
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Configuration watcher reload crashed.");
        }
        finally
        {
            lock (_watcherGate)
            {
                if (ReferenceEquals(_reloadDebounce, debounce))
                {
                    _reloadDebounce = null;
                }
            }

            debounce.Dispose();
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }
}
