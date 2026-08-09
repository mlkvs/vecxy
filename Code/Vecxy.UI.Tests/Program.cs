using Facebook.Yoga;
using Vecxy.Kernel;
using Vecxy.UI;

StyleInvalidationIsClassified();
IncrementalStyleResolutionSkipsUnchangedBranches();
HitTestBoundsDoNotInvalidateRendering();
FractionalGridTracksFillAvailableWidth();
ShrinkTextStyleAndSizingAreApplied();
WrappedTextKeepsWordsIntact();
KeyedCollectionRetainsAndOrdersNodes();
DetachedSubtreeCanBeMountedAgain();
Console.WriteLine("All Vecxy.UI checks passed.");

static void StyleInvalidationIsClassified()
{
    var config = NewConfig();
    var panel = new UiPanel(config, new Dictionary<string, string>());
    var layout = panel.LayoutVersion;
    var visual = panel.VisualVersion;
    var composite = panel.CompositeVersion;

    panel.Style.BackgroundColor = "#ffffff";
    Check(panel.LayoutVersion == layout, "Paint-only style invalidated layout.");
    Check(panel.VisualVersion > visual, "Paint-only style did not invalidate paint.");

    visual = panel.VisualVersion;
    panel.Style.Opacity = "0.5";
    Check(panel.CompositeVersion > composite, "Opacity did not invalidate composite state.");
    Check(panel.VisualVersion > visual, "First composite mutation must associate render batches.");

    layout = panel.LayoutVersion;
    panel.Style.Width = "120px";
    Check(panel.LayoutVersion > layout, "Sizing style did not invalidate layout.");
    panel.ReleaseLayout();
}

static void IncrementalStyleResolutionSkipsUnchangedBranches()
{
    var config = NewConfig();
    var root = new UiPanel(config, new Dictionary<string, string> { ["class"] = "screen" });
    var changingBranch = new UiPanel(config, new Dictionary<string, string> { ["class"] = "branch" });
    var stableBranch = new UiPanel(config, new Dictionary<string, string> { ["class"] = "branch" });
    var changingChild = new UiText(config, new Dictionary<string, string>(), "changing");
    var stableChild = new UiText(config, new Dictionary<string, string>(), "stable");
    changingBranch.Add(changingChild);
    stableBranch.Add(stableChild);
    root.Add(changingBranch);
    root.Add(stableBranch);
    var sheet = UiStyleSheet.Parse(".screen { color: #111111; } .branch { position: absolute; } .branch text { color: #222222; }");
    UiStyleResolver.Resolve(root, [sheet], forceFullResolution: true);

    var rootComputedVersion = root.ComputedStyleVersion;
    var stableBranchComputedVersion = stableBranch.ComputedStyleVersion;
    var stableChildComputedVersion = stableChild.ComputedStyleVersion;
    changingBranch.Style.Set("left", "12px");
    UiStyleResolver.Resolve(root, [sheet]);

    Check(root.ComputedStyleVersion == rootComputedVersion,
        "A leaf inline-style change recomputed the document root.");
    Check(stableBranch.ComputedStyleVersion == stableBranchComputedVersion &&
          stableChild.ComputedStyleVersion == stableChildComputedVersion,
        "A leaf inline-style change recomputed an unchanged sibling branch.");
    Check(changingBranch.ComputedStyle.Inset.Left == UiLength.Pixels(12),
        "Incremental style resolution did not update the changed branch.");

    var treeStyleVersion = root.StyleVersion;
    changingBranch.Style.Set("left", "12px");
    Check(root.StyleVersion == treeStyleVersion,
        "Writing an unchanged inline style invalidated the document.");
    root.ReleaseLayout();
}

static void HitTestBoundsDoNotInvalidateRendering()
{
    var config = NewConfig();
    var button = new UiButton(config, new Dictionary<string, string>());
    var styleVersion = button.StyleVersion;
    var layoutVersion = button.LayoutVersion;
    var visualVersion = button.VisualVersion;

    button.HitTestBounds = new Rect(20, 30, 40, 50);

    Check(button.StyleVersion == styleVersion &&
          button.LayoutVersion == layoutVersion &&
          button.VisualVersion == visualVersion,
        "Changing a hit-test-only rectangle invalidated rendered UI state.");
    Check(ReferenceEquals(button.HitTest(new System.Numerics.Vector2(30, 40)), button),
        "The hit-test-only rectangle was not used for pointer targeting.");
    button.ReleaseLayout();
}

