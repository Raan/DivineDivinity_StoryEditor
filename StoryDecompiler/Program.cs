using System.Text;
using System.Text.Json;
using StoryCompiler;

namespace StoryDecompiler;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "--decompile")
        {
            try
            {
                StoryReteDump dump = StoryReteExporter.Read(args[1]);
                StoryDivDecompileResult result = StoryDivDecompiler.Decompile(dump);
                string output = Path.GetFullPath(args[2]);
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                File.WriteAllText(output, result.Text, new UTF8Encoding(false));

                Console.WriteLine($"Input: {Path.GetFullPath(args[1])}");
                Console.WriteLine($"Output: {output}");
                Console.WriteLine($"Objects: {dump.DivObjects.Count:N0}");
                Console.WriteLine($"API functions: {dump.Functions.Count(x => x.FunctionKind is 1 or 2 or 3 or 6 or 7):N0}");
                Console.WriteLine($"Goals: {result.GoalCount:N0}");
                Console.WriteLine($"Rules: {result.RuleCount:N0}");
                Console.WriteLine($"Inferred PROC prototypes: {result.InferredPrototypeCount:N0}");
                Console.WriteLine($"Inferred PROC parameters: {result.InferredParameterCount:N0}");
                Console.WriteLine($"Unassigned rules: {result.UnassignedRuleCount:N0}");
                Console.WriteLine($"Unread binary bytes: {dump.RemainingBytes:N0}");
                Console.WriteLine($"Output bytes: {new FileInfo(output).Length:N0}");

                foreach (string diagnostic in result.Diagnostics)
                    Console.Error.WriteLine("WARNING: " + diagnostic);

                return result.Diagnostics.Count == 0 && dump.RemainingBytes == 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("DECOMPILATION FAILED: " + ex);
                return 1;
            }
        }

        if (args.Length == 3 && args[0] == "--dump-rete")
        {
            try
            {
                StoryReteDump dump = StoryReteExporter.Read(args[1]);
                string output = Path.GetFullPath(args[2]);
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                File.WriteAllText(output,
                    JsonSerializer.Serialize(dump, new JsonSerializerOptions { WriteIndented = true }),
                    new UTF8Encoding(false));
                Console.WriteLine($"Rete export: {output}");
                Console.WriteLine($"Nodes: {dump.Nodes.Count:N0}; action nodes: {dump.Nodes.Count(x => x.NodeType == 7):N0}");
                Console.WriteLine($"Remaining bytes: {dump.RemainingBytes:N0}");
                return dump.RemainingBytes == 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("RETE EXPORT FAILED: " + ex);
                return 1;
            }
        }

        Console.WriteLine("StoryDecompiler — Divine Divinity Osiris Story decompiler");
        Console.WriteLine("Usage:");
        Console.WriteLine("  StoryDecompiler --decompile <story.000> <output.div>");
        Console.WriteLine("  StoryDecompiler --dump-rete <story.000> <output.json>");
        return 2;
    }
}
