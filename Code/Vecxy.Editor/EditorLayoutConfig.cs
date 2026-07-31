using Vecxy.Assets;

namespace Vecxy.Editor;

public sealed class EditorLayoutConfig : IYamlConfig
{
    private static readonly HashSet<string> DockAreas =
    [
        "left",
        "center",
        "right",
        "bottom",
        "top"
    ];

    private static readonly string[] RequiredWindows =
    [
        "GameView",
        "Hierarchy",
        "Inspector",
        "Configs",
        "Rendering Statistics",
        "Render Settings",
        "Debug Console"
    ];

    public EditorLayoutSplits Splits { get; set; } = new();
    public Dictionary<string, EditorWindowLayout> Windows { get; set; } =
        new(StringComparer.Ordinal);
    public Dictionary<string, string> ActiveTabs { get; set; } =
        new(StringComparer.Ordinal);

    public EditorWindowLayout GetWindow(string name) =>
        Windows.TryGetValue(name, out var layout)
            ? layout
            : throw new InvalidDataException(
                $"Editor layout does not contain window '{name}'.");

    public bool IsActiveTab(string dockArea, string windowName) =>
        ActiveTabs.TryGetValue(dockArea, out var activeWindow) &&
        string.Equals(activeWindow, windowName, StringComparison.Ordinal);

    public void Validate()
    {
        Splits.Validate();

        foreach (var windowName in RequiredWindows)
        {
            if (!Windows.TryGetValue(windowName, out var window))
            {
                throw new InvalidDataException(
                    $"Editor layout does not contain window '{windowName}'.");
            }

            if (!DockAreas.Contains(window.Dock))
            {
                throw new InvalidDataException(
                    $"Editor layout assigns unknown dock area '{window.Dock}' to '{windowName}'.");
            }
        }

        foreach (var (dockArea, windowName) in ActiveTabs)
        {
            if (!DockAreas.Contains(dockArea))
            {
                throw new InvalidDataException(
                    $"Editor layout contains unknown active tab area '{dockArea}'.");
            }

            if (!Windows.TryGetValue(windowName, out var window) ||
                !string.Equals(window.Dock, dockArea, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Editor layout selects '{windowName}' for dock area '{dockArea}', but the window is not assigned there.");
            }
        }
    }

    public static EditorLayoutConfig CreateDefault() =>
        new()
        {
            Windows = new Dictionary<string, EditorWindowLayout>(StringComparer.Ordinal)
            {
                ["GameView"] = new() { Dock = "center", Visible = true },
                ["Hierarchy"] = new() { Dock = "left", Visible = true },
                ["Inspector"] = new() { Dock = "right", Visible = true },
                ["Configs"] = new() { Dock = "bottom", Visible = true },
                ["Rendering Statistics"] = new() { Dock = "top", Visible = false },
                ["Render Settings"] = new() { Dock = "right", Visible = true },
                ["Debug Console"] = new() { Dock = "bottom", Visible = true }
            },
            ActiveTabs = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["left"] = "Hierarchy",
                ["center"] = "GameView",
                ["right"] = "Inspector",
                ["bottom"] = "Debug Console",
                ["top"] = "Rendering Statistics"
            }
        };
}

public sealed class EditorLayoutSplits
{
    public float LeftWidth { get; set; } = 0.19f;
    public float RightWidth { get; set; } = 0.28f;
    public float BottomHeight { get; set; } = 0.20f;
    public float TopHeight { get; set; } = 0.12f;

    public void Validate()
    {
        ValidateRatio(LeftWidth, nameof(LeftWidth));
        ValidateRatio(RightWidth, nameof(RightWidth));
        ValidateRatio(BottomHeight, nameof(BottomHeight));
        ValidateRatio(TopHeight, nameof(TopHeight));
    }

    private static void ValidateRatio(
        float value,
        string name)
    {
        if (value <= 0.0f || value >= 0.9f)
        {
            throw new InvalidDataException(
                $"Editor layout has invalid split ratio '{name}': {value}.");
        }
    }
}

public sealed class EditorWindowLayout
{
    public string Dock { get; set; } = "center";
    public bool Visible { get; set; } = true;
}
