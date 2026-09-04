namespace Vecxy.Kernel;

/// <summary>Platform clipboard access exposed independently from a window backend.</summary>
public interface IClipboard
{
    string? GetText();
    void SetText(string text);
}

