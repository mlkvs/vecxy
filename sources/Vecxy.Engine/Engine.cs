using System.Reflection;
using Vecxy.Kernel;
using Vecxy.Rendering;
using Zenject;

namespace Vecxy.Engine;

public class Engine : IDisposable
{
    public string Version { get; } = "a.001";
    
    //private readonly IModule _render;
    private readonly RenderWindow _window;

    private DiContainer _diContainer;

    public Engine(Project project)
    {
        _diContainer = new DiContainer();
        _diContainer.Install<EngineInstaller>();

        var projectAssembly = Assembly.LoadFrom(project.EntryPointDLL);

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

    public void Run()
    {
        _window.Run();
    }

    private void OnLoad()
    {
        //_render.OnLoad();
        //_render.OnInitialize();
    }

    private void OnUpdate(OpenTK.Windowing.Common.FrameEventArgs e)
    {
        var deltaTime = (float)e.Time;
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