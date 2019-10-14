using System;
using System.Collections.Generic;
using System.Reflection;

using Yuzu.Deserializer;
using Yuzu.Metadata;
using Yuzu.Util;

namespace Yuzu.Binary
{
	public class BinaryDeserializer : AbstractReaderDeserializer
	{
		public static BinaryDeserializer Instance = new BinaryDeserializer();

		public BinarySerializeOptions BinaryOptions = new BinarySerializeOptions();

		public BinaryDeserializer() { InitReaders(); }

		public override void Initialize() {}

		private object ReadSByte() => Reader.ReadSByte();
		private object ReadByte() => Reader.ReadByte();
		private object ReadShort() => Reader.ReadInt16();
		private object ReadUShort() => Reader.ReadUInt16();
		private object ReadInt() => Reader.ReadInt32();
		private object ReadUInt() => Reader.ReadUInt32();
		private object ReadLong() => Reader.ReadInt64();
		private object ReadULong() => Reader.ReadUInt64();
		private object ReadBool() => Reader.ReadBoolean();
		private object ReadChar() => Reader.ReadChar();
		private object ReadFloat() => Reader.ReadSingle();
		private object ReadDouble() => Reader.ReadDouble();
		private object ReadDecimal() => Reader.ReadDecimal();

		private DateTime ReadDateTime() => DateTime.FromBinary(Reader.ReadInt64());
		private DateTimeOffset ReadDateTimeOffset()
		{
			var d = DateTime.FromBinary(Reader.ReadInt64());
			var t = new TimeSpan(Reader.ReadInt64());
			return new DateTimeOffset(d, t);
		}
		private TimeSpan ReadTimeSpan() => new TimeSpan(Reader.ReadInt64());

		private Guid ReadGuid() => new Guid(Reader.ReadBytes(16));

		private object ReadString()
		{
			var s = Reader.ReadString();
			return s != "" ? s : Reader.ReadBoolean() ? null : "";
		}

		private Type ReadType()
		{
			var rt = (RoughType)Reader.ReadByte();
			if (RoughType.FirstAtom <= rt && rt <= RoughType.LastAtom) {
				var t = RT.roughTypeToType[(int)rt];
				if (t != null) return t;
			}
			switch (rt) {
				case RoughType.Sequence:
					return typeof(List<>).MakeGenericType(ReadType());
				case RoughType.Mapping:
					var k = ReadType();
					var v = ReadType();
					return typeof(Dictionary<,>).MakeGenericType(k, v);
				case RoughType.Record:
					return typeof(Record);
				case RoughType.Nullable:
					return typeof(Nullable<>).MakeGenericType(ReadType());
				default:
					throw Error("Unknown rough type {0}", rt);
			}
		}

		private bool ReadCompatibleType(Type expectedType)
		{
			if (expectedType.IsEnum)
				return ReadCompatibleType(Enum.GetUnderlyingType(expectedType));
			if (expectedType.IsRecord() && expectedType.Namespace != "System") {
				var sg = Meta.Get(expectedType, Options).Surrogate;
				if (sg.SurrogateType != null && sg.FuncFrom != null)
					return ReadCompatibleType(sg.SurrogateType);
			}

			var rt = (RoughType)Reader.ReadByte();
			if (RoughType.FirstAtom <= rt && rt <= RoughType.LastAtom) {
				var t = RT.roughTypeToType[(int)rt];
				if (t != null) return t == expectedType;
			}
			if (expectedType.IsArray)
				return rt == RoughType.Sequence && ReadCompatibleType(expectedType.GetElementType());

			var idict = Utils.GetIDictionary(expectedType);
			if (idict != null) {
				if (rt != RoughType.Mapping)
					return false;
				var g = expectedType.GetGenericArguments();
				return ReadCompatibleType(g[0]) && ReadCompatibleType(g[1]);
			}
			if (expectedType.IsGenericType && expectedType.GetGenericTypeDefinition() == typeof(Nullable<>))
				return rt == RoughType.Nullable && ReadCompatibleType(expectedType.GetGenericArguments()[0]);
			var icoll = Utils.GetICollection(expectedType);
			if (icoll != null)
				return rt == RoughType.Sequence && ReadCompatibleType(icoll.GetGenericArguments()[0]);
			if (rt == RoughType.Record)
				return expectedType.IsRecord();
			throw Error("Unknown rough type {0}", rt);
		}

