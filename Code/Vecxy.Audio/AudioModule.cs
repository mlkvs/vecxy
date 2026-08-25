using Autofac;
using Vecxy.Assets;
using Vecxy.Kernel;

namespace Vecxy.Audio;

public interface IAudioManager
{
    void Preload(string assetPath, bool loop = false);
    void Preload(SoundHandle asset, bool loop = false);
    void Play(string assetPath, bool loop = false, float volume = 1.0f);
    void Play(SoundHandle asset, bool loop = false, float volume = 1.0f);
    void Stop(string assetPath, bool loop = false);
    void Stop(SoundHandle asset, bool loop = false);
    void Pause(string assetPath, bool loop = false);
    void Pause(SoundHandle asset, bool loop = false);
    void Resume(string assetPath, bool loop = false);
    void Resume(SoundHandle asset, bool loop = false);
}

public sealed class AudioModule(IAssetsManager assets) : IModule, IModule.IUpdatable, IAudioManager
{
    public sealed class Definition : AModuleDefinition<AudioModule>
    {
        protected override IReadOnlyList<Type> Exports => [typeof(IAudioManager)];

        protected override void RegisterModule(ContainerBuilder builder)
        {
            builder.RegisterType<AudioModule>().AsSelf().SingleInstance();
        }
    }

#if ANDROID
    private readonly Dictionary<(string Path, bool Loop), global::Android.Media.MediaPlayer> _androidPlayers = [];
#else
    private sealed record Playback(string Path, bool Loop, FMOD.Channel Channel);
    private FMOD.System? _system;
    private readonly List<Playback> _playbacks = [];
    private readonly Dictionary<(string Path, bool Loop), FMOD.Sound> _sounds = [];
#endif
    private bool _initialized;
    private bool _disposed;
    private readonly Dictionary<AssetId, string> _extractedAssets = [];

    public void OnInitialize()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized)
            return;

#if !ANDROID
        try
        {
            Check(FMOD.Factory.System_Create(out var system));
            Check(system.setSoftwareChannels(64));
            Check(system.init(128, FMOD.INITFLAGS.NORMAL, nint.Zero));
            _system = system;
        }
        catch (Exception exception)
        {
            // Audio is optional: an unsupported native ABI must not prevent the game from starting.
            Console.Error.WriteLine($"Vecxy.Audio disabled: {exception.Message}");
            _system = null;
        }
#endif
        _initialized = true;
    }

    public void OnUpdate(float deltaTime)
    {
#if !ANDROID
        if (!_initialized || _system is null)
            return;

        Check(_system.Value.update());
        for (var index = _playbacks.Count - 1; index >= 0; index--)
        {
            var playback = _playbacks[index];
            var result = playback.Channel.isPlaying(out var isPlaying);
            if (result == FMOD.RESULT.OK && isPlaying)
                continue;
            _playbacks.RemoveAt(index);
        }
#endif
    }

    public void Preload(string assetPath, bool loop = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_initialized)
            throw new InvalidOperationException("AudioModule is not initialized.");
#if ANDROID
        GetOrCreateAndroidPlayer(assetPath, loop);
#else
        if (_system is null)
            return;
        GetOrCreateSound(assetPath, loop);
#endif
    }

    public void Preload(SoundHandle asset, bool loop = false) => Preload(ResolveAssetPath(asset), loop);

    public void Play(string assetPath, bool loop = false, float volume = 1.0f)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_initialized)
            throw new InvalidOperationException("AudioModule is not initialized.");
#if ANDROID
        var player = GetOrCreateAndroidPlayer(assetPath, loop);
        var clampedVolume = Math.Clamp(volume, 0.0f, 1.0f);
        player.SetVolume(clampedVolume, clampedVolume);
        if (player.IsPlaying)
            player.SeekTo(0);
        player.Start();
#else
        if (_system is null)
            return;
        var sound = GetOrCreateSound(assetPath, loop);
        Check(_system.Value.playSound(sound, new FMOD.ChannelGroup(), false, out var channel));
        Check(channel.setVolume(Math.Clamp(volume, 0.0f, 1.0f)));
        _playbacks.Add(new Playback(ResolveAudioPath(assetPath), loop, channel));
#endif
    }

    public void Play(SoundHandle asset, bool loop = false, float volume = 1.0f) => Play(ResolveAssetPath(asset), loop, volume);

    public void Stop(string assetPath, bool loop = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_initialized)
            throw new InvalidOperationException("AudioModule is not initialized.");
        var fullPath = ResolveAudioPath(assetPath);
#if ANDROID
        if (_androidPlayers.TryGetValue((fullPath, loop), out var player))
        {
            if (player.IsPlaying)
                player.Pause();
            player.SeekTo(0);
        }
#else
        for (var index = _playbacks.Count - 1; index >= 0; index--)
        {
            var playback = _playbacks[index];
            if (!playback.Path.Equals(fullPath, StringComparison.Ordinal) || playback.Loop != loop)
                continue;
            playback.Channel.stop();
            _playbacks.RemoveAt(index);
        }
