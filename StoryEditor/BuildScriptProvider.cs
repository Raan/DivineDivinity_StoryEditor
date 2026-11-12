using System.IO;
using System.Reflection;

namespace StoryEditor2.Wpf;

internal static class BuildScriptProvider
{
    private const string ResourcePrefix = "StoryEditor.Scripts.";

    public static string EnsureExtracted()
    {
        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StoryEditor", "Scripts");
        Directory.CreateDirectory(directory);

        Extract(ResourcePrefix + "BuildStoryWithMerge.ps1",
            Path.Combine(directory, "BuildStoryWithMerge.ps1"));
        Extract(ResourcePrefix + "LoadStoryCheck.ps1",
            Path.Combine(directory, "LoadStoryCheck.ps1"));

        return Path.Combine(directory, "BuildStoryWithMerge.ps1");
    }

    private static void Extract(string resourceName, string destinationPath)
    {
        using Stream source = typeof(BuildScriptProvider).Assembly
            .GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"The required build resource '{resourceName}' is missing.");

        using var destination = new FileStream(
            destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
        source.CopyTo(destination);
    }
}
