using System.Globalization;
using System.Text;

namespace StoryCompiler;

internal sealed record StoryDivDecompileResult(
    string Text,
    int RuleCount,
    int GoalCount,
    int UnassignedRuleCount,
    IReadOnlyList<string> Diagnostics);

internal static class StoryDivDecompiler
{
    private static readonly Dictionary<uint, string> RuleTypeNames = new()
    {
        [1] = "INTEGER",
        [2] = "REAL",
        [3] = "STRING",
        [4] = "NPC",
        [5] = "OBJECT",
        [6] = "DIALOG",
        [7] = "REGION",
        [8] = "LOCATION",
        [9] = "NPC_CLASS",
        [10] = "OBJECT_CLASS",
        [11] = "DIALOG_EVENT",
        [12] = "ENGINE_GAME",
        [13] = "FUNCTION_Math",
        [15] = "SREGION"
    };

    private static readonly Dictionary<uint, string> ApiTypeNames = new()
    {
        [1] = "INTEGER",
        [2] = "REAL",
        [3] = "STRING",
        [4] = "NPC",
        [5] = "OBJECT",
        [6] = "DIALOG",
        [7] = "REGION",
        [8] = "LOCATION",
        [9] = "NPC_CLASS",
        [10] = "OBJECT_CLASS",
        [11] = "DIALOG_EVENT",
        [12] = "ENGINE",
        [13] = "FUNCTION",
        [15] = "SREGION"
    };

    private static readonly Dictionary<byte, string> ApiKindNames = new()
    {
        [1] = "event",
        [2] = "query",
        [3] = "call",
        [6] = "sysquery",
        [7] = "syscall"
    };

    private static readonly byte[] ApiKindOrder = [3, 1, 2, 7, 6];

    private static readonly string[] TypeDeclarations =
    [
        "NPC, 4",
        "OBJECT, 5",
        "DIALOG, 6",
        "REGION, 7",
        "LOCATION, 8",
        "NPC_CLASS, 9",
        "OBJECT_CLASS, 10",
        "DIALOG_EVENT, 11",
        "ENGINE, 12",
        "FUNCTION, 13",
        "SREGION, 15"
    ];

    private sealed class RuleContext
    {
        public required StoryReteNodeRecord ActionNode { get; init; }
        public required uint GoalId { get; init; }
        public required StoryReteNodeRecord HeaderNode { get; init; }
        public required uint HeaderAdaptorId { get; init; }
        public StoryFunctionRecord? HeaderFunction { get; init; }
        public bool IsProc => HeaderFunction?.FunctionKind == 5;
        public HashSet<int> ProcVariableIds { get; } = [];
    }


