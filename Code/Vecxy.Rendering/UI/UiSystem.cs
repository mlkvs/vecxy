using System.Diagnostics;
using System.Numerics;
using Vecxy.Assets;

namespace Vecxy.UI;

public sealed class UiSystem
{
    private readonly Vecxy.Rendering.GraphicsDevice _device;
    private readonly Vecxy.Rendering.IInput _input;
    private UiOpenGlRenderer? _renderer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _lastTime;
    private float _fps;
    private TextAsset? _uxmlAsset;
    private TextAsset? _cssAsset;
    private UiElement? _hovered;
    private UiElement? _pressed;
    private UiElement? _focused;
    private UiElement? _dragged;
    private Vector2 _dragStart;
    private bool _selectingText;
    public UiDocument? Document { get; private set; }

    internal UiSystem(Vecxy.Rendering.GraphicsDevice device, Vecxy.Rendering.IInput input) { _device = device; _input = input; }
    internal void Initialize() => _renderer = new UiOpenGlRenderer(_device);

    public UiDocument Load(TextAsset uxml, TextAsset css)
    {
        Unsubscribe();
        _uxmlAsset = uxml;
        _cssAsset = css;
        uxml.Reloaded += Reload;
        css.Reloaded += Reload;
        return Load(uxml.Content, css.Content);
    }
    public UiDocument Load(string uxml, string css) => Document = UiDocument.Load(uxml, css);
    public void Clear() { Unsubscribe(); Document = null; }

    internal void Update(float deltaTime, int width, int height, int originX = 0, int originY = 0)
    {
        if (Document is null) return;
        Document.Layout(width, height);
        var pointer = _input.MousePosition - new Vector2(originX, originY);
        var typed = _focused is { Type: "TextField" } ? _input.ConsumeTextInput() : string.Empty;
        var editCommands = _focused is { Type: "TextField" } ? _input.ConsumeTextEditCommands() : [];
        if (_focused is { Type: "TextField" } field)
        {
            foreach (var command in editCommands) Edit(field, command);
            if (typed.Length > 0) ReplaceSelection(field, typed);
            EnsureCaretVisible(field);
            field.CaretVisible = nowBlink();
        }
        var hit = HitTest(Document.Root, pointer, null);
        if (!ReferenceEquals(hit, _hovered))
        {
            if (_hovered is not null) { _hovered.IsHovered = false; Document.RefreshStyle(_hovered); }
            _hovered = hit;
            if (_hovered is not null) { _hovered.IsHovered = true; Document.RefreshStyle(_hovered); }
        }

        var scroll = _input.ConsumeScrollDelta();
        if (scroll != Vector2.Zero)
        {
            var scrollView = Ancestor(hit, x => x.Type == "ScrollView");
            if (scrollView is not null)
            {
                var maximum = MathF.Max(0, scrollView.VirtualContentHeight - scrollView.Layout.W);
                scrollView.ScrollY = Math.Clamp(scrollView.ScrollY - scroll.Y * 32, 0, maximum);
            }
        }

        if (_input.IsLeftMousePressed && hit is not null)
        {
            _input.ConsumeLeftMousePressed();
            _pressed = hit;
            hit.IsPressed = true;
            Document.RefreshStyle(hit);
            if (_focused is not null) _focused.IsFocused = false;
            Document.RefreshStyle(_focused);
            _focused = hit;
            hit.IsFocused = true;
            Document.RefreshStyle(hit);
            _dragStart = pointer;
            if (hit.Type == "TextField")
            {
                hit.CaretIndex = hit.SelectionAnchor = CharacterAt(hit, pointer.X);
                EnsureCaretVisible(hit);
                _selectingText = true;
            }
            if (hit.Draggable) _dragged = hit;
            UpdateValueControl(hit, pointer);
        }

        if (_pressed is { Type: "Slider" } slider && _input.IsLeftMouseDown) UpdateValueControl(slider, pointer);
        if (_selectingText && _pressed is { Type: "TextField" } textField && _input.IsLeftMouseDown)
            textField.CaretIndex = CharacterAt(textField, pointer.X);
        if (_dragged is not null && _input.IsLeftMouseDown)
        {
            var delta = pointer - _dragStart;
            if (_dragged.DragVisual) _dragged.VisualOffset = delta;
            _dragged.RaiseDragged(delta);
        }

        if (_input.IsLeftMouseReleased && _pressed is not null)
        {
            _input.ConsumeLeftMouseReleased();
            var pressed = _pressed;
            pressed.IsPressed = false;
            Document.RefreshStyle(pressed);
            if (ReferenceEquals(hit, pressed))
            {
                if (pressed.Type is "Toggle" or "Checkbox") pressed.SetValue(pressed.Value >= .5f ? 0 : 1);
                if (pressed.Type == "RadioButton")
                {
                    if (pressed.Parent is not null)
                        foreach (var sibling in pressed.Parent.Children.Where(x => x.Type == "RadioButton")) sibling.SetValue(0);
                    pressed.SetValue(1);
                }
                if (pressed.Type == "Dropdown" && pressed.Options.Count > 0)
                {
                    var index = Array.FindIndex(pressed.Options.ToArray(), x => x.Equals(pressed.Text, StringComparison.OrdinalIgnoreCase));
                    pressed.Text = pressed.Options[(index + 1) % pressed.Options.Count];
                }
                pressed.RaiseClick();
            }
            if (_dragged is not null)
            {
                var target = Ancestor(hit, x => x.DropTarget);
                if (target is not null && !ReferenceEquals(target, _dragged)) target.AcceptDrop(_dragged);
                _dragged.VisualOffset = Vector2.Zero;
                _dragged = null;
            }
            _pressed = null;
            _selectingText = false;
        }

        bool nowBlink() => ((int)(_clock.Elapsed.TotalSeconds * 2) & 1) == 0;
    }

