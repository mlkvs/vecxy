using System.Diagnostics;
using Vecxy.Diagnostics;

namespace Vecxy.CLI.Commands
{
    public struct LinkParameters 
    {
        [CLIParameter(Name = "project", Alias = 'p', Default = ".")]
        public string ProjectDir { get; set; }
    }

    public class LinkCommand : CLICommandBase<LinkParameters>
    {
        public override string Name => "link";

        public override void Execute(LinkParameters parameters)
        {
            var projectDir = parameters.ProjectDir == "." ? Directory.GetCurrentDirectory() : parameters.ProjectDir;

            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            var exeFolder = Path.GetDirectoryName(exePath);

            if (exeFolder == null)
            {
                Logger.Error("Not found .exe directory");
                return;
            }

            var tempPropsFile = Path.Combine(projectDir, "Temp", "props", "vecxy.props");

            var props = @$"<?xml version=""1.0"" encoding=""utf-8""?>
<Project>
    <PropertyGroup>
        <EngineDir>{exeFolder}</EngineDir>
    </PropertyGroup>
</Project>";
        }
    }
}
