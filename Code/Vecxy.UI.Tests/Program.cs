using Facebook.Yoga;
using Vecxy.Input;
using Vecxy.Kernel;
using Vecxy.UI;

StyleInvalidationIsClassified();
PseudoStateInvalidationIsClassified();
ClassInvalidationIsClassified();
CompositeOnlyAnimationDoesNotInvalidatePaint();
SameSizeDynamicTextDoesNotInvalidateLayout();
HiddenStateDoesNotInvalidateStyle();
VisibilityUsesCompositeFastPath();
OpacityTransitionUsesCompositeFastPath();
OpacityTransitionInterpolatesInBothDirections();
IncrementalStyleResolutionSkipsUnchangedBranches();
AncestorSelectorInvalidatesOnlyWhenItsMatchChanges();
HitTestBoundsDoNotInvalidateRendering();
FractionalGridTracksFillAvailableWidth();
ShrinkTextStyleAndSizingAreApplied();
WrappedTextKeepsWordsIntact();
KeyedCollectionRetainsAndOrdersNodes();
DetachedSubtreeCanBeMountedAgain();
ShortTouchIsNotCollapsedIntoOneFrame();
TextEditingCoreSupportsInsertReplaceAndDelete();
TextEditingCoreSupportsNavigationAndSelection();
TextEditingCoreRespectsUnicodeAndMaxLength();
InputFieldSupportsClipboardAndPasswordSafety();
Console.WriteLine("All Vecxy.UI checks passed.");

static void TextEditingCoreSupportsInsertReplaceAndDelete()
{
    var edit = new TextEditingState();
    edit.SetText("Helo", true);
    edit.Move(2);
    Check(edit.Insert("l") && edit.Text == "Hello" && edit.CaretIndex == 3, "Insert operation failed.");
    edit.SetText("Hello world", true);
    edit.Select(6, 5);
    Check(edit.Insert("Vecxy") && edit.Text == "Hello Vecxy" && edit.CaretIndex == 11, "Selection replacement failed.");
    Check(edit.Backspace() && edit.Text == "Hello Vecx" && edit.CaretIndex == 10, "Backspace failed.");
    edit.SetText("Hello", true);
    edit.Move(4);
    Check(edit.Delete() && edit.Text == "Hell" && edit.CaretIndex == 4, "Forward delete failed.");
    edit.SetText("Hello world", true);
    edit.SelectAll();
    edit.Insert("Test");
    Check(edit.Text == "Test" && edit.SelectionAnchor == 4 && edit.SelectionCaret == 4, "Select-all replacement failed.");
}

static void TextEditingCoreSupportsNavigationAndSelection()
{
    var edit = new TextEditingState();
    edit.SetText("Hello beautiful world", true);
    edit.MoveLeft(word: true);
    Check(edit.CaretIndex == 16, "Word-left navigation failed.");
    edit.MoveLeft(extend: true, word: true);
    Check(edit.SelectionStart == 6 && edit.SelectionLength == 10 && edit.SelectionAnchor == 16 && edit.SelectionCaret == 6,
        "Directional word selection failed.");
    edit.MoveRight();
    Check(!edit.HasSelection && edit.CaretIndex == 16, "Right arrow did not collapse selection to its right edge.");
    edit.Select(2, 5);
    edit.MoveLeft();
    Check(edit.CaretIndex == 2 && !edit.HasSelection, "Left arrow did not collapse selection to its left edge.");
    edit.Move(0);
    edit.MoveRight(word: true);
    Check(edit.CaretIndex == 6, "Word-right navigation did not reach the next word.");
}

static void TextEditingCoreRespectsUnicodeAndMaxLength()
{
    var edit = new TextEditingState { MaxLength = 5 };
    edit.Insert("Hello World");
    Check(edit.Text == "Hello", "MaxLength was not applied to paste/insert.");
    edit.MaxLength = 0;
    edit.SetText("A😀e\u0301", true);
    edit.Select(4, 0);
    Check(edit.CaretIndex == 3, "Selection was not clamped to a grapheme boundary.");
    edit.Collapse(edit.Text.Length);
    edit.Backspace();
    Check(edit.Text == "A😀" && edit.CaretIndex == 3, "Combining sequence was split by backspace.");
    edit.Backspace();
    Check(edit.Text == "A" && edit.CaretIndex == 1, "Surrogate pair was split by backspace.");
    edit.Text = string.Empty;
    Check(edit.CaretIndex == 0 && edit.SelectionAnchor == 0, "Programmatic shortening did not clamp selection.");
}

