using System;
using System.Text.Json;

namespace Vecxy.Engine.Objects;

public class Prefab
{
    public string PrefabName { get; set; }

    // Храним данные объекта в сериализованном виде как "матрицу"
    public string SerializedData { get; private set; }

    public Prefab(Transform source)
    {
        PrefabName = source.Name;
        // Сериализуем объект при создании префаба
        SerializedData = JsonSerializer.Serialize(source);
    }

    // Метод создания копии из префаба (Instantiate)
    public Transform Instantiate()
    {
        var copy = JsonSerializer.Deserialize<Transform>(SerializedData);
        if (copy == null) throw new Exception("Failed to instantiate prefab.");

        // Восстанавливаем ссылки Transform в скриптах после десериализации
        FixReferences(copy);
        return copy;
    }

    private void FixReferences(Transform t)
    {
        foreach (var script in t.Scripts) script.Transform = t;
        foreach (var child in t.Children) FixReferences(child);
    }
}