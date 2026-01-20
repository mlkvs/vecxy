using OpenTK.Graphics.OpenGL;
using System.Numerics;
using System.Reflection;
using Vecxy.Diagnostics;
using Vecxy.Kernel;
using Vecxy.Reflection;

namespace Vecxy.Rendering;

public class ShaderProgram(string vertexSource, string fragmentSource) : IDisposable, IBuildable<ShaderProgram>
{
    #region fields

    public int Id { get; private set; }

    private readonly Shader _vertexShader = new(vertexSource, ShaderType.VertexShader);
    private readonly Shader _fragmentShader = new(fragmentSource, ShaderType.FragmentShader);

    private bool _isDisposed;

    #endregion

    #region IBuildable<ShaderProgram>

    ShaderProgram IBuildable<ShaderProgram>.Build()
    {
        Initialize();
        Compile();
        Link();

        return this;
    }

    #endregion

    #region public api

    public static IBuildable<ShaderProgram> Create(string vertexSourcePath, string fragmentSourcePath)
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

    #region uniforms

    public void SetUniform(string name, int value)
    {
        int location = GL.GetUniformLocation(Id, name);
        if (location != -1)
            GL.Uniform1(location, value);
    }

    public void SetUniform(string name, Vector2 value)
    {
        int location = GL.GetUniformLocation(Id, name);
        if (location != -1)
            GL.Uniform2(location, value.X, value.Y); // правильно: два отдельных float
    }

    public void SetUniform(string name, Vector3 value)
    {
        int location = GL.GetUniformLocation(Id, name);
        if (location != -1)
            GL.Uniform3(location, value.X, value.Y, value.Z);
    }

    public void SetUniform(string name, Vector4 value)
    {
        int location = GL.GetUniformLocation(Id, name);
        if (location != -1)
            GL.Uniform4(location, value.X, value.Y, value.Z, value.W);
    }

    public void SetUniform(string name, Matrix4x4 value, bool transpose = false)
    {
        int location = GL.GetUniformLocation(Id, name);
        if (location != -1)
        {
            float[] m = new float[]
            {
                value.M11, value.M12, value.M13, value.M14,
                value.M21, value.M22, value.M23, value.M24,
                value.M31, value.M32, value.M33, value.M34,
                value.M41, value.M42, value.M43, value.M44
            };
            GL.UniformMatrix4(location, 1, transpose, m);
        }
    }

    #endregion

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
