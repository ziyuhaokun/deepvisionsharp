using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace DeepVisionSharp.TorchSharp.Classification.Pretrained;

/// <summary>A tensor taken out of a PyTorch checkpoint: contiguous little-endian bytes, float32 or int64.</summary>
public sealed record RawTensor(string Name, long[] Shape, string Dtype, byte[] Data);

/// <summary>
/// Reads PyTorch checkpoints (<c>.pth</c> state dicts and Ultralytics <c>.pt</c> training results,
/// zip + pickle) without PyTorch or Python.
/// </summary>
/// <remarks>
/// TorchSharp 0.107 cannot read a PyTorch state dict, so this unpacks the archive itself. It is a
/// deliberately small pickle interpreter: only the opcodes <c>torch.save</c> emits for a state dict,
/// and only the callables a state dict needs (<c>OrderedDict</c>, <c>_rebuild_tensor_v2</c>,
/// <c>_rebuild_parameter</c>). Anything else is kept as an opaque value, never executed, so reading
/// an untrusted file cannot run code. An Ultralytics checkpoint pickles a whole module tree; every
/// class in it becomes an inert <see cref="PickleObject"/> that only keeps its attribute dict, and the
/// weights are collected by walking <c>_parameters</c> / <c>_buffers</c> / <c>_modules</c> like
/// <c>nn.Module.state_dict()</c> does.
/// </remarks>
public static class TorchCheckpointReader
{
    public static IReadOnlyList<RawTensor> ReadStateDict(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var archive = ZipFile.OpenRead(path);
        return ReadStateDict(archive);
    }

    public static IReadOnlyList<RawTensor> ReadStateDict(ZipArchive archive)
    {
        var root = LoadRoot(archive, out var prefix);
        var state = FindStateDict(root)
            ?? throw new InvalidDataException("检查点里没有找到权重字典");

        var storages = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var result = new List<RawTensor>();
        foreach (var (key, value) in state.Items)
        {
            if (key is not string name || value is not TensorValue tensor)
            {
                continue; // e.g. the "_metadata" entry
            }

            result.Add(Materialize(name, tensor, archive, prefix, storages));
        }

        if (result.Count == 0)
        {
            throw new InvalidDataException("检查点里没有张量");
        }

        return result;
    }

    /// <summary>
    /// Reads an Ultralytics training result (<c>yolo26n.pt</c>): prefers the EMA copy, which is what
    /// Ultralytics validates and exports, and falls back to <c>model</c>.
    /// </summary>
    public static IReadOnlyList<RawTensor> ReadModuleStateDict(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var archive = ZipFile.OpenRead(path);
        return ReadModuleStateDict(archive);
    }

    public static IReadOnlyList<RawTensor> ReadModuleStateDict(ZipArchive archive)
    {
        var root = LoadRoot(archive, out var prefix);
        if (root is not PickleDict payload)
        {
            throw new InvalidDataException("检查点不是字典");
        }

        PickleObject? module = null;
        foreach (var key in new[] { "ema", "model" })
        {
            var candidate = payload.Items.FirstOrDefault(item => item.Key as string == key).Value;
            if (candidate is PickleObject { } found && found.Attributes.Contains("_modules"))
            {
                module = found;
                break;
            }
        }

        if (module is null)
        {
            throw new InvalidDataException("检查点里既没有 ema 也没有 model");
        }

        var entries = new List<KeyValuePair<string, TensorValue>>();
        CollectModule(module, string.Empty, entries, depth: 0);

        var storages = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var result = entries
            .Select(entry => Materialize(entry.Key, entry.Value, archive, prefix, storages))
            .ToList();
        if (result.Count == 0)
        {
            throw new InvalidDataException("没有解出任何张量");
        }

        if (!result.Any(tensor => tensor.Name.StartsWith("model.", StringComparison.Ordinal)))
        {
            throw new InvalidDataException("权重键名不是 model.*，无法对应到 YOLO26 模块");
        }

        return result;
    }