		protected object ReadAny()
		{
			var t = ReadType();
			return t == typeof(object) ? null : ReadValueFunc(t)();
		}

		private void InitReaders()
		{
			readerCache[typeof(sbyte)] = ReadSByte;
			readerCache[typeof(byte)] = ReadByte;
			readerCache[typeof(short)] = ReadShort;
			readerCache[typeof(ushort)] = ReadUShort;
			readerCache[typeof(int)] = ReadInt;
			readerCache[typeof(uint)] = ReadUInt;
			readerCache[typeof(long)] = ReadLong;
			readerCache[typeof(ulong)] = ReadULong;
			readerCache[typeof(bool)] = ReadBool;
			readerCache[typeof(char)] = ReadChar;
			readerCache[typeof(float)] = ReadFloat;
			readerCache[typeof(double)] = ReadDouble;
			readerCache[typeof(decimal)] = ReadDecimal;
			readerCache[typeof(DateTime)] = ReadDateTimeObj;
			readerCache[typeof(DateTimeOffset)] = ReadDateTimeOffsetObj;
			readerCache[typeof(TimeSpan)] = ReadTimeSpanObj;
			readerCache[typeof(Guid)] = ReadGuidObj;
			readerCache[typeof(string)] = ReadString;
			readerCache[typeof(object)] = ReadAny;
			readerCache[typeof(Record)] = ReadObject<object>;
		}

		private object ReadDateTimeObj() => ReadDateTime();
		private object ReadDateTimeOffsetObj() => ReadDateTimeOffset();
		private object ReadTimeSpanObj() => ReadTimeSpan();
		private object ReadGuidObj() => ReadGuid();

		protected void ReadIntoCollection<T>(ICollection<T> list)
		{
			var rf = ReadValueFunc(typeof(T));
			var count = Reader.ReadInt32();
			for (int i = 0; i < count; ++i)
				list.Add((T)rf());
		}

		protected void ReadIntoCollectionNG<T>(object list) => ReadIntoCollection((ICollection<T>)list);

		protected I ReadCollection<I, E>() where I : class, ICollection<E>, new()
		{
			var count = Reader.ReadInt32();
			if (count == -1)
				return null;
			var list = new I();
			var rf = ReadValueFunc(typeof(E));
			for (int i = 0; i < count; ++i)
				list.Add((E)rf());
			return list;
		}

		protected List<T> ReadList<T>()
		{
			var count = Reader.ReadInt32();
			if (count == -1) return null;
			var list = new List<T>();
			var rf = ReadValueFunc(typeof(T));
			for (int i = 0; i < count; ++i)
				list.Add((T)rf());
			return list;
		}

		protected List<object> ReadListRecord(Func<object> readValue)
		{
			var count = Reader.ReadInt32();
			if (count == -1)
				return null;
			var list = new List<object>();
			for (int i = 0; i < count; ++i)
				list.Add(readValue());
			return list;
		}

		protected void ReadIntoDictionary<K, V>(IDictionary<K, V> dict)
		{
			var rk = ReadValueFunc(typeof(K));
			var rv = ReadValueFunc(typeof(V));
			var count = Reader.ReadInt32();
			for (int i = 0; i < count; ++i)
				dict.Add((K)rk(), (V)rv());
		}

		protected void ReadIntoDictionaryNG<K, V>(object dict) => ReadIntoDictionary((IDictionary<K, V>)dict);