    private static int CharacterAt(UiElement field, float pointerX)
    {
        var advance = UiTextMetrics.Advance(field.Style.FontSize);
        return Math.Clamp((int)MathF.Round((pointerX - field.Layout.X - field.Style.Padding.Left + field.TextScrollX) / advance), 0, field.Text.Length);
    }

    private static void EnsureCaretVisible(UiElement field)
    {
        var advance = UiTextMetrics.Advance(field.Style.FontSize);
        var caret = field.CaretIndex * advance;
        var available = MathF.Max(1, field.Layout.Z - field.Style.Padding.Left - field.Style.Padding.Right - 3);
        if (caret - field.TextScrollX > available) field.TextScrollX = caret - available;
        if (caret < field.TextScrollX) field.TextScrollX = caret;
        field.TextScrollX = MathF.Max(0, field.TextScrollX);
    }

    private void Edit(UiElement field, Vecxy.Rendering.TextEditCommand command)
    {
        switch (command)
        {
            case Vecxy.Rendering.TextEditCommand.Left: field.CaretIndex = field.SelectionLength > 0 ? field.SelectionStart : Math.Max(0, field.CaretIndex - 1); field.SelectionAnchor = field.CaretIndex; break;
            case Vecxy.Rendering.TextEditCommand.Right: field.CaretIndex = field.SelectionLength > 0 ? field.SelectionStart + field.SelectionLength : Math.Min(field.Text.Length, field.CaretIndex + 1); field.SelectionAnchor = field.CaretIndex; break;
            case Vecxy.Rendering.TextEditCommand.SelectLeft: field.CaretIndex = Math.Max(0, field.CaretIndex - 1); break;
            case Vecxy.Rendering.TextEditCommand.SelectRight: field.CaretIndex = Math.Min(field.Text.Length, field.CaretIndex + 1); break;
            case Vecxy.Rendering.TextEditCommand.Home: field.CaretIndex = field.SelectionAnchor = 0; break;
            case Vecxy.Rendering.TextEditCommand.End: field.CaretIndex = field.SelectionAnchor = field.Text.Length; break;
            case Vecxy.Rendering.TextEditCommand.SelectHome: field.CaretIndex = 0; break;
            case Vecxy.Rendering.TextEditCommand.SelectEnd: field.CaretIndex = field.Text.Length; break;
            case Vecxy.Rendering.TextEditCommand.SelectAll: field.SelectionAnchor = 0; field.CaretIndex = field.Text.Length; break;
            case Vecxy.Rendering.TextEditCommand.Copy: if (field.SelectionLength > 0) _input.ClipboardText = field.Text.Substring(field.SelectionStart, field.SelectionLength); break;
            case Vecxy.Rendering.TextEditCommand.Cut:
                if (field.SelectionLength > 0) { _input.ClipboardText = field.Text.Substring(field.SelectionStart, field.SelectionLength); ReplaceSelection(field, ""); }
                break;
            case Vecxy.Rendering.TextEditCommand.Paste: ReplaceSelection(field, _input.ClipboardText); break;
            case Vecxy.Rendering.TextEditCommand.Backspace:
                if (field.SelectionLength > 0) ReplaceSelection(field, "");
                else if (field.CaretIndex > 0) { field.SelectionAnchor = field.CaretIndex - 1; ReplaceSelection(field, ""); }
                break;
            case Vecxy.Rendering.TextEditCommand.Delete:
                if (field.SelectionLength > 0) ReplaceSelection(field, "");
                else if (field.CaretIndex < field.Text.Length) { field.SelectionAnchor = field.CaretIndex + 1; ReplaceSelection(field, ""); }
                break;
        }
    }

