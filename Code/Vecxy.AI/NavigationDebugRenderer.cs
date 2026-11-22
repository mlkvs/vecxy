using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;
using Vecxy.Pathfinding;
using Vecxy.Rendering;

namespace Vecxy.AI;

internal sealed unsafe class NavigationDebugRenderer : IDisposable
{
    private static readonly Vector4 Walkable = new(.15f, .85f, .38f, 1);
    private static readonly Vector4 Blocked = new(1, .24f, .21f, 1);
    private static readonly Vector4 Route = new(.24f, .9f, 1, 1);
    private static readonly Vector4 Arrived = new(.95f, .91f, .32f, 1);

    private readonly GL _gl;
    private readonly uint _program;
    private readonly uint _vao;
    private readonly uint _buffer;
    private readonly uint _surfaceBuffer;
    private readonly int _viewProjection;
    private readonly List<Vertex> _lines = [];
    private NavigationMap? _surfaceMap;
    private uint _surfaceVertices;

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct Vertex(Vector3 Position, Vector4 Color);

    public NavigationDebugRenderer(GL gl)
    {
        _gl = gl;
        var version = gl.GetStringS(StringName.Version).Contains("OpenGL ES", StringComparison.Ordinal)
            ? "#version 300 es\nprecision highp float;\n"
            : "#version 330 core\n";
        var vertex = Compile(ShaderType.VertexShader, version + """
            layout(location=0) in vec3 position;
            layout(location=1) in vec4 color;
            uniform mat4 viewProjection;
            out vec4 vertexColor;
            void main() {
                gl_Position = viewProjection * vec4(position,1.0);
                vertexColor = color;
            }
            """);
        uint fragment = 0;
        _program = gl.CreateProgram();
        try
        {
            fragment = Compile(ShaderType.FragmentShader, version + """
                in vec4 vertexColor;
                out vec4 pixelColor;
                void main() { pixelColor = vertexColor; }
                """);
            gl.AttachShader(_program, vertex);
            gl.AttachShader(_program, fragment);
            gl.LinkProgram(_program);
            gl.GetProgram(_program, ProgramPropertyARB.LinkStatus, out var linked);
            if (linked == 0)
                throw new InvalidOperationException(gl.GetProgramInfoLog(_program));
        }
        catch
        {
            gl.DeleteProgram(_program);
            throw;
        }
        finally
        {
            gl.DeleteShader(vertex);
            if (fragment != 0)
                gl.DeleteShader(fragment);
        }

        _viewProjection = gl.GetUniformLocation(_program, "viewProjection");
        _vao = gl.GenVertexArray();
        _buffer = gl.GenBuffer();
        _surfaceBuffer = gl.GenBuffer();
    }