    public static StoryDivDecompileResult Decompile(StoryReteDump dump)
    {
        ArgumentNullException.ThrowIfNull(dump);

        var diagnostics = new List<string>();
        if (HasLikelySavePaddedAdaptorMappings(dump.Adaptors))
        {
            diagnostics.Add("Rete adaptor mappings appear to have been zero-padded to five columns by Osiris Load/Save. Some original trailing argument counts cannot be recovered unambiguously from this saved story; KB argument text is best-effort.");
        }
        if (dump.RemainingBytes != 0)
            diagnostics.Add($"The binary parser left {dump.RemainingBytes} unread byte(s).");

        var nodeMap = dump.Nodes.ToDictionary(x => x.NodeId);
        var adaptorMap = dump.Adaptors.ToDictionary(x => x.AdaptorIndex);
        var functionMap = dump.Functions
            .Where(x => x.NodeId > 0)
            .GroupBy(x => checked((int)x.NodeId))
            .ToDictionary(x => x.Key, x => x.First());

        var knownGoals = dump.Goals.Select(x => x.Metadata).ToHashSet();
        var contexts = new List<RuleContext>();
        var unassigned = 0;

        foreach (var actionNode in dump.Nodes
                     .Where(x => x.NodeType == 7)
                     .OrderBy(x => x.SourceLine ?? uint.MaxValue)
                     .ThenBy(x => x.NodeId))
        {
            if (actionNode.ParentNodeIndex is null || actionNode.AdaptorIndex is null)
            {
                diagnostics.Add($"Action node {actionNode.NodeId} has no parent/adaptor reference.");
                unassigned++;
                continue;
            }

            if (!TryResolveGoalId(actionNode, nodeMap, out uint goalId, out string? goalError))
            {
                diagnostics.Add($"Action node {actionNode.NodeId}: {goalError}");
                unassigned++;
                continue;
            }

            if (!knownGoals.Contains(goalId))
            {
                diagnostics.Add($"Action node {actionNode.NodeId} resolves to unknown Goal {goalId}.");
                unassigned++;
                continue;
            }

            if (!TryGetLeftmostLeaf(
                    (int)actionNode.ParentNodeIndex.Value,
                    actionNode.AdaptorIndex.Value,
                    nodeMap,
                    out StoryReteNodeRecord? headerNode,
                    out uint headerAdaptorId))
            {
                diagnostics.Add($"Action node {actionNode.NodeId} has an invalid leftmost Rete leaf.");
                unassigned++;
                continue;
            }

            functionMap.TryGetValue(headerNode!.NodeId, out StoryFunctionRecord? headerFunction);
            var context = new RuleContext
            {
                ActionNode = actionNode,
                GoalId = goalId,
                HeaderNode = headerNode!,
                HeaderAdaptorId = headerAdaptorId,
                HeaderFunction = headerFunction
            };

            if (context.IsProc && adaptorMap.TryGetValue(headerAdaptorId, out StoryAdaptorRecord? headerAdaptor))
            {
                foreach (int variableId in GetUsedProcVariableIds(headerAdaptor))
                    context.ProcVariableIds.Add(variableId);
            }

            contexts.Add(context);
        }

        var rulesByGoal = contexts
            .GroupBy(x => x.GoalId)
            .ToDictionary(x => x.Key, x => x.ToList());

        var output = new StringBuilder(capacity: 2_500_000);
        RenderTypeDeclarations(output);
        RenderObjects(output, dump.DivObjects);
        RenderApiDeclarations(output, dump.Functions);
        output.AppendLine();
        output.Append("version \"").Append(EscapeQuotedString(dump.CompilerVersion)).AppendLine("\"");
        output.AppendLine();

        // DBases are the current Osiris fact-store snapshot, not source declarations.
        // Re-emitting those live tuples as INIT actions would duplicate runtime state.
        RenderGoals(output, dump.Goals, rulesByGoal, nodeMap, adaptorMap, functionMap, diagnostics);
        RenderGoalHierarchy(output, dump.Goals);

        // Keep the separate file-level action list visible as a diagnostic until its
        // source-language representation is independently established.
        if (dump.GlobalActions.Count > 0)
            diagnostics.Add($"The binary contains {dump.GlobalActions.Count} global action(s); this section is not emitted yet.");

        return new StoryDivDecompileResult(
            output.ToString(),
            contexts.Count,
            dump.Goals.Count,
            unassigned,
            diagnostics);
    }

    private static bool TryResolveGoalId(
        StoryReteNodeRecord actionNode,
        IReadOnlyDictionary<int, StoryReteNodeRecord> nodeMap,
        out uint goalId,
        out string? error)
    {
        goalId = 0;
        error = null;

        if (actionNode.ParentNodeIndex is null ||
            !nodeMap.TryGetValue((int)actionNode.ParentNodeIndex.Value, out StoryReteNodeRecord? parent))
        {
            error = "the parent node does not exist.";
            return false;
        }

        if (parent.NodeType is 4 or 5 or 6)
        {
            if (parent.ChildConnection is null)
            {
                error = $"parent node {parent.NodeId} has no child connection.";
                return false;
            }

            goalId = parent.ChildConnection.C;
            return true;
        }

        if (parent.NodeType is 1 or 2)
        {
            StoryConnection? connection = parent.Connections?
                .FirstOrDefault(x => x.A == (uint)actionNode.NodeId);

            if (connection is null)
            {
                error = $"start node {parent.NodeId} has no connection to action node {actionNode.NodeId}.";
                return false;
            }

            goalId = connection.C;
            return true;
        }

        error = $"unsupported parent node type {parent.NodeType} at node {parent.NodeId}.";
        return false;
    }