		protected Dictionary<K, V> ReadDictionary<K, V>()
		{
			var count = Reader.ReadInt32();
			if (count == -1)
				return null;
			var dict = new Dictionary<K, V>();
			var rk = ReadValueFunc(typeof(K));
			var rv = ReadValueFunc(typeof(V));
			for (int i = 0; i < count; ++i) {
				var key = (K)rk();
				var value = rv();
				if (!(value is V))
					throw Error("Incompatible type for key {0}, expected: {1} but got {2}",
						key.ToString(), typeof(V), value.GetType());
				dict.Add(key, (V)value);
			}
			return dict;
		}

		protected I ReadIDictionary<I, K, V>() where I : class, IDictionary<K, V>, new()
		{
			var count = Reader.ReadInt32();
			if (count == -1)
				return null;
			var dict = new I();
			var rk = ReadValueFunc(typeof(K));
			var rv = ReadValueFunc(typeof(V));
			for (int i = 0; i < count; ++i) {
				var key = (K)rk();
				var value = rv();
				if (!(value is V))
					throw Error("Incompatible type for key {0}, expected: {1} but got {2}",
						key.ToString(), typeof(V), value.GetType());
				dict.Add(key, (V)value);
			}
			return dict;
		}

		protected Dictionary<K, object> ReadDictionaryRecord<K>(Func<object> readValue)
		{
			var count = Reader.ReadInt32();
			if (count == -1)
				return null;
			var dict = new Dictionary<K, object>();
			var rk = ReadValueFunc(typeof(K));
			for (int i = 0; i < count; ++i)
				dict.Add((K)rk(), readValue());
			return dict;
		}

		protected T[] ReadArray<T>()
		{
			var count = Reader.ReadInt32();
			if (count == -1)
				return null;
			var rf = ReadValueFunc(typeof(T));
			var array = new T[count];
			for (int i = 0; i < count; ++i)
				array[i] = (T)rf();
			return array;
		}

		protected Action<T> ReadAction<T>() => GetAction<T>(Reader.ReadString());

		// Zeroth element corresponds to 'null'.
		private List<ReaderClassDef> classDefs = new List<ReaderClassDef> { new ReaderClassDef() };

		protected virtual void PrepareReaders(ReaderClassDef def) => def.ReadFields = ReadFields;

		public void ClearClassIds() => classDefs = new List<ReaderClassDef> { new ReaderClassDef() };

		private ReaderClassDef GetClassDefUnknown(string typeName)
		{
			var result = new ReaderClassDef {
				Meta = Meta.Unknown,
				Make = (bd, def) => {
					var obj = new YuzuUnknownBinary { ClassTag = typeName, Def = def };
					ReadFields(bd, def, obj);
					return obj;
				},
			};
			var theirCount = Reader.ReadInt16();
			for (int theirIndex = 0; theirIndex < theirCount; ++theirIndex) {
				var theirName = Reader.ReadString();
				var t = ReadType();
				var rf = ReadValueFunc(t);
				result.Fields.Add(new ReaderClassDef.FieldDef {
					Name = theirName, Type = t, OurIndex = -1,
					ReadFunc = obj => ((YuzuUnknown)obj).Fields[theirName] = rf()
				});
			}
			classDefs.Add(result);
			return result;
		}

		private void AddUnknownFieldDef(ReaderClassDef def, string fieldName, string typeName)
		{
			if (!Options.AllowUnknownFields)
				throw Error("New field {0} for class {1}", fieldName, typeName);
			var fd = new ReaderClassDef.FieldDef { Name = fieldName, OurIndex = -1, Type = ReadType() };
			var rf = ReadValueFunc(fd.Type);
			if (def.Meta.GetUnknownStorage == null)
				fd.ReadFunc = obj => rf();
			else
				fd.ReadFunc = obj => def.Meta.GetUnknownStorage(obj).Add(fieldName, rf());
			def.Fields.Add(fd);
		}

		private Action<object> MakeReadOrMergeFunc(Meta.Item yi)
		{
			if (yi.SetValue != null) {
				var rf = ReadValueFunc(yi.Type);
				return obj => yi.SetValue(obj, rf());
			}
			else {
				var mf = MergeValueFunc(yi.Type);
				return obj => mf(yi.GetValue(obj));
			}
		}