    public void Draw(RenderWorldContext context, NavigationSceneState scene)
    {
        var map = scene.Map!;
        var data = map.Data;
        _gl.GetInteger(GLEnum.CurrentProgram, out int program);
        _gl.GetInteger(GLEnum.VertexArrayBinding, out int vao);
        _gl.GetInteger(GLEnum.ArrayBufferBinding, out int buffer);
        _gl.GetInteger(GLEnum.DepthFunc, out int depthFunc);
        _gl.GetBoolean(GLEnum.DepthWritemask, out bool depthMask);
        var depthTest = _gl.IsEnabled(EnableCap.DepthTest);
        _gl.DepthMask(false);
        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Lequal);
        _gl.UseProgram(_program);
        _gl.BindVertexArray(_vao);
        try
        {
            var matrix = context.ViewProjection;
            _gl.UniformMatrix4(_viewProjection, 1, false, (float*)&matrix);

            if (!ReferenceEquals(map, _surfaceMap))
            {
                _lines.Clear();
                BuildSurface(data);
                Upload(_surfaceBuffer, BufferUsageARB.StaticDraw);
                _surfaceVertices = (uint)_lines.Count;
                _surfaceMap = map;
            }

            Bind(_surfaceBuffer);
            _gl.DrawArrays(PrimitiveType.Lines, 0, _surfaceVertices);
            _lines.Clear();
            var blocked = new HashSet<int>();
            foreach (var volume in data.BakedBlockers ?? [])
                DrawBox(volume, Blocked);
            foreach (var obstacle in scene.Coverage.Values)
            {
                foreach (var cell in obstacle.Cells)
                    if (blocked.Add(cell))
                        DrawCell(data.Center(cell) + Vector3.UnitY * .045f, data.Settings.CellSize * .43f, Blocked);
                foreach (var volume in obstacle.Boxes)
                    DrawBox(volume, Blocked);
            }

            foreach (var agent in scene.Agents)
            {
                if (!agent.IsActive)
                    continue;
                var position = agent.Transform.WorldPosition;
                var size = new Vector3(data.Settings.Radius * 2, data.Settings.Height, data.Settings.Radius * 2);
                var color = agent.State switch
                {
                    NavigationAgentState.NoPath => Blocked,
                    NavigationAgentState.Arrived => Arrived,
                    _ => Route
                };
                DrawBox(NavigationBox.FromTransform(
                    Matrix4x4.CreateTranslation(position + Vector3.UnitY * size.Y * .5f), size), color);
                if (agent.Destination is { } goal)
                    Cross(goal + Vector3.UnitY * .07f, .2f, color);
                var start = position + Vector3.UnitY * .09f;
                for (var i = agent.NextWaypoint; i < agent.Path.Count; i++)
                {
                    var end = agent.Path[i] + Vector3.UnitY * .09f;
                    Line(start, end, color);
                    if (i == agent.NextWaypoint)
                        Cross(end, .08f, color);
                    start = end;
                }
            }

            if (_lines.Count > 0)
            {
                Upload(_buffer, BufferUsageARB.StreamDraw);
                Bind(_buffer);
                _gl.DrawArrays(PrimitiveType.Lines, 0, (uint)_lines.Count);
            }
        }
        finally
        {
            _gl.BindVertexArray((uint)vao);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, (uint)buffer);
            _gl.UseProgram((uint)program);
            _gl.DepthFunc((DepthFunction)depthFunc);
            _gl.DepthMask(depthMask);
            if (!depthTest)
                _gl.Disable(EnableCap.DepthTest);
        }
    }

    private void BuildSurface(NavigationData data)
    {
        var half = data.Settings.CellSize * .5f;
        var stride = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(data.Heights.Length / 16000d)));
        for (var index = 0; index < data.Heights.Length; index++)
        {
            if (!data.Heights[index].HasValue)
                continue;
            var center = data.Center(index) + Vector3.UnitY * .025f;
            var cell = data.Point(index);
            if (cell.X % stride == 0 && cell.Y % stride == 0)
                Cross(center, half * .28f, Walkable);
            Edge(-1, 0, new(-half, 0, -half), new(-half, 0, half));
            Edge(1, 0, new(half, 0, -half), new(half, 0, half));
            Edge(0, -1, new(-half, 0, -half), new(half, 0, -half));
            Edge(0, 1, new(-half, 0, half), new(half, 0, half));

            void Edge(int x, int y, Vector3 a, Vector3 b)
            {
                var adjacent = new GridPoint(cell.X + x, cell.Y + y);
                if (!data.Contains(adjacent) || !data.Heights[data.Index(adjacent)].HasValue)
                    Line(center + a, center + b, Walkable);
            }
        }
    }

    private void DrawCell(Vector3 center, float half, Vector4 color)
    {
        Line(center + new Vector3(-half, 0, -half), center + new Vector3(half, 0, half), color);
        Line(center + new Vector3(-half, 0, half), center + new Vector3(half, 0, -half), color);
    }

    private void Cross(Vector3 center, float radius, Vector4 color)
    {
        Line(center - Vector3.UnitX * radius, center + Vector3.UnitX * radius, color);
        Line(center - Vector3.UnitZ * radius, center + Vector3.UnitZ * radius, color);
    }

    private void DrawBox(NavigationBox box, Vector4 color)
    {
        Edges(box.Right, box.Up, box.Forward);
        Edges(box.Up, box.Right, box.Forward);
        Edges(box.Forward, box.Right, box.Up);

        void Edges(Vector3 direction, Vector3 second, Vector3 third)
        {
            for (var j = -1; j <= 1; j += 2)
            for (var i = -1; i <= 1; i += 2)
            {
                var center = box.Center + second * i + third * j;
                Line(center - direction, center + direction, color);
            }
        }
    }

    private void Line(Vector3 a, Vector3 b, Vector4 color)
    {
        _lines.Add(new(a, color));
        _lines.Add(new(b, color));
    }

    private void Bind(uint buffer)
    {
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, buffer);
        const uint stride = 7 * sizeof(float);
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));
    }

    private void Upload(uint buffer, BufferUsageARB usage)
    {
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, buffer);
        fixed (Vertex* vertices = CollectionsMarshal.AsSpan(_lines))
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(_lines.Count * sizeof(Vertex)), vertices, usage);
    }

    private uint Compile(ShaderType type, string source)
    {
        var shader = _gl.CreateShader(type);
        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);
        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out var compiled);
        if (compiled != 0)
            return shader;
        var error = _gl.GetShaderInfoLog(shader);
        _gl.DeleteShader(shader);
        throw new InvalidOperationException(error);
    }

    public void Dispose()
    {
        _gl.DeleteBuffer(_surfaceBuffer);
        _gl.DeleteBuffer(_buffer);
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteProgram(_program);
    }
}