#endif
    }

    public void Stop(SoundHandle asset, bool loop = false) => Stop(ResolveAssetPath(asset), loop);

    public void Pause(string assetPath, bool loop = false)
    {
        var fullPath = ResolveAudioPath(assetPath);
#if ANDROID
        if (_androidPlayers.TryGetValue((fullPath, loop), out var player) && player.IsPlaying)
            player.Pause();
#else
        foreach (var playback in _playbacks.Where(value => value.Path.Equals(fullPath, StringComparison.Ordinal) && value.Loop == loop))
            playback.Channel.setPaused(true);
#endif
    }

    public void Pause(SoundHandle asset, bool loop = false) => Pause(ResolveAssetPath(asset), loop);

    public void Resume(string assetPath, bool loop = false)
    {
        var fullPath = ResolveAudioPath(assetPath);
#if ANDROID
        if (_androidPlayers.TryGetValue((fullPath, loop), out var player) && !player.IsPlaying)
            player.Start();
#else
        foreach (var playback in _playbacks.Where(value => value.Path.Equals(fullPath, StringComparison.Ordinal) && value.Loop == loop))
            playback.Channel.setPaused(false);
#endif
    }

    public void Resume(SoundHandle asset, bool loop = false) => Resume(ResolveAssetPath(asset), loop);

    public void OnShutdown()
    {
        if (!_initialized)
            return;

#if ANDROID
        foreach (var player in _androidPlayers.Values)
        {
            try
            {
                if (player.IsPlaying)
                    player.Stop();
            }
            finally
            {
                player.Release();
                player.Dispose();
            }
        }
        _androidPlayers.Clear();
#else
        for (var index = _playbacks.Count - 1; index >= 0; index--)
            _playbacks[index].Channel.stop();
        _playbacks.Clear();
        foreach (var sound in _sounds.Values)
            sound.release();
        _sounds.Clear();

        if (_system is not null)
        {
            _system.Value.close();
            _system.Value.release();
            _system = null;
        }
#endif
        _initialized = false;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        OnShutdown();
        _disposed = true;
    }

    private string ResolveAudioPath(string assetPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetPath);
        var fullPath = Path.IsPathRooted(assetPath)
            ? Path.GetFullPath(assetPath)
            : Path.GetFullPath(assetPath, assets.AssetsDirectory);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Audio file was not found: {fullPath}", fullPath);
        return fullPath;
    }

    private string ResolveAssetPath(IAssetHandle handle)
    {
        if (!assets.Registry.TryGet(handle.Id, out var metadata) || metadata is null)
            throw new KeyNotFoundException($"Unknown audio asset ID: {handle.Id}");

        var loosePath = Path.GetFullPath(metadata.Path, assets.AssetsDirectory);
        if (File.Exists(loosePath))
            return loosePath;
        if (_extractedAssets.TryGetValue(handle.Id, out var cached) && File.Exists(cached))
            return cached;

        // Native audio backends consume seekable files. Materialize only requested
        // packaged sounds into the process temp directory instead of shipping a
        // second loose copy of the complete Sounds tree.
        var directory = Path.Combine(Path.GetTempPath(), "vecxy-audio");
        Directory.CreateDirectory(directory);
        var extension = Path.GetExtension(metadata.Path);
        var path = Path.Combine(directory, handle.Id + extension);
        File.WriteAllBytes(path, assets.ReadAllBytes(handle));
        _extractedAssets[handle.Id] = path;
        return path;
    }

#if ANDROID
    private global::Android.Media.MediaPlayer GetOrCreateAndroidPlayer(string assetPath, bool loop)
    {
        var fullPath = ResolveAudioPath(assetPath);
        var key = (fullPath, loop);
        if (_androidPlayers.TryGetValue(key, out var cached))
            return cached;

        var player = new global::Android.Media.MediaPlayer();
        try
        {
            player.SetDataSource(fullPath);
            player.Looping = loop;
            player.Prepare();
            _androidPlayers.Add(key, player);
            return player;
        }
        catch
        {
            player.Release();
            player.Dispose();
            throw;
        }
    }
#else
    private FMOD.Sound GetOrCreateSound(string assetPath, bool loop)
    {
        var fullPath = ResolveAudioPath(assetPath);
        var key = (fullPath, loop);
        if (_sounds.TryGetValue(key, out var cached))
            return cached;

        var mode = FMOD.MODE._2D |
                   (loop ? FMOD.MODE.LOOP_NORMAL | FMOD.MODE.CREATESTREAM : FMOD.MODE.LOOP_OFF);
        Check(_system!.Value.createSound(fullPath, mode, out var sound));
        _sounds.Add(key, sound);
        return sound;
    }

    private static void Check(FMOD.RESULT result)
    {
        if (result != FMOD.RESULT.OK)
            throw new InvalidOperationException($"FMOD error: {result} - {FMOD.Error.String(result)}");
    }
#endif
}
