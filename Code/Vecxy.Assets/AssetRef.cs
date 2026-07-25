namespace Vecxy.Assets;

public sealed class AssetRef<T> : IDisposable where T : class
{
    private AssetRefEntry<T>? _entry;

    public AssetId Id => Entry.Id;
    public AssetMetadata Metadata => Entry.Metadata;
    public int Version => Entry.Version;
    public bool IsLoaded => _entry?.IsLoaded == true;
    public bool HasError => Entry.Error is not null;
    public Exception? Error => Entry.Error;
    public T Value => Entry.Value;

    internal AssetRef(AssetRefEntry<T> entry)
    {
        _entry = entry;
    }

    public AssetRef<T> Acquire()
    {
        var entry = Entry;
        entry.Acquire();
        return new AssetRef<T>(entry);
    }

    public void Dispose()
    {
        var entry = Interlocked.Exchange(ref _entry, null);
        entry?.Release();
    }

    private AssetRefEntry<T> Entry =>
        _entry ?? throw new ObjectDisposedException(
            nameof(AssetRef<T>),
            "The asset reference has already been released.");
}

internal interface IAssetRefEntry
{
    AssetId Id { get; }
    AssetMetadata Metadata { get; }
    Type ValueType { get; }
    int ReferenceCount { get; }

    object? Replace(object value);
    void MarkFailed(Exception exception);
    object? ForceUnload();
}

internal sealed class AssetRefEntry<T> : IAssetRefEntry where T : class
{
    private readonly Action<AssetRefEntry<T>> _onLastReferenceReleased;
    private T? _value;
    private int _referenceCount;

    public AssetId Id => Metadata.Id;
    public AssetMetadata Metadata { get; }
    public Type ValueType => typeof(T);
    public int Version { get; private set; } = 1;
    public int ReferenceCount => _referenceCount;
    public bool IsLoaded => _value is not null;
    public Exception? Error { get; private set; }

    public T Value =>
        _value ?? throw new InvalidOperationException(
            $"Asset '{Metadata.Path}' has no valid value.",
            Error);

    public AssetRefEntry(
        AssetMetadata metadata,
        T value,
        Action<AssetRefEntry<T>> onLastReferenceReleased)
    {
        Metadata = metadata;
        _value = value;
        _onLastReferenceReleased = onLastReferenceReleased;
    }

    public AssetRefEntry(
        AssetMetadata metadata,
        Exception error,
        Action<AssetRefEntry<T>> onLastReferenceReleased)
    {
        Metadata = metadata;
        Error = error;
        _onLastReferenceReleased = onLastReferenceReleased;
    }

    public AssetRef<T> CreateReference()
    {
        Acquire();
        return new AssetRef<T>(this);
    }

    public void Acquire()
    {
        if (_value is null && Error is null)
        {
            throw new InvalidOperationException(
                $"Asset '{Metadata.Path}' is not loaded.");
        }

        checked
        {
            _referenceCount++;
        }
    }

    public void Release()
    {
        if (_referenceCount <= 0)
        {
            return;
        }

        _referenceCount--;
        if (_referenceCount == 0)
        {
            _onLastReferenceReleased(this);
        }
    }

    public object? Replace(object value)
    {
        if (value is not T typedValue)
        {
            throw new InvalidCastException(
                $"Cannot put {value.GetType().Name} into AssetRef<{typeof(T).Name}>.");
        }

        var previous = _value;
        _value = typedValue;
        Error = null;
        Version++;
        return previous;
    }

    public void MarkFailed(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Error = exception;
        Version++;
    }

    public object? ForceUnload()
    {
        var previous = _value;
        _value = null;
        return previous;
    }
}
