using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LBOLMP.Net
{
    /// <summary>
    /// Little-endian binary writer for message payloads.
    /// Deliberately hand-rolled so the wire format is explicit and stable across builds.
    /// (Thanks, devious Mac users)
    /// </summary>
    public sealed class NetWriter
    {
        private readonly MemoryStream _stream = new MemoryStream(256);
        private readonly BinaryWriter _writer;

        public NetWriter()
        {
            _writer = new BinaryWriter(_stream, Encoding.UTF8);
        }

        public void Bool(bool v) => _writer.Write(v);
        public void Byte(byte v) => _writer.Write(v);
        public void SByte(sbyte v) => _writer.Write(v);
        public void Short(short v) => _writer.Write(v);
        public void UShort(ushort v) => _writer.Write(v);
        public void Int(int v) => _writer.Write(v);
        public void UInt(uint v) => _writer.Write(v);
        public void Long(long v) => _writer.Write(v);
        public void ULong(ulong v) => _writer.Write(v);
        public void Float(float v) => _writer.Write(v);

        public void String(string v)
        {
            _writer.Write(v ?? string.Empty);
        }

        public void IntList(IReadOnlyList<int> values)
        {
            if (values == null)
            {
                Int(0);
                return;
            }
            Int(values.Count);
            for (int i = 0; i < values.Count; i++)
            {
                Int(values[i]);
            }
        }

        public void StringList(IReadOnlyList<string> values)
        {
            if (values == null)
            {
                Int(0);
                return;
            }
            Int(values.Count);
            for (int i = 0; i < values.Count; i++)
            {
                String(values[i]);
            }
        }

        public void Bytes(byte[] value)
        {
            if (value == null)
            {
                Int(0);
                return;
            }
            Int(value.Length);
            _writer.Write(value);
        }

        public byte[] ToArray()
        {
            _writer.Flush();
            return _stream.ToArray();
        }
    }

    /// <summary>Reader counterpart to <see cref="NetWriter"/>.</summary>
    public sealed class NetReader
    {
        private readonly MemoryStream _stream;
        private readonly BinaryReader _reader;

        public NetReader(byte[] data)
        {
            _stream = new MemoryStream(data ?? Array.Empty<byte>(), false);
            _reader = new BinaryReader(_stream, Encoding.UTF8);
        }

        public bool Bool() => _reader.ReadBoolean();
        public byte Byte() => _reader.ReadByte();
        public sbyte SByte() => _reader.ReadSByte();
        public short Short() => _reader.ReadInt16();
        public ushort UShort() => _reader.ReadUInt16();
        public int Int() => _reader.ReadInt32();
        public uint UInt() => _reader.ReadUInt32();
        public long Long() => _reader.ReadInt64();
        public ulong ULong() => _reader.ReadUInt64();
        public float Float() => _reader.ReadSingle();
        public string String() => _reader.ReadString();

        public int[] IntArray()
        {
            int count = Int();
            var result = new int[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = Int();
            }
            return result;
        }

        public string[] StringArray()
        {
            int count = Int();
            var result = new string[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = String();
            }
            return result;
        }

        public byte[] Bytes()
        {
            int count = Int();
            return _reader.ReadBytes(count);
        }

        public bool AtEnd => _stream.Position >= _stream.Length;
    }
}
