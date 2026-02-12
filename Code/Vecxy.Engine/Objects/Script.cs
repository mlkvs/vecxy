using System;

namespace Vecxy.Engine.Objects;

[Serializable]
public abstract class Script
{
    // Ссылка на трансформ, к которому прикреплен скрипт
    [NonSerialized]
    public Transform Transform;

    public virtual void OnStart() { }
    public virtual void OnUpdate(float deltaTime) { }
    public virtual void OnDestroy() { }
}