    private static object? LoadRoot(ZipArchive archive, out string prefix)
    {
        var pickleEntry = archive.Entries.FirstOrDefault(entry =>
            entry.FullName == "data.pkl" || entry.FullName.EndsWith("/data.pkl", StringComparison.Ordinal))
            ?? throw new InvalidDataException("不是 PyTorch zip 检查点（没有 data.pkl）");

        prefix = pickleEntry.FullName.Length > "data.pkl".Length
            ? pickleEntry.FullName[..^"/data.pkl".Length]
            : string.Empty;

        byte[] pickleBytes;
        using (var stream = pickleEntry.Open())
        using (var buffer = new MemoryStream())
        {
            stream.CopyTo(buffer);
            pickleBytes = buffer.ToArray();
        }

        return new PickleMachine(pickleBytes).Run();
    }

    /// <summary>Flattens <c>_parameters</c>, <c>_buffers</c> and children exactly like <c>state_dict()</c>.</summary>
    private static void CollectModule(
        PickleObject module,
        string prefix,
        List<KeyValuePair<string, TensorValue>> destination,
        int depth)
    {
        if (depth > 64)
        {
            throw new InvalidDataException("模块嵌套过深");
        }

        foreach (var section in new[] { "_parameters", "_buffers" })
        {
            if (module.Attributes.Get(section) is not PickleDict items)
            {
                continue;
            }

            foreach (var (key, value) in items.Items)
            {
                if (key is string name && value is TensorValue tensor)
                {
                    destination.Add(new($"{prefix}{name}", tensor));
                }
            }
        }

        if (module.Attributes.Get("_modules") is PickleDict children)
        {
            foreach (var (key, value) in children.Items)
            {
                if (key is string name && value is PickleObject child)
                {
                    CollectModule(child, $"{prefix}{name}.", destination, depth + 1);
                }
            }
        }
    }

    private static PickleDict? FindStateDict(object? root)
    {
        if (root is not PickleDict dict)
        {
            return null;
        }

        if (dict.Items.Any(item => item.Value is TensorValue))
        {
            return dict;
        }

        // {"state_dict": {...}} wrappers, as written by some training scripts.
        foreach (var wrapperKey in new[] { "state_dict", "model" })
        {
            var inner = dict.Items.FirstOrDefault(item => item.Key as string == wrapperKey).Value;
            if (inner is PickleDict innerDict && innerDict.Items.Any(item => item.Value is TensorValue))
            {
                return innerDict;
            }
        }

        return null;
    }

    private static RawTensor Materialize(
        string name,
        TensorValue tensor,
        ZipArchive archive,
        string prefix,
        Dictionary<string, byte[]> storages)
    {
        var storage = tensor.Storage;
        if (!storages.TryGetValue(storage.Key, out var raw))
        {
            var entryName = prefix.Length == 0 ? $"data/{storage.Key}" : $"{prefix}/data/{storage.Key}";
            var entry = archive.GetEntry(entryName)
                ?? throw new InvalidDataException($"{name}: 缺少数据文件 {entryName}");
            using var stream = entry.Open();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            raw = buffer.ToArray();
            storages[storage.Key] = raw;
        }

        var element = ElementOf(storage, raw.Length);
        var count = tensor.Shape.Length == 0 ? 1L : tensor.Shape.Aggregate(1L, (a, b) => a * b);

        // Gather through the strides so a non-contiguous view still comes out contiguous.
        var source = new byte[checked(count * element.Size)];
        CopyStrided(raw, element.Size, tensor, source);

        // A 0-d scalar (BatchNorm's num_batches_tracked) is written with shape [1], the way the Python
        // converter always has (numpy's ascontiguousarray promotes 0-d to 1-d). The bundle reader and the
        // pretrained initializers were built against that layout, so keep it identical.
        var shape = tensor.Shape.Length == 0 ? new long[] { 1 } : tensor.Shape;
        return element.Kind switch
        {
            ElementKind.Float32 => new RawTensor(name, shape, "float32", source),
            ElementKind.Float16 => new RawTensor(name, shape, "float32", ToFloat32(source, count, ElementKind.Float16)),
            ElementKind.Float64 => new RawTensor(name, shape, "float32", ToFloat32(source, count, ElementKind.Float64)),
            _ => new RawTensor(name, shape, "int64", ToInt64(source, count, element)),
        };
    }

