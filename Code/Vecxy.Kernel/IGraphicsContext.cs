namespace Vecxy.Kernel;

public interface IGraphicsContext
{
    void MakeCurrent();
    void SwapBuffers();
    nint GetProcAddress(string name);
}