static void InputFieldSupportsClipboardAndPasswordSafety()
{
    var field = new UiInputField(NewConfig(), new Dictionary<string, string>());
    var clipboard = new TestClipboard { Text = "world" };
    var changed = string.Empty;
    field.TextChanged += value => changed = value;
    field.Text = "Hello ";
    Check(changed == "Hello ", "Programmatic Text did not raise TextChanged.");
    field.MoveCaretToEnd();
    field.HandleKey(Vecxy.Assets.EKeyboardKey.V, false, true, clipboard);
    Check(field.Text == "Hello world", "Paste failed.");
    field.Select(6, 5);
    field.HandleKey(Vecxy.Assets.EKeyboardKey.X, false, true, clipboard);
    Check(field.Text == "Hello " && clipboard.Text == "world", "Cut failed.");
    field.Text = "secret";
    field.InputType = TextInputType.Password;
    field.SelectAll();
    clipboard.Text = "unchanged";
    field.HandleKey(Vecxy.Assets.EKeyboardKey.C, false, true, clipboard);
    Check(clipboard.Text == "unchanged" && field.Text == "secret", "Password copy was not blocked.");
    field.ReadOnly = true;
    field.HandleTextInput("x");
    Check(field.Text == "secret", "Read-only input accepted text.");
    field.ReleaseLayout();
}

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

static void PseudoStateInvalidationIsClassified()
{
    var config = NewConfig();
    var button = new UiButton(config, new Dictionary<string, string> { ["class"] = "action" });
    var paintOnly = UiStyleSheet.Parse(".action { width: 100px; opacity: 1; } .action:active { opacity: .7; }");
    UiStyleResolver.Resolve(button, [paintOnly], forceFullResolution: true);
    var layout = button.LayoutVersion;
    var visual = button.VisualVersion;

    button.IsActive = true;
    UiStyleResolver.Resolve(button, [paintOnly]);
    Check(button.LayoutVersion == layout, "A paint-only active state invalidated layout.");
    Check(button.VisualVersion > visual, "An active opacity change did not invalidate paint.");

    layout = button.LayoutVersion;
    visual = button.VisualVersion;
    button.IsFocused = true;
    UiStyleResolver.Resolve(button, [paintOnly]);
    Check(button.LayoutVersion == layout && button.VisualVersion == visual,
        "An unused focus state invalidated rendering.");

    var layoutPseudo = UiStyleSheet.Parse(".action { width: 100px; } .action:active { width: 90px; }");
    button.IsActive = false;
    UiStyleResolver.Resolve(button, [layoutPseudo], forceFullResolution: true);
    layout = button.LayoutVersion;
    button.IsActive = true;
    UiStyleResolver.Resolve(button, [layoutPseudo]);
    Check(button.LayoutVersion > layout, "A layout-changing active state did not invalidate layout.");
    button.ReleaseLayout();
}

static void ClassInvalidationIsClassified()
{
    var config = NewConfig();
    var panel = new UiPanel(config, new Dictionary<string, string> { ["class"] = "card" });
    var sheet = UiStyleSheet.Parse(".card { width: 100px; } .card.selected { background-color: white; }");
    UiStyleResolver.Resolve(panel, [sheet], forceFullResolution: true);
    var layout = panel.LayoutVersion;

    panel.AddClass("selected");
    UiStyleResolver.Resolve(panel, [sheet]);
    Check(panel.LayoutVersion == layout, "A paint-only class change invalidated layout.");
    Check(panel.ComputedStyle.BackgroundColor == System.Numerics.Vector4.One,
        "A class change was not resolved.");
    panel.ReleaseLayout();
}

