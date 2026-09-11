# Vecxy Isometric Editor — техническое задание

Статус: usable MVP реализован; production-возможности ниже остаются дорожной картой

Проект: `Vecxy.Isometric.Editor`

Пользователи: level designer, gameplay developer, technical artist

## Реализовано в MVP

- отдельное приложение `Vecxy.Isometric.Editor` в solution folder `Isometric`;
- открытие корня проекта или папки `Assets`, поиск и фильтрация definitions и миров;
- JSON-форматы `.vworld`, `.vsurface`, `.vwall`, `.vobject`, `.vopening`, `.vconnection` со стабильными asset/instance ID;
- создание и настройка пола, стен, объектов, проёмов и межэтажных соединений;
- изометрическая сетка, этажи, preview до размещения, footprint, стены произвольной длины и поворот с шагом 90°;
- выбор текстуры из `Assets` и её preview в каталоге, инспекторе и viewport;
- выбор, перемещение стрелками или через Inspector, вращение, удаление, undo/redo и atomic save;
- базовые slots и attachment-связи для предметов на мебели, внутри контейнера, на стене, а также door/window в стене;
- автоматическое вычисление чанков и регионов: объект остаётся одним instance при пересечении их границ;
- весь динамически создаваемый UI и world placements создаются через `IPrototypes`;
- UI разбит на переиспользуемые XML-компоненты, а CSS полностью ограничен корнем `.iso-editor`;
- встроен Inter Regular с OFL-лицензией и латинским/кириллическим набором символов.

Не вошедшие в этот MVP пункты (file watcher, dockable layout, brush/line tools, multi-selection, autosave journal, schema migrations, incremental region files, runtime compiler и headless validator) оставлены в плане ниже и не должны считаться завершёнными.

## 1. Цель

Нужен редактор изометрических миров, который открывает корневую папку `Assets`, позволяет создавать определения контента и собирать из них многоэтажные миры.

Разработчик создаёт и настраивает типы пола, стен, мебели, контейнеров, дверей, окон, лестниц и декоративных предметов. Level designer выбирает готовые элементы из каталога, размещает их визуально, редактирует свойства экземпляров и сохраняет мир.

Редактор обязан поддерживать:

- миры, разделённые на регионы и чанки;
- произвольное количество уровней;
- разный контур пола на каждом уровне;
- объекты, стены и лестницы, пересекающие чанки и регионы;
- предметы на мебели, внутри контейнеров и на стенах;
- двери и окна в стенах;
- визуальный preview до размещения;
- поворот, перемещение, копирование и массовое редактирование;
- undo/redo, autosave, валидацию и безопасное сохранение;
- стабильные diff в Git и миграции форматов.

## 2. Главные принципы

1. **Definition и instance разделены.** Файл предмета описывает тип, а мир хранит только экземпляр, ссылку на definition и overrides.
2. **Регион и чанк — техническая деталь хранения.** Дизайнер не обязан создавать их вручную. Они создаются при рисовании и видны в advanced/debug mode.
3. **Один объект — один ID.** Пересечение границы чанка или региона не создаёт копии объекта.
4. **Авторский и runtime-форматы разделены.** В `Assets` лежат читаемые и merge-friendly исходники; asset pipeline компилирует их в быстрый runtime-формат.
5. **Все изменения выполняются командами.** Любая операция поддерживает undo/redo и может быть записана в autosave journal.
6. **Невалидное размещение не скрывается.** Preview объясняет причину ошибки до клика.
7. **Ссылки основаны на стабильных asset ID.** Перемещение файла внутри `Assets` не ломает мир.
8. **Данные не зависят от изометрической проекции.** Модель работает в целочисленной мировой сетке; камера лишь отображает её.

## 3. Термины