    private static void ReplaceSelection(UiElement field, string value)
    {
        var start = field.SelectionStart;
        field.Text = field.Text.Remove(start, field.SelectionLength).Insert(start, value);
        field.CaretIndex = field.SelectionAnchor = start + value.Length;
        field.CaretVisible = true;
    }

    private static void UpdateValueControl(UiElement element, Vector2 pointer)
    {
        if (element.Type != "Slider") return;
        element.SetValue((pointer.X - element.Layout.X) / MathF.Max(1, element.Layout.Z));
    }

    private static UiElement? Ancestor(UiElement? element, Func<UiElement, bool> predicate)
    {
        while (element is not null) { if (predicate(element)) return element; element = element.Parent; }
        return null;
    }

    private static UiElement? HitTest(UiElement element, Vector2 point, Vector4? clip)
    {
        if (!element.IsVirtualVisible) return null;
        if (!element.IsHitTestVisible) return null;
        var r = element.Layout;
        var inside = point.X >= r.X && point.Y >= r.Y && point.X <= r.X + r.Z && point.Y <= r.Y + r.W;
        if (clip is { } c && (point.X < c.X || point.Y < c.Y || point.X > c.X + c.Z || point.Y > c.Y + c.W)) return null;
        var childClip = element.Type == "ScrollView" ? Intersect(clip, r) : clip;
        var first = element.Type == "ScrollView" ? element.VirtualStart : 0;
        var end = element.Type == "ScrollView" ? element.VirtualEnd : element.Children.Count;
        for (var index = end - 1; index >= first; index--)
        {
            var hit = HitTest(element.Children[index], point, childClip);
            if (hit is not null) return hit;
        }
        return inside && element.IsEnabled && element.Type != "UI" ? element : null;
    }

    private static Vector4 Intersect(Vector4? a, Vector4 b)
    {
        if (a is null) return b;
        var x = MathF.Max(a.Value.X, b.X); var y = MathF.Max(a.Value.Y, b.Y);
        var right = MathF.Min(a.Value.X + a.Value.Z, b.X + b.Z); var bottom = MathF.Min(a.Value.Y + a.Value.W, b.Y + b.W);
        return new(x, y, MathF.Max(0, right - x), MathF.Max(0, bottom - y));
    }

    internal void Render(int width, int height, Vecxy.Rendering.RenderStats stats, int originX = 0, int originY = 0,
        int windowWidth = 0, int windowHeight = 0)
    {
        if (Document is null) return;
        var now = _clock.Elapsed.TotalSeconds;
        var delta = now - _lastTime;
        _lastTime = now;
        if (delta > 0) _fps = _fps == 0 ? (float)(1 / delta) : _fps * .9f + (float)(1 / delta) * .1f;
        Set("fps", $"FPS        {_fps:0}");
        Set("draws", $"DRAW CALLS {stats.DrawCalls}");
        Set("triangles", $"TRIANGLES  {stats.Triangles}");
        Set("objects", $"OBJECTS    {stats.SubmittedObjects}");
        Set("instancing", $"INSTANCED  {stats.InstancedBatches}");
        Set("static", $"STATIC     {stats.StaticBatches}");
        var health = Document.Find("health");
        if (health is not null) health.Value = 1f - (float)(now % 10.0 / 10.0);
        Set("health-value", $"{health?.Value * 100f:0} / 100");
        Document.Layout(width, height);
        _renderer?.Render(Document.Root, originX, originY, windowWidth == 0 ? width : windowWidth,
            windowHeight == 0 ? height : windowHeight);
    }

    private void Set(string id, string text)
    {
        var element = Document?.Find(id);
        if (element is not null) element.Text = text;
    }

