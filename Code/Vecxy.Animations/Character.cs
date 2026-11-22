using Vecxy.Assets;
using Vecxy.Prototypes;
using Vecxy.Rendering;
using Vecxy.Scene;

namespace Vecxy.Animations;

/// <summary>A standalone character authored in Blender, including its per-node scene components.</summary>
[SingleComponent]
public sealed class Character : AComponent
{
    private Model? _model;

    public SceneObject ModelRoot { get; private set; } = null!;
    public Model Model => _model ?? throw new InvalidOperationException("Character has no model.");
    public Animator? Animator { get; private set; }
    public FaceSprites? Face { get; private set; }
    public ModelInstance Instance => ModelRoot.GetComponent<ModelInstance>()!;

    public void Play(string name) =>
        (Animator ?? throw new InvalidOperationException("Character has no animation clips.")).Play(name);

    public override void OnDestroy()
    {
        ModelRoot?.Destroy();
        _model?.Dispose();
    }

    public sealed class Prototype(IAssetsManager assets, ISceneInstantiator models, IAnimationSystem animations)
        : APrototype<Character, Prototype.Options>
    {
        public sealed class Options
        {
            public string Model { get; set; } = "";
            public string Face { get; set; } = "";
            public string Name { get; set; } = "Character";
            public string DefaultAnimation { get; set; } = "";
            public List<NodeBinding> Nodes { get; set; } = [];
        }

        public sealed class NodeBinding
        {
            public int Node { get; set; } = -1;
            public bool IsStatic { get; set; }
            public List<ComponentBinding> Components { get; set; } = [];
        }

        public sealed class ComponentBinding
        {
            public string Type { get; set; } = "";
            public string Data { get; set; } = "{}";
        }

        protected override Character Instantiate(IPrototypeContext context) => new();

        protected override void Configure(Character character, Options options, IPrototypeContext context)
        {
            var scene = RequireContext<ScenePrototypeContext>(context);
            ArgumentException.ThrowIfNullOrWhiteSpace(options.Model);
            using var asset = assets.Load<ModelAsset>(options.Model);
            var model = new Model(asset);
            SceneObject? child = null;
            try
            {
                var owner = scene.Object ?? scene.CreateObject(options.Name);
                if (scene.Object is null)
                {
                    owner.Transform.LocalPosition = scene.Position;
                    owner.Transform.LocalRotation = scene.Rotation;
                    owner.Transform.LocalScale = scene.Scale;
                }
                Animator? animator = null;
                if (model.Animations.Count > 0)
                {
                    var first = string.IsNullOrWhiteSpace(options.DefaultAnimation)
                        ? model.Animations[0] : model.GetAnimation(options.DefaultAnimation);
                    var controller = AnimatorController.Build(options.Name, builder => builder.Layer("Base", layer =>
                    {
                        layer.DefaultState(first.Name, first);
                        foreach (var clip in model.Animations.Where(clip => clip != first))
                            layer.State(clip.Name, clip);
                    }));
                    var animated = animations.Instantiate(scene.Scene, model, controller, options.Name);
                    child = animated.Root;
                    animator = animated.Animator;
                }
                else
                {
                    child = models.InstantiateModel(scene.Scene, model, options.Name);
                }
                child.Enabled = false;
                child.SetParent(owner, worldPositionStays: false);
                var instance = child.GetComponent<ModelInstance>()!;
                foreach (var binding in options.Nodes)
                {
                    var node = instance.GetNode(binding.Node);
                    node.IsStatic = binding.IsStatic;
                    foreach (var component in binding.Components)
                        ScenePrototypeSystem.ApplyAuthoredComponent(scene.Prototypes, node, component.Type, component.Data);
                }
                var face = string.IsNullOrWhiteSpace(options.Face) ? null :
                    scene.Prototypes.Instantiate<FaceSprites>(options.Face,
                        new ScenePrototypeContext { Scene = scene.Scene, Object = child });

                character.ModelRoot = child;
                character.Animator = animator;
                character.Face = face;
                character._model = model;
                owner.AddComponent(character);
                child.Enabled = true;
            }
            catch
            {
                child?.Destroy();
                if (character.SceneObject is { } owner)
                    owner.RemoveComponent(character);
                else
                    model.Dispose();
                throw;
            }
        }
    }
}
