using System.Text;
using System.Text.Json;

namespace StoryCompiler;

internal static class StoryReteExporter
{
    public static StoryReteDump Read(string path)
    {
        var parser = new Parser(File.ReadAllBytes(Path.GetFullPath(path)), Path.GetFullPath(path));
        return parser.Parse();
    }

    private sealed class Parser
    {
        private const byte BodyXorKey = 0xAD;
        private readonly byte[] _bytes;
        private readonly MemoryStream _stream;
        private readonly BinaryReader _reader;
        private byte _xorKey;

        public Parser(byte[] bytes, string path)
        {
            _bytes = bytes;
            Path = path;
            _stream = new MemoryStream(bytes, writable: false);
            _reader = new BinaryReader(_stream, Encoding.ASCII, leaveOpen: true);
        }

        private string Path { get; }

        public StoryReteDump Parse()
        {
            if (_bytes.Length == 0)
                throw new InvalidDataException("The file is empty.");

            _xorKey = ReadByte();
            string header = ReadXorString();
            byte[] version = ReadBytes(4);
            string compilerVersion = Encoding.ASCII.GetString(ReadBytes(128)).TrimEnd('\0');
            uint debugFlags = ReadUInt32();
            _xorKey = BodyXorKey;

            var divObjects = ReadDivObjects();
            var functions = ReadFunctions();
            var nodes = ReadReteNodes();
            var adaptors = ReadAdaptors();
            var dbases = ReadDBases();
            var goals = ReadGoals();
            var globalActions = ReadActionList();

            return new StoryReteDump
            {
                FilePath = Path,
                FileLength = _bytes.LongLength,
                HeaderText = header,
                FormatVersion = version,
                CompilerVersion = compilerVersion,
                DebugFlags = debugFlags,
                DivObjects = divObjects,
                Functions = functions,
                Nodes = nodes,
                Adaptors = adaptors,
                DBases = dbases,
                Goals = goals,
                GlobalActions = globalActions,
                FinalPosition = Position,
                RemainingBytes = _bytes.LongLength - Position
            };
        }

        private List<StoryDivObject> ReadDivObjects()
        {
            uint count = ReadCount("DIV object", 100_000);
            var result = new List<StoryDivObject>(checked((int)count));
            for (uint i = 0; i < count; i++)
            {
                string name = ReadXorString();
                byte flags = ReadByte();
                uint[] fields = ReadUInt32Array(4);
                result.Add(new StoryDivObject((int)i, name, flags, fields));
            }
            return result;
        }

        private List<StoryFunctionRecord> ReadFunctions()
        {
            uint count = ReadCount("function", 100_000);
            var result = new List<StoryFunctionRecord>(checked((int)count));
            for (uint i = 0; i < count; i++)
            {
                uint f0 = ReadUInt32();
                uint f1 = ReadUInt32();
                uint f2 = ReadUInt32();
                uint f3 = ReadUInt32();
                byte flag = ReadByte();
                uint f4 = ReadUInt32();
                uint f5 = ReadUInt32();
                uint f6 = ReadUInt32();
                uint f7 = ReadUInt32();
                string name = ReadXorString();
                uint byteCount = ReadCount($"function {i} byte-list", 100_000);
                byte[] byteData = ReadBytes(checked((int)byteCount));
                byte typeCount = ReadByte();
                if (typeCount > 100)
                    throw Error($"Function {i} has unreasonable value-type count {typeCount}.");
                uint[] typeValues = ReadUInt32Array(typeCount);

                result.Add(new StoryFunctionRecord(
                    (int)i, f0, f1, f2, f3, flag, f4, f5, f6, f7,
                    name, byteData, typeValues));
            }
            return result;
        }

