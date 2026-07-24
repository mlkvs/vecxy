using System.Numerics;
using System.Text.Json;

namespace Vecxy.Engine._Legacy;

public sealed class Prefab
{
    public string PrefabName { get; }
    public string SerializedData { get; }

    public Prefab(SceneObject source)
    {
        PrefabName = source.Name;
        SerializedData = JsonSerializer.Serialize(Capture(source));
    }

    public SceneObject Instantiate() => Restore(JsonSerializer.Deserialize<Snapshot>(SerializedData)
        ?? throw new InvalidDataException("Prefab data is invalid."));

    private static Snapshot Capture(SceneObject source) => new(source.Name, source.IsActive,
        source.Transform.Position, source.Transform.Rotation, source.Transform.Scale,
        source.Children.Select(Capture).ToArray());

    private static SceneObject Restore(Snapshot snapshot)
    {
        var result = new SceneObject(snapshot.Name) { IsActive = snapshot.IsActive };
        result.Transform.Position = snapshot.Position;
        result.Transform.Rotation = snapshot.Rotation;
        result.Transform.Scale = snapshot.Scale;
        foreach (var child in snapshot.Children) result.AddChild(Restore(child));
        return result;
    }

    private sealed record Snapshot(string Name, bool IsActive, Vector3 Position, Quaternion Rotation,
        Vector3 Scale, Snapshot[] Children);
}