    private static void CopyStrided(byte[] raw, int elementSize, TensorValue tensor, byte[] destination)
    {
        var shape = tensor.Shape;
        var stride = tensor.Stride;
        var count = shape.Length == 0 ? 1L : shape.Aggregate(1L, (a, b) => a * b);
        if (count == 0)
        {
            return;
        }

        // Fast path: already contiguous.
        var contiguous = true;
        var expected = 1L;
        for (var dim = shape.Length - 1; dim >= 0; dim--)
        {
            if (shape[dim] != 1 && stride[dim] != expected)
            {
                contiguous = false;
                break;
            }

            expected *= shape[dim];
        }

        if (contiguous)
        {
            var start = checked(tensor.Offset * elementSize);
            var length = checked(count * elementSize);
            if (start + length > raw.Length)
            {
                throw new InvalidDataException("张量数据超出存储范围");
            }

            Buffer.BlockCopy(raw, (int)start, destination, 0, (int)length);
            return;
        }

        var index = new long[shape.Length];
        for (var flat = 0L; flat < count; flat++)
        {
            var offset = tensor.Offset;
            for (var dim = 0; dim < shape.Length; dim++)
            {
                offset += index[dim] * stride[dim];
            }

            var from = checked(offset * elementSize);
            if (from < 0 || from + elementSize > raw.Length)
            {
                throw new InvalidDataException("张量数据超出存储范围");
            }

            Buffer.BlockCopy(raw, (int)from, destination, (int)(flat * elementSize), elementSize);
            for (var dim = shape.Length - 1; dim >= 0; dim--)
            {
                if (++index[dim] < shape[dim])
                {
                    break;
                }

                index[dim] = 0;
            }
        }
    }

    private enum ElementKind { Float16, Float32, Float64, Int8, UInt8, Int16, Int32, Int64, Bool }

    private readonly record struct Element(ElementKind Kind, int Size);

    private static Element ElementOf(StorageRef storage, int byteCount)
    {
        var lowered = storage.TypeName.ToLowerInvariant();
        if (lowered.Contains("double")) return new(ElementKind.Float64, 8);
        if (lowered.Contains("bfloat16")) throw new InvalidDataException($"不支持的权重类型 {storage.TypeName}");
        if (lowered.Contains("half") || lowered.Contains("float16")) return new(ElementKind.Float16, 2);
        if (lowered.Contains("float")) return new(ElementKind.Float32, 4);
        if (lowered.Contains("long") || lowered.Contains("int64")) return new(ElementKind.Int64, 8);
        if (lowered.Contains("short") || lowered.Contains("int16")) return new(ElementKind.Int16, 2);
        if (lowered.Contains("byte") || lowered.Contains("uint8")) return new(ElementKind.UInt8, 1);
        if (lowered.Contains("char") || lowered.Contains("int8")) return new(ElementKind.Int8, 1);
        if (lowered.Contains("bool")) return new(ElementKind.Bool, 1);
        if (lowered.Contains("int")) return new(ElementKind.Int32, 4);

        // Untyped storage: the archive does not record the element type, so infer it from size.
        if (storage.Numel > 0 && byteCount % storage.Numel == 0)
        {
            return (byteCount / storage.Numel) switch
            {
                1 => new(ElementKind.UInt8, 1),
                2 => new(ElementKind.Float16, 2),
                8 => new(ElementKind.Int64, 8),
                _ => new(ElementKind.Float32, 4),
            };
        }

        return new(ElementKind.Float32, 4);
    }

