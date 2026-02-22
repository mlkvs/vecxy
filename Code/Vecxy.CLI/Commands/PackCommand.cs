using Vecxy.Assets.Packer;

namespace Vecxy.CLI;

/*
 *  vecxy pack
 *      -d | --dir ./some/path/folder (default: .)
 *      -o | --output ./some/path/folder (default: ./build)    
 */

public struct PackParameters
{
    [CLIParameter(Name = "dir", Alias = 'd')]
    public string PackDir { get; set; }

    [CLIParameter(Name = "output", Alias = 'o')]
    public string OutputDir { get; set; }
}

public class PackCommand : CLICommandBase<PackParameters>
{
    public override string Name => "pack";

    public override void Execute(PackParameters parameters)
    {
        /*var cfg = new AssetsPackConfig
        {
            PackDir = parameters.PackDir,
            OutputDir = parameters.OutputDir
        };*/

        //AssetsPacker.Pack(cfg);
    }
}