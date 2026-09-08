using Vecxy.Prototypes;

namespace Vecxy.UI;

public class UiElementPrototypeOptions
{
    public string? Id { get; set; }
    public string Text { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public bool Visible { get; set; } = true;
    public Dictionary<string, string> Attributes { get; set; } = [];
    public Dictionary<string, string> Styles { get; set; } = [];
    public List<string> Classes { get; set; } = [];
    public List<PrototypeReference> Children { get; set; } = [];
}

internal static class UiPrototypeConfiguration
{
    public static void Apply(UiElement target, UiElementPrototypeOptions options)
    {
        foreach (var (name, value) in options.Attributes) target.SetAttribute(name, value);
        if (!string.IsNullOrWhiteSpace(options.Id)) target.SetAttribute("id", options.Id);
        foreach (var className in options.Classes) target.AddClass(className);
        foreach (var (name, value) in options.Styles) target.SetStyle(name, value);
        target.TextContent = options.Text;
        target.IsEnabled = options.Enabled;
        target.IsVisible = options.Visible;
    }
}

public sealed partial class UiPanel
{
    public sealed class Prototype : APrototype<UiPanel, Prototype.Options>
    {
        public sealed class Options : UiElementPrototypeOptions;
        protected override UiPanel Instantiate(IPrototypeContext context) =>
            RequireContext<UiPrototypeContext>(context).Document.CreatePanel();
        protected override void Configure(UiPanel target, Options options) => UiPrototypeConfiguration.Apply(target, options);
    }
}

public sealed partial class UiText
{
    public sealed class Prototype : APrototype<UiText, Prototype.Options>
    {
        public sealed class Options : UiElementPrototypeOptions;
        protected override UiText Instantiate(IPrototypeContext context) =>
            RequireContext<UiPrototypeContext>(context).Document.CreateText();
        protected override void Configure(UiText target, Options options) => UiPrototypeConfiguration.Apply(target, options);
    }
}

public sealed partial class UiButton
{
    public sealed class Prototype : APrototype<UiButton, Prototype.Options>
    {
        public sealed class Options : UiElementPrototypeOptions;
        protected override UiButton Instantiate(IPrototypeContext context) =>
            RequireContext<UiPrototypeContext>(context).Document.CreateButton();
        protected override void Configure(UiButton target, Options options) => UiPrototypeConfiguration.Apply(target, options);
    }
}

public sealed partial class UiImage
{
    public sealed class Prototype : APrototype<UiImage, Prototype.Options>
    {
        public sealed class Options : UiElementPrototypeOptions
        {
            public string Source { get; set; } = string.Empty;
            public string Sprite { get; set; } = string.Empty;
        }
        protected override UiImage Instantiate(IPrototypeContext context) =>
            RequireContext<UiPrototypeContext>(context).Document.CreateImage(string.Empty);
        protected override void Configure(UiImage target, Options options)
        {
            UiPrototypeConfiguration.Apply(target, options);
            target.Source = options.Source;
            if (options.Sprite.Length > 0) target.Sprite = options.Sprite;
        }
    }
}

public sealed partial class UiProgress
{
    public sealed class Prototype : APrototype<UiProgress, Prototype.Options>
    {
        public sealed class Options : UiElementPrototypeOptions { public float Progress { get; set; } }
        protected override UiProgress Instantiate(IPrototypeContext context) =>
            (UiProgress)RequireContext<UiPrototypeContext>(context).Document.CreateElement("progress");
        protected override void Configure(UiProgress target, Options options)
        { UiPrototypeConfiguration.Apply(target, options); target.Progress = options.Progress; }
    }
}

public sealed partial class UiRadialProgress
{
    public sealed class Prototype : APrototype<UiRadialProgress, Prototype.Options>
    {
        public sealed class Options : UiElementPrototypeOptions { public float Progress { get; set; } }
        protected override UiRadialProgress Instantiate(IPrototypeContext context) =>
            (UiRadialProgress)RequireContext<UiPrototypeContext>(context).Document.CreateElement("radial-progress");
        protected override void Configure(UiRadialProgress target, Options options)
        { UiPrototypeConfiguration.Apply(target, options); target.Progress = options.Progress; }
    }
}
