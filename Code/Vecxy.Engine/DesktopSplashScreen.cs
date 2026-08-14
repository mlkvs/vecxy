using Silk.NET.OpenGL;
using StbImageSharp;
using Vecxy.Kernel;

namespace Vecxy.Engine;

internal sealed class DesktopSplashScreen : IEngineSplashScreen
{
    private const string VertexShaderSource =
        """
        #version 330 core

        layout(location = 0) in vec2 aPosition;
        layout(location = 1) in vec2 aTexCoord;

        out vec2 vTexCoord;

        void main()
        {
            vTexCoord = aTexCoord;
            gl_Position = vec4(aPosition, 0.0, 1.0);
        }
        """;

    private const string FragmentShaderSource =
        """
        #version 330 core

        in vec2 vTexCoord;

        uniform sampler2D uTexture;
        uniform vec4 uColor;
        uniform int uTextured;

        out vec4 oColor;

        void main()
        {
            oColor = uTextured == 1
                ? texture(uTexture, vTexCoord) * uColor
                : uColor;
        }
        """;

    private readonly IWindow _window;
    private readonly GL _gl;
    private uint _program;
    private uint _vertexArray;
    private uint _vertexBuffer;
    private uint _logoTexture;
    private uint _capturedFrameTexture;
    private int _colorLocation;
    private int _texturedLocation;
    private bool _disposed;

    public DesktopSplashScreen(IWindow window, string logoPath)
    {
        _window = window;
        _window.MakeCurrent();
        _gl = GL.GetApi(window.GetProcAddress);

        InitializeGraphics();
        TryLoadLogo(logoPath);
        ReportProgress(0.06f);
    }

    public void ReportProgress(float progress)
    {
        if (_disposed)
            return;

        progress = Math.Clamp(progress, 0.04f, 1.0f);
        _window.MakeCurrent();

        var width = Math.Max(1, _window.Width);
        var height = Math.Max(1, _window.Height);
        var shortestSide = Math.Min(width, height);
        var logoSize = Math.Min(width * 0.62f, height * 0.46f);
        var barWidth = Math.Min(width * 0.56f, logoSize * 0.86f);
        var barHeight = Math.Max(6.0f, shortestSide * 0.012f);
        var gap = Math.Max(24.0f, shortestSide * 0.055f);
        var groupHeight = logoSize + gap + barHeight;
        var logoTop = (height - groupHeight) * 0.5f;
        var logoLeft = (width - logoSize) * 0.5f;
        var barLeft = (width - barWidth) * 0.5f;
        var barTop = logoTop + logoSize + gap;

        _gl.Viewport(0, 0, (uint)width, (uint)height);
        _gl.Disable(EnableCap.DepthTest);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        _gl.ClearColor(0, 0, 0, 1);
        _gl.Clear(ClearBufferMask.ColorBufferBit);

        _gl.UseProgram(_program);
        _gl.BindVertexArray(_vertexArray);

        if (_logoTexture != 0)
        {
            _gl.ActiveTexture(TextureUnit.Texture0);
            _gl.BindTexture(TextureTarget.Texture2D, _logoTexture);
            DrawRectangle(logoLeft, logoTop, logoSize, logoSize, width, height, true, 1, 1, 1, 1);
        }

        DrawRectangle(barLeft, barTop, barWidth, barHeight, width, height, false, 0.86f, 0.91f, 0.86f, 1);
        DrawRectangle(barLeft, barTop, barWidth * progress, barHeight, width, height, false, 0.31f, 0.80f, 0.39f, 1);

        _gl.BindVertexArray(0);
        _gl.UseProgram(0);
        _window.SwapBuffers();
    }

    public void Dismiss()
    {
        if (_disposed)
            return;

        try
        {
            CapturePresentedFrame();
            FadeToPresentedFrame();
        }
        finally
        {
            Dispose();
        }
    }

