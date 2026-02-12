namespace Vecxy.Scripting;

[JSGlobal(Name = "host")]
public class JSHost
{
    private readonly List<dynamic> _scripts = new();

    public void registerScriptObject(dynamic script)
    {
        _scripts.Add(script);
        // Console.WriteLine("DEBUG: Скрипт успешно зарегистрирован в C#");
    }

    public void UpdateAll(float dt)
    {
        // Итерируемся с конца, чтобы была возможность безопасно удалять объекты
        for (int i = _scripts.Count - 1; i >= 0; i--)
        {
            try
            {
                _scripts[i].update(dt);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating script at index {i}: {ex.Message}");
            }
        }
    }
}