static void FractionalGridTracksFillAvailableWidth()
{
    var config = NewConfig();
    var root = new UiPanel(config, new Dictionary<string, string> { ["class"] = "grid" });
    for (var index = 0; index < 4; index++)
        root.Add(new UiPanel(config, new Dictionary<string, string>()));
    var sheet = UiStyleSheet.Parse(
        ".grid { display: grid; grid-template-columns: repeat(4, minmax(96px, 1fr)); " +
        "grid-auto-rows: 100px; gap: 12px; }");
    UiStyleResolver.Resolve(root, [sheet], forceFullResolution: true);

    UiLayout.Calculate(root, 500, 100, enableShadows: false);

    Check(MathF.Abs(root.Children[0].Bounds.Width - 116f) < 0.01f,
        "Fractional minmax grid columns did not consume the available width.");
    Check(MathF.Abs(root.Children[^1].Bounds.Right - root.Bounds.Right) < 0.01f,
        "A fractional grid left unused space at the right edge.");
    root.ReleaseLayout();
}

static void ShrinkTextStyleAndSizingAreApplied()
{
    var config = NewConfig();
    var root = new UiPanel(config, new Dictionary<string, string> { ["class"] = "screen" });
    var label = new UiText(config, new Dictionary<string, string>(), "Long label");
    root.Add(label);
    var sheet = UiStyleSheet.Parse(".screen text { white-space: nowrap; text-fit: shrink; min-font-size: 10px; }");
    UiStyleResolver.Resolve(root, [sheet]);

    Check(label.ComputedStyle.WhiteSpace == "nowrap", "Shrink text unexpectedly wraps.");
    Check(label.ComputedStyle.TextFit == "shrink", "Text fit style was not applied.");
    Check(label.ComputedStyle.MinFontSizeLength == UiLength.Pixels(10), "Minimum font size was not parsed.");
    Check(Math.Abs(UiTextFit.Shrink(20, 10, new System.Numerics.Vector2(200, 20), new Vecxy.Kernel.Rect(0, 0, 100, 20)) - 10) < 0.01f,
        "Text was not reduced to the available width.");
    Check(Math.Abs(UiTextFit.Shrink(20, 12, new System.Numerics.Vector2(400, 20), new Vecxy.Kernel.Rect(0, 0, 100, 20)) - 12) < 0.01f,
        "Text fit ignored its minimum font size.");
    root.ReleaseLayout();
}

static void WrappedTextKeepsWordsIntact()
{
    var lines = UiTextWrap.Lines("Нефритовый лоток", 8, line => line.Length);

    Check(lines.SequenceEqual(["Нефритовый", "лоток"]),
        "Word wrapping split a lexical word into character fragments.");
}

static void KeyedCollectionRetainsAndOrdersNodes()
{
    var config = NewConfig();
    var parent = new UiPanel(config, new Dictionary<string, string>());
    var created = 0;
    var collection = new UiKeyedCollection<int, int, UiPanel>(
        parent,
        value =>
        {
            created++;
            return new UiPanel(config, new Dictionary<string, string> { ["data-key"] = value.ToString() });
        },
        view => view,
        (view, value, _) => view.TextContent = value.ToString());

    collection.Update([1, 2, 3], value => value);
    var retained = parent.Children[1];
    collection.Update([3, 2, 4], value => value);

    Check(created == 4, "Keyed update recreated retained nodes.");
    Check(ReferenceEquals(parent.Children[1], retained), "Keyed update lost node identity.");
    Check(parent.Children.Select(node => node.Attributes["data-key"]).SequenceEqual(["3", "2", "4"]),
        "Keyed update produced an incorrect child order.");
    Check(collection.Count == 3, "Keyed update retained a removed node.");
    parent.ReleaseLayout();
}

static void DetachedSubtreeCanBeMountedAgain()
{
    var config = NewConfig();
    var parent = new UiPanel(config, new Dictionary<string, string>());
    var window = new UiPanel(config, new Dictionary<string, string>());
    var content = new UiText(config, new Dictionary<string, string>(), "retained");
    window.Add(content);
    parent.Add(window);

    Check(window.DetachFromParent(), "Mounted window could not be detached.");
    Check(window.Parent is null && window.Children.Count == 1,
        "Detaching a window destroyed its retained subtree.");
    parent.Add(window);
    Check(ReferenceEquals(parent.Children[0], window) && ReferenceEquals(window.Children[0], content),
        "Detached window could not be mounted with the same node identity.");
    parent.ReleaseLayout();
}

static Config NewConfig()
{
    var config = new Config();
    config.SetUseWebDefaults(false);
    config.SetPointScaleFactor(1.0f);
    return config;
}

static void Check(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
