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
    public abstract class BasePackable : IPackable
    {
        static Dictionary<Type, CachedReadWrite> TypeCache = new Dictionary<Type, CachedReadWrite>();

        static BasePackable()
        {
        }
        protected BasePackable()
        {

        }

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
            bool serialize = true;
            MemberInfo[] members = type.GetMembers(BindingFlags.Instance | BindingFlags.Public);
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

                var pack =
                    (PackAttribute)
                    member.GetCustomAttributes(false).FirstOrDefault(
                        o => o.GetType() == typeof(PackAttribute) || o.GetType() == typeof(DontPackAttribute));

                if (pack != null && pack.Fall)
                    serialize = !pack.Dont;

                if (pack != null && pack.Dont)
                    continue;

                if (serialize || (pack != null && !pack.Dont))
                {
                    ret.Add(member);
                }
            }

            //ret.Sort((o1, o2) => o1.MetadataToken.CompareTo(o2.MetadataToken));

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
                    if (m is FieldInfo)
                    {
                        var f = (FieldInfo)m;
                        il.Emit(OpCodes.Ldarg_1);
                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Castclass, type);
                        il.Emit(OpCodes.Ldfld, f);
                        if (f.FieldType.IsValueType)
                            il.Emit(OpCodes.Box, f.FieldType);
                        il.Emit(OpCodes.Call, WriteFuncs[f.FieldType].Method);
                    }
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
                    if (m is FieldInfo)
                    {
                        var f = (FieldInfo)m;
                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Castclass, type);
                        il.Emit(OpCodes.Ldarg_1);
                        il.Emit(OpCodes.Call, ReadFuncs[f.FieldType].Method);
                        if (f.FieldType.IsValueType)
                            il.Emit(OpCodes.Unbox_Any, f.FieldType);
                        else
                            il.Emit(OpCodes.Castclass, f.FieldType);
                        il.Emit(OpCodes.Stfld, f);
                    }
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
        protected static readonly Dictionary<Type, Action<Stream, object>> WriteFuncs = new Dictionary<Type, Action<Stream, object>>
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
                public static void Write(Stream stream, object obj)
                {
                    stream.WriteBoolean((bool)obj);
                }
            }
            public static class Int8Funcs
            {
                public static object Read( Stream stream)
                {
                    return stream.ReadInt8();
                }
                public static void Write(Stream stream, object obj)
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
                public static void Write(Stream stream, object obj)
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
                public static void Write(Stream stream, object obj)
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
                public static void Write(Stream stream, object obj)
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
                public static void Write(Stream stream, object obj)
                {
                    stream.WriteBytes((byte[])obj);
                }
            }
            public class StringFuncs
            {
                public static object Read(Stream stream)
                {
                    return stream.ReadString();
                }
                public static void Write(Stream stream, object obj)
                {
                    stream.WriteString((string)obj);
                }
            }

        }

        #endregion

    }

    public interface IPackable
    {
        void Pack(Stream stream);
        void Unpack(Stream stream);
    }

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public class PackAttribute : Attribute
    {
        /// <summary>
        /// Do not serialize
        /// </summary>
        public bool Dont { get; set; }
        /// <summary>
        /// Apply to members below this member
        /// </summary>
        public bool Fall { get; set; }
        public PackAttribute(bool fall = false, bool dont = false)
        {
            Dont = dont;
            Fall = fall;
        }
    }
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public class DontPackAttribute : PackAttribute
    {
        public DontPackAttribute(bool fall = false)
            : base(fall, true)
        {
        }
    }


}
