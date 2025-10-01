using System.Reflection;
using OpenTK.Graphics.OpenGL;
using Vecxy.Diagnostics;
using Vecxy.Reflection;

namespace Vecxy.Rendering;

public class ShaderProgram(string vertexSource, string fragmentSource) : IDisposable
{
    #region fields

    public int Id { get; private set; }
    
    private readonly Shader _vertexShader = new(vertexSource, ShaderType.VertexShader);
    private readonly Shader _fragmentShader = new(fragmentSource, ShaderType.FragmentShader);
    
    private bool _isDisposed;

    #endregion

    #region public api

    public static ShaderProgram Create(string vertexSourcePath, string fragmentSourcePath)
    {
        var assembly = Assembly.GetExecutingAssembly();

        var vertexSource = assembly.GetEmbeddedResource(vertexSourcePath)!.Text();
        var fragmentSource = assembly.GetEmbeddedResource(fragmentSourcePath)!.Text();
        
        return new ShaderProgram(vertexSource, fragmentSource);
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
        CompileVertexShader();
        CompileFragmentShader();
    }

    public void Link()
    {
        Logger.Info("Linking shader program...");
        
        _vertexShader.Attach(Id);
        _fragmentShader.Attach(Id);
        
        GL.LinkProgram(Id);
        
        GL.GetProgram(Id, GetProgramParameterName.LinkStatus, out var success);

        if (success == 0)
        {
            var log = GL.GetProgramInfoLog(Id);
            
            throw new Exception($"Shader program linking failed: {log}");
        }
        
        Logger.Info("Shader program linked successfully");
        
        GL.ValidateProgram(Id);
        
        GL.GetProgram(Id, GetProgramParameterName.ValidateStatus, out var validateStatus);
        
        if (validateStatus == 0)
        {
            var log = GL.GetProgramInfoLog(Id);
            Logger.Warning($"Shader program validation warnings: {log}");
        }
        
        _vertexShader.Dispose();
        _fragmentShader.Dispose();
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
        
        Logger.Info("Shader program disposed");
    }

    #endregion

    #region private api

    private void CompileVertexShader()
    {
        Logger.Info("Compiling vertex shader...");
        
        var vertexResult = _vertexShader.Compile();
        
        if (!vertexResult.Success)
        {
            throw new Exception($"Vertex shader compilation failed: {vertexResult.Log}");
        }
        
        Logger.Info("Vertex shader compiled successfully");
    }

    private void CompileFragmentShader()
    {
        Logger.Info("Compiling fragment shader...");
        
        var fragmentResult = _fragmentShader.Compile();
        
        if (!fragmentResult.Success)
        {
            throw new Exception($"Fragment shader compilation failed: {fragmentResult.Log}");
        }
        
        Logger.Info("Fragment shader compiled successfully");
    }

    #endregion
}