    private static bool TryGetLeftmostLeaf(
        int nodeId,
        uint incomingAdaptorId,
        IReadOnlyDictionary<int, StoryReteNodeRecord> nodeMap,
        out StoryReteNodeRecord? leaf,
        out uint adaptorId)
    {
        leaf = null;
        adaptorId = incomingAdaptorId;

        for (int depth = 0; depth < 80; depth++)
        {
            if (!nodeMap.TryGetValue(nodeId, out StoryReteNodeRecord? node))
                return false;

            if (node.NodeType is 4 or 5)
            {
                if (node.LeftNodeIndex is null || node.LeftAdaptorIndex is null)
                    return false;

                nodeId = (int)node.LeftNodeIndex.Value;
                adaptorId = node.LeftAdaptorIndex.Value;
                continue;
            }

            if (node.NodeType == 6)
            {
                if (node.HandleA is null || node.HandleB is null)
                    return false;

                nodeId = (int)node.HandleA.Value;
                adaptorId = node.HandleB.Value;
                continue;
            }

            leaf = node;
            return true;
        }

        return false;
    }

    // Osiris Load/Save pads every adaptor mapping vector to a five-byte boundary.
    // This global signature distinguishes saved histories from the original startup story.
    internal static bool HasLikelySavePaddedAdaptorMappings(IReadOnlyList<StoryAdaptorRecord> adaptors) =>
        adaptors.Count >= 1_000 &&
        adaptors.All(x => x.ArgumentVariableIds.Length % 5 == 0) &&
        adaptors.Count(x => x.ArgumentVariableIds.Length > 0 && x.ArgumentVariableIds[^1] == 0) > adaptors.Count / 2;
    // The binding table stores unique variable IDs; wildcard 255 is intentionally absent.
    // Using it instead of scanning the mapping avoids treating zero-fill as _Var1.
    internal static HashSet<int> GetUsedProcVariableIds(StoryAdaptorRecord adaptor) =>
        adaptor.VariableBindings
            .Select(x => (int)x.VariableId)
            .ToHashSet();
    private static void RenderTypeDeclarations(StringBuilder output)
    {
        foreach (string declaration in TypeDeclarations)
            output.Append("type { ").Append(declaration).AppendLine(" }");
        output.AppendLine();
    }

    private static void RenderObjects(StringBuilder output, IReadOnlyList<StoryDivObject> objects)
    {
        foreach (StoryDivObject item in objects)
        {
            string fields = string.Join(", ", item.Fields.Select(x => x.ToString(CultureInfo.InvariantCulture)));
            output.Append("object { ")
                .Append(item.Name).Append(", ")
                .Append(item.Flags.ToString(CultureInfo.InvariantCulture))
                .Append(", ( ").Append(fields).AppendLine(" ) }");
        }

        output.AppendLine();
    }

    private static void RenderApiDeclarations(
        StringBuilder output,
        IReadOnlyList<StoryFunctionRecord> functions)
    {
        foreach (byte kind in ApiKindOrder)
        {
            foreach (StoryFunctionRecord function in functions
                         .Where(x => x.FunctionKind == kind)
                         .OrderBy(x => x.Name, StringComparer.Ordinal)
                         .ThenBy(x => x.Index))
            {
                var arguments = new List<string>();
                for (int i = 0; i < function.TypeValues.Length; i++)
                {
                    uint typeCode = function.TypeValues[i];
                    if (!ApiTypeNames.TryGetValue(typeCode, out string? typeName))
                        throw new InvalidDataException($"Unknown API type code {typeCode} in {function.Name}.");

                    bool isOut = function.ByteData.Length > 0 &&
                                 (function.ByteData[0] & (0x80 >> i)) != 0;
                    string direction = isOut ? "out" : "in";
                    arguments.Add($"[{direction}]({typeName})_Arg{i}");
                }

                output.Append(ApiKindNames[kind]).Append(' ')
                    .Append(function.Name).Append('(')
                    .Append(string.Join(", ", arguments)).Append(") (")
                    .Append(function.FunctionNodeId.ToString(CultureInfo.InvariantCulture)).Append(", ")
                    .Append(function.Field5.ToString(CultureInfo.InvariantCulture)).Append(", ")
                    .Append(function.Field6.ToString(CultureInfo.InvariantCulture)).Append(", ")
                    .Append(function.Field7.ToString(CultureInfo.InvariantCulture)).AppendLine(")");
            }
        }
    }

