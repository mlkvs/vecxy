namespace Vecxy.SlnKit;

public enum PROJECT_TYPE : byte
{
    UNDEFINED = 0,

    CLASS_LIBRARY = 1,
}

public class SolutionProject
{
    #region fields

    public Guid Id { get; }
    public PROJECT_TYPE Type { get; }
    public string Name { get; private set; }

    public string PathRelative { get; private set; }
    public string PathAbsolute { get; private set; }

    public IReadOnlyList<Configuration> Configurations { get; }

    private static readonly IReadOnlyDictionary<Guid, PROJECT_TYPE> _projectTypes = new Dictionary<Guid, PROJECT_TYPE>
    {
        { Guid.Parse("FAE04EC0-301F-11D3-BF4B-00C04F79EFBC"), PROJECT_TYPE.CLASS_LIBRARY}
    };

    #endregion

    #region public api

    public static PROJECT_TYPE DefineByGuid(Guid id)
    {
        return _projectTypes.GetValueOrDefault(id, PROJECT_TYPE.UNDEFINED);
    }

    #endregion

    #region nested types

    public class Configuration
    {

    }

    #endregion
}