		private void InitClassDef(ReaderClassDef def, string typeName)
		{
			var ourCount = def.Meta.Items.Count;
			var theirCount = Reader.ReadInt16();
			int ourIndex = 0, theirIndex = 0;
			var theirName = "";
			while (ourIndex < ourCount && theirIndex < theirCount) {
				var yi = def.Meta.Items[ourIndex];
				var ourName = yi.Tag(Options);
				if (theirName == "")
					theirName = Reader.ReadString();
				var cmp = String.CompareOrdinal(ourName, theirName);
				if (cmp < 0) {
					if (!yi.IsOptional)
						throw Error("Missing required field {0} for class {1}", ourName, typeName);
					ourIndex += 1;
				}
				else if (cmp > 0) {
					AddUnknownFieldDef(def, theirName, typeName);
					theirIndex += 1;
					theirName = "";
				}
				else {
					if (!ReadCompatibleType(yi.Type))
						throw Error(
							"Incompatible type for field {0}, expected {1}", ourName, yi.Type);
					def.Fields.Add(new ReaderClassDef.FieldDef {
						Name = theirName, OurIndex = ourIndex + 1, Type = yi.Type,
						ReadFunc = MakeReadOrMergeFunc(yi),
					});
					ourIndex += 1;
					theirIndex += 1;
					theirName = "";
				}
			}
			for (; ourIndex < ourCount; ++ourIndex) {
				var yi = def.Meta.Items[ourIndex];
				var ourName = yi.Tag(Options);
				if (!yi.IsOptional)
					throw Error("Missing required field {0} for class {1}", ourName, typeName);
			}
			for (; theirIndex < theirCount; ++theirIndex) {
				if (theirName == "")
					theirName = Reader.ReadString();
				AddUnknownFieldDef(def, theirName, typeName);
				theirName = "";
			}
		}

		private void InitClassDefUnordered(ReaderClassDef def, string typeName)
		{
			var theirCount = Reader.ReadInt16();
			int ourIndex = 0, requiredCountActiual = 0;
			for (int theirIndex = 0; theirIndex < theirCount; ++theirIndex) {
				var theirName = Reader.ReadString();
				Meta.Item yi = null;
				if (def.Meta.TagToItem.TryGetValue(theirName, out yi)) {
					if (!ReadCompatibleType(yi.Type))
						throw Error(
							"Incompatible type for field {0}, expected {1}", theirName, yi.Type);
					def.Fields.Add(new ReaderClassDef.FieldDef {
						Name = theirName,
						OurIndex = def.Meta.Items.IndexOf(yi) + 1,
						Type = yi.Type,
						ReadFunc = MakeReadOrMergeFunc(yi),
					});
					ourIndex += 1;
					if (!yi.IsOptional)
						requiredCountActiual += 1;
				}
				else
					AddUnknownFieldDef(def, theirName, typeName);
			}
			if (requiredCountActiual != def.Meta.RequiredCount)
				throw Error(
					"Expected {0} required field(s), but found {1} for class {2}",
					def.Meta.RequiredCount, requiredCountActiual, typeName);
		}

		private ReaderClassDef GetClassDef(short classId)
		{
			if (classId < classDefs.Count)
				return classDefs[classId];
			if (classId > classDefs.Count)
				throw Error("Bad classId: {0}", classId);
			var typeName = Reader.ReadString();
			var classType = Meta.GetTypeByReadAlias(typeName, Options) ?? TypeSerializer.Deserialize(typeName);
			if (classType == null)
				return GetClassDefUnknown(typeName);
			var result = new ReaderClassDef { Meta = Meta.Get(classType, Options) };
			PrepareReaders(result);
			if (BinaryOptions.Unordered)
				InitClassDefUnordered(result, typeName);
			else
				InitClassDef(result, typeName);
			classDefs.Add(result);
			return result;
		}