    private static byte[] ToFloat32(byte[] source, long count, ElementKind kind)
    {
        var result = new byte[checked(count * 4)];
        for (var i = 0; i < count; i++)
        {
            var value = kind == ElementKind.Float16
                ? (float)BitConverter.ToHalf(source, i * 2)
                : (float)BitConverter.ToDouble(source, i * 8);
            BinaryPrimitives.WriteSingleLittleEndian(result.AsSpan(i * 4), value);
        }

        return result;
    }

    private static byte[] ToInt64(byte[] source, long count, Element element)
    {
        var result = new byte[checked(count * 8)];
        for (var i = 0; i < count; i++)
        {
            var offset = i * element.Size;
            long value = element.Kind switch
            {
                ElementKind.Int64 => BitConverter.ToInt64(source, offset),
                ElementKind.Int32 => BitConverter.ToInt32(source, offset),
                ElementKind.Int16 => BitConverter.ToInt16(source, offset),
                ElementKind.Int8 => (sbyte)source[offset],
                _ => source[offset], // UInt8, Bool
            };
            BinaryPrimitives.WriteInt64LittleEndian(result.AsSpan(i * 8), value);
        }

        return result;
    }

    // ---- pickle values ------------------------------------------------------------------------

    private sealed record GlobalRef(string Module, string Name);

    private sealed record StorageRef(string TypeName, string Key, long Numel);

    private sealed record TensorValue(StorageRef Storage, long Offset, long[] Shape, long[] Stride);

    private sealed class PickleDict
    {
        public List<KeyValuePair<object?, object?>> Items { get; } = [];

        public bool Contains(string key) => Items.Any(item => item.Key as string == key);

        public object? Get(string key) => Items.FirstOrDefault(item => item.Key as string == key).Value;

        public void Set(object? key, object? value)
        {
            for (var i = 0; i < Items.Count; i++)
            {
                if (Equals(Items[i].Key, key))
                {
                    Items[i] = new(key, value);
                    return;
                }
            }

            Items.Add(new(key, value));
        }
    }

    /// <summary>Any class instance in the pickle. Never constructed for real: only its attributes are kept.</summary>
    private sealed class PickleObject
    {
        public PickleObject(GlobalRef? type) => Type = type;

        public GlobalRef? Type { get; }

        public PickleDict Attributes { get; } = new();
    }

    private sealed class PickleMachine
    {
        private readonly byte[] _data;
        private int _position;
        private readonly List<object?> _stack = [];
        private readonly Stack<int> _marks = new();
        private readonly Dictionary<int, object?> _memo = [];

        public PickleMachine(byte[] data) => _data = data;

