using System.Numerics;
using System.Runtime.CompilerServices;
using Silk.NET.OpenGL;
using Vecxy.Assets;
using Vecxy.Diagnostics;

namespace Vecxy.Rendering._Legacy;

public sealed class Renderer : IRenderer, IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly Window _window;
    private readonly GameScreen _gameScreen;
    private readonly List<IDisposable> _resources = [];
    private readonly List<RenderCommand> _commands = [];
    private bool _frameActive;
    private bool _disposed;
    private int _staticBatches;
    private int _staticSourceObjects;

    public Material FallbackMaterial { get; private set; } = null!;
    public Texture2D FallbackTexture { get; private set; } = null!;
    public RenderStats Stats { get; private set; }
    public ScreenRect ScreenBounds => _gameScreen.Bounds;

    public int Width => _gameScreen.Bounds.Width;
    public int Height => _gameScreen.Bounds.Height;
    public IRenderCamera Camera { get; set; } = Camera2D.Default;

    internal Renderer(GraphicsDevice device, Window window, GameScreen gameScreen)
    {
        _device = device;
        _window = window;
        _gameScreen = gameScreen;
    }

    internal void Initialize()
    {
        const string vertex = """
            #version 330 core
            layout(location = 0) in vec3 aPosition;
            layout(location = 1) in vec3 aNormal;
            layout(location = 2) in vec4 aColor;
            layout(location = 3) in vec2 aTexCoord;
            layout(location = 4) in mat4 aModel;
            uniform mat4 uViewProjection;
            out vec4 vColor;
            out vec2 vTexCoord;
            void main() { gl_Position = uViewProjection * aModel * vec4(aPosition, 1.0); vColor = aColor; vTexCoord = aTexCoord; }
            """;
        const string fragment = """
            #version 330 core
            in vec4 vColor;
            in vec2 vTexCoord;
            uniform sampler2D uTexture;
            out vec4 fragColor;
            void main() { fragColor = texture(uTexture, vTexCoord) * vColor; }
            """;
        FallbackTexture = Track(new Texture2D(_device, 2, 2,
        [
            255, 0, 255, 255, 20, 20, 20, 255,
            20, 20, 20, 255, 255, 0, 255, 255
        ], new TextureOptions(TextureFilter.Nearest, TextureWrap.Repeat)));
        var shader = CreateShader(vertex, fragment, "BuiltIn/Fallback");
        FallbackMaterial = CreateMaterial(shader).SetTexture("uTexture", FallbackTexture);
    }

    public ShaderProgram CreateShader(string vertexSource, string fragmentSource, string name = "Shader") =>
        Track(new ShaderProgram(_device, vertexSource, fragmentSource, name));

    public ShaderProgram CreateShader(TextAsset vertexShader, TextAsset fragmentShader) =>
        Track(new ShaderProgram(_device, vertexShader.Content, fragmentShader.Content,
            $"{vertexShader.Path} + {fragmentShader.Path}", vertexShader, fragmentShader));

    public Mesh CreateMesh(ReadOnlySpan<Vertex> vertices, ReadOnlySpan<uint> indices) =>
        Track(new Mesh(_device, vertices, indices));

    public Texture2D CreateTexture(ImageAsset image, TextureOptions options = default) =>
        Track(new Texture2D(_device, image, options));

    public Material CreateMaterial(ShaderProgram shader) => new(shader);

    public void BeginFrame(Color clearColor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_frameActive) throw new InvalidOperationException("A render frame is already active.");
        _frameActive = true;
        _commands.Clear();
        Stats = default;
        _staticBatches = 0;
        _staticSourceObjects = 0;
        var gl = _device.GL;
        var bounds = _gameScreen.Bounds;
        gl.Viewport(bounds.X, Math.Max(0, _window.Size.Y - bounds.Y - bounds.Height), (uint)bounds.Width, (uint)bounds.Height);
        gl.ClearColor(clearColor.R, clearColor.G, clearColor.B, clearColor.A);
        gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
    }

    public void Submit(Mesh mesh, Material material, Matrix4x4 transform)
    {
        if (!_frameActive) throw new InvalidOperationException("BeginFrame must be called before Submit.");
        _commands.Add(new RenderCommand(mesh, material, transform));
    }

    public void MarkStaticBatch(int sourceObjects)
    {
        _staticBatches++;
        _staticSourceObjects += sourceObjects;
    }

    public void EndFrame()
    {
        if (!_frameActive) throw new InvalidOperationException("No render frame is active.");
        var viewProjection = Camera.GetViewProjection(Width, Height);
        _commands.Sort(RenderCommandComparer.Instance);
        var drawCalls = 0;
        var triangles = 0;
        var materialSwitches = 0;
        var instancedBatches = 0;
        Material? previousMaterial = null;
        for (var start = 0; start < _commands.Count;)
        {
            var command = _commands[start];
            var end = start + 1;
            while (end < _commands.Count && ReferenceEquals(_commands[end].Material, command.Material) &&
                   ReferenceEquals(_commands[end].Mesh, command.Mesh) &&
                   SameWinding(_commands[end].Transform, command.Transform)) end++;
            SetCapability(EnableCap.DepthTest, command.Material.DepthTest);
            SetCapability(EnableCap.Blend, command.Material.Blending);
            SetCapability(EnableCap.CullFace, command.Material.CullMode != CullMode.None);
            if (command.Material.CullMode != CullMode.None)
            {
                _device.GL.CullFace(command.Material.CullMode == CullMode.Front ? TriangleFace.Front : TriangleFace.Back);
                _device.GL.FrontFace(command.Transform.GetDeterminant() < 0f
                    ? FrontFaceDirection.CW
                    : FrontFaceDirection.Ccw);
            }
            if (!ReferenceEquals(previousMaterial, command.Material))
            {
                command.Material.Bind();
                materialSwitches++;
                previousMaterial = command.Material;
            }
            command.Material.Shader.Set("uViewProjection", viewProjection);
            var transforms = new Matrix4x4[end - start];
            for (var i = start; i < end; i++) transforms[i - start] = _commands[i].Transform;
            command.Mesh.DrawInstances(transforms);
            drawCalls++;
            if (transforms.Length > 1) instancedBatches++;
            triangles += (int)((command.Mesh.IndexCount > 0 ? command.Mesh.IndexCount : command.Mesh.VertexCount) / 3) * transforms.Length;
            start = end;
        }
        var submittedObjects = _commands.Count - _staticBatches + _staticSourceObjects;
        Stats = new RenderStats(submittedObjects, drawCalls, triangles, materialSwitches, instancedBatches,
            submittedObjects, _staticBatches);
        _device.GL.BindVertexArray(0);
        _device.GL.UseProgram(0);
        CheckErrors();
        _commands.Clear();
        _frameActive = false;
    }

    private void SetCapability(EnableCap capability, bool enabled)
    {
        if (enabled) _device.GL.Enable(capability);
        else _device.GL.Disable(capability);
    }

    [System.Diagnostics.Conditional("DEBUG")]
    private void CheckErrors()
    {
        for (var error = _device.GL.GetError(); error != GLEnum.NoError; error = _device.GL.GetError())
            Logger.Error($"OpenGL error at end of frame: {error}");
    }

    private T Track<T>(T resource) where T : IDisposable
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _resources.Add(resource);
        return resource;
    }

    public void Dispose()
    {
        if (_disposed) return;
        for (var i = _resources.Count - 1; i >= 0; i--) _resources[i].Dispose();
        _resources.Clear();
        _commands.Clear();
        _disposed = true;
    }

    private readonly record struct RenderCommand(Mesh Mesh, Material Material, Matrix4x4 Transform);

    private static bool SameWinding(Matrix4x4 left, Matrix4x4 right) =>
        (left.GetDeterminant() < 0f) == (right.GetDeterminant() < 0f);

    private sealed class RenderCommandComparer : IComparer<RenderCommand>
    {
        public static readonly RenderCommandComparer Instance = new();
        public int Compare(RenderCommand x, RenderCommand y)
        {
            var material = RuntimeHelpers.GetHashCode(x.Material).CompareTo(RuntimeHelpers.GetHashCode(y.Material));
            if (material != 0) return material;
            var mesh = RuntimeHelpers.GetHashCode(x.Mesh).CompareTo(RuntimeHelpers.GetHashCode(y.Mesh));
            if (mesh != 0) return mesh;
            return (x.Transform.GetDeterminant() < 0f).CompareTo(y.Transform.GetDeterminant() < 0f);
        }
    }
}
