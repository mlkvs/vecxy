using Vecxy.Prototypes;
using Vecxy.Rendering;
using Vecxy.UI;

namespace Vecxy.Isometric.Editor;

public sealed class EditorUiFactory(IPrototypes prototypes)
{
    private UiDocument? _document;

    public void Bind(UiDocument document) => _document = document;

    public UiPanel Panel(UiElement parent, string classes, string? id = null) =>
        prototypes.Instantiate<UiPanel>(Context(parent), new UiPanel.Prototype.Options
        {
            Id = id,
            Classes = Classes(classes)
        });

    public UiText Text(UiElement parent, string value, string classes = "") =>
        prototypes.Instantiate<UiText>(Context(parent), new UiText.Prototype.Options
        {
            Text = value,
            Classes = Classes(classes)
        });

    public UiButton Button(UiElement parent, string label, string classes = "") =>
        prototypes.Instantiate<UiButton>(Context(parent), new UiButton.Prototype.Options
        {
            Text = label,
            Classes = Classes(classes)
        });

    public UiImage Image(UiElement parent, Texture texture, string classes = "")
    {
        var image = prototypes.Instantiate<UiImage>(Context(parent), new UiImage.Prototype.Options
        {
            Classes = Classes(classes)
        });
        image.Texture = texture;
        return image;
    }

    public UiInputField Input(UiElement parent, string classes = "", string? id = null) =>
        prototypes.Instantiate<UiInputField>(Context(parent), new UiInputField.Prototype.Options
        {
            Id = id,
            Classes = Classes(classes)
        });

    public UiPanel Field(UiElement parent, string label, out UiInputField input)
    {
        var row = Panel(parent, "editor-field");
        Text(row, label, "editor-field-label");
        input = Input(row, "editor-input");
        return row;
    }

    private UiPrototypeContext Context(UiElement parent) => new()
    {
        Document = _document ?? throw new InvalidOperationException("UI factory is not bound."),
        Parent = parent
    };

    private static List<string> Classes(string value) =>
        value.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
}
