using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;

namespace Vecxy.Rendering;

public class RenderingWindow : GameWindow
{
    public RenderingWindow(RenderingWindowOptions options) : base(GameWindowSettings.Default, new NativeWindowSettings())
    {
        Size = new Vector2i(options.Width, options.Height);
        Title = options.Title;
    }
}