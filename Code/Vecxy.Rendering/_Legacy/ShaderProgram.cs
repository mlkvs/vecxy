using System.Numerics;
using Silk.NET.OpenGL;
using Vecxy.Assets;
using Vecxy.Assets._Legacy;
using Vecxy.Diagnostics;

namespace Vecxy.Rendering._Legacy;

public sealed class ShaderProgram : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly TextAsset? _vertexAsset;
    private readonly TextAsset? _fragmentAsset;
    private string _vertexSource;
    private string _fragmentSource;
    private uint _handle;
    private bool _disposed;

    public string Name { get; }

    internal ShaderProgram(GraphicsDevice device, string vertexSource, string fragmentSource, string name,
        TextAsset? vertexAsset = null, TextAsset? fragmentAsset = null)
    {
        _device = device;
        _vertexSource = vertexSource;
        _fragmentSource = fragmentSource;
        Name = name;
        _vertexAsset = vertexAsset;
        _fragmentAsset = fragmentAsset;
        _handle = BuildProgram(vertexSource, fragmentSource);
        if (_vertexAsset is not null) _vertexAsset.Reloaded += OnAssetReloaded;
        if (_fragmentAsset is not null) _fragmentAsset.Reloaded += OnAssetReloaded;
    }

    internal void Bind()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _device.GL.UseProgram(_handle);
    }

    public void Set(string name, int value)
    {
        var location = _device.GL.GetUniformLocation(_handle, name);
        if (location >= 0) _device.GL.Uniform1(location, value);
    }

    public void Set(string name, float value)
    {
        var location = _device.GL.GetUniformLocation(_handle, name);
        if (location >= 0) _device.GL.Uniform1(location, value);
    }

    public void Set(string name, Vector4 value)
    {
        var location = _device.GL.GetUniformLocation(_handle, name);
        if (location >= 0) _device.GL.Uniform4(location, value.X, value.Y, value.Z, value.W);
    }

    public void Set(string name, Vector2 value)
    {
        var location = _device.GL.GetUniformLocation(_handle, name);
        if (location >= 0) _device.GL.Uniform2(location, value.X, value.Y);
    }

    public unsafe void Set(string name, Matrix4x4 value)
    {
        var location = _device.GL.GetUniformLocation(_handle, name);
        if (location < 0) return;
        _device.GL.UniformMatrix4(location, 1, false, (float*)&value);
    }

    private void OnAssetReloaded(Asset _)
    {
        var vertexSource = _vertexAsset?.Content ?? _vertexSource;
        var fragmentSource = _fragmentAsset?.Content ?? _fragmentSource;
        try
        {
            var replacement = BuildProgram(vertexSource, fragmentSource);
            _device.GL.DeleteProgram(_handle);
            _handle = replacement;
            _vertexSource = vertexSource;
            _fragmentSource = fragmentSource;
            Logger.Info($"Shader reloaded: {Name}");
        }
        catch (Exception exception)
        {
            Logger.Error(exception, $"Shader reload failed, keeping previous program: {Name}");
        }
    }

    private uint BuildProgram(string vertexSource, string fragmentSource)
    {
        _device.EnsureReady();
        var gl = _device.GL;
        var vertex = Compile(ShaderType.VertexShader, vertexSource);
        var fragment = Compile(ShaderType.FragmentShader, fragmentSource);
        var program = gl.CreateProgram();
        try
        {
            gl.AttachShader(program, vertex);
            gl.AttachShader(program, fragment);
            gl.LinkProgram(program);
            gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out var linked);
            if (linked == 0)
                throw new InvalidOperationException($"Shader '{Name}' linking failed:\n{gl.GetProgramInfoLog(program)}");
            return program;
        }
        catch
        {
            gl.DeleteProgram(program);
            throw;
        }
        finally
        {
            gl.DeleteShader(vertex);
            gl.DeleteShader(fragment);
        }
    }

    private uint Compile(ShaderType type, string source)
    {
        var gl = _device.GL;
        var shader = gl.CreateShader(type);
        gl.ShaderSource(shader, source);
        gl.CompileShader(shader);
        gl.GetShader(shader, ShaderParameterName.CompileStatus, out var compiled);
        if (compiled != 0) return shader;
        var log = gl.GetShaderInfoLog(shader);
        gl.DeleteShader(shader);
        throw new InvalidOperationException($"Shader '{Name}' {type} compilation failed:\n{log}");
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (_vertexAsset is not null) _vertexAsset.Reloaded -= OnAssetReloaded;
        if (_fragmentAsset is not null) _fragmentAsset.Reloaded -= OnAssetReloaded;
        if (_handle != 0 && _device.IsInitialized) _device.GL.DeleteProgram(_handle);
        _handle = 0;
        _disposed = true;
    }
}