static void CompositeOnlyAnimationDoesNotInvalidatePaint()
{
    var config = NewConfig();
    var panel = new UiPanel(config, new Dictionary<string, string>
    {
        ["class"] = "tick",
        ["animation-trigger"] = "manual"
    });
    var sheet = UiStyleSheet.Parse("""
        .tick {
            color: #9ff2c9;
            background-color: #10251bb8;
            opacity: 0;
            animation: rise 1.55s ease-out;
        }
        @keyframes rise {
            from { opacity: 0; transform: translate(0, 18px) scale(0.90); }
            14% { opacity: 0.68; transform: translate(0, 0) scale(1.0); }
            68% { opacity: 0.54; transform: translate(0, -34px) scale(1.02); }
            to { opacity: 0; transform: translate(0, -68px) scale(0.96); }
        }
        """);
    UiStyleResolver.Resolve(panel, [sheet], forceFullResolution: true);
    panel.AnimationRuntime.Update(panel, sheet.Keyframes, 0f, 176f, 43f);
    panel.AnimationRuntime.Restart(panel);

    for (var frame = 0; frame < 4; frame++)
        panel.AnimationRuntime.Update(panel, sheet.Keyframes, 1f / 60f, 176f, 43f);
    var allocatedBeforeAnimation = GC.GetAllocatedBytesForCurrentThread();
    for (var frame = 0; frame < 20; frame++)
        panel.AnimationRuntime.Update(panel, sheet.Keyframes, 1f / 60f, 176f, 43f);
    var animationAllocations = GC.GetAllocatedBytesForCurrentThread() - allocatedBeforeAnimation;
    Check(animationAllocations == 0,
        $"A steady CSS animation allocated {animationAllocations / 20.0:F1} bytes per update.");
    panel.AnimationRuntime.Restart(panel);

    var compositeChanged = false;
    for (var frame = 0; frame < 100; frame++)
    {
        var change = panel.AnimationRuntime.Update(panel, sheet.Keyframes, 1f / 60f, 176f, 43f);
        Check((change & UiAnimationChange.Paint) == 0,
            "An opacity/transform-only animation invalidated paint.");
        compositeChanged |= (change & UiAnimationChange.Composite) != 0;
    }

    Check(compositeChanged, "The composite-only animation did not update its composite state.");
    panel.ReleaseLayout();
}

static void SameSizeDynamicTextDoesNotInvalidateLayout()
{
    var config = NewConfig();
    var root = new UiPanel(config, new Dictionary<string, string> { ["class"] = "screen" });
    var text = new UiText(config, new Dictionary<string, string>(), "1000");
    root.Add(text);
    UiStyleResolver.Resolve(root, [UiStyleSheet.Parse(
        ".screen { width: 320px; height: 200px; align-items: flex-start; }")], true);
    UiLayout.Calculate(root, 320, 200);
    var layout = root.LayoutVersion;

    text.Value = "1001";
    Check(root.LayoutVersion == layout,
        "Equal-size dynamic text invalidated the parent layout.");

    text.Value = "10010";
    Check(root.LayoutVersion > layout,
        "A dynamic text size change did not invalidate layout.");
    root.ReleaseLayout();
}

static void HiddenStateDoesNotInvalidateStyle()
{
    var config = NewConfig();
    var root = new UiPanel(config, new Dictionary<string, string>());
    var window = new UiPanel(config, new Dictionary<string, string> { ["hidden"] = "true" });
    root.Add(window);
    UiStyleResolver.Resolve(root, [UiStyleSheet.Parse("panel { width: 100px; height: 100px; }")], true);
    var styleVersion = root.StyleVersion;
    var layoutVersion = root.LayoutVersion;

    window.IsVisible = true;

    Check(root.StyleVersion == styleVersion,
        "Changing retained visibility invalidated the CSS cascade.");
    Check(root.LayoutVersion > layoutVersion,
        "Changing retained visibility did not invalidate layout.");
    Check(window.ComputedStyle.Display == "flex",
        "The hidden state leaked into the computed CSS display property.");
    root.ReleaseLayout();
}