    private void Reload(Asset _) { if (_uxmlAsset is not null && _cssAsset is not null) Document = UiDocument.Load(_uxmlAsset.Content, _cssAsset.Content); }
    private void Unsubscribe()
    {
        if (_uxmlAsset is not null) _uxmlAsset.Reloaded -= Reload;
        if (_cssAsset is not null) _cssAsset.Reloaded -= Reload;
        _uxmlAsset = _cssAsset = null;
    }

    internal void Dispose() { Unsubscribe(); _renderer?.Dispose(); }
}

internal sealed class UiOpenGlRenderer : IDisposable
{
    private readonly Vecxy.Rendering.GraphicsDevice _device;
    private readonly Vecxy.Rendering.ShaderProgram _shader;
    private uint _vao;
    private uint _vbo;
    private readonly List<UiVertex> _vertices = [];

    internal unsafe UiOpenGlRenderer(Vecxy.Rendering.GraphicsDevice device)
    {
        _device = device;
        _shader = new Vecxy.Rendering.ShaderProgram(device, """
            #version 330 core
            layout(location=0) in vec2 aPosition;
            layout(location=1) in vec4 aColor;
            uniform vec2 uViewport;
            uniform vec2 uOrigin;
            out vec4 vColor;
            void main() { vec2 p = (aPosition + uOrigin) / uViewport * 2.0 - 1.0; gl_Position = vec4(p.x, -p.y, 0, 1); vColor = aColor; }
            """, """
            #version 330 core
            in vec4 vColor; out vec4 fragColor;
            void main() { fragColor = vColor; }
            """, "BuiltIn/UI");
        var gl = device.GL;
        _vao = gl.GenVertexArray();
        _vbo = gl.GenBuffer();
        gl.BindVertexArray(_vao);
        gl.BindBuffer(Silk.NET.OpenGL.BufferTargetARB.ArrayBuffer, _vbo);
        gl.VertexAttribPointer(0, 2, Silk.NET.OpenGL.VertexAttribPointerType.Float, false, 24, 0);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(1, 4, Silk.NET.OpenGL.VertexAttribPointerType.Float, false, 24, 8);
        gl.EnableVertexAttribArray(1);
        gl.BindVertexArray(0);
    }

    internal unsafe void Render(UiElement root, int originX, int originY, int width, int height)
    {
        _vertices.Clear();
        Visit(root, Vector2.Zero, null);
        if (_vertices.Count == 0) return;
        var gl = _device.GL;
        gl.Viewport(0, 0, (uint)width, (uint)height);
        gl.Disable(Silk.NET.OpenGL.EnableCap.DepthTest);
        gl.Disable(Silk.NET.OpenGL.EnableCap.CullFace);
        gl.Enable(Silk.NET.OpenGL.EnableCap.Blend);
        gl.BlendFunc(Silk.NET.OpenGL.BlendingFactor.SrcAlpha, Silk.NET.OpenGL.BlendingFactor.OneMinusSrcAlpha);
        _shader.Bind();
        _shader.Set("uViewport", new Vector2(width, height));
        _shader.Set("uOrigin", new Vector2(originX, originY));
        gl.BindVertexArray(_vao);
        gl.BindBuffer(Silk.NET.OpenGL.BufferTargetARB.ArrayBuffer, _vbo);
        var data = _vertices.ToArray();
        fixed (UiVertex* pointer = data)
            gl.BufferData(Silk.NET.OpenGL.BufferTargetARB.ArrayBuffer, (nuint)(data.Length * sizeof(UiVertex)), pointer,
                Silk.NET.OpenGL.BufferUsageARB.StreamDraw);
        gl.DrawArrays(Silk.NET.OpenGL.PrimitiveType.Triangles, 0, (uint)data.Length);
        gl.BindVertexArray(0);
    }

    private Vector4? _clip;

