using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Packable.StreamBinary;
using Packable.StreamBinary.Generic;

namespace Packable
{
    public abstract class BasePackable : IPackable
    {
        protected static Dictionary<Type, Func<BasePackable, Type, Stream, object>> UnpackInterfaces = new Dictionary<Type, Func<BasePackable, Type, Stream, object>>();
        protected static Dictionary<Type, Action<BasePackable, Type, Stream, object>> PackInterfaces = new Dictionary<Type, Action<BasePackable, Type, Stream, object>>();

        static Dictionary<Type, CachedType> TypeCache = new Dictionary<Type, CachedType>();
        static Dictionary<Type, List<CachedMember>> MemberCache = new Dictionary<Type, List<CachedMember>>();

        static BasePackable()
        {
            UnpackInterfaces.Add(typeof(IPackable), delegate(BasePackable self, Type type, Stream s)
            {
                var obj = (IPackable)Activator.CreateInstance(type);
                obj.Unpack(s);
                return obj;
            });
            UnpackInterfaces.Add(typeof(IList), delegate(BasePackable self, Type type, Stream s)
            {
                Type valuetype = type.IsGenericType
                                    ? type.GetGenericArguments()[0]
                                    : typeof(object);
                var collection = (IList)Activator.CreateInstance(type);
                int count = s.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    collection.Add(self.Unpack(s, valuetype));
                }
                return collection;
            });
            UnpackInterfaces.Add(typeof(IDictionary), delegate(BasePackable self, Type type, Stream s)
            {
                Type keytype = type.IsGenericType ? type.GetGenericArguments()[0] : typeof(object);
                Type valuetype = type.IsGenericType ? type.GetGenericArguments()[1] : typeof(object);
                var dictionary = (IDictionary)Activator.CreateInstance(type);
                int count = s.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    object key = self.Unpack(s, keytype);
                    object val = self.Unpack(s, valuetype);
                    dictionary.Add(key, val);
                }
                return dictionary;
            });