        private List<StoryReteNodeRecord> ReadReteNodes()
        {
            uint count = ReadCount("Rete node", 1_000_000);
            var result = new List<StoryReteNodeRecord>(checked((int)count));
            for (uint i = 0; i < count; i++)
            {
                long offset = Position;
                byte type = ReadByte();
                if (type is < 1 or > 8)
                    throw Error($"Invalid Rete node type {type} at node {i}, offset 0x{offset:X}.");

                var node = new StoryReteNodeRecord
                {
                    NodeIndex = (int)i,
                    NodeId = checked((int)i + 1),
                    NodeType = type,
                    FileOffset = offset
                };

                if (type is 1 or 2)
                {
                    ReadNodeBase(node);
                    uint termCount = ReadCount($"start-node {i} term", 100_000);
                    var connections = new List<StoryConnection>(checked((int)termCount));
                    for (uint term = 0; term < termCount; term++)
                        connections.Add(ReadConnection());
                    node.Connections = connections;
                }
                else if (type is 3 or 8)
                {
                    ReadNodeBase(node);
                }
                else if (type is 4 or 5)
                {
                    ReadNodeBase(node);
                    node.ChildConnection = ReadConnection();
                    node.LeftNodeIndex = ReadUInt32();
                    node.RightNodeIndex = ReadUInt32();
                    node.LeftAdaptorIndex = ReadUInt32();
                    node.RightAdaptorIndex = ReadUInt32();
                    node.ExtraNodeHandle = ReadUInt32();
                    node.Connection1 = ReadConnection();
                    node.Flag1 = ReadByte();
                    node.Scalar = ReadUInt32();
                    node.Connection2 = ReadConnection();
                    node.Flag2 = ReadByte();
                }
                else if (type == 6)
                {
                    ReadNodeBase(node);
                    node.ChildConnection = ReadConnection();
                    node.HandleA = ReadUInt32();
                    node.HandleB = ReadUInt32();
                    node.HandleC = ReadUInt32();
                    node.RelationConnection = ReadConnection();
                    node.BaseFlag = ReadByte();
                    node.RelFlag1 = ReadByte();
                    node.RelFlag2 = ReadByte();
                    node.ValueA = ReadTypedValue();
                    node.ValueB = ReadTypedValue();
                    node.Tail = ReadUInt32();
                }
                else
                {
                    ReadNodeBase(node);
                    node.ParentConnection = ReadConnection();
                    node.ParentNodeIndex = ReadUInt32();
                    node.AdaptorIndex = ReadUInt32();
                    node.ActionHandleC = ReadUInt32();
                    node.ActionConnection = ReadConnection();
                    node.ActionNodeFlag = ReadByte();
                    node.Actions = ReadActionList();
                    node.LocalVariables = ReadVariableVector();
                    node.SourceLine = ReadUInt32();
                }

                result.Add(node);
            }
            return result;
        }

        private void ReadNodeBase(StoryReteNodeRecord node)
        {
            node.NodeQueryField = ReadUInt32();
            node.BaseNodeField = ReadUInt32();
            node.Name = ReadXorString();
            if (node.Name.Length > 0)
                node.NameFlag = ReadByte();
        }

        private List<StoryAdaptorRecord> ReadAdaptors()
        {
            uint count = ReadCount("Rete adaptor", 100_000);
            var result = new List<StoryAdaptorRecord>(checked((int)count));
            for (uint i = 0; i < count; i++)
            {
                uint key = ReadUInt32();
                byte literalCount = ReadByte();
                var literals = new List<StoryColumnValue>(literalCount);
                for (int j = 0; j < literalCount; j++)
                    literals.Add(new StoryColumnValue(ReadByte(), ReadTypedValue()));

                byte argumentCount = ReadByte();
                byte[] argumentVariableIds = ReadBytes(argumentCount);

                byte bindingCount = ReadByte();
                var bindings = new List<StoryVariableBinding>(bindingCount);
                for (int j = 0; j < bindingCount; j++)
                    bindings.Add(new StoryVariableBinding(ReadByte(), ReadByte()));

                result.Add(new StoryAdaptorRecord(key, literals, argumentVariableIds, bindings));
            }
            return result;
        }

        private List<StoryDBaseRecord> ReadDBases()
        {
            uint count = ReadCount("Rete DBase", 100_000);
            var result = new List<StoryDBaseRecord>(checked((int)count));
            for (uint i = 0; i < count; i++)
            {
                uint key = ReadUInt32();
                byte typeCount = ReadByte();
                uint[] schema = ReadUInt32Array(typeCount);
                uint tupleCount = ReadCount($"DBase {key} tuple", 100_000);
                var tuples = new List<List<StoryTypedValue>>(checked((int)tupleCount));
                for (uint t = 0; t < tupleCount; t++)
                {
                    byte valueCount = ReadByte();
                    var values = new List<StoryTypedValue>(valueCount);
                    for (int v = 0; v < valueCount; v++)
                        values.Add(ReadTypedValue());
                    tuples.Add(values);
                }
                result.Add(new StoryDBaseRecord(key, schema, tuples));
            }
            return result;
        }

        private List<StoryGoalRecord> ReadGoals()
        {
            uint count = ReadCount("Goal", 100_000);
            var result = new List<StoryGoalRecord>(checked((int)count));
            for (uint i = 0; i < count; i++)
            {
                uint metadata = ReadUInt32();
                string name = ReadXorString();
                byte flags = ReadByte();
                uint idCount = ReadCount($"Goal {i} id-list", 100_000);
                uint[] ids = ReadUInt32Array(idCount);
                uint parseCount = ReadCount($"Goal {i} parse-data", 100_000);
                uint[] parseData = ReadUInt32Array(parseCount);
                byte maskedFlags = ReadByte();
                var initActions = ReadActionList();
                var exitActions = ReadActionList();
                result.Add(new StoryGoalRecord((int)i, metadata, name, flags, ids, parseData, maskedFlags, initActions, exitActions));
            }
            return result;
        }