		private static void ReadFields(BinaryDeserializer d, ReaderClassDef def, object obj)
		{
			def.Meta.BeforeDeserialization.Run(obj);
			d.objStack.Push(obj);
			try {
				if (def.Meta.IsCompact) {
					for (int i = 1; i < def.Fields.Count; ++i)
						def.Fields[i].ReadFunc(obj);
				}
				else {
					if (def.Meta.GetUnknownStorage != null) {
						var storage = def.Meta.GetUnknownStorage(obj);
						storage.Clear();
						storage.Internal = def;
					}
					var actualIndex = d.Reader.ReadInt16();
					for (int i = 1; i < def.Fields.Count; ++i) {
						var fd = def.Fields[i];
						if (i < actualIndex || actualIndex == 0) {
							if (fd.OurIndex < 0 || def.Meta.Items[fd.OurIndex - 1].IsOptional)
								continue;
							throw d.Error("Expected field '{0}' ({1}), but found '{2}'",
								i, fd.Name, actualIndex);
						}
						fd.ReadFunc(obj);
						actualIndex = d.Reader.ReadInt16();
					}
					if (actualIndex != 0)
						throw d.Error("Unfinished object, expected zero, but got {0}", actualIndex);
				}
			}
			finally {
				d.objStack.Pop();
			}
			def.Meta.AfterDeserialization.Run(obj);
		}

		protected void ReadIntoObject<T>(object obj)
		{
			var classId = Reader.ReadInt16();
			if (classId == 0)
				throw Error("Unable to read null into object");
			var def = GetClassDef(classId);
			var expectedType = obj.GetType();
			if (
				expectedType != def.Meta.Type &&
				(!Meta.Get(expectedType, Options).AllowReadingFromAncestor || expectedType.BaseType != def.Meta.Type)
			)
				throw Error("Unable to read type {0} into {1}", def.Meta.Type, expectedType);
			def.ReadFields(this, def, obj);
		}

		protected void ReadIntoObjectUnchecked<T>(object obj)
		{
			var classId = Reader.ReadInt16();
			var def = GetClassDef(classId);
			def.ReadFields(this, def, obj);
		}

		private void CheckAssignable(Type dest, Type src, object value)
		{
			if (!dest.IsAssignableFrom(src))
				throw Error("Unable to assign type \"{0}\" to \"{1}\"",
					src == typeof(YuzuUnknown) ? ((YuzuUnknownBinary)value).ClassTag : src.ToString(), dest);
		}

		protected object ReadObject<T>() where T : class
		{
			var classId = Reader.ReadInt16();
			if (classId == 0)
				return null;
			var def = GetClassDef(classId);
			var result = def.Make?.Invoke(this, def);
			CheckAssignable(typeof(T), def.Meta.Type, result);
			if (result == null) {
				result = def.Meta.Factory();
				def.ReadFields(this, def, result);
			}
			return result;
		}

		protected object ReadObjectUnchecked<T>() where T : class
		{
			var classId = Reader.ReadInt16();
			if (classId == 0)
				return null;
			var def = GetClassDef(classId);
			if (def.Make != null)
				return def.Make(this, def);
			var result = def.Meta.Factory();
			def.ReadFields(this, def, result);
			return result;
		}

		protected void EnsureClassDef(Type t)
		{
			var def = GetClassDef(Reader.ReadInt16());
			if (def.Meta.Type != t)
				throw Error("Expected type {0}, but found {1}", def.Meta.Type, t);
		}

		protected object ReadStruct<T>() where T : struct
		{
			var classId = Reader.ReadInt16();
			if (classId == 0)
				return null;
			var def = GetClassDef(classId);
			var result = def.Make?.Invoke(this, def);
			CheckAssignable(typeof(T), def.Meta.Type, result);
			if (result == null) {
				result = def.Meta.Factory();
				def.ReadFields(this, def, result);
			}
			return result;
		}

