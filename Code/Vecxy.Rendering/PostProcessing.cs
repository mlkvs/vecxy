using Vecxy.Assets;
using Vecxy.Scene;

namespace Vecxy.Rendering;

public sealed class PostProcessing : AComponent
{
    private readonly List<APostProcessEffect> _customEffects = [];
    private readonly RetroPostProcessEffect _retro = new();
    private readonly VignettePostProcessEffect _vignette = new();

    public RetroPostProcessEffect Retro => _retro;

    public VignettePostProcessEffect Vignette => _vignette;

    public IReadOnlyList<APostProcessEffect> CustomEffects => _customEffects;

    public PostProcessing()
    {
    }

    public PostProcessing(
        IConfigProvider configs,
        string basePath = "PostProcessing")
    {
        ArgumentNullException.ThrowIfNull(configs);
        ConfigureBuiltIns(configs, basePath);
    }

    public void ConfigureBuiltIns(
        IConfigProvider configs,
        string basePath = "PostProcessing")
    {
        ObjectDisposedException.ThrowIf(IsDestroyed, this);
        ArgumentNullException.ThrowIfNull(configs);

        configs.Register<RetroPostProcessConfig>();
        configs.Register<VignettePostProcessConfig>();

        _retro.SetConfig(
            configs.LoadConfig<RetroPostProcessConfig>(
                $"{basePath}/Retro.postfx"));
        _vignette.SetConfig(
            configs.LoadConfig<VignettePostProcessConfig>(
                $"{basePath}/Vignette.postfx"));
    }

    public void AddEffect(APostProcessEffect effect)
    {
        ObjectDisposedException.ThrowIf(IsDestroyed, this);
        ArgumentNullException.ThrowIfNull(effect);
        _customEffects.Add(effect);
    }

    public void ClearCustomEffects()
    {
        foreach (var effect in _customEffects)
            effect.Dispose();

        _customEffects.Clear();
    }

    public IEnumerable<APostProcessEffect> EnumerateEffects()
    {
        yield return _retro;
        yield return _vignette;

        foreach (var effect in _customEffects)
            yield return effect;
    }

    public override void OnDestroy()
    {
        ClearCustomEffects();
        _retro.Dispose();
        _vignette.Dispose();
    }
}