    public void PrepareForFirstFrame()
    {
        if (!_disposed)
            _window.SuppressNextSwap();
    }

    private void CapturePresentedFrame()
    {
        var width = Math.Max(1, _window.Width);
        var height = Math.Max(1, _window.Height);

        _window.MakeCurrent();
        _gl.ReadBuffer(ReadBufferMode.Back);
        _capturedFrameTexture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, _capturedFrameTexture);
        _gl.CopyTexImage2D(
            TextureTarget.Texture2D,
            0,
            InternalFormat.Rgba8,
            0,
            0,
            (uint)width,
            (uint)height,
            0);

        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        _gl.BindTexture(TextureTarget.Texture2D, 0);
    }

    private void FadeToPresentedFrame()
    {
        if (_capturedFrameTexture == 0)
            return;

        const double durationSeconds = 0.52;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        DrawFadeFrame(1.0f);

        while (stopwatch.Elapsed.TotalSeconds < durationSeconds)
        {
            var position = Math.Clamp(
                (float)(stopwatch.Elapsed.TotalSeconds / durationSeconds),
                0.0f,
                1.0f);
            var eased = position * position * (3.0f - 2.0f * position);
            DrawFadeFrame(1.0f - eased);
            Thread.Sleep(8);
        }

        DrawFadeFrame(0.0f);
    }

    private void DrawFadeFrame(float splashAlpha)
    {
        var width = Math.Max(1, _window.Width);
        var height = Math.Max(1, _window.Height);
        var shortestSide = Math.Min(width, height);
        var logoSize = Math.Min(width * 0.62f, height * 0.46f);
        var barWidth = Math.Min(width * 0.56f, logoSize * 0.86f);
        var barHeight = Math.Max(6.0f, shortestSide * 0.012f);
        var gap = Math.Max(24.0f, shortestSide * 0.055f);
        var groupHeight = logoSize + gap + barHeight;
        var logoTop = (height - groupHeight) * 0.5f;
        var logoLeft = (width - logoSize) * 0.5f;
        var barLeft = (width - barWidth) * 0.5f;
        var barTop = logoTop + logoSize + gap;

        _gl.Viewport(0, 0, (uint)width, (uint)height);
        _gl.Disable(EnableCap.DepthTest);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        _gl.ClearColor(0, 0, 0, 1);
        _gl.Clear(ClearBufferMask.ColorBufferBit);
        _gl.UseProgram(_program);
        _gl.BindVertexArray(_vertexArray);

        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, _capturedFrameTexture);
        DrawRectangle(0, 0, width, height, width, height, true, 1, 1, 1, 1, flipVertical: true);

        if (splashAlpha > 0.001f)
        {
            DrawRectangle(0, 0, width, height, width, height, false, 0, 0, 0, splashAlpha);
            if (_logoTexture != 0)
            {
                _gl.BindTexture(TextureTarget.Texture2D, _logoTexture);
                DrawRectangle(
                    logoLeft,
                    logoTop,
                    logoSize,
                    logoSize,
                    width,
                    height,
                    true,
                    1,
                    1,
                    1,
                    splashAlpha);
            }

            DrawRectangle(barLeft, barTop, barWidth, barHeight, width, height, false, 0.86f, 0.91f, 0.86f, splashAlpha);
            DrawRectangle(barLeft, barTop, barWidth, barHeight, width, height, false, 0.31f, 0.80f, 0.39f, splashAlpha);
        }

        _gl.BindVertexArray(0);
        _gl.UseProgram(0);
        _window.SwapBuffers();
    }

    private void InitializeGraphics()
    {
        var vertexShader = CompileShader(ShaderType.VertexShader, VertexShaderSource);
        var fragmentShader = CompileShader(ShaderType.FragmentShader, FragmentShaderSource);

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
                    $"Splash screen shader linking failed:{Environment.NewLine}{_gl.GetProgramInfoLog(_program)}");
            }
        }
        finally
        {
            _gl.DeleteShader(vertexShader);
            _gl.DeleteShader(fragmentShader);
        }

        _colorLocation = _gl.GetUniformLocation(_program, "uColor");
        _texturedLocation = _gl.GetUniformLocation(_program, "uTextured");
        _gl.UseProgram(_program);
        _gl.Uniform1(_gl.GetUniformLocation(_program, "uTexture"), 0);
        _gl.UseProgram(0);

        _gl.GenVertexArrays(1, out _vertexArray);
        _gl.GenBuffers(1, out _vertexBuffer);
        _gl.BindVertexArray(_vertexArray);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertexBuffer);

        const uint stride = 4 * sizeof(float);
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, 0);
        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, 2 * sizeof(float));

        _gl.BindVertexArray(0);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
    }

    private uint CompileShader(ShaderType type, string source)
    {
        var shader = _gl.CreateShader(type);
        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);
        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out var compiled);
        if (compiled != 0)
            return shader;

        var log = _gl.GetShaderInfoLog(shader);
        _gl.DeleteShader(shader);
        throw new InvalidOperationException(
            $"Splash screen {type} compilation failed:{Environment.NewLine}{log}");
    }

    private unsafe void TryLoadLogo(string logoPath)
    {
        try
        {
            using var stream = File.OpenRead(logoPath);
            var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);

            _logoTexture = _gl.GenTexture();
            _gl.BindTexture(TextureTarget.Texture2D, _logoTexture);
            fixed (byte* pixels = image.Data)
            {
                _gl.TexImage2D(
                    TextureTarget.Texture2D,
                    0,
                    InternalFormat.Rgba8,
                    (uint)image.Width,
                    (uint)image.Height,
                    0,
                    PixelFormat.Rgba,
                    PixelType.UnsignedByte,
                    pixels);
            }

            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            _gl.BindTexture(TextureTarget.Texture2D, 0);
        }
        catch (IOException)
        {
            // A missing optional logo must not prevent the application from starting.
        }
        catch (InvalidDataException)
        {
        }
    }

    private unsafe void DrawRectangle(
        float left,
        float top,
        float width,
        float height,
        int viewportWidth,
        int viewportHeight,
        bool textured,
        float red,
        float green,
        float blue,
        float alpha,
        bool flipVertical = false)
    {
        var x0 = left / viewportWidth * 2.0f - 1.0f;
        var x1 = (left + width) / viewportWidth * 2.0f - 1.0f;
        var y0 = 1.0f - top / viewportHeight * 2.0f;
        var y1 = 1.0f - (top + height) / viewportHeight * 2.0f;
        var topV = flipVertical ? 1.0f : 0.0f;
        var bottomV = flipVertical ? 0.0f : 1.0f;
        float[] vertices =
        [
            x0, y0, 0, topV,
            x1, y0, 1, topV,
            x0, y1, 0, bottomV,
            x1, y1, 1, bottomV
        ];

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertexBuffer);
        fixed (float* data = vertices)
        {
            _gl.BufferData(
                BufferTargetARB.ArrayBuffer,
                (nuint)(vertices.Length * sizeof(float)),
                data,
                BufferUsageARB.DynamicDraw);
        }

        _gl.Uniform1(_texturedLocation, textured ? 1 : 0);
        _gl.Uniform4(_colorLocation, red, green, blue, alpha);
        _gl.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_logoTexture != 0)
            _gl.DeleteTexture(_logoTexture);
        if (_capturedFrameTexture != 0)
            _gl.DeleteTexture(_capturedFrameTexture);
        if (_vertexBuffer != 0)
            _gl.DeleteBuffer(_vertexBuffer);
        if (_vertexArray != 0)
            _gl.DeleteVertexArray(_vertexArray);
        if (_program != 0)
            _gl.DeleteProgram(_program);
        _gl.Dispose();
    }
}