        public object? Run()
        {
            while (_position < _data.Length)
            {
                var op = _data[_position++];
                switch (op)
                {
                    case 0x80: _position++; break; // PROTO
                    case 0x95: _position += 8; break; // FRAME
                    case (byte)'.': return Pop(); // STOP
                    case (byte)'}': _stack.Add(new PickleDict()); break;
                    case (byte)']': _stack.Add(new List<object?>()); break;
                    case (byte)')': _stack.Add(Array.Empty<object?>()); break;
                    case (byte)'(': _marks.Push(_stack.Count); break;
                    case (byte)'0': Pop(); break;
                    case (byte)'t': _stack.Add(PopToMark()); break;
                    case 0x85: _stack.Add(PopTuple(1)); break;
                    case 0x86: _stack.Add(PopTuple(2)); break;
                    case 0x87: _stack.Add(PopTuple(3)); break;
                    case (byte)'N': _stack.Add(null); break;
                    case 0x88: _stack.Add(true); break;
                    case 0x89: _stack.Add(false); break;
                    case (byte)'K': _stack.Add((long)_data[_position++]); break;
                    case (byte)'M': _stack.Add((long)BinaryPrimitives.ReadUInt16LittleEndian(Take(2))); break;
                    case (byte)'J': _stack.Add((long)BinaryPrimitives.ReadInt32LittleEndian(Take(4))); break;
                    case 0x8a: _stack.Add(ReadLong(_data[_position++])); break;
                    case 0x8b: _stack.Add(ReadLong(BinaryPrimitives.ReadInt32LittleEndian(Take(4)))); break;
                    case (byte)'G': _stack.Add(BinaryPrimitives.ReadDoubleBigEndian(Take(8))); break;
                    case (byte)'X': _stack.Add(ReadString((int)BinaryPrimitives.ReadUInt32LittleEndian(Take(4)), Encoding.UTF8)); break;
                    case 0x8c: _stack.Add(ReadString(_data[_position++], Encoding.UTF8)); break;
                    case (byte)'U': _stack.Add(ReadString(_data[_position++], Encoding.Latin1)); break;
                    case (byte)'T': _stack.Add(ReadString(BinaryPrimitives.ReadInt32LittleEndian(Take(4)), Encoding.Latin1)); break;
                    case (byte)'c': _stack.Add(new GlobalRef(ReadLine(), ReadLine())); break;
                    case 0x93:
                        {
                            var name = (string)Pop()!;
                            var module = (string)Pop()!;
                            _stack.Add(new GlobalRef(module, name));
                            break;
                        }
                    case (byte)'q': _memo[_data[_position++]] = Peek(); break;
                    case (byte)'r': _memo[(int)BinaryPrimitives.ReadUInt32LittleEndian(Take(4))] = Peek(); break;
                    case 0x94: _memo[_memo.Count] = Peek(); break;
                    case (byte)'h': _stack.Add(_memo[_data[_position++]]); break;
                    case (byte)'j': _stack.Add(_memo[(int)BinaryPrimitives.ReadUInt32LittleEndian(Take(4))]); break;
                    case (byte)'s': SetItem(); break;
                    case (byte)'u': SetItems(); break;
                    case (byte)'a':
                        {
                            var value = Pop(); // the item is on top; the list is underneath
                            (Peek() as List<object?>)?.Add(value);
                            break;
                        }
                    case (byte)'e':
                        {
                            var items = PopToMark();
                            (Peek() as List<object?>)?.AddRange(items);
                            break;
                        }
                    case (byte)'R': Reduce(); break;
                    case 0x81: // NEWOBJ
                        {
                            Pop(); // args
                            _stack.Add(new PickleObject(Pop() as GlobalRef));
                            break;
                        }
                    case 0x92: // NEWOBJ_EX
                        {
                            Pop(); // kwargs
                            Pop(); // args
                            _stack.Add(new PickleObject(Pop() as GlobalRef));
                            break;
                        }
                    case (byte)'b': Build(); break;
                    case (byte)'B': _stack.Add(ReadBytes((int)BinaryPrimitives.ReadUInt32LittleEndian(Take(4)))); break;
                    case (byte)'C': _stack.Add(ReadBytes(_data[_position++])); break;
                    case 0x8e: _stack.Add(ReadBytes(checked((int)BinaryPrimitives.ReadUInt64LittleEndian(Take(8))))); break;
                    case 0x96: _stack.Add(ReadBytes(checked((int)BinaryPrimitives.ReadUInt64LittleEndian(Take(8))))); break;
                    case 0x8d: _stack.Add(ReadString(checked((int)BinaryPrimitives.ReadUInt64LittleEndian(Take(8))), Encoding.UTF8)); break;
                    case (byte)'F': _stack.Add(double.Parse(ReadLine(), System.Globalization.CultureInfo.InvariantCulture)); break;
                    case (byte)'I':
                        {
                            var text = ReadLine();
                            _stack.Add(text switch { "00" => false, "01" => true, _ => long.Parse(text, System.Globalization.CultureInfo.InvariantCulture) });
                            break;
                        }
                    case (byte)'L': _stack.Add(long.Parse(ReadLine().TrimEnd('L'), System.Globalization.CultureInfo.InvariantCulture)); break;
                    case (byte)'S': _stack.Add(ReadLine().Trim('\'', '"')); break;
                    case (byte)'V': _stack.Add(ReadLine()); break;
                    case (byte)'p': _memo[int.Parse(ReadLine(), System.Globalization.CultureInfo.InvariantCulture)] = Peek(); break;
                    case (byte)'g': _stack.Add(_memo[int.Parse(ReadLine(), System.Globalization.CultureInfo.InvariantCulture)]); break;
                    case (byte)'d':
                        {
                            var items = PopToMark();
                            var dict = new PickleDict();
                            for (var i = 0; i + 1 < items.Length; i += 2)
                            {
                                dict.Items.Add(new(items[i], items[i + 1]));
                            }

                            _stack.Add(dict);
                            break;
                        }
                    case (byte)'l': _stack.Add(new List<object?>(PopToMark())); break;
                    case (byte)'1': PopToMark(); break;
                    case (byte)'2': _stack.Add(Peek()); break;
                    case 0x8f: _stack.Add(new List<object?>()); break; // EMPTY_SET
                    case 0x90: // ADDITEMS
                        {
                            var items = PopToMark();
                            if (Peek() is List<object?> set)
                            {
                                set.AddRange(items);
                            }

                            break;
                        }
                    case 0x91: _stack.Add(new List<object?>(PopToMark())); break; // FROZENSET
                    case (byte)'Q': _stack.Add(ResolvePersistent(Pop())); break;
                    default:
                        throw new InvalidDataException($"不支持的 pickle 指令 0x{op:X2}（位置 {_position - 1}）");
                }
            }