        private List<StoryActionRecord> ReadActionList()
        {
            uint count = ReadCount("global action", 100_000);
            var result = new List<StoryActionRecord>(checked((int)count));
            for (uint i = 0; i < count; i++)
            {
                string name = ReadXorString();
                byte? hasValues = null;
                List<StoryParameterRecord> parameters = new();
                byte? actionFlag = null;
                if (name.Length > 0)
                {
                    hasValues = ReadByte();
                    if (hasValues != 0)
                        parameters = ReadTypedValueList();
                    actionFlag = ReadByte();
                }
                uint actionMetadata = ReadUInt32();
                result.Add(new StoryActionRecord((int)i, name, hasValues, parameters, actionFlag, actionMetadata));
            }
            return result;
        }

        private List<StoryParameterRecord> ReadTypedValueList()
        {
            byte count = ReadByte();
            var result = new List<StoryParameterRecord>(count);
            for (int i = 0; i < count; i++)
            {
                byte kind = ReadByte();
                if (kind > 1)
                    throw Error($"Invalid parameter kind {kind} in typed-value list.");
                StoryTypedValue value = ReadTypedValue();
                byte[] metadata = ReadBytes(3);
                byte[] extra = kind == 1 ? ReadBytes(3) : Array.Empty<byte>();
                result.Add(new StoryParameterRecord(i, kind, value, metadata, extra));
            }
            return result;
        }

        private List<StoryLocalVariable> ReadVariableVector()
        {
            byte count = ReadByte();
            var result = new List<StoryLocalVariable>(count);
            for (int i = 0; i < count; i++)
            {
                byte kind = ReadByte();
                StoryTypedValue value = ReadTypedValue();
                byte[] metadata = ReadBytes(6);
                result.Add(new StoryLocalVariable(i, kind, value, metadata));
            }
            return result;
        }

        private StoryTypedValue ReadTypedValue()
        {
            long offset = Position;
            byte marker = ReadByte();
            uint type = ReadUInt32();

            if (marker == 0x31)
                return new StoryTypedValue(marker, type, ReadUInt32(), null, null);

            if (marker != 0x30)
                throw Error($"Invalid typed-value marker 0x{marker:X2} at 0x{offset:X}.");

            if (type == 0)
                return new StoryTypedValue(marker, type, null, null, null);
            if (type is 1 or 2)
                return new StoryTypedValue(marker, type, ReadUInt32(), null, null);
            if (type == 3)
            {
                byte present = ReadByte();
                return new StoryTypedValue(marker, type, null, present != 0,
                    present != 0 ? ReadXorString() : null);
            }

            return new StoryTypedValue(marker, type, null, null, ReadXorString());
        }

        private StoryConnection ReadConnection() =>
            new(ReadUInt32(), ReadUInt32(), ReadUInt32());

        private uint[] ReadUInt32Array(uint count)
        {
            var values = new uint[checked((int)count)];
            for (int i = 0; i < values.Length; i++)
                values[i] = ReadUInt32();
            return values;
        }

        private byte[] ReadBytes(int count)
        {
            if (count < 0)
                throw Error($"Negative byte count {count}.");
            EnsureAvailable(count);
            byte[] result = _reader.ReadBytes(count);
            if (result.Length != count)
                throw Error("Unexpected end of file.");
            return result;
        }

        private byte ReadByte()
        {
            EnsureAvailable(1);
            return _reader.ReadByte();
        }

        private uint ReadUInt32()
        {
            EnsureAvailable(4);
            return _reader.ReadUInt32();
        }

        private uint ReadCount(string label, uint maximum)
        {
            uint count = ReadUInt32();
            if (count > maximum)
                throw Error($"Unreasonable {label} count {count}.");
            return count;
        }

        private string ReadXorString()
        {
            var bytes = new List<byte>();
            while (true)
            {
                byte value = (byte)(ReadByte() ^ _xorKey);
                if (value == 0)
                    break;
                bytes.Add(value);
                if (bytes.Count > 65_535)
                    throw Error("XOR string exceeds the safety limit.");
            }
            return Encoding.ASCII.GetString(bytes.ToArray());
        }

        private void EnsureAvailable(long count)
        {
            if (count < 0 || Position + count > _stream.Length)
                throw Error($"Unexpected end of file at 0x{Position:X}; need {count} byte(s).");
        }

        private long Position => _stream.Position;

        private InvalidDataException Error(string message) =>
            new($"{message} (file offset 0x{Position:X}).");
    }
}

