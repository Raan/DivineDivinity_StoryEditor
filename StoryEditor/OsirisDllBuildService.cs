using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Text;

namespace StoryEditor2.Wpf;

internal sealed record OsirisBuildResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    string OutputPath,
    string BuildLogPath,
    string ValidationLogPath)
{
    public bool Success => ExitCode == 0 && File.Exists(OutputPath);
}

internal static class OsirisDllBuildService
{
    public static async Task<OsirisBuildResult> BuildAsync(
        string gameRoot,
        string osirisDllPath,
        string baseStoryPath,
        string sourcePath,
        string outputStoryPath,
        string logPath,
        string workDirectory,
        string scriptPath)
    {
        string powershell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "SysWOW64", "WindowsPowerShell", "v1.0", "powershell.exe");

        if (!File.Exists(powershell))
            throw new FileNotFoundException(
                "32-bit Windows PowerShell was not found. OsirisDLL requires a 32-bit host.",
                powershell);

        if (!File.Exists(scriptPath))
            throw new FileNotFoundException("The verified Osiris build script was not found.", scriptPath);

        var start = new ProcessStartInfo
        {
            FileName = powershell,
            WorkingDirectory = Path.GetDirectoryName(scriptPath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-ExecutionPolicy");
        start.ArgumentList.Add("Bypass");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(scriptPath);
        AddArgument(start, "-GameRoot", gameRoot);
        AddArgument(start, "-OsirisDllPath", osirisDllPath);
        AddArgument(start, "-BaseStoryPath", baseStoryPath);
        AddArgument(start, "-SourcePath", sourcePath);
        AddArgument(start, "-OutputStoryPath", outputStoryPath);
        AddArgument(start, "-LogPath", logPath);
        AddArgument(start, "-WorkDirectory", workDirectory);

        using var process = new Process { StartInfo = start };
        if (!process.Start())
            throw new InvalidOperationException("Could not start the 32-bit Osiris build process.");

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().ConfigureAwait(false);
        string stdout = await stdoutTask.ConfigureAwait(false);
        string stderr = await stderrTask.ConfigureAwait(false);

        return new OsirisBuildResult(
            process.ExitCode,
            stdout,
            stderr,
            outputStoryPath,
            logPath,
            logPath + ".validation.log");
    }

    private static void AddArgument(ProcessStartInfo start, string name, string value)
    {
        start.ArgumentList.Add(name);
        start.ArgumentList.Add(value);
    }
}