    private void Visit(UiElement element, Vector2 inheritedOffset, Vector4? parentClip)
    {
        if (!element.IsVirtualVisible) return;
        var offset = inheritedOffset + element.VisualOffset;
        var source = element.Layout;
        var r = source with { X = source.X + offset.X, Y = source.Y + offset.Y };
        var previousClip = _clip;
        _clip = element.Type == "ScrollView" ? Intersect(parentClip, r) : parentClip;
        Rect(r.X, r.Y, r.Z, r.W, element.Style.Background);
        if (element.Type == "Icon") DrawIcon(element.IconName, r, element.Style.IconSize, element.Style.Color);
        if (element.Type is "ProgressBar" or "Slider")
        {
            var value = Math.Clamp(element.Value, 0f, 1f);
            Rect(r.X + 3, r.Y + 3, MathF.Max(0, (r.Z - 6) * value), MathF.Max(0, r.W - 6), element.Style.FillColor);
            if (element.Type == "Slider") Rect(r.X + 3 + MathF.Max(0, r.Z - 6) * value - 3, r.Y, 6, r.W, element.Style.Color);
        }
        else if (element.Type is "Toggle" or "Checkbox" or "RadioButton" && element.Value >= .5f)
            Rect(r.X + 4, r.Y + 4, MathF.Max(0, r.W - 8), MathF.Max(0, r.W - 8), element.Style.FillColor);
        var controlClip = _clip;
        if (element.Type == "TextField") _clip = Intersect(_clip, r);
        if (element.Type == "TextField" && element.SelectionLength > 0)
        {
            var advance = UiTextMetrics.Advance(element.Style.FontSize);
            Rect(r.X + element.Style.Padding.Left + element.SelectionStart * advance - element.TextScrollX, r.Y + 3,
                element.SelectionLength * advance, MathF.Max(0, r.W - 6), new Vecxy.Rendering.Color(.22f, .48f, .75f, .75f));
        }
        if (element.Style.BorderWidth > 0)
        {
            var b = element.Style.BorderWidth; var c = element.Style.BorderColor;
            Rect(r.X, r.Y, r.Z, b, c); Rect(r.X, r.Y + r.W - b, r.Z, b, c);
            Rect(r.X, r.Y, b, r.W, c); Rect(r.X + r.Z - b, r.Y, b, r.W, c);
        }
        if (element.Text.Length > 0)
        {
            var textWidth = UiTextMetrics.Width(element.Text, element.Style.FontSize);
            var contentX = r.X + element.Style.Padding.Left;
            var contentY = r.Y + element.Style.Padding.Top;
            var contentWidth = MathF.Max(0, r.Z - element.Style.Padding.Left - element.Style.Padding.Right);
            var contentHeight = MathF.Max(0, r.W - element.Style.Padding.Top - element.Style.Padding.Bottom);
            var textX = element.Type == "TextField" ? contentX - element.TextScrollX : element.Style.TextAlign switch
            {
                UiAlign.Center => contentX + (contentWidth - textWidth) * .5f,
                UiAlign.End => contentX + contentWidth - textWidth,
                _ => contentX
            };
            var glyphHeight = UiTextMetrics.Height(element.Style.FontSize);
            var textY = element.Style.VerticalAlign switch
            {
                UiAlign.Start => contentY,
                UiAlign.End => contentY + MathF.Max(0, contentHeight - glyphHeight),
                _ => contentY + MathF.Max(0, (contentHeight - glyphHeight) * .5f)
            };
            Text(element.Text, textX, textY, element.Style.FontSize, element.Style.Color);
        }
        if (element.Type == "TextField" && element.IsFocused && element.CaretVisible)
        {
            var advance = UiTextMetrics.Advance(element.Style.FontSize);
            Rect(r.X + element.Style.Padding.Left + element.CaretIndex * advance - element.TextScrollX, r.Y + 4, 1.5f,
                MathF.Max(0, r.W - 8), element.Style.Color);
        }
        _clip = controlClip;
        if (element.Type == "ScrollView")
        {
            for (var i = element.VirtualStart; i < element.VirtualEnd; i++) Visit(element.Children[i], offset, _clip);
        }
        else foreach (var child in element.Children) Visit(child, offset, _clip);
        if (element.Type == "ScrollView" && element.Children.Count > 0)
        {
            var contentHeight = MathF.Max(r.W, element.VirtualContentHeight);
            if (contentHeight > r.W)
            {
                var thumbHeight = MathF.Max(18, r.W * r.W / contentHeight);
                var maxScroll = contentHeight - r.W;
                var thumbY = r.Y + (r.W - thumbHeight) * element.ScrollY / MathF.Max(1, maxScroll);
                Rect(r.X + r.Z - 3, thumbY, 3, thumbHeight, new Vecxy.Rendering.Color(.55f, .65f, .75f, .8f));
            }
        }
        _clip = previousClip;
    }

