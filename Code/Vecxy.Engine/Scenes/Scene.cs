using System;
using System.Collections.Generic;
using System.Text;
using Vecxy.Engine.Objects;

namespace Vecxy.Engine.Scene;

public enum SCENE_MODE : byte
{
    SINGLE = 0,
    ADDITIVE = 1,
}

[Serializable]
public class Scene
{
    public string Name { get; set; }
    public SCENE_MODE Mode { get; set; }

    private readonly List<Transform> _rootTransforms = new();
    public List<Transform> RootTransforms => _rootTransforms;

    public Scene(string name, SCENE_MODE mode = SCENE_MODE.SINGLE)
    {
        Name = name;
        Mode = mode;
    }

    public void OnLoad()
    {
        // Инициализация скриптов при загрузке
        foreach (var transform in _rootTransforms)
            InitializeTransform(transform);
    }

    private void InitializeTransform(Transform t)
    {
        foreach (var script in t.Scripts) script.OnStart();
        foreach (var child in t.Children) InitializeTransform(child);
    }

    public void OnTick(float deltaTime)
    {
        foreach (var transform in _rootTransforms)
            UpdateTransform(transform, deltaTime);
    }

    private void UpdateTransform(Transform t, float deltaTime)
    {
        foreach (var script in t.Scripts) script.OnUpdate(deltaTime);
        foreach (var child in t.Children) UpdateTransform(child, deltaTime);
    }
}