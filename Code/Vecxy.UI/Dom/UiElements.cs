using Facebook.Yoga;

namespace Vecxy.UI;

public sealed class UiPanel : UiElement
{
    internal UiPanel(Config config, IReadOnlyDictionary<string, string> attributes, string? text = null)
        : base(config, "panel", attributes, text)
    {
    }
}

public sealed class UiText : UiElement
{
    internal UiText(Config config, IReadOnlyDictionary<string, string> attributes, string? text = null)
        : base(config, "text", attributes, text)
    {
    }

    public string Value
    {
        get => Text;
        set => Text = value;
    }
}

public sealed class UiButton : UiElement
{
    internal UiButton(Config config, IReadOnlyDictionary<string, string> attributes, string? text = null)
        : base(config, "button", attributes, text)
    {
    }

    public string Label
    {
        get => TextContent;
        set => TextContent = value;
    }
}

public sealed class UiImage : UiElement
{
    internal UiImage(Config config, IReadOnlyDictionary<string, string> attributes, string? text = null)
        : base(config, "image", attributes, text)
    {
    }

    public string Source
    {
        get => Attributes.GetValueOrDefault("src") ?? string.Empty;
        set => SetAttribute("src", value);
    }

    public string Sprite
    {
        get => Attributes.GetValueOrDefault("sprite") ?? string.Empty;
        set => SetAttribute("sprite", value);
    }
}

public sealed class UiProgress : UiElement
{
    internal UiProgress(Config config, IReadOnlyDictionary<string, string> attributes, string? text = null)
        : base(config, "progress", attributes, text)
    {
    }
}

public sealed class UiRadialProgress : UiElement
{
    internal UiRadialProgress(Config config, IReadOnlyDictionary<string, string> attributes, string? text = null)
        : base(config, "radial-progress", attributes, text)
    {
    }
}

public sealed class UiInlineStyle
{
    private readonly UiElement _element;

    internal UiInlineStyle(UiElement element)
    {
        _element = element;
    }

    public string? this[string propertyName]
    {
        get => _element.GetInlineStyle(propertyName);
        set
        {
            if (value is null)
                _element.RemoveStyle(propertyName);
            else
                _element.SetStyle(propertyName, value);
        }
    }

    public string? Width { get => this["width"]; set => this["width"] = value; }
    public string? Height { get => this["height"]; set => this["height"] = value; }
    public string? Color { get => this["color"]; set => this["color"] = value; }
    public string? BackgroundColor { get => this["background-color"]; set => this["background-color"] = value; }
    public string? BorderColor { get => this["border-color"]; set => this["border-color"] = value; }
    public string? Opacity { get => this["opacity"]; set => this["opacity"] = value; }
    public string? Transform { get => this["transform"]; set => this["transform"] = value; }

    public void Set(string propertyName, string value) => _element.SetStyle(propertyName, value);
    public bool Remove(string propertyName) => _element.RemoveStyle(propertyName);

    public void SetWidthPercent(float fraction) =>
        Width = $"{Math.Clamp(fraction, 0.0f, 1.0f) * 100.0f:0.##}%";
}

/// <summary>
/// Base class for a reusable, strongly typed view over an XML component.
/// Element references are resolved once when the component is attached.
/// </summary>
public abstract class UiComponent
{
    private readonly Dictionary<string, List<UiElement>> _classes =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, UiElement> _ids =
        new(StringComparer.Ordinal);

    protected UiComponent(UiElement root)
    {
        Root = root;
        foreach (var element in root.DescendantsAndSelf())
        {
            if (element.Id is { Length: > 0 } id)
                _ids.TryAdd(id, element);
            foreach (var className in element.Classes)
            {
                if (!_classes.TryGetValue(className, out var elements))
                {
                    elements = [];
                    _classes.Add(className, elements);
                }
                elements.Add(element);
            }
        }
    }

    public UiElement Root { get; }

    protected T Element<T>(string selector) where T : UiElement
    {
        UiElement? element = selector.Length > 1 && selector[0] == '#'
            ? _ids.GetValueOrDefault(selector[1..])
            : selector.Length > 1 && selector[0] == '.' &&
              _classes.TryGetValue(selector[1..], out var elements)
                ? elements.FirstOrDefault(candidate => candidate is T)
                : null;
        return element as T ?? throw new InvalidDataException(
            $"Required {typeof(T).Name} is missing: {selector}");
    }

    protected IReadOnlyList<T> Elements<T>(string className) where T : UiElement
    {
        className = className.TrimStart('.');
        if (!_classes.TryGetValue(className, out var elements))
            return Array.Empty<T>();
        var result = new List<T>(elements.Count);
        foreach (var element in elements)
            if (element is T typed)
                result.Add(typed);
        return result;
    }
}
