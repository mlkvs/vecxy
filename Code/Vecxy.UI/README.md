# Vecxy.UI — полное руководство

Vecxy.UI — retained-mode UI-система движка Vecxy. Структура интерфейса хранится в XML,
оформление — в CSS, раскладка рассчитывается Yoga и встроенным Grid, а отрисовка
выполняется рендерером Vecxy. Документ и его элементы живут между кадрами: игра меняет
только данные, классы и свойства, а система сама обновляет стили, layout и геометрию.

Документ описывает фактически реализованные возможности `Vecxy.UI`.

## Содержание

- [Get Started](#get-started)
- [Как устроена система](#как-устроена-система)
- [Глобальная конфигурация](#глобальная-конфигурация)
- [XML-документы](#xml-документы)
- [Элементы и атрибуты](#элементы-и-атрибуты)
- [CSS, селекторы и каскад](#css-селекторы-и-каскад)
- [Типы значений CSS](#типы-значений-css)
- [Полный справочник CSS-свойств](#полный-справочник-css-свойств)
- [Flex layout](#flex-layout)
- [Grid layout](#grid-layout)
- [Текст и шрифты](#текст-и-шрифты)
- [Изображения и атласы](#изображения-и-атласы)
- [Переходы и анимации](#переходы-и-анимации)
- [Прокрутка и виртуализация](#прокрутка-и-виртуализация)
- [События и состояния](#события-и-состояния)
- [Работа из C#](#работа-из-c)
- [Переиспользуемые компоненты](#переиспользуемые-компоненты)
- [Hot reload](#hot-reload)
- [Диагностика и производительность](#диагностика-и-производительность)
- [Ограничения и диагностика ошибок](#ограничения-и-диагностика-ошибок)

## Get Started

Ниже — минимальный экран с текстом, кнопкой, картинкой, прогрессом, обработчиком
нажатия и hot reload.

### 1. Подключите UI-слой

`EngineLayer` уже регистрирует необходимые модули движка, включая `UiModule`. Для
разработки укажите исходный каталог `Assets`, а не его копию в `bin`, иначе watcher
не увидит изменения файлов, которые открыты в редакторе.

```csharp
using Vecxy.Assets;
using Vecxy.Engine;
using Vecxy.Platforms;

public sealed class Application : IEntryPoint
{
    public void OnConfigureEngine(PlatformContext context, Engine.Options options)
    {
        options.Window = new IWindow.Options("UI demo", 1280, 720);
    }

    public void OnConfigureLayers(
        PlatformContext context,
        List<AAppLayer.IDefinition> layers)
    {
        layers.Add(new EngineLayer.Definition(new AssetsModule.Options
        {
            AssetsDirectory = context.AssetsDirectory,
            HotReloadEnabled = true,
            HotReloadDelay = TimeSpan.FromMilliseconds(100)
        }));
        layers.Add(new HudLayer.Definition());
    }
}
```

Если игра использует исходные ассеты игры и defaults движка из разных каталогов,
основной каталог имеет больший приоритет:

```csharp
new AssetsModule.Options
{
    AssetsDirectory = gameAssets,
    AdditionalAssetDirectories = [engineAssets],
    HotReloadEnabled = true
};
```

Обычный ассет с одинаковым путём берётся из первого найденного каталога. YAML-конфиги,
загруженные через `LoadConfig<T>`, объединяются: движок задаёт defaults, игра
переопределяет только присутствующие ключи. Вложенные mappings объединяются
рекурсивно, скаляры и массивы заменяются целиком.

### 2. Создайте глобальный UI-конфиг

Файл `Assets/Configs/UI.yaml`:

```yaml
referenceResolution: [1920, 1080]
scaleMode: fit
scrollSpeed: 48
dragScrollThreshold: 8
scrollDeceleration: 2400
enableShadows: true
spriteAtlases: {}
```

Если такой полный файл уже есть в ассетах движка, игра может создать частичный:

```yaml
# Assets/Configs/UI.yaml игры
scrollSpeed: 72
```

### 3. Создайте XML

`Assets/UI/Hud.xml`:

```xml
<ui class="screen" styles="Hud.css">
    <panel class="card">
        <text id="title" class="title">ПЕРВЫЙ ЭКРАН</text>
        <image id="portrait" class="portrait" src="../Textures/Hero.png" />
        <progress id="health" class="health" />
        <button id="continue" class="primary">ПРОДОЛЖИТЬ</button>
    </panel>
</ui>
```

### 4. Добавьте CSS

`Assets/UI/Hud.css`:

```css
:root {
    --accent: #65e6ff;
}

.screen {
    width: 100%;
    height: 100%;
    justify-content: center;
    align-items: center;
    background-color: #09101a;
}

.card {
    width: 520ui;
    gap: 20ui;
    padding: 28ui;
    background-color: rgba(18, 28, 44, 0.95);
    border: 2ui solid var(--accent);
    border-radius: 14ui;
    box-shadow: 0 12ui 32ui rgba(0, 0, 0, 0.45);
}

.title {
    font-size: 34ui;
    text-align: center;
    color: white;
}

.portrait {
    width: 128ui;
    height: 128ui;
    align-self: center;
    object-fit: contain;
}

.health {
    width: 100%;
    height: 18ui;
    background-color: #38cf72;
    border-radius: 9ui;
}

.primary {
    height: 56ui;
    background-color: var(--accent);
    color: #071018;
    transition: transform 120ms ease-out, opacity 120ms ease-out;
}

.primary:hover { transform: scale(1.03); }
.primary:active { transform: scale(0.97); opacity: 0.8; }
.primary:disabled { opacity: 0.35; }
```

### 5. Загрузите документ и привяжите события

```csharp
using Vecxy.Engine;
using Vecxy.UI;

public sealed class HudLayer(IUiManager ui) : AAppLayer
{
    public sealed class Definition : ADefinition<HudLayer>;

    private UiDocument? _document;
    private UiText? _title;
    private UiProgress? _health;

    public override void OnInitialize()
    {
        _document = ui.Load("UI/Hud.xml");
        _document.Reloaded += Bind;
        Bind(_document);
    }

    private void Bind(UiDocument document)
    {
        // После XML reload элементы создаются заново, поэтому ссылки получаем снова.
        _title = document.GetElementById<UiText>("title");
        _health = document.GetElementById<UiProgress>("health");
        document.GetElementById<UiButton>("continue").Clicked += OnContinue;

        _health.Progress = 0.75f; // диапазон автоматически ограничивается 0..1
    }

    private void OnContinue(UiElement button)
    {
        _title!.Value = "ГОТОВО";
        button.IsEnabled = false;
    }

    public override void OnUnload()
    {
        if (_document is null)
            return;
        _document.Reloaded -= Bind;
        ui.Unload(_document);
        _document = null;
    }
}
```

Теперь изменение CSS сразу пересчитывает стили, а изменение XML перестраивает дерево
и вызывает `UiDocument.Reloaded`.

## Как устроена система

Жизненный цикл одного документа:

1. `IUiManager.Load("UI/Hud.xml")` загружает XML как asset.
2. `UiDocument` создаёт retained-дерево `UiElement`.
3. CSS-файлы из атрибута `styles` парсятся и применяются каскадом.
4. Yoga рассчитывает Flex; дополнительный проход размещает Grid.
5. Renderer строит геометрию и кеширует слой документа.
6. Изменения текста, классов, атрибутов или стилей инвалидируют только необходимые
   стадии.
7. Asset watcher обновляет XML/CSS без перезапуска приложения.

Не нужно каждый кадр пересоздавать документ или элементы. Сохраните ссылки и меняйте
их свойства только при изменении игровых данных.

## Глобальная конфигурация

`UiModule` загружает `Configs/UI.yaml` через систему YAML-конфигов.

| Параметр | Тип и default | Что делает | Пример |
|---|---|---|---|
| `referenceResolution` | массив двух положительных `float`, `[1920, 1080]` | Логическое разрешение для масштабируемого UI. | `referenceResolution: [1280, 720]` |
| `scaleMode` | string, `fit` | Алгоритм перевода логических координат в output. | `scaleMode: pixel-perfect` |
| `scrollSpeed` | положительный float, `48` | Смещение колесом мыши за один шаг. | `scrollSpeed: 72` |
| `dragScrollThreshold` | float ≥ 0, `8` | Дистанция до начала drag-scroll/drag-and-drop. | `dragScrollThreshold: 12` |
| `scrollDeceleration` | положительный float, `2400` | Замедление инерционной прокрутки. | `scrollDeceleration: 1800` |
| `enableShadows` | bool, `true` | Глобально включает `box-shadow`. | `enableShadows: false` |
| `spriteAtlases` | map string→path | Псевдонимы атласов для `sprite(alias, name)`. | `hud: UI/Hud.atlas` |

Режимы масштаба:

| Значение | Результат |
|---|---|
| `fit`, `scale`, `scale-with-screen` | Берётся меньший scale; весь reference canvas виден. |
| `fill` | Берётся больший scale; canvas заполняет output, края могут выйти за экран. |
| `width` | Scale вычисляется только по ширине. |
| `height` | Scale вычисляется только по высоте. |
| `pixel-perfect`, `pixelperfect` | Целочисленный fit-scale, минимум `1`. |
| `none` | Один логический pixel равен одному output pixel. |

Корень отдельного документа может переопределить настройки:

```xml
<ui styles="Hud.css"
    scale-mode="width"
    reference-width="1920"
    reference-height="1080">
    ...
</ui>
```

## XML-документы

### Корень и подключение CSS

Корнем обычно служит `<ui>`. `styles` и `stylesheet` равнозначны. Несколько файлов
разделяются запятыми или `;`; пути считаются относительно XML.

```xml
<ui styles="Base.css, Styles/Buttons.css; Styles/Hud.css">
    ...
</ui>
```

### Встроенные типизированные элементы

| XML tag | C# тип | Назначение | Минимальный пример |
|---|---|---|---|
| `ui` | `UiElement` | Корень документа. | `<ui styles="Main.css" />` |
| `panel` | `UiPanel` | Контейнер. | `<panel class="row" />` |
| `text` | `UiText` | Текст. | `<text id="score">100</text>` |
| `button` | `UiButton` | Фокусируемая кнопка. | `<button id="play">PLAY</button>` |
| `image` | `UiImage` | Текстура или sprite. | `<image src="Icon.png" />` |
| `progress` | `UiProgress` | Линейное заполнение 0..1. | `<progress id="hp" />` |
| `radial-progress` | `UiRadialProgress` | Круговое заполнение 0..1. | `<radial-progress id="cooldown" />` |

Неизвестный tag допустим и создаёт обычный `UiElement`. Это удобно для семантических
контейнеров и CSS-селекторов:

```xml
<inventory-header class="header">ИНВЕНТАРЬ</inventory-header>
```

`input`, `select`, `slider` и `textarea` распознаются системой интерактивности и
фокуса как tag-и, но специализированных C# контролов и встроенного редактирования
значения у них пока нет. Они создаются как обычный `UiElement`; поведение реализует
игровой код.

### Текст внутри контейнера

Прямой текст в `text` становится его `Text`. Прямой текст в другом tag превращается
во вложенный `UiText`:

```xml
<button>КУПИТЬ</button>
<!-- Эквивалентно для TextContent/Label вложенному text. -->
```

### Общие атрибуты

| Атрибут | Значение | Эффект | Пример |
|---|---|---|---|
| `id` | уникальная строка | Поиск через `#id`, специфичность CSS 100. | `id="save"` |
| `class` | имена через пробел | CSS-классы и runtime-переключение. | `class="button danger"` |
| `style` | CSS declarations | Inline-стиль с максимальным приоритетом. | `style="width: 80ui; opacity: .5"` |
| `hidden` | bool-атрибут | Исключает элемент из layout/render. | `hidden="true"` |
| `disabled` | bool-атрибут | Отключает интерактивность, включает `:disabled`. | `disabled="true"` |
| `aria-disabled` | `true`/другое | `true` также включает disabled. | `aria-disabled="true"` |
| `checked` | bool-атрибут | Включает `:checked`; checkbox-tag `input` переключает его по click. | `checked="true"` |
| `selected` | bool-атрибут | Включает `:selected`. | `selected="true"` |
| `draggable` | bool-атрибут | Источник drag-and-drop. | `draggable="true"` |
| `drop-target` | bool-атрибут | Принимает drop и включает `:drop-target` при наведении. | `drop-target="true"` |
| `tabindex` | строка | Делает интерактивный элемент focusable; `-1` исключает из Tab. | `tabindex="0"` |
| `action` | строка | Делает элемент интерактивным/focusable; смысл action задаёт игра. | `action="open-shop"` |
| `virtualize` | bool-атрибут | Отсекает далёкие дочерние ветки scroll-контейнера. | `virtualize="true"` |

Bool-атрибут считается включённым при любом значении, кроме `false`.

### Атрибуты специализированных элементов

```xml
<!-- Файл относительно XML -->
<image src="../Textures/Sword.png" />

<!-- Sprite из атласа -->
<image sprite="Inventory.atlas#legendary-sword" />

<!-- Горизонтальный progress — default -->
<progress id="xp" />

<!-- Заполнение снизу вверх -->
<progress id="mana" direction="vertical-bottom" />

<!-- Круговая дуга; border-width задаёт толщину, border-color — track -->
<radial-progress id="cooldown" clockwise-depletion="true" />

<!-- Если есть image/sprite, заполнение вырезает сектор текстуры.
     radial-rect тянет сектор до края прямоугольника, а не окружности. -->
<radial-progress id="skill" src="Skill.png" radial-rect="true" />
```

## CSS, селекторы и каскад

### Поддерживаемые селекторы

```css
* { opacity: 1; }                         /* universal */
button { color: white; }                  /* tag */
#save { background-color: #2d8cff; }      /* id */
.danger { color: #ff5b65; }               /* class */
button.primary.large { height: 64ui; }    /* compound */
[action] { pointer-events: auto; }         /* наличие атрибута */
[action="delete"] { color: red; }         /* точное значение */
.toolbar button { margin-left: 8ui; }      /* descendant */
.toolbar > button { flex-grow: 1; }        /* direct child */
.row > button:hover { opacity: .8; }       /* комбинация */
h1, h2, .title { color: white; }           /* список */
```

Поддерживаемые pseudo-классы:

| Pseudo | Когда активен | Пример |
|---|---|---|
| `:root` | У элемента нет parent. | `:root { width: 100%; }` |
| `:hover` | Мышь над элементом или его веткой. На touch постоянного hover нет. | `button:hover { opacity: .9; }` |
| `:active` | Primary pointer удерживается на элементе. | `button:active { transform: scale(.96); }` |
| `:focus` | Элемент получил фокус. | `button:focus { border-color: white; }` |
| `:focus-visible` | Фокус получен клавиатурой через Tab. | `button:focus-visible { border-width: 2ui; }` |
| `:disabled` | `disabled` или `aria-disabled="true"`. | `button:disabled { opacity: .35; }` |
| `:checked` | `IsChecked`/`checked`. | `.toggle:checked { background-color: green; }` |
| `:selected` | `IsSelected`/`selected`. | `.tab:selected { color: cyan; }` |
| `:dragging` | Элемент сейчас перетаскивается. | `.item:dragging { opacity: .5; }` |
| `:drop-target` | Над принимающим элементом находится drag source. | `.slot:drop-target { border-color: yellow; }` |
| `:first-child` | Первый ребёнок parent. | `.row > panel:first-child { margin-left: 0; }` |
| `:last-child` | Последний ребёнок parent. | `.row > panel:last-child { margin-right: 0; }` |
| `:empty` | Нет children и собственного непустого текста. | `.list:empty { background-color: #311; }` |

Не поддерживаются pseudo-elements (`::before`), функциональные pseudo (`:not()`,
`:nth-child()`), sibling combinators (`+`, `~`) и media queries.

### Каскад и наследование

Специфичность: `id = 100`, class/pseudo/attribute = `10`, tag = `1`. При равной
специфичности побеждает правило, объявленное позже. Inline `style` применяется после
таблиц стилей.

Наследуются: `color`, `font-size`, `font-family`, `text-align`, `white-space`,
`text-fit`, `min-font-size` и CSS variables. Остальные свойства начинаются с default
для каждого элемента.

### CSS variables

```css
:root {
    --surface: #172235;
    --spacing: 12ui;
}

.card {
    padding: var(--spacing);
    background-color: var(--surface, #111827); /* fallback после запятой */
}
```

Поддерживается вложенное раскрытие `var()`. Если переменная отсутствует и fallback не
задан, такое значение свойства не применяется.

## Типы значений CSS

### Длины

| Формат | Значение | Пример |
|---|---|---|
| число или `px` | Логические points; `12` и `12px` равнозначны. | `border-width: 2px` |
| `ui` | Логические UI-points; в текущем layout численно равны points и масштабируются canvas. | `padding: 16ui` |
| `%` | Процент от доступной оси/контейнера в зависимости от свойства. | `width: 50%` |
| `vw` | Процент логической ширины viewport. | `width: 25vw` |
| `vh` | Процент логической высоты viewport. | `height: 10vh` |
| `auto` | Автоматический размер/позиция; разрешён не для всех свойств. | `margin-left: auto` |

`calc()`, `em`, `rem`, `vmin` и `vmax` не поддерживаются.

### Цвета

```css
.examples {
    color: transparent;
    border-color: white;                 /* также black */
    background-color: #4af;              /* #rgb */
    image-tint: #44aaffcc;                /* #rgba, #rrggbb, #rrggbbaa */
    scrollbar-color: rgb(40, 180, 255);  /* 0..255 или проценты */
    box-shadow: 0 4ui 12ui rgba(0, 0, 0, 0.45);
    /* Современная форма тоже допустима: rgb(20 40 60 / 80%) */
}
```

Произвольные CSS-имена цветов (`red`, `cornflowerblue`) не поддерживаются; из имён
есть только `transparent`, `white`, `black`.

## Полный справочник CSS-свойств

### Display и позиционирование

| Свойство | Значения / default | Что делает и пример |
|---|---|---|
| `display` | `flex` (default), `grid`, `contents`, `none` | `none` убирает ветку; `grid` включает Grid; `contents` оставляет children без собственного box. Пример: `.closed { display: none; }` |
| `position` | `relative` (default), `static`, `absolute`, `fixed` | `absolute`/`fixed` исключают элемент из потока Yoga; оба сейчас работают как absolute. Пример: `.badge { position: absolute; top: 4ui; right: 4ui; }` |
| `top`, `right`, `bottom`, `left` | length, `%`, `auto` | Смещение positioned-элемента. Пример: `.footer { position: absolute; left: 24ui; right: 24ui; bottom: 16ui; }` |
| `inset` | 1–4 length, default `auto` | Shorthand в порядке CSS: все; vertical/horizontal; top/horizontal/bottom; top/right/bottom/left. Пример: `.screen { position: absolute; inset: 0; }` |
| `z-index` | integer, `0` | Порядок siblings: большее значение рисуется и hit-test-ится сверху. Пример: `.modal { z-index: 100; }` |
| `visibility` | `visible` (default), `hidden` | `hidden` сохраняет layout, но не рисует элемент. Пример: `.placeholder { visibility: hidden; }` |
| `pointer-events` | `auto` (default), `none` | `none` исключает сам элемент из hit-test; children проверяются раньше и могут оставаться интерактивными. Пример: `.decoration { pointer-events: none; }` |

### Размеры и box model

| Свойство | Значения / default | Что делает и пример |
|---|---|---|
| `width`, `height` | length/`%`/`auto`, default `auto` | Основной размер. Пример: `.dialog { width: 640ui; height: 70vh; }` |
| `min-width`, `min-height` | length/`%`/`auto` | Нижняя граница. Пример: `.button { min-width: 140ui; min-height: 48ui; }` |
| `max-width`, `max-height` | length/`%`/`auto` | Верхняя граница. Пример: `.description { max-width: 60vw; }` |
| `aspect-ratio` | число или `a / b` | Сохраняет отношение сторон. Пример: `.portrait { width: 20%; aspect-ratio: 1 / 1; }` |
| `margin` | 1–4 lengths; `auto` разрешён | Внешние отступы. Пример: `.centered { margin: 0 auto; }` |
| `margin-top`, `margin-right`, `margin-bottom`, `margin-left` | length/`%`/`auto` | Одна сторона. Пример: `.next { margin-left: auto; }` |
| `padding` | 1–4 lengths; `auto` превращается в `0` | Внутренние отступы. Пример: `.card { padding: 12ui 20ui; }` |
| `padding-top`, `padding-right`, `padding-bottom`, `padding-left` | length/`%` | Одна сторона. Пример: `.safe-area { padding-top: 32ui; }` |
| `gap` | length/`%`, default `0` | Общий промежуток Flex/Grid. Пример: `.row { gap: 8ui; }` |
| `row-gap`, `column-gap` | length/`%` | Переопределяют gap по оси. Пример: `.grid { row-gap: 12ui; column-gap: 6ui; }` |

### Flex

| Свойство | Значения / default | Что делает и пример |
|---|---|---|
| `flex-direction` | `column` default; `row`, `row-reverse`, `column-reverse` | Главная ось. Пример: `.toolbar { flex-direction: row; }` |
| `flex-wrap` | `nowrap` default; `wrap`, `wrap-reverse` | Перенос children. Пример: `.chips { flex-wrap: wrap; }` |
| `justify-content` | `flex-start`/`start`, `center`, `flex-end`/`end`, `space-between`, `space-around`, `space-evenly` | Распределение по главной оси. Пример: `.toolbar { justify-content: space-between; }` |
| `align-items` | `stretch` default; `start`/`flex-start`, `center`, `end`/`flex-end`, `baseline`, `space-between`, `space-around` | Выравнивание children по поперечной оси. Пример: `.row { align-items: center; }` |
| `align-self` | `auto` default и значения align-items | Переопределяет alignment одного child. Пример: `.icon { align-self: flex-start; }` |
| `place-content` | одно или два align-значения | Shorthand: первое задаёт `align-items`, второе — `justify-content`; также влияет на text align. Пример: `.empty { place-content: center; }` |
| `flex-grow` | float, `0` | Доля свободного места. Пример: `.content { flex-grow: 1; }` |
| `flex-shrink` | float, `0` | Разрешение сжиматься. Пример: `.label { flex-shrink: 1; }` |
| `flex-basis` | length/`%`/`auto` | Базовый размер по главной оси. Пример: `.sidebar { flex-basis: 320ui; }` |
| `flex` | `none`, `auto`, либо `grow [shrink [basis]]` | Shorthand. `flex: 1` → `1 1 0`; `flex: none` → `0 0 auto`. Пример: `.column { flex: 1 1 0; }` |

### Grid

| Свойство | Значения / default | Что делает и пример |
|---|---|---|
| `grid-template-columns` | список tracks | Явные колонки. Пример: `grid-template-columns: 180ui 1fr 2fr;` |
| `grid-template-rows` | список tracks | Явные строки. Пример: `grid-template-rows: auto 1fr 64ui;` |
| `grid-auto-columns` | список tracks | Размер implicit columns; API присутствует, но текущий auto-placement ограничен количеством template columns. Пример: `grid-auto-columns: 1fr;` |
| `grid-auto-rows` | список tracks | Циклические размеры implicit rows. Пример: `grid-auto-rows: 80ui 100ui;` |
| `grid-column-start`, `grid-column-end` | `auto`, integer, `span N` | Линии/размер span. Пример: `.wide { grid-column-start: 1; grid-column-end: span 2; }` |
| `grid-row-start`, `grid-row-end` | `auto`, integer, `span N` | То же для строк. Пример: `.hero { grid-row: 1 / span 2; }` |
| `grid-column`, `grid-row` | `start / end` | Shorthand placement. Пример: `.header { grid-column: 1 / 4; }` |

Tracks: фиксированная длина, `%`, `fr`, `auto`, `min-content`, `max-content`,
`minmax(min, max)` и `repeat(N, tracks)`, где `N` от 1 до 256.

```css
.inventory {
    display: grid;
    grid-template-columns: repeat(4, minmax(72ui, 1fr));
    grid-auto-rows: 92ui;
    gap: 10ui;
}

.featured { grid-column: 1 / span 2; grid-row: 1 / span 2; }
```

Named lines, `auto-fit`, `auto-fill`, `grid-template-areas` и `grid-auto-flow` не
поддерживаются.

### Текст

| Свойство | Значения / default | Что делает и пример |
|---|---|---|
| `color` | color, `white` | Цвет текста и цвет дуги radial progress; наследуется. Пример: `.price { color: #ffd76a; }` |
| `font-family` | первое имя из списка, `Vecxy Fallback` | Выбирает `@font-face`; наследуется. Пример: `font-family: "Inter";` |
| `font-size` | length, `16ui` | Размер шрифта; наследуется. Пример: `.title { font-size: 32ui; }` |
| `min-font-size` | length, `1px` | Нижняя граница для `text-fit: shrink`; наследуется. Пример: `min-font-size: 12ui;` |
| `text-align` | `left` default, `center`, `right` | Горизонтальное положение строк внутри text box. Пример: `.score { text-align: right; }` |
| `vertical-align` | `top` default, `middle`, `bottom` | Вертикальное положение текста внутри box фиксированной высоты. Пример: `.label { vertical-align: middle; }` |
| `white-space` | `nowrap` default; `normal`, `pre-wrap` включают wrapping | Перенос строк по доступной ширине. Пример: `.description { white-space: normal; }` |
| `text-fit` | `none` default, `shrink` | Для single-line текста уменьшает font до `min-font-size`, чтобы он вошёл. Пример: `.name { text-fit: shrink; min-font-size: 10ui; }` |

### Фон, границы и изображения

| Свойство | Значения / default | Что делает и пример |
|---|---|---|
| `background`, `background-color` | color, transparent | `background` здесь является только shorthand цвета. Пример: `.panel { background: rgba(0,0,0,.8); }` |
| `background-image` | `url(path)` или `sprite(atlas, name)` | Рисует текстуру в box. Пример: `background-image: url("Panel.png");` |
| `background-size` | `fill` default, `contain`, `cover` | Масштаб фоновой картинки. Пример: `background-size: cover;` |
| `background-position` | `left/right/top/bottom/center` или `% %` | Alignment для contain/crop cover. Пример: `background-position: 25% top;` |
| `background-slice` | length, `0` | Включает nine-slice с одинаковой границей со всех сторон. Пример: `background-slice: 12px;` |
| `image-tint` | color, white | Умножает цвет image/background texture. Пример: `.locked { image-tint: #777777aa; }` |
| `object-fit` | `fill` default, `contain`, `cover` | Масштаб именно `<image>`. Пример: `.portrait { object-fit: cover; }` |
| `border` | width и color | Shorthand читает толщину и цвет; стиль `solid` можно писать, но он не влияет. Пример: `border: 2ui solid #65e6ff;` |
| `border-width` | length, `0` | Одинаковая толщина всех сторон. Пример: `border-width: 1ui;` |
| `border-color` | color, transparent | Цвет border и track radial progress. Пример: `border-color: #ffffff44;` |
| `border-radius` | одна length или `%` | Один общий radius; если передано несколько значений, используется первое. Пример: `border-radius: 12ui;` |
| `box-shadow` | comma-list: `[inset] x y [blur] [spread] [color]` | Несколько внешних/внутренних теней. `%` и `auto` для частей shadow не принимаются. Пример: `box-shadow: 0 8ui 24ui #0008, inset 0 0 8ui #fff2;` |

### Композитинг, overflow и scrollbars

| Свойство | Значения / default | Что делает и пример |
|---|---|---|
| `opacity` | float 0..1, `1` | Прозрачность всей ветки; значение clamp-ится. Пример: `.ghost { opacity: .45; }` |
| `overflow` | `visible` default; `hidden`, `scroll`, `auto` | Задаёт обе оси; hidden clip-ит, scroll/auto также разрешают прокрутку при переполнении. Пример: `.list { overflow-y: auto; }` |
| `overflow-x`, `overflow-y` | те же значения | Управление одной осью. Пример: `.carousel { overflow-x: scroll; overflow-y: hidden; }` |
| `scrollbar-width` | length, `8ui` | Толщина вертикального scrollbar. Пример: `scrollbar-width: 10ui;` |
| `scrollbar-color` | `thumbColor [trackColor]` | Цвет thumb и опционально track. Пример: `scrollbar-color: #65e6ff #ffffff18;` |

### Transform и движение

| Свойство | Значения / default | Что делает и пример |
|---|---|---|
| `transform` | `none`; функции `translate`, `translateX/Y`, `scale`, `scaleX/Y`, `rotate` | Visual transform без перерасчёта layout. Пример: `transform: translateY(-4ui) scale(1.05) rotate(2deg);` |
| `transform-origin` | `left/center/right`, `top/center/bottom`, `%` | Pivot, default `center center`. Пример: `transform-origin: left bottom;` |
| `transition` | comma-list `property duration [easing] [delay]` | Плавный переход поддерживаемых paint/composite свойств. Пример: `transition: opacity 150ms ease-out, transform .2s;` |
| `animation` | `name duration [delay] [easing] [iterations] [fill]` | Запускает один `@keyframes`. Пример: `animation: pulse 800ms ease-in-out infinite both;` |

`rotate` принимает `deg`, `rad`, `turn` или число градусов. Translate принимает
обычные length и `%` от размера самого элемента. Transform-функции применяются в
фиксированную агрегированную модель translation + scale + rotation, а не как
произвольная браузерная матричная цепочка.

## Flex layout

Flex — default layout каждого контейнера, направление по умолчанию — column.

```xml
<panel class="toolbar">
    <button>НАЗАД</button>
    <text class="caption">ИНВЕНТАРЬ</text>
    <button>ЗАКРЫТЬ</button>
</panel>
```

```css
.toolbar {
    flex-direction: row;
    align-items: center;
    gap: 12ui;
}

.caption {
    flex: 1;
    text-align: center;
}
```

Частая ошибка новичка: ожидать row по умолчанию. Если элементы должны идти слева
направо, явно задайте `flex-direction: row`.

## Grid layout

Grid размещает только отображаемых, не `absolute/fixed` children. Если template
columns пуст, создаётся одна колонка `1fr`. Auto-placement идёт слева направо,
затем сверху вниз.

```xml
<panel class="equipment">
    <panel class="slot helmet" />
    <panel class="slot armor" />
    <panel class="slot weapon" />
</panel>
```

```css
.equipment {
    display: grid;
    width: 420ui;
    grid-template-columns: repeat(3, 1fr);
    grid-template-rows: 100ui 160ui;
    gap: 8ui;
}

.armor { grid-column: 2; grid-row: 1 / span 2; }
```

Grid использует отдельный placement-pass поверх Yoga. У Grid-item итоговая ячейка
задаёт позицию и размер, поэтому его собственные `width/height` не должны использоваться
как основной способ растянуть item внутри ячейки.

## Текст и шрифты

### Fallback font

Без `@font-face` используется встроенный `Vecxy Fallback`: интерфейс останется
читаемым, но для игры следует подключить собственный шрифт.

### TrueType

```css
@font-face {
    font-family: "Game Sans";
    src: url("../Fonts/GameSans.ttf");
}

:root { font-family: "Game Sans"; }
```

TTF при импорте пакуется в runtime-атлас 1024×1024 с source size 64. Встроенный набор
содержит основные Latin, Cyrillic и ряд типографских символов. Отсутствующие glyph
не появятся автоматически.

### AngelCode BMFont

Поддерживается XML `.fnt` с ровно одной texture page:

```css
@font-face {
    font-family: "Pixel";
    src: url("../Fonts/Pixel.fnt");
}

.pixel-label { font-family: "Pixel"; font-size: 24ui; }
```

Перенос поддерживает `white-space: normal` и `pre-wrap`. Для динамических коротких
подписей фиксируйте width/height или используйте `text-fit: shrink`, если layout не
должен менять размер при каждой цифре.

## Изображения и атласы

Путь `/Textures/Icon.png` или `Assets/Textures/Icon.png` считается от корня Assets.
`src`, `sprite` и `background-image` без такого префикса разрешаются относительно
XML-документа. Путь `@font-face src` разрешается относительно CSS-файла.

```xml
<image src="../Textures/Icon.png" />
<image sprite="Hud.atlas#coin" />
```

```css
.icon { background-image: url("../Textures/Icon.png"); }
.coin { background-image: sprite("hud", "coin"); }
```

Atlas alias `hud` берётся из `UI.yaml`. Можно указать path вместо alias. Для alias
удобно задавать путь от корня, например `hud: /UI/Hud.atlas`, чтобы результат не
зависел от каталога XML-документа.

Готовый atlas descriptor:

```json
{
  "texture": "Hud.png",
  "sprites": {
    "coin": { "x": 0, "y": 0, "width": 64, "height": 64 },
    "gem":  { "x": 64, "y": 0, "width": 64, "height": 64 }
  }
}
```

Atlas из исходников:

```json
{
  "width": 1024,
  "padding": 2,
  "sources": {
    "coin": "Sprites/Coin.png",
    "gem": "Sprites/Gem.png"
  }
}
```

`width` clamp-ится к 64..4096, `padding` — к 1..16; итоговая высота — степень двойки
до 4096. При импорте padding заполняется extrusion краевых пикселей. Проекты,
импортирующие `Vecxy.Platforms.props`, компилируют atlas descriptors на MSBuild.
Шаг отключается свойством `VecxyCompileAtlases=false`.

Runtime texture, например render target камеры:

```csharp
UiImage preview = document.GetElementById<UiImage>("preview");
preview.Texture = cameraRenderTexture;
```

## Переходы и анимации

Анимируются только `color`, `background-color`, `opacity`, `transform`.

```css
.button {
    transition: background-color 120ms ease-out,
                opacity 120ms linear,
                transform 180ms ease-in-out 40ms;
}

.button:hover {
    background-color: #337ab7;
    transform: translateY(-2ui) scale(1.02);
}
```

Easing: `linear`, `ease`, `ease-in`, `ease-out`, `ease-in-out`, `step-start`,
`step-end`. Время задаётся в `ms` или `s`.

```css
@keyframes toast-enter {
    from { opacity: 0; transform: translateY(20ui); }
    60%  { opacity: 1; transform: translateY(-3ui); }
    to   { opacity: 1; transform: translateY(0); }
}

.toast { animation: toast-enter 280ms ease-out 1 forwards; }
```

Iteration — число или `infinite`; fill — `none`, `forwards`, `backwards`, `both`.
Одновременно у элемента хранится одна CSS animation, но несколько transitions.

```csharp
element.AnimationStarted += (_, e) => Logger.Info($"Start {e.Name}");
element.AnimationIteration += (_, e) => Logger.Info($"Iteration {e.Iteration}");
element.AnimationEnded += (_, e) => element.RemoveFromParent();
element.TransitionEnded += (_, e) => Logger.Info(e.Property);
```

## Прокрутка и виртуализация

```css
.list {
    height: 480ui;
    overflow-y: auto;
    scrollbar-width: 8ui;
    scrollbar-color: #65e6ff #ffffff18;
}
```

Поддерживаются wheel, Shift+wheel для горизонтальной оси, drag-scroll на mouse/touch,
инерция, вложенный поиск ближайшего scrollable ancestor и перетаскивание вертикального
thumb. Drag, начавший прокрутку, отменяет click.

```csharp
list.ScrollTo(new Vector2(0, 240));
list.ScrollBy(new Vector2(0, 48));
list.Scrolled += element => SaveOffset(element.ScrollOffset);

bool vertical = list.CanScrollVertically;
Vector2 totalContent = list.ScrollExtent;
```

Для очень длинной retained-ветки:

```xml
<panel class="list" virtualize="true">...</panel>
```

Виртуализация пропускает layout/render веток, находящихся дальше примерно одного
viewport вокруг видимой области. Это не data virtualization: элементы остаются в DOM.
Для больших динамических коллекций сочетайте её с `UiKeyedCollection`.

## События и состояния

### Pointer и click

```csharp
button.Clicked += element => Buy();
button.ClickedAt += (element, logicalPosition) => ShowMenu(logicalPosition);
```

Click происходит только если pointer отпущен над тем же элементом и жест не стал
scroll/drag. Интерактивны button/input/select/slider, scrollable элементы, элементы с
`action`, `tabindex` или подписчиком `Clicked`/touch.

### Фокус

```csharp
button.Focused += _ => ShowHint();
button.Blurred += _ => HideHint();
ui.Focus(button, focusVisible: true);
ui.Focus(null); // снять фокус
```

Tab/Shift+Tab обходят focusable элементы в порядке документов и DOM. `:focus-visible`
включается для клавиатурного фокуса; click даёт обычный `:focus`.

### Drag-and-drop

```xml
<panel id="item" draggable="true" />
<panel id="slot" drop-target="true" />
```

```csharp
item.DragStarted += _ => PlayPickup();
item.DragEnded += _ => StopPickup();
slot.Dropped += (_, e) => Equip(e.Source, e.Target);
```

### Touch

```csharp
element.TouchStarted += (_, touch) => Begin(touch.Id, touch.Position);
element.TouchMoved += (_, touch) => Move(touch.Id, touch.Position, touch.Delta);
element.TouchEnded += (_, touch) => End(touch.Id);
element.TouchCancelled += (_, touch) => Cancel(touch.Id);
```

`UiTouchEvent` содержит `Id`, логическую `Position`, `Delta`, `Pressure`, `IsPrimary`.
Touch захватывается начальным элементом до end/cancel. Primary touch также участвует
в обычном click pipeline.

## Работа из C#

### IUiManager и документы

```csharp
UiDocument document = ui.Load("UI/Main.xml");
IReadOnlyList<UiDocument> all = ui.Documents;
document.IsVisible = false; // скрывает весь документ
ui.Unload(document);        // удаляет и Dispose-ит
```

Документы рисуются в порядке загрузки; последний документ находится сверху.

### Поиск элементов

```csharp
UiButton save = document.GetElementById<UiButton>("save"); // обязателен, иначе exception
UiElement? optional = document.Query("#optional");
UiText? firstLabel = document.Query<UiText>(".label");
IReadOnlyList<UiElement> panels = document.QueryAll("panel");

UiButton? nested = card.Query<UiButton>(".buy");
```

Важно: runtime `Query/QueryAll` поддерживает только один простой selector: `#id`,
`.class` или tag. Выражение `.card > .buy` работает в CSS, но не в C# Query.

### Свойства UiElement

| API | Назначение | Пример |
|---|---|---|
| `TagName`, `Id`, `Classes`, `Attributes` | Read-only представление identity. | `if (element.Classes.Contains("selected"))` |
| `Parent`, `Children` | Retained tree. | `var count = list.Children.Count;` |
| `Bounds` | Итоговый logical rect после layout. | `tooltip.SetAttribute("x", target.Bounds.X.ToString());` |
| `HitTestBounds` | Отдельный rect только для pointer hit-test. | `marker.HitTestBounds = worldScreenRect;` |
| `Text`, `TextContent` | Собственный текст / текст первого вложенного `UiText`. | `button.TextContent = "OK";` |
| `Progress` | Clamp 0..1 для progress. | `bar.Progress = hp / maxHp;` |
| `IsVisible` | Управляет `hidden`. | `dialog.IsVisible = isOpen;` |
| `IsEnabled`, `IsDisabled` | Управляет/читает disabled. | `buy.IsEnabled = money >= price;` |
| `IsChecked`, `IsSelected` | Состояния и pseudo-классы. | `tab.IsSelected = tabId == active;` |
| `IsDraggable`, `AcceptsDrop` | Read-only bool из XML-атрибутов. | `item.SetAttribute("draggable", "true");` |
| `ScrollOffset`, `ScrollExtent` | Текущее и полное scroll-пространство. | `list.ScrollTo(Vector2.Zero);` |

### Атрибуты, классы и inline styles

```csharp
element.SetAttribute("data-rarity", "legendary");
element.RemoveAttribute("data-rarity");

element.AddClass("selected");
element.RemoveClass("locked");
element.ToggleClass("affordable", money >= price);

element.SetStyle("opacity", "0.5");
element.RemoveStyle("opacity");
element.Style.Width = "320ui";
element.Style.BackgroundColor = "#172235";
element.Style["border-radius"] = "12ui";
element.Style.SetWidthPercent(healthFraction);
```

`UiInlineStyle` имеет shortcuts `Width`, `Height`, `Color`, `BackgroundColor`,
`BorderColor`, `Opacity`, `Transform`; любое другое свойство доступно через indexer
или `Set`.

### Изменение дерева

```csharp
parent.Add(child);
parent.Insert(0, child);
parent.MoveChild(child, 2);
parent.Clear();

child.DetachFromParent(); // можно позднее Add снова, Yoga nodes сохранены
child.RemoveFromParent(); // subtree и Yoga nodes уничтожаются
```

Нельзя добавить элемент, у которого уже есть parent. Сначала вызовите
`DetachFromParent`, если его требуется переместить между контейнерами.

### Создание элементов

```csharp
var panel = document.CreatePanel(new Dictionary<string, string>
{
    ["class"] = "notification"
});
panel.Add(document.CreateText("Получен предмет"));
panel.Add(document.CreateImage("Textures/Item.png"));
panel.Add(document.CreateButton("ЗАБРАТЬ"));
document.Root.Add(panel);
```

Также доступны `CreateElement(tag, attributes, text)` и generic/обычный `Instantiate`.

## Переиспользуемые компоненты

`Assets/UI/Components/ShopCard.xml`:

```xml
<panel class="shop-card" data-id="{{id}}">
    <text class="name">{{name}}</text>
    <text class="price">{{price}}</text>
    <button class="buy">КУПИТЬ</button>
</panel>
```

```csharp
UiElement root = document.Instantiate(
    "Components/ShopCard.xml",
    list,
    new Dictionary<string, string>
    {
        ["id"] = item.Id,
        ["name"] = item.Name,
        ["price"] = item.Price.ToString()
    });
```

Подстановка `{{name}}` применяется к атрибутам и прямому тексту всех nodes. Путь
компонента относителен основному XML. Component asset кешируется и обновляется при
следующей инстанциации после hot reload; уже созданные instances автоматически не
перестраиваются.

Типизированный view:

```csharp
public sealed class ShopCardView(UiElement root) : AUiComponent(root)
{
    public UiText Name { get; } = Element<UiText>(".name");
    public UiText Price { get; } = Element<UiText>(".price");
    public UiButton Buy { get; } = Element<UiButton>(".buy");
}
```

`AUiComponent.Element<T>` принимает `#id` или `.class`; `Elements<T>` возвращает все
элементы класса.

### UiKeyedCollection

Для списка, где важно сохранить focus, subscriptions и локальное состояние:

```csharp
var cards = new UiKeyedCollection<string, Item, ShopCardView>(
    parent: list,
    create: item => new ShopCardView(document.Instantiate(
        "Components/ShopCard.xml", list,
        new Dictionary<string, string> { ["name"] = item.Name })),
    root: view => view.Root,
    update: (view, item, index) =>
    {
        view.Name.Value = item.Name;
        view.Price.Value = item.Price.ToString();
        view.Buy.IsEnabled = item.CanBuy;
    });

cards.Update(items, item => item.Id);
```

Коллекция создаёт, перемещает и удаляет только изменившиеся keys. Повторный key в
одном update вызывает exception. `Clear()` удаляет все retained roots.

## Hot reload

Условия работы:

1. `AssetsModule.Options.HotReloadEnabled = true`.
2. `AssetsDirectory`/`AdditionalAssetDirectories` указывают на редактируемые исходники.
3. Документ или stylesheet уже загружен.
4. `AssetsModule` и `UiModule` получают update каждый кадр.

Поведение:

| Изменение | Результат |
|---|---|
| CSS | DOM и C# subscriptions сохраняются; стили/layout обновляются. |
| Основной XML | Корень и все элементы создаются заново; вызывается `Reloaded`. |
| Подключённый font/image/atlas | Asset обновляется; зависимый UI перечитывает ресурс. |
| Component XML | Template обновится при следующем `Instantiate`; старые instances остаются. |

Правильный bind pattern:

```csharp
public override void OnInitialize()
{
    _document = ui.Load("UI/Main.xml");
    _document.Reloaded += Bind;
    Bind(_document);
}

private void Bind(UiDocument document)
{
    _close = document.GetElementById<UiButton>("close");
    _close.Clicked += _ => document.IsVisible = false;
}
```

Не храните старые element references после `Reloaded`. При ошибке парсинга assets
система логирует проблему и старается сохранить предыдущую корректную версию.

## Диагностика и производительность

```csharp
public sealed class DebugLayer(IUiDiagnostics diagnostics) : AAppLayer
{
    public override void OnUpdate(float deltaTime)
    {
        UiPerformanceStatistics s = diagnostics.Statistics;
        Console.WriteLine(
            $"UI {s.RenderCpu.CurrentMilliseconds:0.00} ms, " +
            $"elements {s.Elements}, batches {s.Batches}, " +
            $"rebuilds {s.LayerRebuilds}, cache hits {s.LayerCacheHits}");
    }
}
```

Доступны timing-группы update/layout/style/Yoga/grid/text/animation/hit-test/input/
render/tessellation/upload/composite, allocations, counts элементов, vertices,
indices, batches, shadows и статистика каждого документа. `ResetPeaks()` сбрасывает
накопленные пики.

Рекомендации:

- Не пересоздавайте статический DOM каждый кадр.
- Обновляйте `Text`, `Progress`, class или inline property только при изменении значения.
- Для счётчиков фиксируйте box, чтобы смена текста не требовала полного layout.
- Для повторяющихся списков используйте components и `UiKeyedCollection`.
- Для длинных scroll-веток используйте `virtualize="true"`.
- `opacity` и `transform` дешевле layout-анимаций; width/height не анимируются.
- Большое число `box-shadow` увеличивает число shadow layers и геометрию.
- Скрывайте целый документ через `document.IsVisible`, если экран не нужен.

## Справочник публичных C# типов

Большинство игр постоянно работают только с `IUiManager`, `UiDocument`, наследниками
`UiElement`, `AUiComponent` и `UiKeyedCollection`. Остальные public-типы нужны для
конфигурации, диагностики, инструментов и расширения asset pipeline.

| Тип | Назначение и пример использования |
|---|---|
| `IUiManager` | Загружает/выгружает документы, перечисляет `Documents`, управляет фокусом: `ui.Focus(button)`. |
| `UiModule`, `UiModule.Definition` | Реализация UI-модуля и его DI definition; обычно подключается составным engine layer. |
| `UiDocument` | Загруженный XML screen: query, factories, components, `IsVisible`, `Reloaded`, `Dispose`. |
| `UiElement` | Базовый retained node, дерево, состояния, стили, события и scroll API. |
| `UiPanel` | Типизированный контейнер `<panel>`. |
| `UiText` | `<text>` с удобным свойством `Value`. |
| `UiButton` | `<button>` с `Label` и общей системой click/focus. |
| `UiImage` | `<image>` со свойствами `Source`, `Sprite`, `Texture`. |
| `UiProgress` | `<progress>`; значение хранится в унаследованном `Progress`. |
| `UiRadialProgress` | `<radial-progress>`; значение хранится в унаследованном `Progress`. |
| `UiInlineStyle` | Runtime inline CSS: `element.Style["gap"] = "8ui"`. |
| `AUiComponent` | Базовый типизированный view над XML component. |
| `UiKeyedCollection<TKey,TItem,TView>` | Retained reconciliation списка по стабильному key. |
| `UiConfig` | Тип `Configs/UI.yaml`; содержит reference resolution, scale, scroll и atlas aliases. |
| `EUiScaleMode` | Enum `Fit`, `Fill`, `Width`, `Height`, `PixelPerfect`, `None`; YAML использует строковые варианты. |
| `UiLength` | Разобранная CSS-длина: `UiLength.Pixels(12)`, `Percent(50)`, `Ui(16)`, `Auto`, `TryParse`. |
| `EUiLengthUnit` | `Auto`, `Pixel`, `Percent`, `Ui`, `ViewportWidth`, `ViewportHeight`. |
| `UiEdges` | Четыре `UiLength` в порядке Top/Right/Bottom/Left; `UiEdges.Zero`. |
| `UiBoxShadow` | Исходное описание shadow: offsets, blur, spread, color, inset. |
| `UiResolvedBoxShadow` | Shadow после перевода CSS-длин в logical points. |
| `UiTransform` | Runtime translation/scale/rotation/origin; `Identity`, `ToMatrix`, `Lerp`. |
| `UiComputedStyle` | Разрешённый набор CSS-свойств. Это public data type движка, но computed style конкретного элемента сейчас не выставлен публичным свойством. |
| `UiAnimationEvent` | `Name`, `ElapsedTime`, `Iteration` для animation events. |
| `UiTransitionEvent` | `Property`, `ElapsedTime` для `TransitionEnded`. |
| `UiDragEvent` | `Source` и `Target` события `Dropped`. |
| `UiTouchEvent` | `Id`, `Position`, `Delta`, `Pressure`, `IsPrimary`. |
| `IUiDiagnostics` | Точка доступа к `UiPerformanceStatistics`. |
| `UiPerformanceStatistics` | Общие timings, allocations, geometry, documents и cache counters. |
| `UiTimingStatistics` | `CurrentMilliseconds`, `AverageMilliseconds`, `PeakMilliseconds`, `LastWorkMilliseconds`. |
| `UiDocumentStatistics` | Статистика одного документа: visibility, rebuild/cache, elements, geometry, versions, memory. |
| `UiDocumentAsset`, `UiStyleSheetAsset` | Asset payload импортированного XML/CSS: source и path. |
| `UiDocumentAssetImporter`, `UiStyleSheetAssetImporter` | Importers расширений `.xml` и `.css`; штатно регистрируются `UiModule`. |
| `UiFontAsset`, `UiFontAssetImporter` | Импортированный `.fnt`/`.ttf` и importer; `UiFontAsset` сообщает family/source size/line height. |
| `UiSpriteAtlasAsset`, `UiSpriteAtlasAssetImporter` | Runtime atlas и importer `.atlas`. |
| `UiSprite` | Прямоугольник sprite в пикселях: `X`, `Y`, `Width`, `Height`. |

Низкоуровневые asset/importer-типы не требуется создавать вручную при обычной работе:
их жизненным циклом управляют `AssetsModule` и `UiModule`.

## Ограничения и диагностика ошибок

На данный момент отсутствуют:

- gradients;
- `calc()`, `em/rem`, media/container queries;
- `:not()`, `:nth-child()`, pseudo-elements;
- sibling selectors;
- named grid lines/areas, `auto-fit`, `auto-fill`;
- отдельные border widths/radii для каждой стороны/угла;
- browser form controls и ввод текста;
- layout-анимации width/height/margin;
- несколько одновременных CSS animations на одном элементе.

Rounded backgrounds, borders, clipping и `box-shadow` поддерживаются. Проценты в
`border-radius` считаются от меньшей стороны элемента.

Частые проблемы:

| Симптом | Проверьте |
|---|---|
| UI не появился | Добавлен ли UI/Engine layer, существует ли `Configs/UI.yaml`, верен ли path в `ui.Load`. |
| CSS не применяется | CSS подключён через `styles`, selector поддерживается, declaration имеет `;`, path относителен XML. |
| Hot reload не работает | Watcher должен смотреть исходный `Assets`, а не копию `bin/Assets`; после изменения C# приложение нужно один раз перезапустить. |
| Click не приходит | Элемент disabled/hidden, `pointer-events: none`, перекрыт большим `z-index` или не имеет интерактивного tag/subscription. |
| Текст не переносится | Нужны ограниченная width и `white-space: normal`/`pre-wrap`. |
| `Query(".card .buy")` возвращает null | C# Query поддерживает только простой selector; сначала найдите `.card`, затем вызовите `card.Query(".buy")`. |
| После XML reload обработчик пропал | Подпишитесь на `UiDocument.Reloaded` и выполните bind заново. |
| Grid выглядит неожиданно | Укажите число template columns, tracks и gap; absolute children Grid не размещает. |
| Картинка искажена | Для `<image>` задайте `object-fit: contain/cover`; для background — `background-size`. |
