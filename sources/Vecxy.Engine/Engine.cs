using System.Reflection;
using Vecxy.Rendering;
using Zenject;

namespace Vecxy.Engine;

public interface IGameSettings
{
    public string Verison { get; set; }
}

public class GameSettings : IGameSettings
{
    public string Verison { get; set; }
}

public interface IGame
{
    public void InstallBindings(DiContainer container);
    public void Initialize();
    public void Update(float deltaTime);
}

public interface IGameHost
{
    public IGame Game { get; }
    public IGameSettings Settings { get; }
}

public class GameHost(IGame game, IGameSettings settings) : IGameHost
{
    public IGame Game => game;
    public IGameSettings Settings => settings;
}

public class Engine : IDisposable
{
    public string Version { get; } = "a.001";
    
    private readonly RenderWindow _window;

    private DiContainer _diContainer;

    private IGameHost? _gameHost;

    [Inject] private readonly ISceneManager _sceneManager;
    
    /*private void Test()
    {
        var projectAssembly = Assembly.LoadFrom(game.AssemblyPath);

        var projectInstallerType = projectAssembly
            .GetTypes()
            .FirstOrDefault(type =>
            {
                if (type.IsAbstract)
                {
                    return false;
                }

                return type.IsSubclassOf(typeof(ProjectInstaller));
            });

        if (projectInstallerType == null)
        {
            throw new Exception("Could not find project assembly");
        }

        var installer = _diContainer.Instantiate(projectInstallerType) as ProjectInstaller;
        
        installer!.InstallBindings();
    }*/

    public Engine()
    {
        _diContainer = new DiContainer();
        _diContainer.Install<EngineInstaller>();
        _diContainer.Inject(this);
        
        _window = RenderWindow.Create(new RenderWindowOptions
        {
            Width = 800,
            Height = 600,
            Title = "Vecxy.Rendering",
            IsDebug = true
        });
        
        _window.Load += OnLoad;
        _window.UpdateFrame += OnUpdate;
        _window.RenderFrame += OnFrame;
        _window.Unload += OnUnload;
        
        //_render = new RenderingModule(_window);
    }

    public void Run(IGameHost gameHost)
    {
        _gameHost = gameHost;
            
        var gameContainer = _diContainer
            .CreateSubContainer();
        
        gameContainer.Inject(_gameHost.Game);
        
        gameContainer.Bind<IGame>()
            .FromInstance(_gameHost.Game)
            .AsSingle()
            .NonLazy();
        
        var settings = gameContainer.Resolve<IGameSettings>();

        settings.Verison = gameHost.Settings.Verison;
        
        _gameHost.Game.InstallBindings(gameContainer);
        
        _window.Run();
    }

    private void OnLoad()
    {
        _gameHost?.Game.Initialize();
        //_render.OnLoad();
        //_render.OnInitialize();
    }

    private void OnUpdate(OpenTK.Windowing.Common.FrameEventArgs e)
    {
        var deltaTime = (float)e.Time;

        _sceneManager.Update(deltaTime);
        
        _gameHost?.Game.Update(deltaTime);
    }

    private void OnFrame(OpenTK.Windowing.Common.FrameEventArgs e)
    {
        var deltaTime = (float)e.Time;
        
        //_render.OnTick(deltaTime);
    }

    private void OnUnload()
    {
        //_render.OnUnload();
    }

    public void Dispose()
    {
        //_render.Dispose();
        _window.Dispose();
    }
}