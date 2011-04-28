using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using Packable.StreamBinary;
using Packable.StreamBinary.Generic;

namespace Packable
{
    public abstract class BaseFastPackable : IPackable
    {
        static Dictionary<Type, CachedReadWrite> TypeCache = new Dictionary<Type, CachedReadWrite>();

        CachedReadWrite GetCachedType(Type type)
        {
            lock (TypeCache)
            {
                CachedReadWrite ret;
                if (!TypeCache.TryGetValue(type, out ret))
                    TypeCache.Add(type, ret = new CachedReadWrite(type));
                return ret;
            }
        }

        static List<MemberInfo> GetMembers(Type type)
        {
            var ret = new List<MemberInfo>();
            var members = type.GetMembers(BindingFlags.Instance | BindingFlags.Public);
            foreach (MemberInfo member in members)
            {
                if (!(member is FieldInfo) && !(member is PropertyInfo))
                    continue;

                if (member is PropertyInfo)
                {
                    var prop = (PropertyInfo)member;
                    if (!prop.CanRead || !prop.CanWrite)
                        continue;
                }

                if (member.GetCustomAttributes(typeof(DontPackAttribute), false).Length == 0)
                {
                    ret.Add(member);
                }
            }

            return ret;
        }

        public byte[] ToArray()
        {
            var ms = new MemoryStream();
            Pack(ms);
            return ms.ToArray();
        }
        public override string ToString()
        {
            return ToArray().Aggregate(string.Empty, (str, b) =>
            {
                if (str != string.Empty)
                    str += ", ";
                return string.Format("{0}0x{1:X2}", str, b);
            });
        }

        public virtual void Pack(Stream stream)
        {
            var c = GetCachedType(GetType());
            c.Write(this, stream);
        }


        public virtual void Unpack(Stream stream)
        {
            var c = GetCachedType(GetType());
            c.Read(this, stream);
        }

        public class CachedReadWrite
        {
            public Action<object, Stream> Write { get; set; }
            public Action<object, Stream> Read { get; set; }


            public CachedReadWrite(Type type)
            {
                Write = CreateWrite(type);
                Read = CreateRead(type);
            }

            public Action<object, Stream> CreateWrite(Type type)
            {
                var members = GetMembers(type);
                var ret = new DynamicMethod("Write_" + type.MetadataToken, null, new Type[] { typeof(object), typeof(Stream) });
                var il = ret.GetILGenerator();
                foreach (MemberInfo m in members)
                {
                    var f = m as FieldInfo;
                    var p = m as PropertyInfo;

                    Type mtype = null;
                    if (m is FieldInfo)
                        mtype = f.FieldType;
                    else if (m is PropertyInfo)
                        mtype = p.PropertyType;

                    if (!WriteFuncs.ContainsKey(mtype))
                        throw new NotSupportedException();

                    var ifnotnull = il.DefineLabel();
                    var ifnull = il.DefineLabel();

                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Castclass, type);

                    if (m is FieldInfo)
                        il.Emit(OpCodes.Ldfld, f);
                    else if (m is PropertyInfo)
                        il.Emit(OpCodes.Call, p.GetGetMethod());

                    if (!mtype.IsValueType)
                    {
                        il.Emit(OpCodes.Dup);
                        il.Emit(OpCodes.Brtrue, ifnotnull);
                        il.Emit(OpCodes.Ldc_I4_0);
                        il.Emit(OpCodes.Box, typeof(byte));
                        il.Emit(OpCodes.Ldarg_1);
                        il.Emit(OpCodes.Call, WriteFuncs[typeof(byte)].Method);
                        il.Emit(OpCodes.Pop);
                        il.Emit(OpCodes.Br, ifnull);
                        il.MarkLabel(ifnotnull);
                        il.Emit(OpCodes.Ldc_I4_1);
                        il.Emit(OpCodes.Box, typeof(byte));
                        il.Emit(OpCodes.Ldarg_1);
                        il.Emit(OpCodes.Call, WriteFuncs[typeof(byte)].Method);
                    }

                    if (mtype.IsValueType)
                        il.Emit(OpCodes.Box, mtype);

                    il.Emit(OpCodes.Ldarg_1);
                    il.Emit(OpCodes.Call, WriteFuncs[mtype].Method);
                    il.MarkLabel(ifnull);
                }
                il.Emit(OpCodes.Ret);
                return (Action<object, Stream>)ret.CreateDelegate(typeof(Action<object, Stream>));
            }
            public Action<object, Stream> CreateRead(Type type)
            {
                var members = GetMembers(type);
                var ret = new DynamicMethod("Read_" + type.MetadataToken, null, new Type[] { typeof(object), typeof(Stream) });
                var il = ret.GetILGenerator();
                foreach (MemberInfo m in members)
                {
                    var f = m as FieldInfo;
                    var p = m as PropertyInfo;

                    Type mtype = null;
                    if (m is FieldInfo)
                        mtype = f.FieldType;
                    else if (m is PropertyInfo)
                        mtype = p.PropertyType;

                    if (!ReadFuncs.ContainsKey(mtype))
                        throw new NotSupportedException();

                    var ifnotnull = il.DefineLabel();
                    var ifnull = il.DefineLabel();

                    if (!mtype.IsValueType)
                    {
                        il.Emit(OpCodes.Ldarg_1);
                        il.Emit(OpCodes.Call, ReadFuncs[typeof(byte)].Method);
                        il.Emit(OpCodes.Unbox_Any, typeof(byte));
                        il.Emit(OpCodes.Brtrue, ifnotnull);
                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Castclass, type);
                        il.Emit(OpCodes.Ldnull);
                        if (m is FieldInfo)
                            il.Emit(OpCodes.Stfld, f);
                        else if (m is PropertyInfo)
                            il.Emit(OpCodes.Call, p.GetSetMethod());
                        il.Emit(OpCodes.Br, ifnull);
                    }

                    il.MarkLabel(ifnotnull);

                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Castclass, type);
                    il.Emit(OpCodes.Ldarg_1);
                    il.Emit(OpCodes.Call, ReadFuncs[mtype].Method);

