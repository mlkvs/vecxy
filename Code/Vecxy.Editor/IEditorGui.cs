namespace Vecxy.Editor;

public interface IEditorGui
{
    void RegisterWindow(Action draw);
    void RegisterWindow(string name, Action draw);
    void UnregisterWindow(Action draw);
}
