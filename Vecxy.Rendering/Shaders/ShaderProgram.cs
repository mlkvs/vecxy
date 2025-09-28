using OpenTK.Graphics.OpenGL;
using Vecxy.Diagnostics;
using Vecxy.Rendering;

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
        Logger.Info("Compiling vertex shader...");
        var vertexResult = _vertexShader.Compile();
        
        if (!vertexResult.Success)
        {
            throw new Exception($"Vertex shader compilation failed: {vertexResult.Log}");
        }
        Logger.Info("Vertex shader compiled successfully");
        
        Logger.Info("Compiling fragment shader...");
        var fragmentResult = _fragmentShader.Compile();
        
        if (!fragmentResult.Success)
        {
            throw new Exception($"Fragment shader compilation failed: {fragmentResult.Log}");
        }
        Logger.Info("Fragment shader compiled successfully");
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
        
        // Валидация программы (опционально, но полезно для отладки)
        GL.ValidateProgram(Id);
        GL.GetProgram(Id, GetProgramParameterName.ValidateStatus, out var validateStatus);
        if (validateStatus == 0)
        {
            var log = GL.GetProgramInfoLog(Id);
            Logger.Warning($"Shader program validation warnings: {log}");
        }

        // Удаляем шейдеры после успешной линковки
        _vertexShader.Dispose();
        _fragmentShader.Dispose();
    }

    public void Use()
    {
        GL.UseProgram(Id);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        
        _vertexShader.Dispose();
        _fragmentShader.Dispose();
        
        GL.DeleteProgram(Id);
        _isDisposed = true;
        
        Logger.Info("Shader program disposed");
    }
}