                    if (mtype.IsValueType)
                        il.Emit(OpCodes.Unbox_Any, mtype);
                    else
                        il.Emit(OpCodes.Castclass, mtype);

                    if (m is FieldInfo)
                        il.Emit(OpCodes.Stfld, f);
                    else if (m is PropertyInfo)
                        il.Emit(OpCodes.Call, p.GetSetMethod());

                    il.MarkLabel(ifnull);
                }
                il.Emit(OpCodes.Ret);
                return (Action<object, Stream>)ret.CreateDelegate(typeof(Action<object, Stream>));
            }
        }

        #region Read/Write Functions
        protected static readonly Dictionary<Type, Func<Stream, object>> ReadFuncs = new Dictionary<Type, Func<Stream, object>>
        {
            {typeof(bool), DefaultFuncs.BooleanFuncs.Read },
            {typeof(byte), DefaultFuncs.Int8Funcs.Read },
            {typeof(Int16), DefaultFuncs.Int16Funcs.Read },
            {typeof(Int32), DefaultFuncs.Int32Funcs.Read },
            {typeof(Int64), DefaultFuncs.Int64Funcs.Read },
            {typeof(byte[]), DefaultFuncs.ByteArrayFuncs.Read },
            {typeof(string), DefaultFuncs.StringFuncs.Read },
        };
        protected static readonly Dictionary<Type, Action<object, Stream>> WriteFuncs = new Dictionary<Type, Action<object, Stream>>
        {
            {typeof(bool), DefaultFuncs.BooleanFuncs.Write },
            {typeof(byte), DefaultFuncs.Int8Funcs.Write },
            {typeof(Int16), DefaultFuncs.Int16Funcs.Write },
            {typeof(Int32), DefaultFuncs.Int32Funcs.Write },
            {typeof(Int64), DefaultFuncs.Int64Funcs.Write },
            {typeof(byte[]), DefaultFuncs.ByteArrayFuncs.Write },
            {typeof(string), DefaultFuncs.StringFuncs.Write },
        };

        public static class DefaultFuncs
        {
            public static class BooleanFuncs
            {
                public static object Read(Stream stream)
                {
                    return stream.ReadBoolean();
                }
                public static void Write(object obj, Stream stream)
                {
                    stream.WriteBoolean((bool)obj);
                }
            }
            public static class Int8Funcs
            {
                public static object Read(Stream stream)
                {
                    return stream.ReadInt8();
                }
                public static void Write(object obj, Stream stream)
                {
                    stream.WriteInt8((byte)obj);
                }
            }
            public static class Int16Funcs
            {
                public static object Read(Stream stream)
                {
                    return stream.ReadInt16();
                }
                public static void Write(object obj, Stream stream)
                {
                    stream.WriteInt16((Int16)obj);
                }
            }
            public class Int32Funcs
            {
                public static object Read(Stream stream)
                {
                    return stream.ReadInt32();
                }
                public static void Write(object obj, Stream stream)
                {
                    stream.WriteInt32((Int32)obj);
                }
            }
            public class Int64Funcs
            {
                public static object Read(Stream stream)
                {
                    return stream.ReadInt64();
                }
                public static void Write(object obj, Stream stream)
                {
                    stream.WriteInt64((Int64)obj);
                }
            }
            public class ByteArrayFuncs
            {
                public static object Read(Stream stream)
                {
                    return stream.ReadBytes();
                }
                public static void Write(object obj, Stream stream)
                {
                    stream.WriteBytesWithLength((byte[])obj);
                }
            }
            public class StringFuncs
            {
                public static object Read(Stream stream)
                {
                    return stream.ReadString();
                }
                public static void Write(object obj, Stream stream)
                {
                    stream.WriteString((string)obj);
                }
            }

        }

        #endregion

    }
}
