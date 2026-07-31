using System.ComponentModel.DataAnnotations;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using Vecxy.Diagnostics;

namespace Vecxy.Assets;

public interface IYamlConfig
{
    void Validate();
}

public interface IConfigProvider
{
    void Register<T>() where T : class, IYamlConfig;
    ConfigRef<T> LoadConfig<T>(string path) where T : class, IYamlConfig;
    IReadOnlyList<IConfigRef> GetLoadedConfigs();
    void SaveConfig(IConfigRef config, object value);
}

public interface IObservableConfig<T> where T : class, IYamlConfig
{
    event Action<T>? Changed;
    string Path { get; }
    int Version { get; }
    Exception? LastError { get; }
    T Value { get; }
    bool TryGetValue(out T? value);
}

public interface IConfigRef
{
    string Path { get; }
    Type ValueType { get; }
    int Version { get; }
    Exception? LastError { get; }
    bool TryGetUntypedValue(out object? value);
    void NotifySourceChanged();
}

public sealed class ConfigRef<T> : IDisposable,  IConfigRef, IObservableConfig<T> where T : class, IYamlConfig
{
    private readonly AssetRef<TextAsset> _source;
    private readonly Action<IConfigRef> _onDisposed;
    private T? _cachedValue;
    private int _observedVersion;
    private Exception? _lastError;
    private bool _disposed;

    internal ConfigRef(
        AssetRef<TextAsset> source,
        Action<IConfigRef> onDisposed)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(onDisposed);
        _source = source.Acquire();
        _onDisposed = onDisposed;
    }

    public string Path => _source.Metadata.Path;
    public Type ValueType => typeof(T);
    public int Version => _source.Version;
    public Exception? LastError => _lastError;
    public event Action<T>? Changed;

    public T Value
    {
        get
        {
            ThrowIfDisposed();
            Refresh();

            return _cachedValue
                   ?? throw new InvalidOperationException(
                       $"Config '{Path}' has no valid value.",
                       _lastError);
        }
    }

    public bool TryGetValue(out T? value)
    {
        ThrowIfDisposed();
        Refresh();
        value = _cachedValue;
        return value is not null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _onDisposed(this);
        _source.Dispose();
        _cachedValue = null;
        _lastError = null;
    }

    public void NotifySourceChanged()
    {
        ThrowIfDisposed();
        Refresh(notify: true);
    }

    public bool TryGetUntypedValue(out object? value)
    {
        var ok = TryGetValue(out var typed);
        value = typed;
        return ok;
    }

    private void Refresh()
    {
        Refresh(notify: false);
    }

    private void Refresh(bool notify)
    {
        if (_observedVersion == _source.Version)
            return;

        try
        {
            var value = YamlConfigSerializer.Deserialize<T>(
                _source.Value.Content,
                Path);
            _cachedValue = value;
            _lastError = null;

            if (notify)
                Changed?.Invoke(value);
        }
        catch (Exception exception)
        {
            _lastError = exception;

            if (_cachedValue is not null)
            {
                Logger.Error(
                    exception,
                    $"Config reload failed, keeping previous value: {Path}");
            }
        }
        finally
        {
            _observedVersion = _source.Version;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}

internal static class YamlConfigSerializer
{
    private static readonly IDeserializer Deserializer =
        new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

    public static T Deserialize<T>(
        string source,
        string path)
        where T : class, IYamlConfig
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var value = Deserializer.Deserialize<T>(source)
                    ?? throw new InvalidDataException(
                        $"Config is empty: {path}");

        try
        {
            value.Validate();
        }
        catch (Exception e)
        {
            throw new ValidationException($"Config '{path}' no validate!", e);
        }
       
        return value;
    }
}
