using Vecxy.Assets;

namespace Vecxy.Rendering;

public abstract class APostProcessEffect : IDisposable
{
    private AssetRef<ShaderAsset>? _shaderAsset;
    private bool _disposed;

    public abstract string Name { get; }
    public abstract string ShaderPath { get; }
    public abstract bool Enabled { get; }
    public abstract int Order { get; }
    public abstract void Apply(Shader shader, in PostProcessContext context);
    public virtual object? InspectorSettings => null;
    public virtual string? InspectorSourcePath => null;
    public virtual int InspectorVersion => -1;
    public virtual Exception? InspectorError => null;

    internal AssetRef<ShaderAsset> GetShaderAsset(IAssetsManager assets)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(assets);

        return _shaderAsset ??= assets.Load<ShaderAsset>(ShaderPath);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _shaderAsset?.Dispose();
        _shaderAsset = null;
        DisposeCore();
    }

    protected virtual void DisposeCore()
    {
    }
}

public abstract class APostProcessEffect<TConfig> : APostProcessEffect where TConfig : APostProcessConfig
{
    private readonly TConfig _defaults;
    private ConfigRef<TConfig>? _config;

    protected APostProcessEffect(TConfig defaults)
    {
        ArgumentNullException.ThrowIfNull(defaults);
        _defaults = defaults;
    }

    public void SetConfig(ConfigRef<TConfig>? config)
    {
        _config?.Dispose();
        _config = config;
    }

    protected ConfigRef<TConfig>? Config => _config;

    protected TConfig Settings =>
        _config is not null && _config.TryGetValue(out var value)
            ? value
            : _defaults;

    public override object? InspectorSettings =>
        _config is not null && _config.TryGetValue(out var value)
            ? value
            : _defaults;

    public override string? InspectorSourcePath => _config?.Path;

    public override int InspectorVersion => _config?.Version ?? -1;

    public override Exception? InspectorError => _config?.LastError;

    public override bool Enabled => Settings.Enabled;

    public override int Order => Settings.Order;

    protected override void DisposeCore()
    {
        _config?.Dispose();
        _config = null;
    }
}