- **Asset workspace** — открытая папка `Assets` и служебный индекс проекта.
- **Definition** — переиспользуемое описание поверхности, объекта, стены или opening.
- **Instance** — размещённый в мире экземпляр definition.
- **Tile column** — координата `X/Y` со sparse-набором уровней.
- **Surface** — ground или floor на конкретном `TileCoord`.
- **Wall segment** — каноническое ребро между двумя вершинами сетки.
- **Footprint** — занятые ячейки или сегменты относительно anchor.
- **Slot** — область или точка, принимающая дочерние предметы.
- **Opening** — проём в стене: дверь, окно, арка или пустой проход.
- **Level** — логический этаж. Это не фиксированный прямоугольник и не контейнер чанков.
- **Region/chunk** — единица стриминга и сохранения, а не граница дизайна.

## 4. Структура Assets

Рекомендуемая структура по умолчанию:

```text
Assets/
  Isometric/
    Worlds/
      House/
        House.vworld
        Regions/
          r_0_0.vregion
    Surfaces/
      Grass.vsurface
      WoodFloor.vsurface
    Walls/
      BrickWall.vwall
      InteriorWall.vwall
    Openings/
      WoodenDoor.vopening
      Window120.vopening
    Objects/
      Furniture/
        Table.vobject
        Wardrobe.vobject
      Decorations/
        Cup.vobject
        Painting.vobject
    Connections/
      WoodenStairs.vconnection
    Textures/
    Models/
```

Имена расширений рабочие и должны быть утверждены вместе со схемами. Пользователь может создавать подпапки и собственные категории. Категория хранится в metadata, а не выводится только из пути.

## 5. Asset definitions

### 5.1 Общие поля

Каждый definition-файл содержит:

- `schemaVersion`;
- стабильный `assetId`;
- `displayName` и необязательный localization key;
- `category`, tags и searchable aliases;
- preview icon/thumbnail;
- ссылки на texture, sprite, model, material и animation;
- editor color и gizmo settings;
- gameplay prototype/component references;
- пользовательские properties с типами и ограничениями;
- дата изменения только в служебном индексе, не в authoring-файле.

### 5.2 Surface definition (`.vsurface`)

- тип: `Ground`, `Floor`, специальная поверхность;
- texture/material и варианты;
- tile scale, rotation policy и UV rules;
- movement cost, walkable, slippery, sound/footstep profile;
- допустимая нагрузка и support rules;
- высота/offset визуальной плоскости;
- правила auto-tiling и transition edges;
- разрешённые соседства и fallback-вариант.

### 5.3 Object definition (`.vobject`)

- cell footprint по уровням;
- pivot/anchor;
- доступные повороты;
- blocking, collision и navigation flags;
- визуальные варианты и orientation mapping;
- support slots: столешница, полка, сиденье;
- container slots: шкаф, ящик, сундук;
- socket slots: фиксированные точки крепления;
- максимальный вес, категории допустимых предметов и capacity;
- скрывать ли содержимое container в обычном viewport;
- destructible/state variants, например открытый шкаф;
- gameplay prototype и default properties.

### 5.4 Wall definition (`.vwall`)

- segment height и thickness;
- material для сторон и торцов;
- допустимые углы/соединения;
- auto-join rules, caps и corner variants;
- wall-mount slots или mountable areas;
- допустимые типы openings;
- blocking по движению, обзору, свету и звуку;
- damaged/destroyed variants.

### 5.5 Opening definition (`.vopening`)

Opening размещается только на совместимой стене и вырезает диапазон в одном или нескольких сегментах.

- тип: door, window, arch, empty opening;
- размер проёма, sill и vertical offset;
- требования к толщине/высоте стены;
- frame, leaf/glass visual assets;
- opening direction и hinge side;
- состояния: open, closed, locked, broken;
- проход, visibility и navigation modifier;
- возможность инвертировать сторону без пересоздания.

Дверь состоит из wall opening и интерактивной створки. Окно состоит из opening, рамы и опциональной прозрачной/разрушаемой части.

### 5.6 Connection definition (`.vconnection`)

Для лестниц, лифтов, люков и пандусов:

- footprint на исходном и целевом уровнях;
- входные navigation portals;
- разница уровней;
- направление подъёма;
- clearance volume;
- визуальная геометрия и rail variants;
- правила вырезания отверстия в полу;
- one-way или bidirectional traversal.

