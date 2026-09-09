using Autofac;
using Vecxy.Kernel;
using Vecxy.Rendering;
using Vecxy.Scene;

namespace Vecxy.Animations;

public interface IAnimationSystem
{
    AnimatedModelInstance Instantiate(
        SceneInstance scene,
        Model model,
        AnimatorController controller,
        string? name = null,
        Material? fallbackMaterial = null);
}

public sealed class AnimatedModelInstance
{
    private readonly ModelInstance _modelInstance;

    public SceneObject Root { get; }
    public Animator Animator { get; }
    public Model Model => _modelInstance.Model;

    internal AnimatedModelInstance(SceneObject root, Animator animator, ModelInstance modelInstance)
    {
        Root = root;
        Animator = animator;
        _modelInstance = modelInstance;
    }

    public SceneObject GetNode(string name) => _modelInstance.GetNode(name);
    public SceneObject GetNode(int index) => _modelInstance.GetNode(index);
    public Transform GetBone(string name) => GetNode(name).Transform;
}

public sealed class AnimationsModule(ISceneInstantiator sceneInstantiator) : IModule, IAnimationSystem
{
    public sealed class Definition : AModuleDefinition<AnimationsModule>
    {
        protected override IReadOnlyList<Type> Exports => [typeof(IAnimationSystem)];

        public override void RegisterGlobal(ContainerBuilder builder)
        {
            builder.RegisterType<AnimationSceneSystem>()
                .AsSelf()
                .As<ISceneSystem>()
                .SingleInstance();
        }

        protected override void RegisterModule(ContainerBuilder builder)
        {
            builder.RegisterType<AnimationsModule>().AsSelf().SingleInstance();
        }
    }

    public void OnInitialize() { }
    public void OnShutdown() { }
    public void Dispose() { }

    public AnimatedModelInstance Instantiate(
        SceneInstance scene,
        Model model,
        AnimatorController controller,
        string? name = null,
        Material? fallbackMaterial = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(controller);
        var root = sceneInstantiator.InstantiateModel(scene, model, name, fallbackMaterial);
        try
        {
            var modelInstance = root.GetComponent<ModelInstance>()!;
            var animator = root.AddComponent(new Animator(controller));
            return new AnimatedModelInstance(root, animator, modelInstance);
        }
        catch
        {
            root.Destroy();
            throw;
        }
    }
}

internal sealed class AnimationSceneSystem : ASceneSystem
{
    private readonly HashSet<Animator> _animators = [];
    private readonly List<Animator> _snapshot = [];

    public override void OnComponentAdded(SceneObject sceneObject, AComponent component)
    {
        if (component is Animator animator)
            _animators.Add(animator);
    }

    public override void OnComponentRemoved(SceneObject sceneObject, AComponent component)
    {
        if (component is Animator animator)
            _animators.Remove(animator);
    }

    public override void OnSceneDetached(SceneInstance sceneInstance)
    {
        _animators.RemoveWhere(animator =>
            animator.IsDestroyed || ReferenceEquals(animator.SceneObject?.SceneInstance, sceneInstance));
    }

    public override void Update(SceneInstance sceneInstance, float deltaTime)
    {
        _snapshot.Clear();
        _snapshot.AddRange(_animators);
        foreach (var animator in _snapshot)
        {
            if (!animator.IsDestroyed &&
                animator.SceneObject is { } sceneObject &&
                ReferenceEquals(sceneObject.SceneInstance, sceneInstance))
            {
                animator.Evaluate(deltaTime);
            }
        }
    }
}