static void VisibilityUsesCompositeFastPath()
{
    var config = NewConfig();
    var panel = new UiPanel(config, new Dictionary<string, string>());
    UiStyleResolver.Resolve(panel, [], forceFullResolution: true);
    var layout = panel.LayoutVersion;
    var visual = panel.VisualVersion;
    var composite = panel.CompositeVersion;
    var hitTest = panel.HitTestVersion;

    panel.Style.Set("visibility", "hidden");
    UiStyleResolver.Resolve(panel, []);

    Check(panel.LayoutVersion == layout, "Visibility invalidated layout.");
    Check(panel.VisualVersion == visual, "Visibility invalidated retained geometry.");
    Check(panel.CompositeVersion > composite, "Visibility did not invalidate composite state.");
    Check(panel.HitTestVersion > hitTest, "Visibility did not invalidate hit testing.");
    panel.ReleaseLayout();
}

static void OpacityTransitionUsesCompositeFastPath()
{
    var config = NewConfig();
    var panel = new UiPanel(config, new Dictionary<string, string> { ["class"] = "fade-surface" });
    var sheet = UiStyleSheet.Parse(".fade-surface { opacity: 0; transition: opacity 0.16s ease-in-out; } .fade-surface.open { opacity: 1; }");
    UiStyleResolver.Resolve(panel, [sheet], forceFullResolution: true);
    var layout = panel.LayoutVersion;
    var visual = panel.VisualVersion;
    var composite = panel.CompositeVersion;

    panel.AddClass("open");
    UiStyleResolver.Resolve(panel, [sheet]);

    Check(panel.LayoutVersion == layout, "Opacity transition invalidated layout.");
    Check(panel.VisualVersion == visual, "Opacity transition invalidated retained geometry.");
    Check(panel.CompositeVersion > composite, "Opacity transition did not invalidate composite state.");
    panel.ReleaseLayout();
}