		protected void ReadIntoStruct<T>(ref T s) where T : struct
		{
			var classId = Reader.ReadInt16();
			if (classId == 0)
				return;
			var def = GetClassDef(classId);

			var result = def.Make?.Invoke(this, def);
			CheckAssignable(typeof(T), def.Meta.Type, result);
			if (result == null) {
				result = def.Meta.Factory();
				def.ReadFields(this, def, result);
			}
			s = (T)result;
		}

		protected object ReadStructUnchecked<T>() where T : struct
		{
			var classId = Reader.ReadInt16();
			if (classId == 0)
				return null;
			var def = GetClassDef(classId);
			if (def.Make != null)
				return def.Make(this, def);
			var result = def.Meta.Factory();
			def.ReadFields(this, def, result);
			return result;
		}

		private Dictionary<Type, Func<object>> readerCache = new Dictionary<Type, Func<object>>();
		private Dictionary<Type, Action<object>> mergerCache = new Dictionary<Type, Action<object>>();

		private Func<object> ReadValueFunc(Type t)
		{
			Func<object> f;
			if (readerCache.TryGetValue(t, out f))
				return f;
			return readerCache[t] = MakeReaderFunc(t);
		}

		private Action<object> MergeValueFunc(Type t)
		{
			Action<object> f;
			if (mergerCache.TryGetValue(t, out f))
				return f;
			return mergerCache[t] = MakeMergerFunc(t);
		}

		private Func<object> MakeEnumReaderFunc(Type t)
		{
			var ut = Enum.GetUnderlyingType(t);
			if (ut == typeof(sbyte))
				return () => Enum.ToObject(t, Reader.ReadSByte());
			if (ut == typeof(byte))
				return () => Enum.ToObject(t, Reader.ReadByte());
			if (ut == typeof(short))
				return () => Enum.ToObject(t, Reader.ReadInt16());
			if (ut == typeof(ushort))
				return () => Enum.ToObject(t, Reader.ReadUInt16());
			if (ut == typeof(int))
				return () => Enum.ToObject(t, Reader.ReadInt32());
			if (ut == typeof(uint))
				return () => Enum.ToObject(t, Reader.ReadUInt32());
			if (ut == typeof(long))
				return () => Enum.ToObject(t, Reader.ReadInt64());
			if (ut == typeof(ulong))
				return () => Enum.ToObject(t, Reader.ReadUInt64());
			throw new YuzuException();
		}

		private Func<object> ReadDataStructureOfRecord(Type t)
		{
			if (t == typeof(Record))
				return ReadObject<object>;
			if (!t.IsGenericType)
				return null;
			var g = t.GetGenericTypeDefinition();
			if (g == typeof(List<>)) {
				var readValue = ReadDataStructureOfRecord(t.GetGenericArguments()[0]);
				if (readValue == null) return null;
				return () => ReadListRecord(readValue);
			}
			if (g == typeof(Dictionary<,>)) {
				var readValue = ReadDataStructureOfRecord(t.GetGenericArguments()[1]);
				if (readValue == null) return null;
				var d = (Func<Func<object>, object>)Delegate.CreateDelegate(
					typeof(Func<Func<object>, object>), this,
					Utils.GetPrivateCovariantGeneric(GetType(), nameof(ReadDictionaryRecord), t)
				);
				return () => d(readValue);
			}
			return null;
		}