    private void Text(string text, float x, float y, float size, Vecxy.Rendering.Color color)
    {
        var scale = UiTextMetrics.PixelScale(size);
        foreach (var character in text.ToUpperInvariant())
        {
            if (Glyphs.TryGetValue(character, out var glyph))
                for (var row = 0; row < 7; row++) for (var column = 0; column < 5; column++)
                    if ((glyph[row] & (1 << (4 - column))) != 0) Rect(x + column * scale, y + row * scale, scale, scale, color);
            x += scale * 6;
        }
    }

    private void DrawIcon(string name, Vector4 bounds, float requestedSize, Vecxy.Rendering.Color color)
    {
        var size = MathF.Min(requestedSize, MathF.Min(bounds.Z, bounds.W));
        var x = bounds.X + (bounds.Z - size) * .5f; var y = bounds.Y + (bounds.W - size) * .5f;
        var u = size / 24f; var stroke = MathF.Max(1.5f, 2f * u);
        void Line(float x1, float y1, float x2, float y2) => Stroke(x + x1 * u, y + y1 * u, x + x2 * u, y + y2 * u, stroke, color);
        switch (name.ToLowerInvariant())
        {
            case "play": Triangle(x + 7*u, y + 4*u, x + 20*u, y + 12*u, x + 7*u, y + 20*u, color); break;
            case "pause": Rect(x + 6*u, y + 4*u, 4*u, 16*u, color); Rect(x + 14*u, y + 4*u, 4*u, 16*u, color); break;
            case "external-link": Line(5,5,5,19); Line(5,19,19,19); Line(19,19,19,13); Line(12,5,19,5); Line(19,5,19,12); Line(10,14,19,5); break;
            case "folder": Line(3,7,10,7); Line(10,7,12,10); Line(12,10,21,10); Line(21,10,20,19); Line(20,19,4,19); Line(4,19,3,7); break;
            case "file": Line(6,3,14,3); Line(14,3,19,8); Line(19,8,19,21); Line(19,21,6,21); Line(6,21,6,3); Line(14,3,14,8); Line(14,8,19,8); break;
            case "search": Circle(x+10*u,y+10*u,6*u,stroke,color); Line(14.5f,14.5f,21,21); break;
            case "chevron-down": Line(5,9,12,16); Line(12,16,19,9); break;
            case "settings": Circle(x+12*u,y+12*u,4*u,stroke,color); for(var i=0;i<8;i++){var a=i*MathF.PI/4; Line(12+MathF.Cos(a)*7,12+MathF.Sin(a)*7,12+MathF.Cos(a)*10,12+MathF.Sin(a)*10);} break;
            default: Rect(x+4*u,y+4*u,16*u,16*u,new Vecxy.Rendering.Color(color.R,color.G,color.B,color.A*.35f)); break;
        }
    }

    private void Stroke(float x1, float y1, float x2, float y2, float width, Vecxy.Rendering.Color color)
    {
        var direction = Vector2.Normalize(new Vector2(x2-x1,y2-y1)); var normal = new Vector2(-direction.Y,direction.X)*width*.5f;
        var a=new Vector2(x1,y1)+normal; var b=new Vector2(x2,y2)+normal; var c=new Vector2(x2,y2)-normal; var d=new Vector2(x1,y1)-normal;
        Triangle(a.X,a.Y,b.X,b.Y,c.X,c.Y,color); Triangle(a.X,a.Y,c.X,c.Y,d.X,d.Y,color);
    }
    private void Circle(float cx,float cy,float radius,float width,Vecxy.Rendering.Color color)
    { const int segments=20; for(var i=0;i<segments;i++){var a=i*MathF.Tau/segments;var b=(i+1)*MathF.Tau/segments;Stroke(cx+MathF.Cos(a)*radius,cy+MathF.Sin(a)*radius,cx+MathF.Cos(b)*radius,cy+MathF.Sin(b)*radius,width,color);} }
    private void Triangle(float ax,float ay,float bx,float by,float cx,float cy,Vecxy.Rendering.Color color)
    { _vertices.Add(new UiVertex(ax,ay,color)); _vertices.Add(new UiVertex(bx,by,color)); _vertices.Add(new UiVertex(cx,cy,color)); }

