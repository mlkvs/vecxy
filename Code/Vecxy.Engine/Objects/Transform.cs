using System;
using System.Collections.Generic;
using System.Numerics;

namespace Vecxy.Engine.Objects;

[Serializable]
public class Transform
{
    public string Name { get; set; } = "New Entity";

    public Vector3 Position { get; set; } = Vector3.Zero;
    public Vector3 Rotation { get; set; } = Vector3.Zero;
    public Vector3 Scale { get; set; } = Vector3.One;

    // Иерархия
    public Transform? Parent { get; set; }
    public List<Transform> Children { get; set; } = new();

    // Компоненты (Скрипты)
    public List<Script> Scripts { get; set; } = new();

    public void AddChild(Transform child)
    {
        child.Parent = this;
        Children.Add(child);
    }

    public T AddScript<T>() where T : Script, new()
    {
        var script = new T { Transform = this };
        Scripts.Add(script);
        return script;
    }
}