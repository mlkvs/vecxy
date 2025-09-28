using OpenTK.Graphics.OpenGL;
using Vecxy.Diagnostics;

namespace Vecxy.Rendering;

public class ShaderProgram : IDisposable
{
    public int Id { get; private set; }
    
    private readonly Shader _vertexShader;
    private readonly Shader _fragmentShader;
    
    private bool _isDisposed;
    
    public ShaderProgram(string vertexSource, string fragmentSource)
    {
        _vertexShader = new Shader(vertexSource, ShaderType.VertexShader);
        _fragmentShader = new Shader(fragmentSource, ShaderType.FragmentShader);
    }

    public void Initialize()
    {
        Id = GL.CreateProgram();
        
        _vertexShader.Initialize();
        _fragmentShader.Initialize();

        _isDisposed = false;
    }

    public void Compile()
    {
        var result = _vertexShader.Compile();
        
        if (result.Success == false)
        {
            Logger.Error($"Shader {_vertexShader.Type} is not compiled. LogInfo: {result.Log}");
        }
        
        result = _fragmentShader.Compile();
        
        if (result.Success == false)
        {
            Logger.Error($"Shader {_fragmentShader.Type} is not compiled. LogInfo: {result.Log}");
        }
    }

    public LinkResult Link()
    {
        var result = new LinkResult
        {
            Log = null,
            Success = true
        };
        
        _vertexShader.Attach(Id);
        _fragmentShader.Attach(Id);
        
        GL.LinkProgram(Id);
        
        GL.GetProgram(Id, GetProgramParameterName.LinkStatus, out var success);

        if (success == 0)
        {
            var log = GL.GetProgramInfoLog(Id);
            
            result.Log = log;
            result.Success = false;
        }

        return result;
    }

    public void Use()
    {
        GL.UseProgram(Id);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }
        
        _vertexShader.Dispose();
        _fragmentShader.Dispose();
        
        GL.DeleteProgram(Id);

        _isDisposed = true;
    }
    
    public struct LinkResult
    {
        public bool Success;
        public string? Log;
    }
}