## 6. Формат мира

### 6.1 World manifest (`.vworld`)

Manifest содержит:

- `schemaVersion`, `worldId`, имя и metadata;
- grid settings, размеры чанка и региона;
- параметры логических уровней и их отображаемые имена;
- world origin и физический scale одного tile;
- ссылки на region-файлы;
- environment/lighting defaults;
- список world-level gameplay settings;
- dependency manifest используемых definitions;
- bounds, вычисляемые редактором, но не ограничивающие рост мира.

### 6.2 Region и chunk data

- Регион — отдельный merge/streaming-файл.
- Chunk может быть секцией region-файла или отдельным бинарным блоком после профилирования.
- Surface data хранится компактно по координатам и уровням.
- Глобальный object/wall catalog хранит каждый экземпляр один раз.
- Пространственный индекс чанка является производным и может пересобираться.
- Вложенные предметы сохраняются как attachment relation, а не как копии в родителе и мире одновременно.

### 6.3 Instance data

Экземпляр содержит:

- стабильный `instanceId`;
- `definitionId`;
- anchor, rotation и variant;
- property overrides относительно definition;
- attachment parent/slot/local transform;
- runtime initial state, если оно задаётся дизайнером;
- editor-only note, layer, lock и hidden flags.

### 6.4 Сериализация

- Authoring-файлы: UTF-8 JSON или YAML; итоговый выбор должен обеспечивать deterministic formatting.
- Координаты и GUID записываются в стабильном формате.
- Коллекции сортируются по координате/ID перед сохранением.
- Запись выполняется во временный файл с последующим atomic replace.
- Поддерживаются schema migrations и резервная копия до destructive migration.
- Неизвестные поля по возможности сохраняются для forward compatibility.
- Runtime получает скомпилированные, проверенные и индексированные данные через AssetCompiler.

## 7. Attachments и вложенность

Нужны четыре базовых вида slot:

1. `SupportSurface` — стол, полка, кровать.
2. `Container` — шкаф, сундук, ящик; содержимое может быть скрыто.
3. `WallMount` — картина, лампа, полка на стене.
4. `Socket` — строго определённая точка, например ручка или модуль техники.

Slot описывает локальную область, capacity, допустимые категории, максимальный вес, правила пересечения и визуализации.

Attachment хранит `parentId`, `slotId`, local position и local rotation. При перемещении/повороте родителя дочерние объекты двигаются одной undo-транзакцией. Циклы запрещены. Удаление родителя требует выбора: удалить содержимое, вынести его в мир или отменить операцию.

Для container редактор показывает содержимое в Inspector и отдельном режиме `Show contents`. Скрытые предметы не рендерятся в обычном viewport, но участвуют в поиске, валидации и сохранении.

## 8. Интерфейс редактора

### 8.1 Основная раскладка

```text
┌ Asset Browser ┬──────────── Viewport ────────────┬ Inspector ┐
│ search/tags   │ grid, world, preview, gizmos    │ selection │
│ categories    │                                  │ settings  │
├───────────────┴──────────────────────────────────┴───────────┤
│ World Outliner / Levels / Validation / Console / Status      │
└───────────────────────────────────────────────────────────────┘
```

- Панели dockable, layout сохраняется на пользователя.
- Viewport занимает основное пространство.
- Asset Browser имеет list/grid mode, thumbnails, поиск, tags, favorites и recent.
- Inspector поддерживает multi-selection и mixed values.
- World Outliner показывает уровни, логические группы и instances, но не навязывает иерархию чанков.

### 8.2 Открытие workspace

- `Open Assets Folder` проверяет структуру и создаёт служебный `.vecxy` index при необходимости.
- Выполняется фоновое сканирование и импорт definitions.
- Ошибочные assets остаются видимыми с диагностикой.
- File watcher обновляет каталог без перезапуска.
- Перед переключением workspace редактор предлагает сохранить изменённые документы.

### 8.3 Создание definition

