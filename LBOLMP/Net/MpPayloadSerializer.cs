using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace LBOLMP.Net
{
    /// <summary>
    /// Turns the public fields of an <see cref="MpEffectPayload"/> into bytes and back.
    ///
    /// Exists so that nobody has to hand-write a matching pair of Write/Read methods, which is
    /// easy to get out of step and fails silently when it happens. Fields are ordered by name
    /// rather than by declaration order, because the CLR does not promise the latter.
    /// </summary>
    internal static class MpPayloadSerializer
    {
        private static readonly Dictionary<Type, FieldInfo[]> Cache = new Dictionary<Type, FieldInfo[]>();

        internal static void Write(MpEffectPayload payload, NetWriter w)
        {
            foreach (var field in FieldsOf(payload.GetType()))
            {
                WriteValue(field.FieldType, field.GetValue(payload), w);
            }
        }

        internal static void Read(MpEffectPayload payload, NetReader r)
        {
            foreach (var field in FieldsOf(payload.GetType()))
            {
                field.SetValue(payload, ReadValue(field.FieldType, r));
            }
        }

        /// <summary>
        /// Checks that every field can actually be sent. Called when an effect registers, so an
        /// unsupported field is a startup error naming the field rather than a garbled message
        /// in somebody else's lobby.
        /// </summary>
        internal static void Validate(Type payloadType)
        {
            foreach (var field in FieldsOf(payloadType))
            {
                if (!IsSupported(field.FieldType))
                {
                    throw new InvalidOperationException(
                        $"{payloadType.FullName}.{field.Name}: {field.FieldType.Name} cannot be sent over the network. " +
                        "Use a primitive, string, enum, or a list of ints or strings, or override Write/Read yourself.");
                }
            }
        }

        private static FieldInfo[] FieldsOf(Type type)
        {
            if (Cache.TryGetValue(type, out var cached))
            {
                return cached;
            }

            var fields = type
                .GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(f => !f.IsInitOnly)
                .OrderBy(f => f.Name, StringComparer.Ordinal)
                .ToArray();

            Cache[type] = fields;
            return fields;
        }

        private static bool IsSupported(Type type)
        {
            if (type.IsEnum)
            {
                return true;
            }

            return type == typeof(bool) || type == typeof(byte) || type == typeof(sbyte)
                   || type == typeof(short) || type == typeof(ushort)
                   || type == typeof(int) || type == typeof(uint)
                   || type == typeof(long) || type == typeof(ulong)
                   || type == typeof(float) || type == typeof(string)
                   || type == typeof(byte[]) || type == typeof(int[]) || type == typeof(string[])
                   || type == typeof(List<int>) || type == typeof(List<string>);
        }

        private static void WriteValue(Type type, object value, NetWriter w)
        {
            if (type.IsEnum)
            {
                w.Int(Convert.ToInt32(value));
                return;
            }

            if (type == typeof(bool)) { w.Bool((bool)value); return; }
            if (type == typeof(byte)) { w.Byte((byte)value); return; }
            if (type == typeof(sbyte)) { w.SByte((sbyte)value); return; }
            if (type == typeof(short)) { w.Short((short)value); return; }
            if (type == typeof(ushort)) { w.UShort((ushort)value); return; }
            if (type == typeof(int)) { w.Int((int)value); return; }
            if (type == typeof(uint)) { w.UInt((uint)value); return; }
            if (type == typeof(long)) { w.Long((long)value); return; }
            if (type == typeof(ulong)) { w.ULong((ulong)value); return; }
            if (type == typeof(float)) { w.Float((float)value); return; }
            if (type == typeof(string)) { w.String((string)value); return; }
            if (type == typeof(byte[])) { w.Bytes((byte[])value); return; }
            if (type == typeof(int[])) { w.IntList((int[])value); return; }
            if (type == typeof(string[])) { w.StringList((string[])value); return; }
            if (type == typeof(List<int>)) { w.IntList((List<int>)value); return; }
            if (type == typeof(List<string>)) { w.StringList((List<string>)value); return; }

            throw new InvalidOperationException($"Cannot send a {type.Name} in an MP effect payload");
        }

        private static object ReadValue(Type type, NetReader r)
        {
            if (type.IsEnum)
            {
                return Enum.ToObject(type, r.Int());
            }

            if (type == typeof(bool)) { return r.Bool(); }
            if (type == typeof(byte)) { return r.Byte(); }
            if (type == typeof(sbyte)) { return r.SByte(); }
            if (type == typeof(short)) { return r.Short(); }
            if (type == typeof(ushort)) { return r.UShort(); }
            if (type == typeof(int)) { return r.Int(); }
            if (type == typeof(uint)) { return r.UInt(); }
            if (type == typeof(long)) { return r.Long(); }
            if (type == typeof(ulong)) { return r.ULong(); }
            if (type == typeof(float)) { return r.Float(); }
            if (type == typeof(string)) { return r.String(); }
            if (type == typeof(byte[])) { return r.Bytes(); }
            if (type == typeof(int[])) { return r.IntArray(); }
            if (type == typeof(string[])) { return r.StringArray(); }
            if (type == typeof(List<int>)) { return new List<int>(r.IntArray()); }
            if (type == typeof(List<string>)) { return new List<string>(r.StringArray()); }

            throw new InvalidOperationException($"Cannot read a {type.Name} from an MP effect payload");
        }
    }
}