    private static void RenderGoals(
        StringBuilder output,
        IReadOnlyList<StoryGoalRecord> goals,
        IReadOnlyDictionary<uint, List<RuleContext>> rulesByGoal,
        IReadOnlyDictionary<int, StoryReteNodeRecord> nodeMap,
        IReadOnlyDictionary<uint, StoryAdaptorRecord> adaptorMap,
        IReadOnlyDictionary<int, StoryFunctionRecord> functionMap,
        List<string> diagnostics)
    {
        foreach (StoryGoalRecord goal in goals.OrderBy(x => x.Metadata))
        {
            uint goalId = goal.Metadata;
            output.Append("Goal(").Append(goalId.ToString(CultureInfo.InvariantCulture))
                .Append(").Title(\"").Append(EscapeQuotedString(goal.Name)).AppendLine("\");");
            output.Append("Goal(").Append(goalId.ToString(CultureInfo.InvariantCulture)).AppendLine(") {");

            output.AppendLine("INIT {");
            foreach (string actionLine in RenderActionList(goal.InitActions))
                output.AppendLine(actionLine);
            output.AppendLine("}");

            output.AppendLine("KB {");
            if (rulesByGoal.TryGetValue(goalId, out List<RuleContext>? goalRules))
            {
                foreach (RuleContext context in goalRules)
                {
                    IReadOnlyList<string> ruleLines = RenderRule(
                        context, nodeMap, adaptorMap, functionMap, diagnostics);

                    foreach (string line in ruleLines)
                        output.AppendLine(line);
                    output.AppendLine();
                }
            }
            output.AppendLine("}");

            output.AppendLine("EXIT {");
            foreach (string actionLine in RenderActionList(goal.ExitActions))
                output.AppendLine(actionLine);
            output.AppendLine("}");

            output.AppendLine("}");
            output.AppendLine();
        }
    }

    private static IReadOnlyList<string> RenderRule(
        RuleContext context,
        IReadOnlyDictionary<int, StoryReteNodeRecord> nodeMap,
        IReadOnlyDictionary<uint, StoryAdaptorRecord> adaptorMap,
        IReadOnlyDictionary<int, StoryFunctionRecord> functionMap,
        List<string> diagnostics)
    {
        var lines = new List<string>();
        uint rootId = context.ActionNode.ParentNodeIndex!.Value;
        uint rootAdaptorId = context.ActionNode.AdaptorIndex!.Value;

        if (context.IsProc)
        {
            lines.Add("PROC");
            if (!adaptorMap.TryGetValue(context.HeaderAdaptorId, out StoryAdaptorRecord? headerAdaptor))
            {
                diagnostics.Add($"PROC action node {context.ActionNode.NodeId} has missing header adaptor {context.HeaderAdaptorId}.");
                lines.Add(context.HeaderNode.Name + "(_)");
            }
            else
            {
                var reservedVariableIds = context.ActionNode.LocalVariables?
                    .Select(variable => variable.LocalVariableIndex)
                    .ToHashSet() ?? new HashSet<int>();
                reservedVariableIds.UnionWith(context.ProcVariableIds);
                string[] arguments = RenderAdaptorArguments(
                    headerAdaptor,
                    context.HeaderFunction,
                    context.ProcVariableIds,
                    isProcHeader: true,
                    reservedVariableIds);
                lines.Add(context.HeaderNode.Name + "(" + string.Join(", ", arguments) + ")");
            }

            List<string> conditions = RenderCondition(
                (int)rootId, rootAdaptorId, context, nodeMap, adaptorMap, functionMap, 0, diagnostics);
            if (conditions.Count > 1)
                lines.AddRange(conditions.Skip(1));
        }
        else
        {
            lines.Add("IF");
            List<string> conditions = RenderCondition(
                (int)rootId, rootAdaptorId, context, nodeMap, adaptorMap, functionMap, 0, diagnostics);
            lines.AddRange(conditions);
        }

        lines.Add("THEN");
        foreach (string actionLine in RenderRuleActions(context))
            lines.Add(actionLine);

        return lines;
    }

