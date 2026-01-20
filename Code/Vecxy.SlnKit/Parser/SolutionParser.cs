using Tokens;
using Vecxy.IO;

namespace Vecxy.SlnKit;

// TODO: Возможно потом напишем свой Tokenizer
public struct SolutionInfo
{

}

public class SolutionParser
{
    public bool TryParse(string path, out Solution? solution)
    {
        solution = null;

        var tokenizer = new Tokenizer();

        var pattern = @"Microsoft Visual Studio Solution File, Format Version {FormatVersion}";
        var sourceSln = FileReader.ReadToEnd(path);

        var tokenized = tokenizer.Tokenize<Solution>(pattern, sourceSln);

        if (tokenized == null)
        {
            return false;
        }

        solution = tokenized.Value;

        return true;
    }
}