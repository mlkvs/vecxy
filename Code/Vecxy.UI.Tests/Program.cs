using Facebook.Yoga;
using Vecxy.UI;

StyleInvalidationIsClassified();
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
