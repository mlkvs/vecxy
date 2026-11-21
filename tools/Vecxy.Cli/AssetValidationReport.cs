using Vecxy.AssetPipeline;

/// <summary>
/// Failure text for missing catalog entries. The build logger hides ordinary
/// command output and only keeps MSBuild errors, so the same lines are written
/// to stderr and to obj/vecxy.asset-errors.txt.
/// </summary>
internal static class AssetValidationReport
{
    internal const string ErrorFileName = "vecxy.asset-errors.txt";

    internal static void Report(string project, IReadOnlyList<MissingAsset> missing, IReadOnlyList<string> packageErrors)
    {
        var messages = Messages(missing, packageErrors);
        var file = Path.Combine(project, "obj", ErrorFileName);
        if (messages.Count == 0)
        {
            if (File.Exists(file)) File.Delete(file);
            Console.WriteLine("Asset validation succeeded.");
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllLines(file, messages);
        Console.Error.WriteLine("BUILD FAILED");
        foreach (var message in messages) Console.Error.WriteLine(message);
    }

    internal static string Summarize(IReadOnlyList<MissingAsset> missing, IReadOnlyList<string> packageErrors) =>
        string.Join(Environment.NewLine, Messages(missing, packageErrors));

    private static List<string> Messages(IReadOnlyList<MissingAsset> missing, IReadOnlyList<string> packageErrors)
    {
        var messages = missing.Select(MissingAssetMessage).ToList();
        messages.AddRange(packageErrors.Select(error => string.Join(" ", error.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))));
        return messages;
    }

    private static string MissingAssetMessage(MissingAsset item)
    {
        var references = item.References.Count == 0
            ? "No C# code references it."
            : "Referenced from " + string.Join(", ", item.References.Select(reference => $"{reference.File}:{reference.Line}")) + ".";
        return $"'{item.Asset.Path}' ({item.Asset.Id:D}) is listed in Assets.manifest, but the file is missing from Assets. " +
               "Deleted assets stay in the manifest, and 'vecxy assets scan' does not remove them. " +
               $"Delete this entry or restore the file. {references}";
    }
}