    private void Rect(float x, float y, float width, float height, Vecxy.Rendering.Color color)
    {
        if (width <= 0 || height <= 0 || color.A <= 0) return;
        if (_clip is { } clip)
        {
            var right = MathF.Min(x + width, clip.X + clip.Z); var bottom = MathF.Min(y + height, clip.Y + clip.W);
            x = MathF.Max(x, clip.X); y = MathF.Max(y, clip.Y);
            width = right - x; height = bottom - y;
            if (width <= 0 || height <= 0) return;
        }
        var a = new UiVertex(x, y, color); var b = new UiVertex(x + width, y, color);
        var c = new UiVertex(x + width, y + height, color); var d = new UiVertex(x, y + height, color);
        _vertices.AddRange([a, b, c, a, c, d]);
    }

    private static Vector4 Intersect(Vector4? a, Vector4 b)
    {
        if (a is null) return b;
        var x = MathF.Max(a.Value.X, b.X); var y = MathF.Max(a.Value.Y, b.Y);
        var right = MathF.Min(a.Value.X + a.Value.Z, b.X + b.Z); var bottom = MathF.Min(a.Value.Y + a.Value.W, b.Y + b.W);
        return new(x, y, MathF.Max(0, right - x), MathF.Max(0, bottom - y));
    }

    public void Dispose()
    {
        _shader.Dispose();
        if (_device.IsInitialized) { _device.GL.DeleteBuffer(_vbo); _device.GL.DeleteVertexArray(_vao); }
        _vbo = _vao = 0;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private readonly struct UiVertex(float x, float y, Vecxy.Rendering.Color color)
    { public readonly Vector2 Position = new(x, y); public readonly Vecxy.Rendering.Color Color = color; }

    private static readonly Dictionary<char, byte[]> Glyphs = new()
    {
        ['0']=[14,17,19,21,25,17,14], ['1']=[4,12,4,4,4,4,14], ['2']=[14,17,1,2,4,8,31],
        ['3']=[30,1,1,14,1,1,30], ['4']=[2,6,10,18,31,2,2], ['5']=[31,16,16,30,1,1,30],
        ['6']=[14,16,16,30,17,17,14], ['7']=[31,1,2,4,8,8,8], ['8']=[14,17,17,14,17,17,14],
        ['9']=[14,17,17,15,1,1,14], ['A']=[14,17,17,31,17,17,17], ['B']=[30,17,17,30,17,17,30],
        ['C']=[14,17,16,16,16,17,14], ['D']=[30,17,17,17,17,17,30], ['E']=[31,16,16,30,16,16,31],
        ['F']=[31,16,16,30,16,16,16], ['G']=[14,17,16,23,17,17,15], ['I']=[14,4,4,4,4,4,14],
        ['H']=[17,17,17,31,17,17,17], ['J']=[7,2,2,2,2,18,12], ['K']=[17,18,20,24,20,18,17],
        ['L']=[16,16,16,16,16,16,31], ['M']=[17,27,21,21,17,17,17], ['N']=[17,25,25,21,19,19,17],
        ['O']=[14,17,17,17,17,17,14], ['R']=[30,17,17,30,20,18,17], ['S']=[15,16,16,14,1,1,30],
        ['P']=[30,17,17,30,16,16,16], ['Q']=[14,17,17,17,21,18,13],
        ['T']=[31,4,4,4,4,4,4], ['U']=[17,17,17,17,17,17,14], ['V']=[17,17,17,17,17,10,4],
        ['W']=[17,17,17,21,21,21,10], ['X']=[17,17,10,4,10,17,17], ['Y']=[17,17,10,4,4,4,4],
        ['Z']=[31,1,2,4,8,16,31], [' '] = [0,0,0,0,0,0,0], ['+']=[0,4,4,31,4,4,0],
        ['.']=[0,0,0,0,0,12,12], [',']=[0,0,0,0,4,4,8], [':']=[0,4,4,0,4,4,0],
        ['-']=[0,0,0,31,0,0,0], ['/']=[1,2,2,4,8,8,16], ['%']=[17,2,4,8,16,17,0],
        ['!']=[4,4,4,4,4,0,4], ['?']=[14,17,1,2,4,0,4], ['[']=[14,8,8,8,8,8,14],
        [']']=[14,2,2,2,2,2,14], ['(']=[2,4,8,8,8,4,2], [')']=[8,4,2,2,2,4,8],
        ['_']=[0,0,0,0,0,0,31], ['=']=[0,31,0,31,0,0,0]
    };
}
