using System.Numerics;
using Silk.NET.OpenGL;
using Vecxy.Kernel;
using Vecxy.Rendering;

namespace Vecxy.Editor;

public sealed class GizmoRenderer(
    IWindow window) : IDisposable
{
    private const string VertexShaderSource =
        """
        #version 330 core

        layout(location = 0) in vec3 aPosition;
        layout(location = 1) in vec4 aColor;

        uniform mat4 uViewProjection;
        uniform float uAlphaScale;

        out vec4 vColor;

        void main()
        {
            gl_Position = uViewProjection * vec4(aPosition, 1.0);
            vColor = vec4(aColor.rgb, aColor.a * uAlphaScale);
        }
        """;

    private const string FragmentShaderSource =
        """
        #version 330 core

        in vec4 vColor;
        out vec4 oColor;

        void main()
        {
            oColor = vColor;
        }
        """;

    private readonly List<LineSegment> _segments = [];
    private GL? _gl;
    private uint _program;
    private uint _vertexArray;
    private uint _vertexBuffer;
    private int _viewProjectionLocation;
    private int _alphaScaleLocation;
    private bool _initialized;
    private bool _disposed;

    public EGizmoDisplayMode DisplayMode { get; set; } =
        EGizmoDisplayMode.HiddenAndVisible;

    public void Initialize()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_initialized)
            return;

        window.MakeCurrent();
        _gl = GL.GetApi(window.GetProcAddress);

        var vertexShader =
            CompileStage(ShaderType.VertexShader, VertexShaderSource);
        var fragmentShader =
            CompileStage(ShaderType.FragmentShader, FragmentShaderSource);

        try
        {
            _program = _gl.CreateProgram();
            _gl.AttachShader(_program, vertexShader);
            _gl.AttachShader(_program, fragmentShader);
            _gl.LinkProgram(_program);
            _gl.GetProgram(_program, ProgramPropertyARB.LinkStatus, out var linked);

            if (linked == 0)
            {
                throw new InvalidOperationException(
                    $"Gizmo shader link failed:{Environment.NewLine}{_gl.GetProgramInfoLog(_program)}");
            }

            _gl.GenVertexArrays(1, out _vertexArray);
            _gl.GenBuffers(1, out _vertexBuffer);

            _gl.BindVertexArray(_vertexArray);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertexBuffer);

            const uint stride = 7 * sizeof(float);
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
            _gl.EnableVertexAttribArray(1);
            _gl.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));

            _gl.BindVertexArray(0);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);

            _viewProjectionLocation =
                _gl.GetUniformLocation(_program, "uViewProjection");
            _alphaScaleLocation =
                _gl.GetUniformLocation(_program, "uAlphaScale");

            _initialized = true;
        }
        finally
        {
            _gl.DeleteShader(vertexShader);
            _gl.DeleteShader(fragmentShader);
        }
    }

    public void Clear()
    {
        _segments.Clear();
    }

    public void AddLine(
        Vector3 from,
        Vector3 to,
        Vector4 color,
        float thickness)
    {
        _segments.Add(new LineSegment(from, to, color, Math.Max(1.0f, thickness)));
    }

    public unsafe void Render(
        Camera camera,
        int width,
        int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_initialized || _gl is null || _segments.Count == 0)
            return;

        var viewProjection =
            camera.ViewMatrix *
            camera.GetProjectionMatrix(width / (float)Math.Max(1, height));

        var previousBlend = _gl.IsEnabled(EnableCap.Blend);
        var previousDepthTest = _gl.IsEnabled(EnableCap.DepthTest);
        _gl.GetInteger(GetPName.DepthFunc, out var previousDepthFunc);
        _gl.GetInteger(GetPName.DepthWritemask, out var previousDepthMask);

        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        _gl.DepthMask(false);

        _gl.UseProgram(_program);
        Span<float> matrixValues =
        [
            viewProjection.M11, viewProjection.M12, viewProjection.M13, viewProjection.M14,
            viewProjection.M21, viewProjection.M22, viewProjection.M23, viewProjection.M24,
            viewProjection.M31, viewProjection.M32, viewProjection.M33, viewProjection.M34,
            viewProjection.M41, viewProjection.M42, viewProjection.M43, viewProjection.M44
        ];

        fixed (float* matrix = matrixValues)
        {
            _gl.UniformMatrix4(_viewProjectionLocation, 1, false, matrix);
        }

        foreach (var group in _segments.GroupBy(static segment => segment.Thickness))
        {
            var vertices = BuildVertices(group);
            UploadVertices(vertices);

            _gl.LineWidth(group.Key);
            _gl.BindVertexArray(_vertexArray);

            switch (DisplayMode)
            {
                case EGizmoDisplayMode.VisibleOnly:
                    _gl.Enable(EnableCap.DepthTest);
                    _gl.DepthFunc(DepthFunction.Lequal);
                    _gl.Uniform1(_alphaScaleLocation, 1.0f);
                    _gl.DrawArrays(PrimitiveType.Lines, 0, (uint)(vertices.Length / 7));
                    break;

                case EGizmoDisplayMode.XRay:
                    _gl.Disable(EnableCap.DepthTest);
                    _gl.Uniform1(_alphaScaleLocation, 1.0f);
                    _gl.DrawArrays(PrimitiveType.Lines, 0, (uint)(vertices.Length / 7));
                    break;

                default:
                    _gl.Disable(EnableCap.DepthTest);
                    _gl.Uniform1(_alphaScaleLocation, 0.25f);
                    _gl.DrawArrays(PrimitiveType.Lines, 0, (uint)(vertices.Length / 7));

                    _gl.Enable(EnableCap.DepthTest);
                    _gl.DepthFunc(DepthFunction.Lequal);
                    _gl.Uniform1(_alphaScaleLocation, 1.0f);
                    _gl.DrawArrays(PrimitiveType.Lines, 0, (uint)(vertices.Length / 7));
                    break;
            }
        }

        _gl.BindVertexArray(0);
        _gl.UseProgram(0);
        _gl.LineWidth(1.0f);
        _gl.DepthFunc((DepthFunction)previousDepthFunc);
        _gl.DepthMask(previousDepthMask != 0);

        if (!previousDepthTest)
            _gl.Disable(EnableCap.DepthTest);

        if (!previousBlend)
            _gl.Disable(EnableCap.Blend);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _segments.Clear();

        if (_gl is not null)
        {
            if (_vertexBuffer != 0)
                _gl.DeleteBuffer(_vertexBuffer);

            if (_vertexArray != 0)
                _gl.DeleteVertexArray(_vertexArray);

            if (_program != 0)
                _gl.DeleteProgram(_program);

            _gl.Dispose();
        }

        _program = 0;
        _vertexArray = 0;
        _vertexBuffer = 0;
        _gl = null;
    }

    private uint CompileStage(
        ShaderType type,
        string source)
    {
        var shader = _gl!.CreateShader(type);
        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);
        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out var compiled);

        if (compiled != 0)
            return shader;

        var log = _gl.GetShaderInfoLog(shader);
        _gl.DeleteShader(shader);
        throw new InvalidOperationException(
            $"Gizmo shader {type} compilation failed:{Environment.NewLine}{log}");
    }

    private unsafe void UploadVertices(float[] vertices)
    {
        _gl!.BindBuffer(BufferTargetARB.ArrayBuffer, _vertexBuffer);

        fixed (float* data = vertices)
        {
            _gl.BufferData(
                BufferTargetARB.ArrayBuffer,
                (nuint)(vertices.Length * sizeof(float)),
                data,
                BufferUsageARB.DynamicDraw);
        }
    }

    private static float[] BuildVertices(
        IEnumerable<LineSegment> segments)
    {
        var data = new List<float>();

        foreach (var segment in segments)
        {
            AppendVertex(data, segment.From, segment.Color);
            AppendVertex(data, segment.To, segment.Color);
        }

        return data.ToArray();
    }

    private static void AppendVertex(
        List<float> data,
        Vector3 position,
        Vector4 color)
    {
        data.Add(position.X);
        data.Add(position.Y);
        data.Add(position.Z);
        data.Add(color.X);
        data.Add(color.Y);
        data.Add(color.Z);
        data.Add(color.W);
    }

    private readonly record struct LineSegment(
        Vector3 From,
        Vector3 To,
        Vector4 Color,
        float Thickness);
}
