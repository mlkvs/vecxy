using System.Numerics;
using Facebook.Yoga;
using Xunit;

namespace Vecxy.UI.Tests;

public sealed class UiStyleAndLayoutTests
{
    [Fact]
    public void Cascade_ResolvesVariablesAndDynamicPseudoClass()
    {
        var config = new Config();
        var root = Element(config, "ui", ("class", "root"));
        var button = Element(config, "button", ("class", "button primary"));
        root.Add(button);

        try
        {
            var sheet = UiStyleSheet.Parse("""
                @font-face {
                    font-family: "Game UI";
                    src: url("Fonts/GameUi.fnt");
                }
                :root { --accent: #58d6ff; color: white; }
                .button { background-color: #101820; color: var(--accent); }
                .root > .button.primary:hover { background-color: #203850; }
                """);

            var fontFace = Assert.Single(sheet.FontFaces);
            Assert.Equal("Game UI", fontFace.Family);
            Assert.Equal("Fonts/GameUi.fnt", fontFace.Source);

            UiStyleResolver.Resolve(root, [sheet]);
            Assert.Equal(new Vector4(0x58 / 255f, 0xd6 / 255f, 1f, 1f), button.ComputedStyle.Color);
            Assert.Equal(new Vector4(0x10 / 255f, 0x18 / 255f, 0x20 / 255f, 1f), button.ComputedStyle.BackgroundColor);

            button.IsHovered = true;
            UiStyleResolver.Resolve(root, [sheet]);
            Assert.Equal(new Vector4(0x20 / 255f, 0x38 / 255f, 0x50 / 255f, 1f), button.ComputedStyle.BackgroundColor);
        }
        finally
        {
            root.ReleaseLayout();
        }
    }

    [Fact]
    public void YogaLayout_AppliesFlexGapAndFixedSizes()
    {
        var config = new Config();
        var root = Element(
            config,
            "ui",
            ("style", "flex-direction: row; gap: 10px; align-items: flex-start;"));
        var first = Element(config, "panel", ("style", "width: 100px; height: 30px;"));
        var second = Element(config, "panel", ("style", "width: 50px; height: 20px;"));
        root.Add(first);
        root.Add(second);

        try
        {
            UiStyleResolver.Resolve(root, []);
            UiLayout.Calculate(root, 400, 200);

            Assert.Equal(100, first.Bounds.Width);
            Assert.Equal(30, first.Bounds.Height);
            Assert.Equal(0, first.Bounds.X);
            Assert.Equal(110, second.Bounds.X);
            Assert.Equal(50, second.Bounds.Width);
        }
        finally
        {
            root.ReleaseLayout();
        }
    }

    [Theory]
    [InlineData(900, 1800, 2.0f, 450, 900)]
    [InlineData(900, 450, 0.5f, 1800, 900)]
    [InlineData(225, 900, 0.5f, 450, 1800)]
    public void Canvas_ScalesFromReferenceResolutionAndExpandsForAspectRatio(
        int outputWidth,
        int outputHeight,
        float expectedScale,
        int expectedWidth,
        int expectedHeight)
    {
        var config = new Config();
        var root = Element(
            config,
            "ui",
            ("scale-mode", "scale-with-screen"),
            ("reference-width", "450"),
            ("reference-height", "900"));

        try
        {
            var canvas = UiCanvas.Resolve(root, outputWidth, outputHeight);
            Assert.Equal(expectedScale, canvas.Scale);
            Assert.Equal(expectedWidth, canvas.Width);
            Assert.Equal(expectedHeight, canvas.Height);
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
