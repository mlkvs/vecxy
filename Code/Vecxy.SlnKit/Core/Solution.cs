namespace Vecxy.SlnKit;

public class Solution
{
    public string FormatVersion { get; private set; }
    public string PathAbsolute { get; private set; }

    public IReadOnlyList<SolutionProject> Projects => _projects;

    private readonly List<SolutionProject> _projects;
}