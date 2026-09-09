using Vecxy.Animations;
using Vecxy.Assets;
using Vecxy.Rendering;
using Vecxy.Scene;

var assetsDirectory = Path.Combine(AppContext.BaseDirectory, "Assets");
using var assets = new AssetsModule(new AssetsModule.Options
{
    AssetsDirectory = assetsDirectory,
    HotReloadEnabled = false
});
assets.OnInitialize();

using var fox = assets.Load<ModelAsset>("Models/Fox.glb");
Check(!fox.HasError, fox.Error?.ToString() ?? "Fox import failed");
Check(fox.Value.Skins.Count == 1, "Fox skin count");
Check(fox.Value.Skins[0].Joints.Count == 24, "Fox joint count");
Check(fox.Value.Skins[0].InverseBindMatrices.Count == 24, "Fox inverse bind count");
Check(fox.Value.Meshes.SelectMany(mesh => mesh.Primitives).All(primitive => primitive.IsSkinned),
    "Fox primitives have skin attributes");
Check(fox.Value.Animations.Select(animation => animation.Name)
    .SequenceEqual(["Survey", "Walk", "Run"]), "Fox clip names");
Check(fox.Value.Animations.All(animation => animation.Duration > 0.0f && animation.Channels.Count > 0),
    "Fox clips contain channels");

var parents = Enumerable.Repeat(-1, fox.Value.Nodes.Count).ToArray();
for (var parent = 0; parent < fox.Value.Nodes.Count; parent++)
{
    foreach (var child in fox.Value.Nodes[parent].Children)
        parents[child] = parent;
}
var world = new System.Numerics.Matrix4x4[fox.Value.Nodes.Count];
var calculated = new bool[fox.Value.Nodes.Count];
System.Numerics.Matrix4x4 GetWorld(int index)
{
    if (calculated[index])
        return world[index];
    world[index] = parents[index] < 0
        ? fox.Value.Nodes[index].LocalTransform
        : fox.Value.Nodes[index].LocalTransform * GetWorld(parents[index]);
    calculated[index] = true;
    return world[index];
}
var skinnedNodeIndex = Enumerable.Range(0, fox.Value.Nodes.Count)
    .Single(index => fox.Value.Nodes[index].SkinIndex is not null);
Check(System.Numerics.Matrix4x4.Invert(GetWorld(skinnedNodeIndex), out var inverseMesh),
    "Fox mesh transform is invertible");
for (var joint = 0; joint < fox.Value.Skins[0].Joints.Count; joint++)
{
    var palette = fox.Value.Skins[0].InverseBindMatrices[joint] *
                  GetWorld(fox.Value.Skins[0].Joints[joint]) * inverseMesh;
    Check(MatrixNearIdentity(palette, 0.002f), $"Fox bind palette joint {joint}");
}

var bindPose = fox.Value.Nodes.Select(node => NodePose.FromMatrix(node.LocalTransform)).ToArray();
var sampling = new AnimationSamplingWorkspace(bindPose);
var sample = sampling.Sample(
    new ClipMotion(fox.Value.Animations[1]),
    0.5f,
    new Dictionary<AnimatorParameter, object>(),
    null);
Check(sample.Written.Count(value => value) > 0, "clip sampling writes animated nodes");
Check(sample.Pose.All(pose =>
    float.IsFinite(pose.Position.X) &&
    float.IsFinite(pose.Rotation.W) &&
    float.IsFinite(pose.Scale.X)), "sampled pose is finite");

var speed = new AnimatorFloat("Speed");
var moving = new AnimatorBool("Moving");
var survey = new AnimatorTrigger("Survey");
var controller = AnimatorController.Build("Test", graph =>
{
    graph.Parameter(speed).Parameter(moving).Parameter(survey);
    graph.Layer("Base", layer =>
    {
        layer.DefaultState("Rest", fox.Value.Animations[0]);
        layer.State("Move", new BlendTree1D(speed)
            .At(0.0f, fox.Value.Animations[1])
            .At(1.0f, fox.Value.Animations[2]));
        layer.From("Rest").To("Move").When(moving, true);
        layer.AnyState().To("Rest").When(survey).Duration(0.05f);
    });
});
Check(controller.Parameters.Count == 3, "controller parameter count");
Check(controller.Layers[0].States.Count == 2, "controller state count");

using var runtimeModel = new Model(fox);
var scene = (SceneInstance)Activator.CreateInstance(
    typeof(SceneInstance),
    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
    binder: null,
    args: [new EmptyScene()],
    culture: null)!;
var rootObject = scene.CreateObject("Animated model");
var nodeObjects = new SceneObject?[runtimeModel.Nodes.Count];
foreach (var rootNode in runtimeModel.RootNodes)
    InstantiateNode(rootNode, rootObject);
var modelInstance = (ModelInstance)Activator.CreateInstance(
    typeof(ModelInstance),
    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
    binder: null,
    args: [runtimeModel, nodeObjects],
    culture: null)!;
rootObject.AddComponent(modelInstance);
typeof(SceneInstance).GetMethod("Activate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
    .Invoke(scene, null);
var animator = rootObject.AddComponent(new Animator(controller));
animator.SetFloat(speed, 0.75f);
animator.SetBool(moving, true);
animator.Evaluate(0.1f);
Check(animator.IsInTransition(), "state transition started");
animator.Evaluate(0.2f);
Check(animator.GetCurrentState() == "Move", "state transition completed");
animator.SetTrigger(survey);
animator.Evaluate(0.01f);
Check(!animator.IsTriggerSet(survey), "trigger consumed by transition");

void InstantiateNode(int nodeIndex, SceneObject parentObject)
{
    var source = runtimeModel.Nodes[nodeIndex];
    var nodeObject = scene.CreateObject(source.Name);
    nodeObject.SetParent(parentObject, worldPositionStays: false);
    nodeObject.Transform.LocalMatrix = source.LocalTransform;
    nodeObjects[nodeIndex] = nodeObject;
    foreach (var child in source.Children)
        InstantiateNode(child, nodeObject);
}

Console.WriteLine("All Vecxy.Animations checks passed.");

static void Check(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException($"Test failed: {message}");
}

static bool MatrixNearIdentity(System.Numerics.Matrix4x4 matrix, float tolerance)
{
    var identity = System.Numerics.Matrix4x4.Identity;
    return
        MathF.Abs(matrix.M11 - identity.M11) <= tolerance &&
        MathF.Abs(matrix.M12 - identity.M12) <= tolerance &&
        MathF.Abs(matrix.M13 - identity.M13) <= tolerance &&
        MathF.Abs(matrix.M14 - identity.M14) <= tolerance &&
        MathF.Abs(matrix.M21 - identity.M21) <= tolerance &&
        MathF.Abs(matrix.M22 - identity.M22) <= tolerance &&
        MathF.Abs(matrix.M23 - identity.M23) <= tolerance &&
        MathF.Abs(matrix.M24 - identity.M24) <= tolerance &&
        MathF.Abs(matrix.M31 - identity.M31) <= tolerance &&
        MathF.Abs(matrix.M32 - identity.M32) <= tolerance &&
        MathF.Abs(matrix.M33 - identity.M33) <= tolerance &&
        MathF.Abs(matrix.M34 - identity.M34) <= tolerance &&
        MathF.Abs(matrix.M41 - identity.M41) <= tolerance &&
        MathF.Abs(matrix.M42 - identity.M42) <= tolerance &&
        MathF.Abs(matrix.M43 - identity.M43) <= tolerance &&
        MathF.Abs(matrix.M44 - identity.M44) <= tolerance;
}

public sealed class EmptyScene : IScene;
