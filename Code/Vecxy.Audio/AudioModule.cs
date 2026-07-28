using Autofac;
using Vecxy.Kernel;

namespace Vecxy.Audio;

public interface IAudioManager
{
    void Play(string path);
}

public sealed class AudioModule : IModule, IModule.IUpdatable, IAudioManager
{
    public sealed class Definition : AModuleDefinition<AudioModule>
    {
        protected override IReadOnlyList<Type> Exports => [typeof(IAudioManager)];

        protected override void RegisterModule(ContainerBuilder builder)
        {
            builder
                .RegisterType<AudioModule>()
                .AsSelf()
                .SingleInstance();
        }
    }
    
    private FMOD.System? _system;
    private readonly List<FMOD.Sound> _sounds = [];
    private bool _initialized;
    private bool _disposed;

    public void OnInitialize()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_initialized)
        {
            return;
        }

        Check(FMOD.Factory.System_Create(out var system));
        Check(system.setSoftwareChannels(64));
        Check(system.init(
            128,
            FMOD.INITFLAGS.NORMAL,
            nint.Zero));

        _system = system;
        _initialized = true;
    }

    public void OnUpdate(float deltaTime)
    {
        if (!_initialized || _system is null)
        {
            return;
        }

        Check(_system.Value.update());
    }

    public void Play(string path)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_initialized || _system is null)
        {
            throw new InvalidOperationException(
                "AudioModule is not initialized.");
        }

        var fullPath = Path.GetFullPath(path);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                $"Audio file was not found: {fullPath}",
                fullPath);
        }

        Check(_system.Value.createSound(
            fullPath,
            FMOD.MODE.DEFAULT,
            out var sound));

        _sounds.Add(sound);

        Check(_system.Value.playSound(
            sound,
            new FMOD.ChannelGroup(),
            false,
            out _));
    }

    public void OnShutdown()
    {
        if (!_initialized)
        {
            return;
        }

        for (var index = _sounds.Count - 1; index >= 0; --index)
        {
            _sounds[index].release();
        }

        _sounds.Clear();

        if (_system is not null)
        {
            _system.Value.close();
            _system.Value.release();
            _system = null;
        }

        _initialized = false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        OnShutdown();
        _disposed = true;
    }

    private static void Check(FMOD.RESULT result)
    {
        if (result == FMOD.RESULT.OK)
        {
            return;
        }

        throw new InvalidOperationException(
            $"FMOD error: {result} — {FMOD.Error.String(result)}");
    }
}