            throw new InvalidDataException("pickle 数据提前结束");
        }

        private ReadOnlySpan<byte> Take(int count)
        {
            if (_position + count > _data.Length)
            {
                throw new InvalidDataException("pickle 数据提前结束");
            }

            var span = new ReadOnlySpan<byte>(_data, _position, count);
            _position += count;
            return span;
        }

        private string ReadString(int length, Encoding encoding)
        {
            if (length < 0)
            {
                throw new InvalidDataException("pickle 字符串长度无效");
            }

            return encoding.GetString(Take(length));
        }

        private string ReadLine()
        {
            var end = Array.IndexOf(_data, (byte)'\n', _position);
            if (end < 0)
            {
                throw new InvalidDataException("pickle 数据提前结束");
            }

            var line = Encoding.UTF8.GetString(_data, _position, end - _position);
            _position = end + 1;
            return line;
        }

        private long ReadLong(int length)
        {
            if (length == 0)
            {
                return 0;
            }

            if (length > 8)
            {
                throw new InvalidDataException("pickle 整数过大");
            }

            var bytes = Take(length);
            long value = 0;
            for (var i = 0; i < length; i++)
            {
                value |= (long)bytes[i] << (8 * i);
            }

            if ((bytes[length - 1] & 0x80) != 0 && length < 8)
            {
                value -= 1L << (8 * length); // sign extend
            }

            return value;
        }

        private object? Pop()
        {
            if (_stack.Count == 0)
            {
                throw new InvalidDataException("pickle 栈为空");
            }

            var value = _stack[^1];
            _stack.RemoveAt(_stack.Count - 1);
            return value;
        }

        private object? Peek() =>
            _stack.Count == 0 ? throw new InvalidDataException("pickle 栈为空") : _stack[^1];

        private object?[] PopToMark()
        {
            if (_marks.Count == 0)
            {
                throw new InvalidDataException("pickle 缺少 MARK");
            }

