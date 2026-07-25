using System.Numerics;
using ImGuiNET;
using Silk.NET.Input;
using Silk.NET.OpenGL.Extensions.ImGui;
using Vecxy.Kernel;

namespace Vecxy.Rendering;

public sealed class ImGuiOverlay(
    IWindow window,
    GraphicsDevice device,
    MaterialLibrary materials) : IDisposable
{
    private IInputContext? _input;
    private ImGuiController? _controller;
    private bool _disposed;

    public void Initialize()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_controller is not null)
        {
            return;
        }

        if (window is not Window nativeWindow)
        {
            throw new NotSupportedException(
                $"ImGui requires the Silk.NET window, but received '{window.GetType().FullName}'.");
        }

        _input = nativeWindow.Native.CreateInput();
        _controller = new ImGuiController(
            device.GL,
            nativeWindow.Native,
            _input);
    }

    public void BeginFrame(float deltaTime)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _controller?.Update(Math.Max(deltaTime, 0.000001f));
    }

    public void Render(RenderingStatistics statistics)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_controller is null)
        {
            return;
        }

        ImGui.SetNextWindowBgAlpha(0.9f);
        ImGui.Begin("Rendering Statistics", ImGuiWindowFlags.NoCollapse);

        ImGui.Text($"FPS: {statistics.FramesPerSecond:F1}");
        ImGui.Text($"Frame: {statistics.FrameTimeMilliseconds:F2} ms");
        ImGui.Separator();
        ImGui.Text($"Views: {statistics.ActiveViews}");
        ImGui.Text($"Render items: {statistics.RenderItems}");
        ImGui.Text($"Draw calls: {statistics.DrawCalls}");
        ImGui.End();

        _controller.Render();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _controller?.Dispose();
        _controller = null;
        _input?.Dispose();
        _input = null;
    }
}