Wizard предлагает тип, имя, категорию, путь и template. После создания открывается definition editor:

- выбор texture/model через asset picker;
- footprint painter;
- pivot и rotation preview;
- slot/area editor;
- collision/navigation preview;
- property inspector;
- thumbnail renderer;
- validation и `Save`/`Save As`/`Duplicate`.

Изменение definition обновляет все instances в открытом мире. Overrides сохраняются. Breaking-изменения footprint показываются как конфликты и не применяются молча.

### 8.4 World viewport

- ортографическая изометрическая камера;
- pan, zoom, rotate camera и `Frame Selection`;
- переключение уровня и режим показа соседних уровней;
- ghost/cutaway стен, скрывающих текущую область;
- grid major/minor lines и координаты под курсором;
- overlays чанков, регионов, navigation и collision;
- выбор кликом, box selection и cycling перекрывающихся элементов;
- gizmo перемещения и поворота;
- временное скрытие/изоляция selection и категорий.

### 8.5 Placement tool

После выбора asset под курсором появляется ghost preview:

- зелёный — placement валиден;
- красный — запрещён;
- жёлтый — placement допустим с предупреждением;
- подсвечивается полный footprint, включая соседние чанки и уровни;
- tooltip объясняет конфликт: занято, нет support, неверный slot, нет стены, недостаточный clearance;
- `R` вращает объект; `Shift+R` вращает в обратную сторону;
- ЛКМ размещает, drag рисует поверхности/стены;
- `Shift` продолжает серию, `Ctrl` включает альтернативный snap;
- ПКМ/Escape отменяет активный tool;
- eyedropper выбирает definition и overrides из мира.

Preview использует тот же placement validator, что и команда размещения. Отдельная упрощённая проверка в UI запрещена.

### 8.6 Специализированные инструменты

- **Surface brush:** один tile, rectangle, fill, line, replace, erase.
- **Wall tool:** line/polyline, auto-join, углы, erase segment.
- **Opening tool:** snap к совместимой стене, flip side, hinge preview.
- **Object tool:** placement по footprint, rotation и variant.
- **Attachment tool:** подсветка доступных slots, local snap, перенос между slots.
- **Connection tool:** выбор начального/конечного уровня, проверка clearance.
- **Region tool:** только advanced mode — inspect, load/unload, save, validate.

### 8.7 Рекомендуемые shortcuts

- `Ctrl+S` save current document, `Ctrl+Shift+S` save all;
- `Ctrl+Z`/`Ctrl+Y` undo/redo;
- `W/E/R` select/move/rotate либо настраиваемая схема;
- `Delete` удалить с подтверждением для иерархий;
- `F` frame selection;
- `G` grid, `C` chunk overlay;
- `PageUp/PageDown` соседний уровень;
- `[`/`]` размер brush;
- `Ctrl+D` duplicate;
- `H` hide, `Shift+H` show all;
- shortcuts переназначаемые и отображаются в меню.

## 9. Рабочие сценарии

### Level designer: собрать комнату

1. Открыть мир и выбрать уровень.
2. Нарисовать floor rectangle.
3. Провести wall polyline; углы соединяются автоматически.
4. Выбрать дверь и окно, увидеть совместимые сегменты и вставить openings.
5. Выбрать мебель по thumbnail или поиску, разместить и повернуть.
6. Активировать attachment tool, положить предметы на стол и в шкаф.
7. Повесить картину на wall-mount area.
8. Добавить лестницу и связать уровни.
9. Запустить validation и navigation preview.
10. Сохранить изменённые регионы и world manifest.

### Developer: создать новый предмет

1. Создать `.vobject` из template.
2. Назначить visual asset и footprint.
3. Настроить collision, navigation и gameplay prototype.
4. Добавить support/container slots.
5. Проверить четыре поворота и thumbnail.
6. Сохранить; asset появляется в каталоге без перезапуска.
7. Открыть usage list и проверить существующие instances.

## 10. Undo, autosave и безопасность