            var mark = _marks.Pop();
            var items = _stack.GetRange(mark, _stack.Count - mark).ToArray();
            _stack.RemoveRange(mark, _stack.Count - mark);
            return items;
        }

        private byte[] ReadBytes(int length)
        {
            if (length < 0)
            {
                throw new InvalidDataException("pickle 字节串长度无效");
            }

            return Take(length).ToArray();
        }

        /// <summary>BUILD: copy the state's attribute dict onto an inert object. Nothing is ever called.</summary>
        private void Build()
        {
            var state = Pop();
            if (Peek() is not PickleObject target)
            {
                return;
            }

            // Either {attrs} or ({attrs}, {slots}) as written by __reduce_ex__.
            IEnumerable<object?> parts = state is object?[] tuple ? tuple : [state];
            foreach (var part in parts)
            {
                if (part is PickleDict attributes)
                {
                    foreach (var (key, value) in attributes.Items)
                    {
                        target.Attributes.Set(key, value);
                    }
                }
            }
        }

        private object?[] PopTuple(int count)
        {
            var items = _stack.GetRange(_stack.Count - count, count).ToArray();
            _stack.RemoveRange(_stack.Count - count, count);
            return items;
        }

        private void SetItem()
        {
            var value = Pop();
            var key = Pop();
            if (Peek() is PickleDict dict)
            {
                dict.Items.Add(new(key, value));
            }
        }

        private void SetItems()
        {
            var items = PopToMark();
            if (Peek() is PickleDict dict)
            {
                for (var i = 0; i + 1 < items.Length; i += 2)
                {
                    dict.Items.Add(new(items[i], items[i + 1]));
                }
            }
        }

        private void Reduce()
        {
            var args = Pop() as object?[] ?? [];
            var callable = Pop();
            if (callable is not GlobalRef global)
            {
                _stack.Add(new PickleObject(null));
                return;
            }

            switch (global.Module, global.Name)
            {
                case ("collections", "OrderedDict"):
                    var dict = new PickleDict();
                    if (args.Length > 0 && args[0] is IEnumerable<object?> pairs)
                    {
                        foreach (var pair in pairs.OfType<object?[]>().Where(pair => pair.Length == 2))
                        {
                            dict.Items.Add(new(pair[0], pair[1]));
                        }
                    }

                    _stack.Add(dict);
                    return;

                case ("torch._utils", "_rebuild_tensor_v2") or ("torch._utils", "_rebuild_tensor"):
                    _stack.Add(RebuildTensor(args));
                    return;

                case ("torch._utils", "_rebuild_parameter"):
                    _stack.Add(args.Length > 0 ? args[0] : null);
                    return;

                default:
                    // Never import or call anything else: keep it as an inert object.
                    _stack.Add(new PickleObject(global));
                    return;
            }
        }

        private static TensorValue RebuildTensor(object?[] args)
        {
            if (args.Length < 4 || args[0] is not StorageRef storage)
            {
                throw new InvalidDataException("张量重建参数无效");
            }

            return new TensorValue(storage, AsLong(args[1]), AsLongs(args[2]), AsLongs(args[3]));
        }

        private static StorageRef ResolvePersistent(object? pid)
        {
            if (pid is not object?[] { Length: >= 5 } tuple || tuple[0] as string != "storage")
            {
                throw new InvalidDataException("不支持的 pickle 持久化对象");
            }

            var typeName = tuple[1] switch
            {
                GlobalRef global => global.Name,
                string text => text,
                _ => string.Empty,
            };
            return new StorageRef(typeName, Convert.ToString(tuple[2], System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty, AsLong(tuple[4]));
        }

        private static long AsLong(object? value) =>
            value is long number ? number : throw new InvalidDataException("pickle 整数类型无效");

        private static long[] AsLongs(object? value) =>
            value is object?[] items ? items.Select(AsLong).ToArray() : throw new InvalidDataException("pickle 元组类型无效");
    }
}