    private static List<string> RenderCondition(
        int nodeId,
        uint incomingAdaptorId,
        RuleContext context,
        IReadOnlyDictionary<int, StoryReteNodeRecord> nodeMap,
        IReadOnlyDictionary<uint, StoryAdaptorRecord> adaptorMap,
        IReadOnlyDictionary<int, StoryFunctionRecord> functionMap,
        int depth,
        List<string> diagnostics)
    {
        if (depth > 80)
        {
            diagnostics.Add($"Condition recursion depth exceeded at node {nodeId}.");
            return ["<DEPTH LIMIT>"];
        }

        if (!nodeMap.TryGetValue(nodeId, out StoryReteNodeRecord? node))
        {
            diagnostics.Add($"Condition references missing Rete node {nodeId}.");
            return [$"<MISSING NODE {nodeId}>"];
        }

        if (node.NodeType is 1 or 2 or 3 or 8)
            return [RenderCall(node, incomingAdaptorId, context, adaptorMap, functionMap)];

        if (node.NodeType is 4 or 5)
        {
            if (node.LeftNodeIndex is null || node.RightNodeIndex is null ||
                node.LeftAdaptorIndex is null || node.RightAdaptorIndex is null)
            {
                diagnostics.Add($"Logical node {nodeId} has incomplete child references.");
                return [$"<INVALID LOGICAL NODE {nodeId}>"];
            }

            List<string> left = RenderCondition(
                (int)node.LeftNodeIndex.Value, node.LeftAdaptorIndex.Value,
                context, nodeMap, adaptorMap, functionMap, depth + 1, diagnostics);
            List<string> right = RenderCondition(
                (int)node.RightNodeIndex.Value, node.RightAdaptorIndex.Value,
                context, nodeMap, adaptorMap, functionMap, depth + 1, diagnostics);

            var result = new List<string>(left.Count + right.Count + 1);
            result.AddRange(left);
            result.Add(node.NodeType == 4 ? "AND" : "AND NOT");
            result.AddRange(right);
            return result;
        }

        if (node.NodeType == 6)
        {
            if (node.HandleA is null || node.HandleB is null || node.Tail is null)
            {
                diagnostics.Add($"Relational node {nodeId} has incomplete comparison data.");
                return [$"<INVALID RELATION NODE {nodeId}>"];
            }

            List<string> predicate = RenderCondition(
                (int)node.HandleA.Value, node.HandleB.Value,
                context, nodeMap, adaptorMap, functionMap, depth + 1, diagnostics);

            string[] operators = ["<", "<=", ">", ">=", "==", "!="];
            if (node.Tail.Value >= operators.Length)
            {
                diagnostics.Add($"Relational node {nodeId} has unknown operator index {node.Tail.Value}.");
                return [$"<INVALID RELATION OPERATOR {node.Tail.Value}>"];
            }

            string left = node.RelFlag1 is not null && node.RelFlag1.Value != byte.MaxValue
                ? "_Var" + (node.RelFlag1.Value + 1).ToString(CultureInfo.InvariantCulture)
                : RenderTypedValue(node.ValueA);
            string right = node.RelFlag2 is not null && node.RelFlag2.Value != byte.MaxValue
                ? "_Var" + (node.RelFlag2.Value + 1).ToString(CultureInfo.InvariantCulture)
                : RenderTypedValue(node.ValueB);

            predicate.Add("AND");
            predicate.Add(left + " " + operators[node.Tail.Value] + " " + right);
            return predicate;
        }

        diagnostics.Add($"Unsupported condition node type {node.NodeType} at node {nodeId}.");
        return [$"<UNHANDLED NODE TYPE {node.NodeType}>"];
    }

