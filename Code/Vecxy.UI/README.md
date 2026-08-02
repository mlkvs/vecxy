# Vecxy.UI

Retained-mode game UI for Vecxy. Documents are XML, presentation is CSS, layout is
calculated by Meta Yoga, and drawing is performed by the engine's OpenGL renderer.
UI is rendered into the game output texture, so it works both in a standalone game
and inside the editor viewport.

Resolution-independent coordinates are configured globally in
`Assets/Configs/UI.yaml`:

```yaml
referenceResolution:
- 1920
- 1080
scaleMode: fit
scrollSpeed: 48
spriteAtlases:
  hud-atlas: UI/hud.atlas
```

Available scale modes are `fit`, `fill`, `width`, `height`, `pixel-perfect`, and
`none`. The document root may still override the global values when a particular
screen needs it:

```xml
<ui styles="hud.css"
    scale-mode="scale-with-screen"
    reference-width="1920"
    reference-height="1080">
    <!-- authored in logical 1920x1080 pixels -->
</ui>
```

`fit` chooses the smaller output/reference axis, `fill` chooses the larger axis,
`width` and `height` lock scaling to one axis, and `pixel-perfect` uses an integer
fit scale. Extra visible space is represented by an expanded logical root, so
percentage and edge-anchored elements adapt to the actual aspect ratio.

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
`:focus`, `:focus-visible`, `:disabled`, `:checked`, `:selected`, `:dragging`,
`:drop-target`, `:first-child`, `:last-child`, and `:empty`.
Inline `style` attributes and CSS custom properties with `var()` fallbacks are
supported.

## CSS support

- Yoga layout: `display`, `position`, `width`, `height`, min/max sizes, `top`,
  `right`, `bottom`, `left`, `inset`, `margin`, `padding`, `gap`, `flex`,
  `flex-grow`, `flex-shrink`, `flex-basis`, `flex-direction`, `flex-wrap`,
  `justify-content`, `align-items`, `align-self`, and `aspect-ratio`.
- Grid layout: `grid-template-columns`, `grid-template-rows`,
  `grid-auto-columns`, `grid-auto-rows`, `grid-column`, `grid-row`, their
  start/end longhands, `gap`, `row-gap`, and `column-gap`. Tracks support fixed
  and percentage sizes, `fr`, `auto`, `minmax()`, and numeric `repeat()`.
- Paint and behavior: `color`, `background`, `background-color`,
  `background-image`, `background-size`, `background-position`, `border`,
  `border-color`, `border-width`, `font-family`, `font-size`, `opacity`,
  `overflow`, `overflow-x`, `overflow-y`, `visibility`, `z-index`, and
  `pointer-events`. Images support `fill`, `contain`, and `cover`.
- Scrolling: nested clipping, mouse wheel input, per-axis scroll offsets,
  programmatic `ScrollTo`/`ScrollBy`, scroll events, and rendered scrollbars with
  `scrollbar-width` and `scrollbar-color`.
- Lengths: pixels, percentages, `vw`, `vh`, `ui`, zero, and `auto` where accepted.
  One `ui` is one logical pixel at the reference resolution and scales according
  to the global scale mode.
- Colors: `transparent`, `white`, `black`, `#rgb`, `#rgba`, `#rrggbb`,
  `#rrggbbaa`, `rgb()`, and `rgba()`.
- Visual motion: `transform`, `transform-origin`, `transition`, `animation`, and
  `@keyframes`. Animated properties are `color`, `background-color`, `opacity`,
  and `transform`.

Gradients, shadows, rounded-corner clipping, text wrapping, named grid lines,
`auto-fit`/`auto-fill`, and browser CSS functions such as `calc()` are not yet part
of the renderer.

## Responsive layout

```css
.icon {
    width: 20%;
    height: 8vh;
    min-width: 64px;
    max-width: 120px;
    aspect-ratio: 1 / 1;
    font-size: 16ui;
}

.inventory {
    display: grid;
    grid-template-columns: repeat(4, minmax(64ui, 1fr));
    grid-auto-rows: 78ui;
    gap: 8ui;
    overflow-y: auto;
}
```

`ScrollOffset`, `ScrollExtent`, `CanScrollHorizontally`, and
`CanScrollVertically` expose scroll state on `UiElement`.

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

The same sources can be used as CSS backgrounds. Atlas aliases come from the
global UI config shown above:

```css
.panel {
    background-image: url("panel.png");
    background-size: cover;
}

.icon {
    background-image: sprite("hud-atlas", "inventory");
    background-size: contain;
}
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

## Transitions, animations, and events

```css
.nav-button {
    transition: background-color 0.12s, transform 0.12s, opacity 0.12s;
}

.nav-button:hover { transform: scale(1.05); }

@keyframes toast-enter {
    from { opacity: 0; transform: translateY(12ui); }
    to { opacity: 1; transform: translateY(0); }
}

.toast { animation: toast-enter 0.2s ease-out; }
```

Runtime events are exposed directly on an element:

```csharp
toast.AnimationStarted += (_, animation) => { };
toast.AnimationIteration += (_, animation) => { };
toast.AnimationEnded += (_, animation) => toast.RemoveFromParent();
button.TransitionEnded += (_, transition) => { };
panel.Scrolled += _ => { };
```

Elements also expose `Focused`, `Blurred`, `DragStarted`, `DragEnded`, and
`Dropped`. Set `IsDraggable` on a source and `AcceptsDrop` on a target; their
`:dragging` and `:drop-target` states update automatically during pointer input.