		private Func<object> MakeReaderFunc(Type t)
		{
			if (t.IsEnum)
				return MakeEnumReaderFunc(t);
			if (t.IsGenericType) {
				var readRecord = ReadDataStructureOfRecord(t);
				if (readRecord != null)
					return readRecord;
				var g = t.GetGenericTypeDefinition();
				if (g == typeof(List<>))
					return MakeDelegate(
						Utils.GetPrivateCovariantGeneric(GetType(), nameof(ReadList), t));
				if (g == typeof(Dictionary<,>))
					return MakeDelegate(
						Utils.GetPrivateCovariantGenericAll(GetType(), nameof(ReadDictionary), t));
				if (g == typeof(Action<>))
					return MakeDelegate(Utils.GetPrivateCovariantGeneric(GetType(), nameof(ReadAction), t));
				if (g == typeof(Nullable<>)) {
					var r = ReadValueFunc(t.GetGenericArguments()[0]);
					return () => Reader.ReadBoolean() ? null : r();
				}
			}
			if (t.IsArray)
				return MakeDelegate(Utils.GetPrivateCovariantGeneric(GetType(), nameof(ReadArray), t));

			var meta = Meta.Get(t, Options);
			var sg = meta.Surrogate;
			if (sg.SurrogateType != null && sg.FuncFrom != null) {
				var rf = ReadValueFunc(sg.SurrogateType);
				return () => sg.FuncFrom(rf());
			}

			var idict = Utils.GetIDictionary(t);
			if (idict != null) {
				var kv = idict.GetGenericArguments();
				return MakeDelegate(Utils.GetPrivateGeneric(GetType(), nameof(ReadIDictionary), t, kv[0], kv[1]));
			}

			var icoll = Utils.GetICollection(t);
			if (icoll != null) {
				var elemType = icoll.GetGenericArguments()[0];
				return MakeDelegate(Utils.GetPrivateGeneric(GetType(), nameof(ReadCollection), t, elemType));
			}

			if (t.IsClass || t.IsInterface) {
				return MakeDelegate(Utils.GetPrivateGeneric(GetType(), nameof(ReadObject), t));
			}
			if (Utils.IsStruct(t))
				return MakeDelegate(Utils.GetPrivateGeneric(GetType(), nameof(ReadStruct), t));
			throw new NotImplementedException(t.Name);
		}

		private Action<object> MakeMergerFunc(Type t)
		{
			var idict = Utils.GetIDictionary(t);
			if (idict != null)
				return MakeDelegateAction(
					Utils.GetPrivateCovariantGenericAll(GetType(), nameof(ReadIntoDictionaryNG), idict));
			var icoll = Utils.GetICollection(t);
			if (icoll != null)
				return MakeDelegateAction(
					Utils.GetPrivateCovariantGeneric(GetType(), nameof(ReadIntoCollectionNG), icoll));
			if ((t.IsClass || t.IsInterface || Utils.IsStruct(t)) && t != typeof(object))
				return MakeDelegateAction(Utils.GetPrivateGeneric(GetType(), nameof(ReadIntoObject), t));
			throw Error("Unable to merge field of type {0}", t);
		}

		public override object FromReaderInt()
		{
			if (BinaryOptions.AutoSignature)
				CheckSignature();
			return ReadAny();
		}

		public override object FromReaderInt(object obj)
		{
			var expectedType = obj.GetType();
			if (expectedType == typeof(object))
				throw Error("Unable to read into untyped object");
			if (BinaryOptions.AutoSignature)
				CheckSignature();
			if (!ReadCompatibleType(expectedType))
				throw Error("Incompatible type to read into {0}", expectedType.Name);
			MergeValueFunc(expectedType)(obj);
			return obj;
		}

		public override T FromReaderInt<T>()
		{
			if (BinaryOptions.AutoSignature)
				CheckSignature();
			if (typeof(T) == typeof(object))
				return (T)ReadAny();
			if (!ReadCompatibleType(typeof(T)))
				throw Error("Incompatible type to read into {0}", typeof(T));
			return (T)ReadValueFunc(typeof(T))();
		}

		// If possible, preserves stream position if signature is absent.
		public bool IsValidSignature()
		{
			var s = BinaryOptions.Signature;
			if (s.Length == 0)
				return true;
			if (!Reader.BaseStream.CanSeek)
				return s.Equals(Reader.ReadBytes(s.Length));
			var pos = Reader.BaseStream.Position;
			if (Reader.BaseStream.Length - pos < s.Length)
				return false;
			foreach (var b in s)
				if (b != Reader.ReadByte()) {
					Reader.BaseStream.Position = pos;
					return false;
				}
			return true;
		}

		public void CheckSignature()
		{
			if (!IsValidSignature())
				throw Error("Signature not found");
		}

	}
}
