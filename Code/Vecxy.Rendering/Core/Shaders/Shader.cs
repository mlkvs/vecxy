using OpenTK.Graphics.OpenGL;

namespace Vecxy.Rendering;

public class Shader(string source, ShaderType type) : IDisposable
{
    public int Id { get; private set; }
    public int ProgramId { get; private set; } = -1;
    public string Source { get; } = source;
    public ShaderType Type { get; } = type;

    private bool _isDisposed;

    public void Initialize()
    {
        Id = GL.CreateShader(Type);

        GL.ShaderSource(Id, Source);

        _isDisposed = false;
    }

    public CompileResult Compile()
    {
        var result = new CompileResult
        {
            Log = null,
            Success = true
        };

        GL.CompileShader(Id);

        GL.GetShader(Id, ShaderParameter.CompileStatus, out var success);

        if (success == 0)
        {
            var log = GL.GetShaderInfoLog(Id);

            result.Success = false;
            result.Log = log;
        }

        return result;
    }

    public void Attach(int programId)
    {
        ProgramId = programId;

        GL.AttachShader(programId, Id);
    }

    public void Detach()
    {
        if (ProgramId == -1)
        {
            return;
        }

        GL.DetachShader(ProgramId, Id);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        Detach();

        GL.DeleteShader(Id);

        _isDisposed = true;
    }

    public struct CompileResult
    {
        public bool Success;
        public string? Log;
    }
}