            PackInterfaces.Add(typeof(IPackable), delegate(BasePackable self, Type type, Stream s, object obj)
            {
                ((IPackable)obj).Pack(s);
            });
            PackInterfaces.Add(typeof(IList), delegate(BasePackable self, Type type, Stream s, object obj)
            {
                var collection = (IList)obj;
                s.Write((Int32)collection.Count);
                for (int i = 0; i < collection.Count; i++)
                    self.Pack(s, collection[i]);
            });
            PackInterfaces.Add(typeof(IDictionary), delegate(BasePackable self, Type type, Stream s, object obj)
            {
                var dictionary = (IDictionary)obj;
                s.Write((Int32)dictionary.Count);
                foreach (var key in dictionary.Keys)
                {
                    self.Pack(s, key);
                    self.Pack(s, dictionary[key]);
                }
            });
        }
        protected BasePackable()
        {

        }

        List<CachedMember> GetCachedMembers(Type type)
        {
            if (!MemberCache.ContainsKey(type))
            {
                lock (MemberCache)
                {
                    if (!MemberCache.ContainsKey(type))
                        MemberCache.Add(type, GetTypeCachedMembers(type));
                }
            }
            return MemberCache[type];

        }
        CachedType GetCachedType(Type type)
        {
            if (!TypeCache.ContainsKey(type))
            {
                lock (TypeCache)
                {
                    if (!TypeCache.ContainsKey(type))
                        TypeCache.Add(type, new CachedType(type));
                }
            }
            return TypeCache[type];
        }


        List<CachedMember> GetTypeCachedMembers(Type type)
        {
            var ret = new List<CachedMember>();
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

                if (member.GetCustomAttributes(typeof(DontPackAttribute), false).Length == 0)
                {
                    ret.Add(new CachedMember(member));
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
            List<CachedMember> members = GetCachedMembers(GetType());
            foreach (CachedMember member in members)
            {
                Pack(stream, member, member.Get(this));
            }
        }
        protected virtual void Pack(Stream stream, CachedType ct, object obj)
        {
            if (obj == null)
            {
                stream.WriteInt8(0);
                return;
            }

            if (!ct.IsValueType)
                stream.WriteInt8(1);

            ct.WriteFunc(this, ct.Type, stream, obj);
        }

        protected void Pack(Stream stream, object obj)
        {
            CachedType ct = GetCachedType(obj.GetType());
            Pack(stream, ct, obj);
        }

        public virtual void Unpack(Stream stream)
        {
            List<CachedMember> members = GetCachedMembers(GetType());
            foreach (CachedMember member in members)
            {
                member.Set(this, Unpack(stream, member.Type));
            }
        }
        protected virtual object Unpack(Stream stream, CachedType ct)
        {
            if (!ct.IsValueType && stream.ReadInt8() == 0)
                return null;

            return ct.ReadFunc(this, ct.Type, stream);
        }

        protected virtual object Unpack(Stream stream, Type type)
        {
            CachedType ct = GetCachedType(type);
            return Unpack(stream, ct);
        }

        public class CachedType
        {
            public bool IsValueType { get; set; }
            public Type Type { get; set; }
            public Func<BasePackable, Type, Stream, object> ReadFunc { get; set; }
            public Action<BasePackable, Type, Stream, object> WriteFunc { get; set; }

            public CachedType(Type type)
            {
                Type = type;
                IsValueType = Type.IsValueType;

                ReadFunc = GetReadFunc(Type);
                WriteFunc = GetWriteFunc(Type);
            }

            Func<BasePackable, Type, Stream, object> GetReadFunc(Type type)
            {
                if (ReadFuncs.ContainsKey(type))
                {
                    return ReadFuncs[type];
                }
                foreach (var t in type.GetInterfaces())
                {
                    if (UnpackInterfaces.ContainsKey(t))
                        return UnpackInterfaces[t];
                }
                throw new NotSupportedException();
            }
            Action<BasePackable, Type, Stream, object> GetWriteFunc(Type type)
            {
                if (WriteFuncs.ContainsKey(type))
                {
                    return WriteFuncs[type];
                }
                foreach (var t in type.GetInterfaces())
                {
                    if (PackInterfaces.ContainsKey(t))
                        return PackInterfaces[t];
                }
                throw new NotSupportedException();
            }
        }
        public interface IFastMember
        {
            void Set(object obj, object value);
            object Get(object obj);
        }
        public class FastProperty<T, F> : IFastMember
        {
            Action<T, F> Setter;
            Func<T, F> Getter;
            public FastProperty(PropertyInfo pi)
            {
                Setter = (Action<T, F>)Delegate.CreateDelegate(typeof(Action<T, F>), pi.GetSetMethod());
                Getter = (Func<T, F>)Delegate.CreateDelegate(typeof(Func<T, F>), pi.GetGetMethod());
            }

            public void Set(object obj, object value)
            {
                Setter((T)obj, (F)value);
            }
            public object Get(object obj)
            {
                return Getter((T)obj);
            }
        }
        public static class FastProperty
        {
            public static IFastMember Create(PropertyInfo pi)
            {
                return (IFastMember)Activator.CreateInstance(typeof(FastProperty<,>).MakeGenericType(pi.DeclaringType, pi.PropertyType), pi);
            }
        }
        public class CachedMember : CachedType
        {
            public MemberInfo Member { get; set; }

            IFastMember fastproperty;

            public void Set(object obj, object value)
            {
                if (Member is FieldInfo)
                    ((FieldInfo)Member).SetValue(obj, value);
                else if (Member is PropertyInfo)
                    fastproperty.Set(obj, value);
                else
                    throw new NotSupportedException();
            }
            public object Get(object obj)
            {
                if (Member is FieldInfo)
                    return ((FieldInfo)Member).GetValue(obj);
                if (Member is PropertyInfo)
                    return fastproperty.Get(obj);

                throw new NotSupportedException();
            }

            public CachedMember(MemberInfo mi)
                : base(GetMemberType(mi))
            {
                Member = mi;

                if (Member is PropertyInfo)
                    fastproperty = FastProperty.Create((PropertyInfo)Member);


            }


            static Type GetMemberType(MemberInfo mi)
            {
                Type type;

                if (mi is FieldInfo)
                    type = ((FieldInfo)mi).FieldType;
                else if (mi is PropertyInfo)
                    type = ((PropertyInfo)mi).PropertyType;
                else
                    throw new NotSupportedException();

                if (type.BaseType == typeof(Enum))
                    type = Enum.GetUnderlyingType(type);

                return type;
            }

        }

        #region Read/Write Functions
        protected static readonly Dictionary<Type, Func<BasePackable, Type, Stream, object>> ReadFuncs = new Dictionary<Type, Func<BasePackable, Type, Stream, object>>
        {
            {typeof(bool), (p, y, s) => s.ReadBoolean() },
            {typeof(byte), (p, y, s) => s.ReadInt8() },
            {typeof(Int16), (p, y, s) => s.ReadInt16() },
            {typeof(Int32), (p, y, s) => s.ReadInt32() },
            {typeof(Int64), (p, y, s) => s.ReadInt64() },
            {typeof(byte[]), (p, y, s) => s.ReadBytes() },
            {typeof(string), (p, y, s) => s.ReadString() },
        };
        protected static readonly Dictionary<Type, Action<BasePackable, Type, Stream, object>> WriteFuncs = new Dictionary<Type, Action<BasePackable, Type, Stream, object>>
        {
            {typeof(bool), (p, y, s, o) => s.WriteBoolean((bool)o) },
            {typeof(byte), (p, y, s, o) => s.WriteInt8((byte)o) },
            {typeof(Int16), (p, y, s, o) => s.WriteInt16((Int16)o) },
            {typeof(Int32), (p, y, s, o) => s.WriteInt32((Int32)o) },
            {typeof(Int64), (p, y, s, o) => s.WriteInt64((Int64)o) },
            {typeof(byte[]), (p, y, s, o) => s.WriteBytes((byte[])o) },
            {typeof(string), (p, y, s, o) => s.WriteString((string)o) },
        };
        #endregion
    }
    public interface IPackable
    {
        void Pack(Stream stream);
        void Unpack(Stream stream);
    }

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public class DontPackAttribute : Attribute
    {
    }
}