static void OpacityTransitionInterpolatesInBothDirections()
{
    var config = NewConfig();
    var panel = new UiPanel(config, new Dictionary<string, string> { ["class"] = "fade-surface" });
    var sheet = UiStyleSheet.Parse(".fade-surface { opacity: 0; transition: opacity 0.16s ease-in-out; } .fade-surface.open { opacity: 1; }");
    panel.Style.Set("visibility", "hidden");
    UiStyleResolver.Resolve(panel, [sheet], forceFullResolution: true);
    panel.AnimationRuntime.Update(panel, sheet.Keyframes, 0.0f, 320.0f, 180.0f);

    var completedOpacityTransitions = 0;
    panel.TransitionEnded += (_, transition) =>
    {
        if (transition.Property.Equals("opacity", StringComparison.OrdinalIgnoreCase))
            completedOpacityTransitions++;
    };

    panel.Style.Set("visibility", "visible");
    panel.AddClass("open");
    UiStyleResolver.Resolve(panel, [sheet]);
    panel.AnimationRuntime.Update(panel, sheet.Keyframes, 0.08f, 320.0f, 180.0f);
    Check(panel.RenderOpacity > 0.0f && panel.RenderOpacity < 1.0f,
        "The opening opacity transition did not produce an intermediate frame.");
    panel.AnimationRuntime.Update(panel, sheet.Keyframes, 0.08f, 320.0f, 180.0f);
    Check(Math.Abs(panel.RenderOpacity - 1.0f) < 0.001f,
        "The opening opacity transition did not reach full opacity.");
    Check(completedOpacityTransitions == 1,
        "The opening opacity transition did not report completion exactly once.");

    panel.RemoveClass("open");
    UiStyleResolver.Resolve(panel, [sheet]);
    panel.AnimationRuntime.Update(panel, sheet.Keyframes, 0.08f, 320.0f, 180.0f);
    Check(panel.RenderOpacity > 0.0f && panel.RenderOpacity < 1.0f,
        "The closing opacity transition did not produce an intermediate frame.");
    panel.AnimationRuntime.Update(panel, sheet.Keyframes, 0.08f, 320.0f, 180.0f);
    Check(panel.RenderOpacity < 0.001f,
        "The closing opacity transition did not reach zero opacity.");
    Check(completedOpacityTransitions == 2,
        "The closing opacity transition did not report completion exactly once.");
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

static void AncestorSelectorInvalidatesOnlyWhenItsMatchChanges()
{
    var config = NewConfig();
    var root = new UiPanel(config, new Dictionary<string, string>());
    var child = new UiPanel(config, new Dictionary<string, string> { ["class"] = "label" });
    root.Add(child);
    var sheet = UiStyleSheet.Parse(".enabled .label { background-color: white; }");
    UiStyleResolver.Resolve(root, [sheet], true);

    root.AddClass("enabled");
    UiStyleResolver.Resolve(root, [sheet]);
    Check(child.ComputedStyle.BackgroundColor == System.Numerics.Vector4.One,
        "Adding an ancestor selector class did not update its descendant.");

    root.RemoveClass("enabled");
    UiStyleResolver.Resolve(root, [sheet]);
    Check(child.ComputedStyle.BackgroundColor == System.Numerics.Vector4.Zero,
        "Removing an ancestor selector class left stale descendant styling.");
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

static void ShortTouchIsNotCollapsedIntoOneFrame()
{
    using var window = new TestWindow();
    var input = new InputModule(window, new TestInputCaptureState());
    input.OnInitialize();
    window.EmitTouch(new IWindow.TouchEvent(7, 40, 50, ETouchPhase.Began, IsPrimary: true));
    window.EmitTouch(new IWindow.TouchEvent(7, 40, 50, ETouchPhase.Ended, IsPrimary: true));

    input.OnUpdate(1f / 60f);
    Check(input.IsPrimaryPointerPressed, "A short touch lost its Began frame.");
    Check(input.Touches.Count == 1 && input.Touches[0].Phase == ETouchPhase.Began,
        "Began was overwritten by Ended in the same input snapshot.");

    input.OnUpdate(1f / 60f);
    Check(!input.IsPrimaryPointerPressed, "A deferred short touch did not end.");
    Check(input.Touches.Count == 1 && input.Touches[0].Phase == ETouchPhase.Ended,
        "Ended was not delivered on the frame after Began.");
    input.Dispose();
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

sealed class TestInputCaptureState : IInputCaptureState
{
    public bool SuppressKeyboard { get; set; }
    public bool SuppressMouse { get; set; }
}

sealed class TestClipboard : IClipboard
{
    public string? Text { get; set; }
    public string? GetText() => Text;
    public void SetText(string text) => Text = text;
}

sealed class TestWindow : IWindow, IClipboard, ITextInputSource
{
    public int Width => 100;
    public int Height => 100;
    public int ClientWidth => 100;
    public int ClientHeight => 100;
    public bool IsRunning { get; private set; }
    public bool IsFullscreen => false;
    public bool IsCursorCaptured => false;
    public event Action<int, int>? Resized;
    public event Action<IWindow.KeyEvent>? KeyChanged;
    public event Action<TextInputEvent>? TextInput;
    public event Action? CompositionStarted;
    public event Action<TextCompositionEvent>? CompositionUpdated;
    public event Action<TextInputEvent>? CompositionCommitted;
    public event Action? CompositionEnded;
    public event Action<IWindow.MouseButtonEvent>? MouseButtonChanged;
    public event Action<IWindow.MouseMoveEvent>? MouseMoved;
    public event Action<IWindow.MouseWheelEvent>? MouseWheelChanged;
    public event Action<IWindow.TouchEvent>? TouchChanged;

    public void EmitTouch(IWindow.TouchEvent eventData) => TouchChanged?.Invoke(eventData);
    public void Initialize() { IsRunning = true; Resized?.Invoke(Width, Height); }
    public void PollEvents() { }
    public void MakeCurrent() { }
    public void SuppressNextSwap() { }
    public void SwapBuffers() { }
    public void Close() => IsRunning = false;
    public void ToggleFullscreen() { }
    public void SetCursorCaptured(bool captured) { }
    public System.Numerics.Vector2 ClientToFramebuffer(System.Numerics.Vector2 position) => position;
    public System.Numerics.Vector2 FramebufferToClient(System.Numerics.Vector2 position) => position;
    public nint GetProcAddress(string name) => 0;
    public string? GetText() => null;
    public void SetText(string text) { }
    public void Dispose() => IsRunning = false;
}