    private static string RenderCall(
        StoryReteNodeRecord node,
        uint adaptorId,
        RuleContext context,
        IReadOnlyDictionary<uint, StoryAdaptorRecord> adaptorMap,
        IReadOnlyDictionary<int, StoryFunctionRecord> functionMap)
    {
        if (!adaptorMap.TryGetValue(adaptorId, out StoryAdaptorRecord? adaptor))
            return $"<MISSING ADAPTOR {adaptorId}>";

        functionMap.TryGetValue(node.NodeId, out StoryFunctionRecord? function);
        string[] arguments = RenderAdaptorArguments(
            adaptor,
            function,
            context.ProcVariableIds,
            isProcHeader: false);
        return node.Name + "(" + string.Join(", ", arguments) + ")";
    }

    internal static string[] RenderAdaptorArguments(
        StoryAdaptorRecord adaptor,
        StoryFunctionRecord? function,
        IReadOnlySet<int> procVariableIds,
        bool isProcHeader,
        IReadOnlySet<int>? reservedVariableIds = null)
    {
        // FunctionKind 5 records carry the authoritative typed PROC signature.
        // Save-game adaptors may be padded to five columns, so the signature
        // determines the real PROC arity rather than the padded mapping width.
        int argumentCount = isProcHeader && function?.FunctionKind == 5
            ? function.TypeValues.Length
            : adaptor.ArgumentVariableIds.Length;

        var reserved = new HashSet<int>(procVariableIds);
        if (reservedVariableIds is not null)
            reserved.UnionWith(reservedVariableIds);
        foreach (byte id in adaptor.ArgumentVariableIds)
            if (id != byte.MaxValue)
                reserved.Add(id);

        var arguments = new List<string>(argumentCount);
        for (int slot = 0; slot < argumentCount; slot++)
        {
            byte variableId = slot < adaptor.ArgumentVariableIds.Length
                ? adaptor.ArgumentVariableIds[slot]
                : byte.MaxValue;
            StoryColumnValue? literal = adaptor.LiteralPairs.FirstOrDefault(x => x.ColumnIndex == slot);

            if (variableId != byte.MaxValue)
            {
                string argument = "_Var" + (variableId + 1).ToString(CultureInfo.InvariantCulture);
                if (isProcHeader || function?.FunctionKind == 1 ||
                    (function?.FunctionKind == 4 && slot < function.TypeValues.Length &&
                     function.TypeValues[slot] == 6 && !procVariableIds.Contains(variableId)))
                {
                    if (function is not null && slot < function.TypeValues.Length &&
                        RuleTypeNames.TryGetValue(function.TypeValues[slot], out string? typeName))
                    {
                        argument = "(" + typeName + ")" + argument;
                    }
                }

                arguments.Add(argument);
                continue;
            }

            if (literal is not null)
            {
                arguments.Add(RenderTypedValue(literal.Value));
                continue;
            }

            if (isProcHeader && function?.FunctionKind == 5)
            {
                uint typeCode = function.TypeValues[slot];
                if (!RuleTypeNames.TryGetValue(typeCode, out string? typeName))
                    throw new InvalidDataException(
                        $"PROC {function.Name} parameter {slot + 1} has unknown type code {typeCode}.");

                // A typed, unused variable is semantically a wildcard in a PROC
                // head, but gives the original compiler the parameter type it needs.
                int syntheticId = Enumerable.Range(0, 255).FirstOrDefault(id => !reserved.Contains(id), -1);
                if (syntheticId < 0)
                    throw new InvalidDataException($"PROC {function.Name} has no free variable slot for a typed wildcard.");
                reserved.Add(syntheticId);
                arguments.Add("(" + typeName + ")_Var" +
                    (syntheticId + 1).ToString(CultureInfo.InvariantCulture));
                continue;
            }

            arguments.Add("_");
        }

        return arguments.ToArray();
    }

