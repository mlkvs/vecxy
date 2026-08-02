# Vecxy.UI

Retained-mode game UI for Vecxy. Documents are XML, presentation is CSS, layout is
calculated by Meta Yoga, and drawing is performed by the engine's OpenGL renderer.
UI is rendered into the game output texture, so it works both in a standalone game
and inside the editor viewport.

Screens can opt into resolution-independent coordinates. Yoga then lays out an
expanded logical canvas, while rendering, fonts, borders, images, and hit testing
use one framebuffer-aware scale:

```xml
<ui styles="hud.css"
    scale-mode="scale-with-screen"
    reference-width="1920"
    reference-height="1080">
    <!-- authored in logical 1920x1080 pixels -->
</ui>
```

The scale is chosen from the smaller output/reference axis. This preserves aspect
and keeps the entire reference area visible; extra space from a different aspect
ratio expands the logical root so percentage and edge-anchored elements adapt.

## Quick start

```xml
<ui styles="hud.css">
    <panel class="hud">
        <image class="portrait" sprite="hud.atlas#player" />
        <text class="title">PLAYER</text>
        <button id="inventory">INVENTORY</button>
    </panel>
</ui>
```

```css
:root {
    --accent: #65e6ff;
}

.hud {
    position: absolute;
    left: 24px;
    bottom: 24px;
    flex-direction: row;
    align-items: center;
    gap: 12px;
    padding: 16px;
    background-color: rgba(8, 12, 20, 0.92);
    border: 1px solid var(--accent);
}

button:hover { background-color: #28617a; }
button:active { background-color: #163744; }
```

```csharp
public sealed class HudLayer(IUiManager ui) : AAppLayer
{
    private UiDocument? _document;

    public override void OnInitialize()
    {
        _document = ui.Load("UI/hud.xml");
        _document.Reloaded += Bind;
        Bind(_document);
    }

    public override void OnUnload()
    {
        if (_document is not null)
        {
            _document.Reloaded -= Bind;
            ui.Unload(_document);
        }
    }

    private void Bind(UiDocument document) =>
        document.Query("#inventory")!.Clicked += OpenInventory;

    private void OpenInventory(UiElement _) { }
}
```

XML and linked CSS assets are hot-reloaded. A document rebuilt after an XML change
also rebuilds its element instances and raises `Reloaded`, allowing game code to
rebind callbacks. CSS-only changes keep the DOM.

## Elements and selectors

Built-in tags are `ui`, `panel`, `text`, `image`, `button`, `input`, `select`, and
`slider`. Unknown tags work as regular containers. Text directly inside a container
is converted into a text child.

Selectors support tags, `#id`, `.class`, `[attribute]`, `[attribute=value]`, child
and descendant combinators, and the pseudo-classes `:root`, `:hover`, `:active`,
`:focus`, `:focus-visible`, `:disabled`, `:first-child`, `:last-child`, and `:empty`.
Inline `style` attributes and CSS custom properties with `var()` fallbacks are
supported.

## CSS support

- Yoga layout: `display`, `position`, `width`, `height`, min/max sizes, `top`,
  `right`, `bottom`, `left`, `inset`, `margin`, `padding`, `gap`, `flex`,
  `flex-grow`, `flex-shrink`, `flex-basis`, `flex-direction`, `flex-wrap`,
  `justify-content`, `align-items`, `align-self`, and `aspect-ratio`.
- Paint and behavior: `color`, `background`, `background-color`,
  `background-image: url(...)`, `border`, `border-color`, `border-width`,
  `font-family`, `font-size`, `opacity`, `overflow`, `visibility`, `z-index`, and
  `pointer-events`. Images support `object-fit: fill`, `contain`, and `cover`.
- Lengths: pixels, percentages, zero, and `auto` where Yoga accepts it.
- Colors: `transparent`, `white`, `black`, `#rgb`, `#rgba`, `#rrggbb`,
  `#rrggbbaa`, `rgb()`, and `rgba()`.

`display: grid` is passed to Yoga, but explicit CSS grid track syntax, animations,
transforms, gradients, shadows, rounded-corner clipping, text wrapping, and browser
CSS functions such as `calc()` are not part of the current renderer.

## Images, sprite atlases, and fonts

An image can load a texture directly:

```xml
<image src="../Textures/portrait.png" />
```

Paths beginning with `/` or `Assets/` resolve from the Assets root. Other paths
resolve relative to the XML or CSS file that owns them.

Or select a region from a `.atlas` JSON asset:

```json
{
  "texture": "hud.png",
  "sprites": {
    "player": { "x": 0, "y": 0, "width": 64, "height": 64 }
  }
}
```

```xml
<image sprite="hud.atlas#player" />
```

Bitmap fonts use the XML form of the AngelCode BMFont `.fnt` format with one
texture page. Register them from CSS:

```css
@font-face {
    font-family: "Oxanium";
    src: url("Fonts/Oxanium.fnt");
}

.title {
    font-family: "Oxanium";
    font-size: 32px;
    color: #ffffff;
}
```

When no bitmap font is selected, Vecxy.UI uses a small built-in debug font so a UI
remains readable without external font assets.
