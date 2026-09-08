# Vecxy prototypes

`IPrototypes` is the public facade for code-defined factories and `.prototype` assets. A target exposes its
factory as a nested `Prototype` class; no per-type registration or string identifier is required.

```csharp
public sealed class Item
{
    public sealed class Prototype : APrototype<Item, Prototype.Options>
    {
        public sealed class Options
        {
            public int Amount { get; set; } = 1;
        }

        protected override Item Instantiate(IPrototypeContext context) => new();

        protected override void Configure(Item target, Options options)
        {
            // Apply options to target.
        }
    }
}
```

A prototype system declares which target base type it owns. The module discovers both prototypes and systems
from application assemblies and selects the most specific system by inheritance.

```csharp
public sealed class ItemContext : APrototypeContext;
public sealed class ItemSystem : APrototypeSystem<Item, ItemContext>;
```

Prototype assets use YAML and the final `.prototype` extension:

```yaml
format: 1
type: Game.Item
data:
  amount: 3
```

They can inherit another file and override only selected values:

```yaml
format: 1
type: Game.Item
prototype: BaseItem.prototype
data:
  amount: 10
```

Create instances directly or from an asset:

```csharp
var direct = prototypes.Instantiate<Item>(new ItemContext(), new Item.Prototype.Options { Amount = 2 });
var saved = prototypes.Instantiate<Item>(Assets.Prototypes.Item, new ItemContext());
```

`ScenePrototypeContext` builds disabled objects and commits them only after configuration. `SceneObject.Prototype`
supports component and child prototype references. `UiPrototypeContext` builds UI elements separately from the
scene and supports nested child prototype references. Custom systems get the same discovery, validation,
serialization, inheritance, override, nesting, and cycle-detection pipeline.
