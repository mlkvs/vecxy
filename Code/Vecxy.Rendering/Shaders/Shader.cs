using System.Numerics;

namespace Vecxy.Rendering;

public sealed class Shader : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly Dictionary<string, int> _uniformLocations = new(StringComparer.Ordinal);
    private uint _program;
    private bool _disposed;

    public string Name { get; }

    internal Shader(GraphicsDevice device, uint program, string name)
    {
        _device = device;
        _program = program;
        Name = name;
    }

    public void Bind()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _device.GL.UseProgram(_program);
    }

    public void Set(string name, int value)
    {
        var location = GetUniformLocation(name);
        if (location >= 0)
        {
            _device.GL.Uniform1(location, value);
        }
    }

    public void Set(string name, float value)
    {
        var location = GetUniformLocation(name);
        if (location >= 0)
        {
            _device.GL.Uniform1(location, value);
        }
    }

    public void Set(string name, Vector2 value)
    {
        var location = GetUniformLocation(name);
        if (location >= 0)
        {
            _device.GL.Uniform2(location, value.X, value.Y);
        }
    }

    public void Set(string name, Vector3 value)
    {
        var location = GetUniformLocation(name);
        if (location >= 0)
        {
            _device.GL.Uniform3(location, value.X, value.Y, value.Z);
        }
    }

    public void Set(string name, Vector4 value)
    {
        var location = GetUniformLocation(name);
        if (location >= 0)
        {
            _device.GL.Uniform4(location, value.X, value.Y, value.Z, value.W);
        }
    }

    public unsafe void Set(string name, Matrix4x4 value)
    {
        var location = GetUniformLocation(name);
        if (location >= 0)
        {
            // Matrix4x4 uses row-vector transforms while GLSL uses column
            // vectors. Its row-major memory is therefore already the
            // column-major representation GLSL needs.
            _device.GL.UniformMatrix4(
                location,
                1,
                false,
                (float*)&value);
        }
    }

    private int GetUniformLocation(string name)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_uniformLocations.TryGetValue(name, out var location))
            return location;
        location = _device.GL.GetUniformLocation(_program, name);
        _uniformLocations.Add(name, location);
        return location;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_program != 0)
        {
            _device.GL.DeleteProgram(_program);
            _program = 0;
        }
        _uniformLocations.Clear();
    }
}