    private static IEnumerable<string> RenderRuleActions(RuleContext context)
    {
        foreach (StoryActionRecord action in context.ActionNode.Actions ?? [])
        {
            if (string.IsNullOrEmpty(action.Name))
            {
                string? directive = RenderDirective(action.ActionMetadata);
                if (directive is not null)
                    yield return directive;
                continue;
            }

            var arguments = new List<string>();
            foreach (StoryParameterRecord parameter in action.Parameters)
            {
                if (parameter.Kind == 1)
                {
                    int variableId = parameter.ExtraMetadata.Length > 0
                        ? parameter.ExtraMetadata[0]
                        : 0;
                    string argument = "_Var" + (variableId + 1).ToString(CultureInfo.InvariantCulture);

                    if (action.ActionFlag == 1 && parameter.Value.Type == 6 &&
                        !context.ProcVariableIds.Contains(variableId))
                    {
                        argument = "(DIALOG)" + argument;
                    }

                    arguments.Add(argument);
                }
                else
                {
                    arguments.Add(RenderTypedValue(parameter.Value));
                }
            }

            string prefix = action.ActionFlag == 1 ? "NOT " : "";
            yield return prefix + action.Name + "(" + string.Join(", ", arguments) + ");";
        }
    }

    internal static IEnumerable<string> RenderActionList(IReadOnlyList<StoryActionRecord> actions)
    {
        foreach (StoryActionRecord action in actions)
        {
            if (string.IsNullOrEmpty(action.Name))
            {
                string? directive = RenderDirective(action.ActionMetadata);
                if (directive is not null)
                    yield return directive;
                continue;
            }

            var arguments = new List<string>();
            foreach (StoryParameterRecord parameter in action.Parameters)
            {
                if (parameter.Kind == 1)
                {
                    int variableId = parameter.ExtraMetadata.Length > 0
                        ? parameter.ExtraMetadata[0]
                        : 0;
                    arguments.Add("_Var" + (variableId + 1).ToString(CultureInfo.InvariantCulture));
                }
                else
                {
                    arguments.Add(RenderTypedValue(parameter.Value));
                }
            }

            string prefix = action.ActionFlag == 1 ? "NOT " : "";
            yield return prefix + action.Name + "(" + string.Join(", ", arguments) + ");";
        }
    }

    private static string? RenderDirective(uint rawMetadata)
    {
        int metadata = unchecked((int)rawMetadata);
        if (metadata > 0)
            return "GoalCompleted;";
        if (metadata < 0)
            return "!" + Math.Abs((long)metadata).ToString(CultureInfo.InvariantCulture) + ";";
        return null;
    }

    private static string RenderTypedValue(StoryTypedValue? value)
    {
        if (value is null)
            return "_";

        if (value.Type == 0)
            return "_";

        if (value.Type == 1)
        {
            int number = unchecked((int)(value.RawValue ?? 0));
            return number.ToString(CultureInfo.InvariantCulture);
        }

        if (value.Type == 2)
        {
            float number = BitConverter.UInt32BitsToSingle(value.RawValue ?? 0);
            string formatted = number.ToString("R", CultureInfo.InvariantCulture);
            if (!formatted.Contains('.') && !formatted.Contains('E') && !formatted.Contains('e'))
                formatted += ".0";
            return formatted;
        }

        if (value.Type == 3)
        {
            string text = value.IsPresent == true ? value.Text ?? "" : "";
            return "\"" + EscapeQuotedString(text) + "\"";
        }

        return value.Text ?? "_";
    }

    private static string EscapeQuotedString(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

    private static void RenderGoalHierarchy(StringBuilder output, IReadOnlyList<StoryGoalRecord> goals)
    {
        foreach (StoryGoalRecord goal in goals.OrderBy(x => x.Metadata))
        {
            string mode = goal.Flags == 1 ? "AND" : "OR";
            output.Append("Goal(").Append(goal.Metadata.ToString(CultureInfo.InvariantCulture))
                .Append(").SubGoals(").Append(mode).AppendLine(");");

            foreach (uint childId in goal.ParseData)
            {
                output.Append("Goal(").Append(goal.Metadata.ToString(CultureInfo.InvariantCulture))
                    .Append(").SubGoal(").Append(childId.ToString(CultureInfo.InvariantCulture))
                    .AppendLine(");");
            }
        }
    }
}