- Command stack хранится отдельно на каждый открытый документ.
- Brush stroke, перемещение объекта с детьми и wall polyline — одна транзакция.
- Команды содержат минимальный before/after state.
- Autosave journal пишется через короткий debounce и восстанавливается после crash.
- Обычный save очищает соответствующую часть journal.
- Закрытие с dirty documents всегда требует явного решения.
- Удаление definition показывает usages; запрещено оставлять молча broken references.
- Массовая migration сначала показывает dry-run report.

## 11. Валидация

Ошибки:

- duplicate/empty ID;
- missing definition или texture;
- overlapping solid footprints;
- overlapping wall segments;
- opening без стены или несовместимого размера;
- attachment cycle, missing slot или выход за slot area;
- предмет без support;
- stairs без свободного выхода или целевого уровня;
- unreachable navigation area;
- invalid region/chunk index;
- объект частично потерян при сохранении;
- неизвестная версия schema.

Validation panel группирует проблемы по severity и asset. Двойной клик открывает файл или фокусирует instance. Доступны `Validate Selection`, `Validate World`, автоисправления только для однозначных случаев и headless validation для CI.

## 12. Архитектура editor-проекта

Предлагаемые подсистемы:

```text
Vecxy.Isometric.Editor
  Workspace/
    AssetWorkspace
    AssetIndex
    AssetFileWatcher
  Documents/
    WorldDocument
    DefinitionDocument
    DirtyState
    AutosaveJournal
  Commands/
    IEditorCommand
    CommandHistory
    TransactionCommand
  Catalog/
    DefinitionCatalog
    SearchIndex
    ThumbnailCache
  Placement/
    PlacementController
    PlacementPreview
    PlacementValidator
    SnapService
  Viewport/
    IsometricViewport
    IsometricCameraController
    GridRenderer
    SelectionService
    GizmoRenderer
  Inspectors/
  Validation/
  Persistence/
    WorldAuthoringSerializer
    DefinitionSerializer
    SchemaMigrationRegistry
    AtomicFileWriter
```

Core-модуль `Vecxy.Isometric` не должен зависеть от editor. Общие схемы authoring-data могут находиться в отдельном будущем проекте `Vecxy.Isometric.Assets`, если editor DTO начнут загрязнять runtime API.

## 13. Производительность

- Асинхронное открытие workspace и миров с progress/cancellation.
- Стриминг чанков вокруг камеры с configurable radius.
- Отдельный небольшой prefetch radius для placement preview.
- Thumbnail generation в background queue.
- Spatial picking через chunk index, без полного перебора мира.
- Brush изменяет чанки пакетно.
- Только изменённые регионы сериализуются повторно.
- Большие операции показывают progress и остаются отменяемыми.
- Целевой viewport: стабильные 60 FPS на типичной сцене; конкретные budgets фиксируются после прототипа.

## 14. Совместная работа и Git

- Один region-файл является основной единицей merge/ownership.
- Форматирование deterministic, без timestamps и случайного порядка.
- Editor показывает dirty regions до save.
- Опциональный soft lock-файл для команды, но Git остаётся источником истины.
- Conflict resolver может сравнить base/ours/theirs по instance ID.
- Definition rename выполняется как asset move с сохранением asset ID.

## 15. План реализации

### Phase 0 — утвердить схемы

- [ ] Утвердить расширения и JSON/YAML для authoring.
- [ ] Утвердить asset ID и instance ID policy.
- [ ] Описать versioning и migration contract.
- [ ] Определить связь authoring formats с AssetCompiler.
- [ ] Зафиксировать оси мира, rotation order и physical height уровня.

### Phase 1 — workspace и документы

- [ ] Открытие `Assets`.
- [ ] Индекс definitions и file watcher.
- [ ] World/definition document abstractions.
- [ ] Dirty state, atomic save и recent workspaces.
- [ ] Базовый shell с dockable panels.

### Phase 2 — definition editors

