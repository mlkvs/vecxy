using System.Numerics;
using Facebook.Yoga;
using Xunit;

namespace Vecxy.UI.Tests;

public sealed class UiAdvancedFeaturesTests
{
    [Theory]
    [InlineData("120ui", EUiLengthUnit.Ui, 120)]
    [InlineData("8vh", EUiLengthUnit.ViewportHeight, 8)]
    [InlineData("20vw", EUiLengthUnit.ViewportWidth, 20)]
    [InlineData("20%", EUiLengthUnit.Percent, 20)]
    public void Lengths_ParseResponsiveUnits(string source, EUiLengthUnit unit, float value)
    {
        Assert.True(UiLength.TryParse(source, out var length));
        Assert.Equal(unit, length.Unit);
        Assert.Equal(value, length.Value);
    }

    [Theory]
    [InlineData("fit", 2.5f, 400, 200)]
    [InlineData("fill", 5.0f, 200, 100)]
    [InlineData("width", 5.0f, 200, 100)]
    [InlineData("height", 2.5f, 400, 200)]
    [InlineData("pixel-perfect", 2.0f, 500, 250)]
    public void Canvas_UsesConfiguredScaleMode(
        string mode,
        float scale,
        int width,
        int height)
    {
        var yoga = new Config();
        var root = Element(yoga, "ui");
        var settings = new UiConfig
        {
            ReferenceResolution = [200, 200],
            ScaleMode = mode
        };
        try
        {
            var canvas = UiCanvas.Resolve(root, 1000, 500, settings);
            Assert.Equal(scale, canvas.Scale);
            Assert.Equal(width, canvas.Width);
            Assert.Equal(height, canvas.Height);
        }
        finally
        {
            root.ReleaseLayout();
        }
    }

    [Fact]
    public void Grid_LaysOutFractionTracksAndSpan()
    {
        var yoga = new Config();
        var root = Element(
            yoga,
            "ui",
            ("style", "display: grid; grid-template-columns: repeat(2, 1fr); grid-template-rows: 50ui 50ui; gap: 10ui;"));
        var first = Element(yoga, "panel");
        var second = Element(yoga, "panel");
        var spanning = Element(yoga, "panel", ("style", "grid-column: 1 / span 2;"));
        root.Add(first);
        root.Add(second);
        root.Add(spanning);
        try
        {
            UiStyleResolver.Resolve(root, []);
            UiLayout.Calculate(root, 410, 110);
            Assert.Equal(200, first.Bounds.Width, 1);
            Assert.Equal(210, second.Bounds.X, 1);
            Assert.Equal(410, spanning.Bounds.Width, 1);
            Assert.Equal(60, spanning.Bounds.Y, 1);
        }
        finally
        {
            root.ReleaseLayout();
        }
    }

    [Fact]
    public void Scroller_ClampsOffsetToContentExtent()
    {
        var yoga = new Config();
        var root = Element(yoga, "ui", ("style", "height: 100ui; overflow-y: auto;"));
        root.Add(Element(yoga, "panel", ("style", "height: 300ui; min-height: 300ui;")));
        try
        {
            UiStyleResolver.Resolve(root, []);
            UiLayout.Calculate(root, 200, 100);
            Assert.True(root.CanScrollVertically);
            root.ScrollTo(new Vector2(0, 500));
            Assert.Equal(200, root.ScrollOffset.Y, 1);
        }
        finally
        {
            root.ReleaseLayout();
        }
    }

    [Fact]
    public void GridOverflow_CreatesScrollableContentExtent()
    {
        var yoga = new Config();
        var root = Element(
            yoga,
            "ui",
            ("style", "display: grid; width: 200ui; height: 100ui; grid-template-columns: repeat(2, 1fr); grid-auto-rows: 60ui; gap: 4ui; overflow-y: scroll;"));
        for (var index = 0; index < 6; index++)
            root.Add(Element(yoga, "panel"));
        try
        {
            UiStyleResolver.Resolve(root, []);
            UiLayout.Calculate(root, 200, 100);
            Assert.True(root.CanScrollVertically);
            Assert.True(root.ScrollExtent.Y >= 188.0f);
            root.ScrollBy(new Vector2(0, 500));
            Assert.True(root.ScrollOffset.Y >= 88.0f);
        }
        finally
        {
            root.ReleaseLayout();
        }
    }

    [Fact]
    public void Selectors_MatchInteractiveStatesAndStructure()
    {
        var yoga = new Config();
        var root = Element(yoga, "ui");
        var input = Element(yoga, "input", ("checked", "true"));
        root.Add(input);
        var sheet = UiStyleSheet.Parse("input:checked:first-child:last-child { opacity: 0.25; }");
        try
        {
            UiStyleResolver.Resolve(root, [sheet]);
            Assert.Equal(0.25f, input.ComputedStyle.Opacity);
        }
        finally
        {
            root.ReleaseLayout();
        }
    }

    [Fact]
    public void Transform_AffectsHitTesting()
    {
        var yoga = new Config();
        var root = Element(
            yoga,
            "panel",
            ("action", "test"),
            ("style", "width: 100ui; height: 100ui; transform: translateX(50ui);"));
        try
        {
            UiStyleResolver.Resolve(root, []);
            UiLayout.Calculate(root, 300, 200);
            root.AnimationRuntime.Update(root, new Dictionary<string, UiKeyframes>(), 0, 300, 200);
            Assert.Null(root.HitTest(new Vector2(25, 50)));
            Assert.Same(root, root.HitTest(new Vector2(125, 50)));
        }
        finally
        {
            root.ReleaseLayout();
        }
    }

    [Fact]
    public void Keyframes_AnimateAndRaiseCompletionEvent()
    {
        var yoga = new Config();
        var root = Element(yoga, "panel", ("class", "toast"));
        var sheet = UiStyleSheet.Parse("""
            @keyframes toast-enter {
                from { opacity: 0; transform: translateY(12ui); }
                to { opacity: 1; transform: translateY(0); }
            }
            .toast { animation: toast-enter 0.2s ease-out forwards; }
            """);
        var ended = 0;
        root.AnimationEnded += (_, data) =>
        {
            Assert.Equal("toast-enter", data.Name);
            ended++;
        };
        try
        {
            UiStyleResolver.Resolve(root, [sheet]);
            root.AnimationRuntime.Update(root, sheet.Keyframes, 0.0f, 400, 200);
            root.AnimationRuntime.Update(root, sheet.Keyframes, 0.1f, 400, 200);
            Assert.InRange(root.RenderOpacity, 0.1f, 0.99f);
            root.AnimationRuntime.Update(root, sheet.Keyframes, 0.1f, 400, 200);
            Assert.Equal(1, ended);
            Assert.Equal(1.0f, root.RenderOpacity);
        }
        finally
        {
            root.ReleaseLayout();
        }
    }

    [Fact]
    public void Element_CanRemoveItselfAfterAnEvent()
    {
        var yoga = new Config();
        var root = Element(yoga, "ui");
        var toast = Element(yoga, "panel");
        root.Add(toast);
        try
        {
            Assert.True(toast.RemoveFromParent());
            Assert.Null(toast.Parent);
            Assert.Empty(root.Children);
            Assert.False(toast.RemoveFromParent());
        }
        finally
        {
            root.ReleaseLayout();
        }
    }

    private static UiElement Element(
        Config config,
        string tag,
        params (string Name, string Value)[] attributes) =>
        new(
            config,
            tag,
            attributes.ToDictionary(
                item => item.Name,
                item => item.Value,
                StringComparer.OrdinalIgnoreCase));
}
