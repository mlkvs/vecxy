namespace Vecxy.Engine;

public static class Env
{
#if DEBUG
    public const bool IsDev = true;
#else
    public const bool IsDev = false;
#endif
}