- [ ] Surface editor.
- [ ] Object footprint/pivot editor.
- [ ] Wall editor.
- [ ] Opening editor.
- [ ] Connection/stairs editor.
- [ ] Slots и container settings.
- [ ] Thumbnail generation.

### Phase 3 — viewport MVP

- [ ] Изометрическая камера, grid и уровни.
- [ ] Рендер surfaces, walls и objects.
- [ ] Picking и selection.
- [ ] Ghost preview и единый placement validator.
- [ ] Move/rotate/delete/duplicate.
- [ ] Surface brush и wall line tool.

### Phase 4 — сложное размещение

- [ ] SupportSurface attachments.
- [ ] Container contents mode.
- [ ] WallMount attachments.
- [ ] Door/window openings.
- [ ] Stairs и level connections.
- [ ] Перемещение родителя вместе с attachment tree.

### Phase 5 — production persistence

- [ ] World/region serializers.
- [ ] Object/wall/attachment catalogs.
- [ ] Incremental region save.
- [ ] Autosave journal и crash recovery.
- [ ] Schema migrations.
- [ ] Runtime compilation.

### Phase 6 — удобство и качество

- [ ] Undo/redo всех команд.
- [ ] Search, tags, favorites и recent.
- [ ] Multi-selection и batch inspector.
- [ ] Validation panel и quick fixes.
- [ ] Navigation/collision overlays.
- [ ] User settings и shortcuts.
- [ ] Git-friendly diff/conflict workflow.

## 16. Definition of Done для первого usable editor

- Разработчик может открыть `Assets`, создать surface, wall, object, door и stairs definitions без ручного редактирования файлов.
- Level designer может создать мир и несколько уровней, нарисовать пол и стены, вставить дверь/окно и расставить мебель.
- Предмет можно положить на стол, убрать в шкаф и повесить на стену.
- Preview до клика точно совпадает с итогом команды и объясняет запрет размещения.
- Все операции поддерживают undo/redo и не теряются после crash recovery.
- Мир сохраняется, закрывается, повторно открывается и даёт эквивалентную сцену.
- Объекты на границах чанков/регионов остаются едиными instances.
- Headless validator успешно проверяет сохранённый мир.
- Authoring-файлы дают стабильный diff при повторном сохранении без изменений.

## 17. Тесты

- Unit: coordinate conversion, rotation, footprints, canonical wall edges, slots, migrations.
- Property-based: round-trip serialize/deserialize и отрицательные координаты.
- Integration: save/reopen world, partial region load, cross-region object move.
- Editor command: execute/undo/redo возвращает byte-equivalent logical state.
- Golden files: schema versions и deterministic serialization.
- UI automation: основные developer и level-designer workflows.
- Recovery: падение между temporary write и atomic replace.
- Performance: большой каталог assets, большой мир и массовый brush stroke.

## 18. Открытые решения

До начала Phase 2 необходимо принять решения:

- JSON или YAML как authoring format;
- единая физическая высота level или height profile на каждый уровень;
- 2D sprites, 3D models или оба визуальных режима в первой версии;
- отдельные region-файлы либо region directory с chunk blocks;
- как gameplay prototypes задают типизированные custom properties;
- нужен ли встроенный prefab/composite-object format;
- разрешено ли свободное sub-tile размещение декоративных объектов;
- стратегия collaborative locks.

Рекомендуемые defaults для MVP: JSON, deterministic formatting, целочисленный tile snap, четыре поворота, одинаковая базовая высота уровней с override, один region-файл и поддержка sprites вместе с optional models.

## 19. Текущее состояние

В `Vecxy.Isometric` уже есть:

- координаты world/region/chunk/tile;
- sparse уровни;
- surfaces;
- multi-tile object footprints;
- canonical wall segments;
- глобальные каталоги объектов и стен;
- chunk streaming, dirty state, leases и in-memory storage;
- базовые runtime-тесты.

Перед editor placement MVP ещё нужны runtime-модели attachments, openings и level connections, а также production authoring serializers. Их API следует проектировать одновременно со схемами Phase 0, чтобы редактор и runtime не получили две несовместимые модели.