internal sealed class StoryReteDump
{
    public required string FilePath { get; init; }
    public required long FileLength { get; init; }
    public required string HeaderText { get; init; }
    public required byte[] FormatVersion { get; init; }
    public required string CompilerVersion { get; init; }
    public required uint DebugFlags { get; init; }
    public required List<StoryDivObject> DivObjects { get; init; }
    public required List<StoryFunctionRecord> Functions { get; init; }
    public required List<StoryReteNodeRecord> Nodes { get; init; }
    public required List<StoryAdaptorRecord> Adaptors { get; init; }
    public required List<StoryDBaseRecord> DBases { get; init; }
    public required List<StoryGoalRecord> Goals { get; init; }
    public required List<StoryActionRecord> GlobalActions { get; init; }
    public required long FinalPosition { get; init; }
    public required long RemainingBytes { get; init; }
}

internal sealed record StoryDivObject(int Index, string Name, byte Flags, uint[] Fields);

internal sealed record StoryFunctionRecord(
    int Index, uint Field0, uint Field1, uint Field2, uint NodeId, byte FunctionKind,
    uint FunctionNodeId, uint Field5, uint Field6, uint Field7, string Name,
    byte[] ByteData, uint[] TypeValues);

internal sealed class StoryReteNodeRecord
{
    public required int NodeIndex { get; init; }
    public required int NodeId { get; init; }
    public required byte NodeType { get; init; }
    public required long FileOffset { get; init; }
    public uint NodeQueryField { get; set; }
    public uint BaseNodeField { get; set; }
    public string Name { get; set; } = "";
    public byte? NameFlag { get; set; }
    public List<StoryConnection>? Connections { get; set; }
    public StoryConnection? ChildConnection { get; set; }
    public uint? LeftNodeIndex { get; set; }
    public uint? RightNodeIndex { get; set; }
    public uint? LeftAdaptorIndex { get; set; }
    public uint? RightAdaptorIndex { get; set; }
    public uint? ExtraNodeHandle { get; set; }
    public StoryConnection? Connection1 { get; set; }
    public byte? Flag1 { get; set; }
    public uint? Scalar { get; set; }
    public StoryConnection? Connection2 { get; set; }
    public byte? Flag2 { get; set; }
    public uint? HandleA { get; set; }
    public uint? HandleB { get; set; }
    public uint? HandleC { get; set; }
    public StoryConnection? RelationConnection { get; set; }
    public byte? BaseFlag { get; set; }
    public byte? RelFlag1 { get; set; }
    public byte? RelFlag2 { get; set; }
    public StoryTypedValue? ValueA { get; set; }
    public StoryTypedValue? ValueB { get; set; }
    public uint? Tail { get; set; }
    public StoryConnection? ParentConnection { get; set; }
    public uint? ParentNodeIndex { get; set; }
    public uint? AdaptorIndex { get; set; }
    public uint? ActionHandleC { get; set; }
    public StoryConnection? ActionConnection { get; set; }
    public byte? ActionNodeFlag { get; set; }
    public List<StoryActionRecord>? Actions { get; set; }
    public List<StoryLocalVariable>? LocalVariables { get; set; }
    public uint? SourceLine { get; set; }
}

internal sealed record StoryConnection(uint A, uint B, uint C);
internal sealed record StoryColumnValue(byte ColumnIndex, StoryTypedValue Value);
internal sealed record StoryVariableBinding(byte VariableId, byte ArgumentIndex);
internal sealed record StoryAdaptorRecord(
    uint AdaptorIndex, List<StoryColumnValue> LiteralPairs,
    byte[] ArgumentVariableIds, List<StoryVariableBinding> VariableBindings);
internal sealed record StoryTypedValue(
    byte Marker, uint Type, uint? RawValue, bool? IsPresent, string? Text);
internal sealed record StoryParameterRecord(
    int ParameterIndex, byte Kind, StoryTypedValue Value,
    byte[] Metadata, byte[] ExtraMetadata);
internal sealed record StoryLocalVariable(
    int LocalVariableIndex, byte Kind, StoryTypedValue Value, byte[] Metadata);
internal sealed record StoryActionRecord(
    int ActionIndex, string Name, byte? HasValues,
    List<StoryParameterRecord> Parameters, byte? ActionFlag, uint ActionMetadata);
internal sealed record StoryDBaseRecord(
    uint Key, uint[] Schema, List<List<StoryTypedValue>> Tuples);
internal sealed record StoryGoalRecord(
    int Index, uint Metadata, string Name, byte Flags, uint[] Ids,
    uint[] ParseData, byte MaskedFlags,
    List<StoryActionRecord> InitActions, List<StoryActionRecord> ExitActions);
