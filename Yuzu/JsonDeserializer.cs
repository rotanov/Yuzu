using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

using Yuzu.Deserializer;
using Yuzu.Metadata;
using Yuzu.Util;

namespace Yuzu.Json
{
	public class JsonDeserializer : AbstractReaderDeserializer
	{
		public static JsonDeserializer Instance = new JsonDeserializer();
		public JsonSerializeOptions JsonOptions = new JsonSerializeOptions();

		private char? buf;

		public override void Initialize()
		{
			buf = null;
			if (JsonOptions.BOM && Reader.PeekChar() == '\uFEFF')
				Reader.ReadChar();
		}

		private char Next()
		{
			if (!buf.HasValue)
				return Reader.ReadChar();
			var result = buf.Value;
			buf = null;
			return result;
		}

		protected void KillBuf()
		{
			if (buf != null)
				throw Error("Unconsumed character: {0}", buf);
		}

		private void PutBack(char ch)
		{
			if (buf.HasValue)
				throw new YuzuAssert();
			buf = ch;
		}

		private char SkipSpaces()
		{
			char ch = Next();
			if (JsonOptions.Comments)
				while (true) {
					if (ch == '/') {
						ch = Reader.ReadChar();
						if (ch != '/')
							throw Error("Expected '/', but found '{0}'", ch);
						do {
							ch = Reader.ReadChar();
						} while (ch != '\n') ;
					}
					else if (ch != ' ' && ch != '\t' && ch != '\n' && ch != '\r')
						break;
					ch = Reader.ReadChar();
				}
			else
				while (ch == ' ' || ch == '\t' || ch == '\n' || ch == '\r')
					ch = Reader.ReadChar();
			return ch;
		}

		// Return \0 instead of throwing on EOF.
		protected char SkipSpacesCarefully()
		{
			if (buf.HasValue)
				throw new YuzuAssert();
			if (JsonOptions.Comments)
				while (true) {
					var v = Reader.PeekChar();
					if (v == '/') {
						Reader.ReadChar();
						v = Reader.PeekChar();
						// Unable to look ahead 2 chars, so disallow lone slash.
						if (v != '/')
							throw Error("Expected '/', but found '{0}'",
								v > 0 ? ((char)v).ToString() : "EOF");
						do {
							Reader.ReadChar();
							v = Reader.PeekChar();
							if (v < 0)
								return '\0';
						} while (v != '\n');
					}
					if (v != ' ' && v != '\t' && v != '\n' && v != '\r')
						return (char)v;
					Reader.ReadChar();
				}
			else
				while (true) {
					var v = Reader.PeekChar();
					if (v != ' ' && v != '\t' && v != '\n' && v != '\r')
						return v < 0 ? '\0' : (char)v;
					Reader.ReadChar();
				}
		}

		protected char Require(params char[] chars)
		{
			var ch = SkipSpaces();
			if(Array.IndexOf(chars, ch) < 0)
				throw Error("Expected '{0}' but found '{1}'", String.Join("','", chars), ch);
			return ch;
		}

		protected void Require(string s)
		{
			foreach (var ch in s) {
				var r = Reader.ReadChar();
				if (r != ch)
					throw Error("Expected '{0}', but found '{1}'", ch, r);
			}
		}

		// Optimization: avoid re-creating StringBuilder.
		private StringBuilder sb = new StringBuilder();

		protected string RequireUnescapedString()
		{
			sb.Clear();
			if (RequireOrNull('"')) return null;
			while (true) {
				var ch = Reader.ReadChar();
				if (ch == '"')
					break;
				sb.Append(ch);
			}
			return sb.ToString();
		}

		protected string RequireString()
		{
			sb.Clear();
			if (RequireOrNull('"')) return null;
			while (true) {
				// Optimization: buf is guaranteed to be empty after Require, so no need to call Next.
				char ch;
				try {
					ch = Reader.ReadChar();
				}
				catch (ArgumentException) {
					// Encountered surrogate pair. Ignore replacement (\uFFFD) inserted by ReadChars.
					var chars = Reader.ReadChars(3);
					sb.Append(chars[1]);
					sb.Append(chars[2]);
					continue;
				}
				if (ch == '"')
					break;
				if (ch == '\\') {
					ch = Reader.ReadChar();
					if (ch == 'u') {
						int code = 0;
						for (int i = 0; i < 4; ++i) {
							ch = Reader.ReadChar();
							int h = ch <= 'f' ? JsonEscapeData.hexDigits[ch] : -1;
							if (h < 0)
								throw Error("Bad hexadecimal digit in unicode escape: '{0}'", ch);
							code = code * 16 + h;
						}
						ch = (char)code;
					}
					else {
						var escaped = ch <= 't' ? JsonEscapeData.unescapeChars[ch] : '\0';
						if (escaped == 0)
							throw Error("Unexpected escape chararcter: '{0}'", ch);
						ch = escaped;
					}
				}
				sb.Append(ch);
			}
			return sb.ToString();
		}

		protected char RequireChar()
		{
			var s = RequireString();
			if (s.Length != 1)
				throw Error("Expected single char but found: '{0}'", s);
			return s[0];
		}

		protected bool RequireBool()
		{
			var ch = SkipSpaces();
			if (ch == 't') {
				Require("rue");
				return true;
			}
			if (ch == 'f') {
				Require("alse");
				return false;
			}
			throw Error("Expected 'true' or 'false', but found: {0}", ch);
		}

		// Some code duplication within integer parsers to speed up hot path.

		protected uint RequireUInt()
		{
			var ch = SkipSpaces();
			uint result = 0;
			while ('0' <= ch && ch <= '9') {
				var d = (uint)ch - (uint)'0';
				checked { result = result * 10 + d; }
				ch = Reader.ReadChar();
			}
			PutBack(ch);
			return result;
		}

		protected int RequireInt()
		{
			var ch = SkipSpaces();
			int result = 0;
			if (ch == '-') {
				ch = Reader.ReadChar();
				while ('0' <= ch && ch <= '9') {
					var d = (int)'0' - (int)ch;
					checked { result = result * 10 + d; }
					ch = Reader.ReadChar();
				}
			}
			else {
				while ('0' <= ch && ch <= '9') {
					var d = (int)ch - (int)'0';
					checked { result = result * 10 + d; }
					ch = Reader.ReadChar();
				}
			}
			PutBack(ch);
			return result;
		}

		protected ulong RequireULong()
		{
			var ch = SkipSpaces();
			if (JsonOptions.Int64AsString) {
				if (ch != '"')
					throw Error("Expected '\"' but found '{0}'", ch);
				ch = Reader.ReadChar();
			}
			ulong result = 0;
			while ('0' <= ch && ch <= '9') {
				var d = (ulong)ch - (ulong)'0';
				checked { result = result * 10 + d; }
				ch = Reader.ReadChar();
			}
			if (JsonOptions.Int64AsString) {
				if (ch != '"')
					throw Error("Expected '\"' but found '{0}'", ch);
			}
			else
				PutBack(ch);
			return result;
		}

		protected long RequireLong()
		{
			var ch = SkipSpaces();
			if (JsonOptions.Int64AsString) {
				if (ch != '"')
					throw Error("Expected '\"' but found '{0}'", ch);
				ch = Reader.ReadChar();
			}
			int sign = 1;
			if (ch == '-') {
				sign = -1;
				ch = Reader.ReadChar();
			}
			long result = 0;
			while ('0' <= ch && ch <= '9') {
				var d = sign * ((long)ch - (long)'0');
				checked { result = result * 10 + d; }
				ch = Reader.ReadChar();
			}
			if (JsonOptions.Int64AsString) {
				if (ch != '"')
					throw Error("Expected '\"' but found '{0}'", ch);
			}
			else
				PutBack(ch);
			return result;
		}

		private string ParseFloat()
		{
			// Optimization: Do not extract helper methods.
			sb.Clear();
			var ch = SkipSpaces();
			bool neg = ch == '-';
			if (neg) {
				sb.Append(ch);
				ch = Reader.ReadChar();
			}
			if (ch == 'N') {
				Require("aN");
				return "NaN";
			}
			if (ch == 'I') {
				Require("nfinity");
				return neg ? "-Infinity" : "Infinity";
			}
			while ('0' <= ch && ch <= '9') {
				sb.Append(ch);
				ch = Reader.ReadChar();
			}
			if (ch == '.') {
				sb.Append(ch);
				ch = Reader.ReadChar();
				while ('0' <= ch && ch <= '9') {
					sb.Append(ch);
					ch = Reader.ReadChar();
				}
			}
			if (ch == 'e'|| ch == 'E') {
				sb.Append(ch);
				ch = Reader.ReadChar();
				if (ch == '+' || ch == '-') {
					sb.Append(ch);
					ch = Reader.ReadChar();
				}
				while ('0' <= ch && ch <= '9') {
					sb.Append(ch);
					ch = Reader.ReadChar();
				}
			}
			PutBack(ch);
			return sb.ToString();
		}

		protected double RequireDouble() => Double.Parse(ParseFloat(), CultureInfo.InvariantCulture);

		protected float RequireSingle() => Single.Parse(ParseFloat(), CultureInfo.InvariantCulture);

		protected decimal RequireDecimal() => Decimal.Parse(ParseFloat(), CultureInfo.InvariantCulture);

		protected decimal RequireDecimalAsString() =>
			Decimal.Parse(RequireUnescapedString(), CultureInfo.InvariantCulture);

		protected DateTime RequireDateTime()
		{
			var s = JsonOptions.DateFormat == "O" ? RequireUnescapedString() : RequireString();
			return DateTime.ParseExact(
				s, JsonOptions.DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
		}

		protected DateTimeOffset RequireDateTimeOffset()
		{
			var s = JsonOptions.DateFormat == "O" ? RequireUnescapedString() : RequireString();
			return DateTimeOffset.ParseExact(
				s, JsonOptions.DateTimeOffsetFormat,
				CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
		}

		protected TimeSpan RequireTimeSpan()
		{
			var s = JsonOptions.TimeSpanFormat == "c" ? RequireUnescapedString() : RequireString();
			return TimeSpan.ParseExact(s, JsonOptions.TimeSpanFormat, CultureInfo.InvariantCulture);
		}

		protected Guid RequireGuid() => Guid.Parse(RequireUnescapedString());

		protected string GetNextName(bool first)
		{
			var ch = SkipSpaces();
			if (ch == ',') {
				if (first)
					throw Error("Expected name, but found ','");
				ch = SkipSpaces();
			}
			if (ch == '}')
				return "";
			if (ch != '"')
				throw Error("Expected '\"' but found '{0}'", ch);
			sb.Clear();
			while (true) {
				ch = Reader.ReadChar();
				if (ch == '"')
					break;
				sb.Append(ch);
			}
			Require(':');
			return sb.ToString();
		}

		protected bool RequireOrNull(char ch)
		{
			if (Require(ch, 'n') == ch)
				return false;
			Require("ull");
			return true;
		}

		protected char RequireBracketOrNull()
		{
			var ch = Require('{', '[', 'n');
			if (ch == 'n')
				Require("ull");
			return ch;
		}

		protected void ReadIntoCollection<T>(ICollection<T> list)
		{
			// ReadValue might invoke a new serializer, so we must not rely on PutBack.
			if (SkipSpacesCarefully() == ']') {
				Require(']');
				return;
			}
			var rf = ReadValueFunc(typeof(T));
			do {
				list.Add((T)rf());
			} while (Require(']', ',') == ',');
		}

		protected void ReadIntoCollectionNG<T>(object list) => ReadIntoCollection((ICollection<T>)list);

		protected List<T> ReadList<T>()
		{
			if (RequireOrNull('['))
				return null;
			var list = new List<T>();
			ReadIntoCollection(list);
			return list;
		}

		protected static Dictionary<Type, Func<string, object>> keyParsers = new Dictionary<Type, Func<string, object>> {
			{ typeof(int), s => int.Parse(s) },
			{ typeof(uint), s => uint.Parse(s) },
			{ typeof(long), s => long.Parse(s) },
			{ typeof(ulong), s => ulong.Parse(s) },
			{ typeof(short), s => short.Parse(s) },
			{ typeof(ushort), s => ushort.Parse(s) },
			{ typeof(sbyte), s => sbyte.Parse(s) },
			{ typeof(byte), s => byte.Parse(s) },
			{ typeof(char), s => char.Parse(s) },
			{ typeof(string), s => s },
			{ typeof(bool), s => bool.Parse(s) },
			{ typeof(float), s => float.Parse(s) },
			{ typeof(double), s => double.Parse(s) },
			{ typeof(DateTime), s => DateTime.Parse(s) },
			{ typeof(DateTimeOffset), s => DateTimeOffset.Parse(s) },
			{ typeof(TimeSpan), s => TimeSpan.Parse(s) },
			{ typeof(Guid), s => Guid.Parse(s) },
		};

		public static void RegisterKeyParser(Type t, Func<string, object> parser)
		{
			keyParsers.Add(t, parser);
		}

		protected void ReadIntoDictionary<K, V>(IDictionary<K, V> dict)
		{
			// ReadValue might invoke a new serializer, so we must not rely on PutBack.
			if (SkipSpacesCarefully() == '}') {
				Require('}');
				return;
			}
			Func<string, object> rk;
			if (typeof(K).IsEnum)
				rk = s => Enum.Parse(typeof(K), s);
			else if (!keyParsers.TryGetValue(typeof(K), out rk))
				throw new YuzuAssert("Unable to find key parser for type: " + typeof(K).Name);

			var rf = ReadValueFunc(typeof(V));
			do {
				var key = RequireString();
				Require(':');
				dict.Add((K)rk(key), (V)rf());
			} while (Require('}', ',') == ',');
		}

		protected void ReadIntoDictionaryNG<K, V>(object dict) => ReadIntoDictionary((IDictionary<K, V>)dict);

		protected Dictionary<K, V> ReadDictionary<K, V>()
		{
			if (RequireOrNull('{'))
				return null;
			var dict = new Dictionary<K, V>();
			ReadIntoDictionary(dict);
			return dict;
		}

		protected I ReadIDictionary<I, K, V>() where I: class, IDictionary<K, V>, new()
		{
			if (RequireOrNull('{'))
				return null;
			var dict = new I();
			ReadIntoDictionary(dict);
			return dict;
		}

		private T[] ReadArray<T>()
		{
			var lst = ReadList<T>();
			return lst == null ? null : lst.ToArray();
		}

		private T[] ReadArrayWithLengthPrefix<T>()
		{
			if (RequireOrNull('[')) return null;
			// ReadValue might invoke a new serializer, so we must not rely on PutBack.
			if (SkipSpacesCarefully() == ']') {
				Require(']');
				return new T[0];
			}
			var array = new T[RequireUInt()];
			var rf = ReadValueFunc(typeof(T));
			for (int i = 0; i < array.Length; ++i) {
				Require(',');
				array[i] = (T)rf();
			}
			Require(']');
			return array;
		}

		private Action<T> ReadAction<T>() { return GetAction<T>(RequireUnescapedString()); }

		private object ReadNullable(Func<object> normalRead)
		{
			var ch = SkipSpaces();
			if (ch == 'n') {
				Require("ull");
				return null;
			}
			PutBack(ch);
			return normalRead();
		}

		protected object ReadAnyObject()
		{
			var ch = SkipSpaces();
			PutBack(ch);
			switch (ch) {
				case '\"':
					return RequireString();
				case 't': case 'f':
					return RequireBool();
				case 'n':
					Next();
					Require("ull");
					return null;
				case '{':
					Next();
					var name = GetNextName(first: true);
					if (name != JsonOptions.ClassTag) {
						var any = new Dictionary<string, object>();
						if (name != "") {
							var val = ReadAnyObject();
							any.Add(name, val);
							if (Require(',', '}') == ',')
								ReadIntoDictionary(any);
						}
						return any;
					}
					var typeName = RequireUnescapedString();
					var t = Meta.GetTypeByReadAlias(typeName, Options) ?? TypeSerializer.Deserialize(typeName);
					if (t == null) {
						var result = new YuzuUnknown { ClassTag = typeName };
						if (Require(',', '}') == ',')
							ReadIntoDictionary(result.Fields);
						return result;
					}
					var meta = Meta.Get(t, Options);
					return ReadFields(meta.Factory(), GetNextName(first: false));
				case '[':
					return ReadList<object>();
				default:
					return RequireDouble();
			}
		}

		// Optimization: Avoid creating trivial closures.
		private object RequireIntObj() => RequireInt();
		private object RequireUIntObj() => RequireUInt();
		private object RequireLongObj() => RequireLong();
		private object RequireULongObj() => RequireULong();
		private object RequireShortObj() => checked((short)RequireInt());
		private object RequireUShortObj() => checked((ushort)RequireUInt());
		private object RequireSByteObj() => checked((sbyte)RequireInt());
		private object RequireByteObj() => checked((byte)RequireInt());
		private object RequireCharObj() => RequireChar();
		private object RequireStringObj() => RequireString();
		private object RequireBoolObj() => RequireBool();
		private object RequireSingleObj() => RequireSingle();
		private object RequireDoubleObj() => RequireDouble();
		private object RequireDecimalObj() => RequireDecimal();
		private object RequireDecimalAsStringObj() => RequireDecimalAsString();
		private object RequireDateTimeObj() => RequireDateTime();
		private object RequireDateTimeOffsetObj() => RequireDateTimeOffset();
		private object RequireTimeSpanObj() => RequireTimeSpan();
		private object RequireGuidObj() => RequireGuid();

		private Dictionary<Type, Func<object>> readerCache = new Dictionary<Type, Func<object>>();
		private Dictionary<Type, Action<object>> mergerCache = new Dictionary<Type, Action<object>>();
		private int jsonOptionsGeneration = 0;

		private Func<object> ReadValueFunc(Type t)
		{
			if (jsonOptionsGeneration != JsonOptions.Generation) {
				readerCache.Clear();
				jsonOptionsGeneration = JsonOptions.Generation;
			}
			Func<object> f;
			if (readerCache.TryGetValue(t, out f))
				return f;
			return readerCache[t] = MakeReaderFunc(t);
		}

		private Action<object> MergeValueFunc(Type t)
		{
			if (jsonOptionsGeneration != JsonOptions.Generation) {
				mergerCache.Clear();
				jsonOptionsGeneration = JsonOptions.Generation;
			}
			Action<object> f;
			if (mergerCache.TryGetValue(t, out f))
				return f;
			return mergerCache[t] = MakeMergerFunc(t);
		}

		private Func<object> MakeEnumReaderFunc(Type t)
		{
			if (JsonOptions.EnumAsString)
				return () => Enum.Parse(t, RequireString());
			var ut = t.GetEnumUnderlyingType();
			if (ut == typeof(int))
				return () => Enum.ToObject(t, RequireInt());
			if (ut == typeof(uint))
				return () => Enum.ToObject(t, RequireUInt());
			if (ut == typeof(long))
				return () => Enum.ToObject(t, RequireLong());
			if (ut == typeof(ulong))
				return () => Enum.ToObject(t, RequireULong());
			return () => Enum.ToObject(t, RequireInt());
		}

		private Dictionary<Type, Func<object>> simpleReaders;
		private void InitSimpleReaders() => simpleReaders = new Dictionary<Type, Func<object>> {
			{ typeof(int), RequireIntObj },
			{ typeof(uint), RequireUIntObj },
			{ typeof(long), RequireLongObj },
			{ typeof(ulong), RequireULongObj },
			{ typeof(short), RequireShortObj },
			{ typeof(ushort), RequireUShortObj },
			{ typeof(sbyte), RequireSByteObj },
			{ typeof(byte), RequireByteObj },
			{ typeof(char), RequireCharObj },
			{ typeof(string), RequireStringObj },
			{ typeof(bool), RequireBoolObj },
			{ typeof(float), RequireSingleObj },
			{ typeof(double), RequireDoubleObj },
			{ typeof(DateTime), RequireDateTimeObj },
			{ typeof(DateTimeOffset), RequireDateTimeOffsetObj },
			{ typeof(TimeSpan), RequireTimeSpanObj },
			{ typeof(Guid), RequireGuidObj },
			{ typeof(object), ReadAnyObject },
		};

		private Func<object> MakeReaderFunc(Type t)
		{
			{
				if (simpleReaders == null)
					InitSimpleReaders();
				Func<object> result;
				if (simpleReaders.TryGetValue(t, out result))
					return result;
			}
			if (t == typeof(decimal)) {
				if (JsonOptions.DecimalAsString)
					return RequireDecimalAsStringObj;
				else
					return RequireDecimalObj;
			}
			if (t.IsEnum)
				return MakeEnumReaderFunc(t);
			if (t.IsGenericType) {
				var g = t.GetGenericTypeDefinition();
				if (g == typeof(List<>))
					return MakeDelegate(Utils.GetPrivateCovariantGeneric(GetType(), nameof(ReadList), t));
				if (g == typeof(Action<>))
					return MakeDelegate(Utils.GetPrivateCovariantGeneric(GetType(), nameof(ReadAction), t));
				if (g == typeof(Nullable<>)) {
					var r = ReadValueFunc(t.GetGenericArguments()[0]);
					return () => ReadNullable(r);
				}
			}
			if (t.IsArray) {
				var n = JsonOptions.ArrayLengthPrefix ? nameof(ReadArrayWithLengthPrefix) : nameof(ReadArray);
				return MakeDelegate(Utils.GetPrivateCovariantGeneric(GetType(), n, t));
			}
			var idict = Utils.GetIDictionary(t);
			if (idict != null) {
				var kv = idict.GetGenericArguments();
				return MakeDelegate(Utils.GetPrivateGeneric(GetType(), nameof(ReadIDictionary), t, kv[0], kv[1]));
			}
			var icoll = Utils.GetICollection(t);
			if (icoll != null) {
				var m = Utils.GetPrivateCovariantGeneric(GetType(), nameof(ReadIntoCollectionNG), icoll);
				var d = MakeDelegateAction(m);
				return () => {
					if (RequireOrNull('['))
						return null;
					var list = Activator.CreateInstance(t);
					d(list);
					return list;
				};
			}
			if (t.IsClass && !t.IsAbstract) {
				Meta.Get(t, Options); // Populate aliases etc.
				return MakeDelegate(Utils.GetPrivateGeneric(GetType(), nameof(ReadObject), t));
			}
			if (t.IsInterface || t.IsAbstract)
				return MakeDelegate(Utils.GetPrivateGeneric(GetType(), nameof(ReadInterface), t));
			if (Utils.IsStruct(t))
				return MakeDelegate(Utils.GetPrivateGeneric(GetType(), nameof(ReadStruct), t));
			throw new NotImplementedException(t.Name);
		}

		private Action<object> MakeMergerFunc(Type t)
		{
			if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Dictionary<,>)) {
				var m = Utils.GetPrivateCovariantGenericAll(GetType(), nameof(ReadIntoDictionaryNG), t);
				var d = MakeDelegateAction(m);
				return obj => { Require('{'); d(obj); };
			}
			var icoll = Utils.GetICollection(t);
			if (icoll != null) {
				var m = Utils.GetPrivateCovariantGeneric(GetType(), nameof(ReadIntoCollectionNG), icoll);
				var d = MakeDelegateAction(m);
				return obj => { Require('['); d(obj); };
			}
			if ((t.IsClass || t.IsInterface) && t != typeof(object)) {
				var m = Utils.GetPrivateGeneric(GetType(), nameof(ReadIntoObject), t);
				return MakeDelegateAction(m);
			}
			throw Error("Unable to merge field of type {0}", t);
		}

		protected void ReadUnknownFieldsTail(YuzuUnknownStorage storage, string name)
		{
			while (name != "") {
				storage.Add(name, ReadAnyObject());
				name = GetNextName(false);
			}
		}

		protected int ReadUnknownFields(YuzuUnknownStorage storage, string tag, ref string name)
		{
			var cmp = String.CompareOrdinal(tag, name);
			while (cmp > 0 && name != "") {
				storage.Add(name, ReadAnyObject());
				name = GetNextName(false);
				cmp = String.CompareOrdinal(tag, name);
			}
			return cmp;
		}

		protected virtual object ReadFields(object obj, string name)
		{
			var meta = Meta.Get(obj.GetType(), Options);
			meta.BeforeDeserialization.Run(obj);
			objStack.Push(obj);
			try {
				// Optimization: duplicate loop to extract options check.
				if (JsonOptions.Unordered) {
					var storage = !Options.AllowUnknownFields || meta.GetUnknownStorage == null ?
						NullYuzuUnknownStorage.Instance : meta.GetUnknownStorage(obj);
					storage.Clear();
					int requiredCountActiual = 0;
					var migrations = MigrationContext == null ? null : Migrations.Storage.GetMigrationsForInstance(obj, MigrationContext.Version).ToList();
					var combinedInputs = migrations == null ? null : migrations.SelectMany(m => m.Inputs).Distinct();
					(object Owner, List<Migrations.MigrationSpecification> Migrations, Dictionary<string, object> PathToValue) dstTuple = (null, null, null);
					if (migrations != null && migrations.Any()) {
						dstTuple.Owner = obj;
						dstTuple.Migrations = migrations;
						dstTuple.PathToValue = new Dictionary<string, object>();
					}
					while (name != "") {
						Meta.Item yi;
						var inputsStartingWithName = combinedInputs == null ? null : combinedInputs.Where(i => i.Parts[0] == name).ToList();
						if (!meta.TagToItem.TryGetValue(name, out yi)) {
							if (inputsStartingWithName != null && inputsStartingWithName.Any()) {
								var substituteObject = ReadValueFunc(inputsStartingWithName[0].Types[0])();
								foreach (var i in inputsStartingWithName) {
									if (i.TryGetValueByPath(substituteObject, out var value)) {
										dstTuple.PathToValue.Add(i.ToString(), value);
									} // else set default value starting from point in path where it couldnt be found
								}
							} else {
								if (!Options.AllowUnknownFields)
									throw Error("Unknown field '{0}'", name);
								storage.Add(name, ReadAnyObject());
							}
							name = GetNextName(false);
							continue;
						}
						bool later = false;
						if (inputsStartingWithName != null) {
							if (inputsStartingWithName.Any() && yi.Type != inputsStartingWithName[0].Types[0]) {
								// global unknown storage
								var substituteObject = ReadValueFunc(inputsStartingWithName[0].Types[0])();
								foreach (var i in inputsStartingWithName) {
									if (i.TryGetValueByPath(substituteObject, out var value)) {
										dstTuple.PathToValue.Add(i.ToString(), value);
									} // else set default value starting from point in path where it couldnt be found
								}
								name = GetNextName(false);
								continue;
							} else {
								later = true;
							}
						}
						if (!yi.IsOptional)
							requiredCountActiual += 1;
						if (yi.SetValue != null)
							yi.SetValue(obj, ReadValueFunc(yi.Type)());
						else
							MergeValueFunc(yi.Type)(yi.GetValue(obj));
						if (later) {
							foreach (var i in inputsStartingWithName) {
								if (i.TryGetValueByPath(yi.GetValue(obj), out var value)) {
									dstTuple.PathToValue.Add(i.ToString(), value);
								} // else set default value starting from point in path where it couldnt be found
							}
						}
						name = GetNextName(false);
					}
					if (combinedInputs != null) {
						foreach (var i in combinedInputs) {
							if (i.IsDirectInput(MigrationContext.Version)) {
								if (!dstTuple.PathToValue.ContainsKey(i.ToString())) {
									// there's only one case when this fails: previously named field had default value, thus wan't serialized
									if (i.TryGetValueByPath(obj, out var value, false)) {
										dstTuple.PathToValue.Add(i.ToString(), value);
									} // else set default value starting from point in path where it couldnt be found
								}
							}
						}
						if (dstTuple.Owner != null && dstTuple.Migrations != null && dstTuple.PathToValue != null && dstTuple.PathToValue.Any()) {
							MigrationContext.MigrationTable.Add(dstTuple);
						}
					}
					if (requiredCountActiual != meta.RequiredCount)
						throw Error(
							"Expected {0} required field(s), but found {1} for type {2}",
							meta.RequiredCount, requiredCountActiual, obj.GetType());
				}
				else if (Options.AllowUnknownFields) {
					var storage = meta.GetUnknownStorage == null ?
						NullYuzuUnknownStorage.Instance : meta.GetUnknownStorage(obj);
					storage.Clear();
					foreach (var yi in meta.Items) {
						if (ReadUnknownFields(storage, yi.Tag(Options), ref name) != 0) {
							if (!yi.IsOptional)
								throw Error("Expected field '{0}', but found '{1}'", yi.NameTagged(Options), name);
							continue;
						}
						if (yi.SetValue != null)
							yi.SetValue(obj, ReadValueFunc(yi.Type)());
						else
							MergeValueFunc(yi.Type)(yi.GetValue(obj));
						name = GetNextName(false);
					}
					ReadUnknownFieldsTail(storage, name);
				}
				else {
					foreach (var yi in meta.Items) {
						if (yi.Tag(Options) != name) {
							if (!yi.IsOptional)
								throw Error("Expected field '{0}', but found '{1}'", yi.NameTagged(Options), name);
							continue;
						}
						if (yi.SetValue != null)
							yi.SetValue(obj, ReadValueFunc(yi.Type)());
						else
							MergeValueFunc(yi.Type)(yi.GetValue(obj));
						name = GetNextName(false);
					}
					if (name != "")
						throw Error("Unknown field '{0}'", name);
				}
			}
			finally {
				objStack.Pop();
			}
			meta.AfterDeserialization.Run(obj);
			return obj;
		}

		protected virtual object ReadFieldsCompact(object obj)
		{
			var meta = Meta.Get(obj.GetType(), Options);
			if (!meta.IsCompact) {
				if (meta.Surrogate.FuncFrom == null)
					throw Error("Attempt to read non-compact type '{0}' from compact format", obj.GetType());
				return meta.Surrogate.FuncFrom(
					ReadFieldsCompact(Activator.CreateInstance(meta.Surrogate.SurrogateType)));
			}
			meta.BeforeDeserialization.Run(obj);
			bool isFirst = true;
			objStack.Push(obj);
			try {
				foreach (var yi in meta.Items) {
					if (!isFirst)
						Require(',');
					isFirst = false;
					yi.SetValue(obj, ReadValueFunc(yi.Type)());
				}
			}
			finally {
				objStack.Pop();
			}
			Require(']');
			meta.AfterDeserialization.Run(obj);
			return obj;
		}

		protected void CheckClassTag(string name)
		{
			if (name != JsonOptions.ClassTag)
				throw Error("Expected class tag, but found '{0}'", name);
		}

		private Surrogate GetSurrogate<T>(Type actualType)
		{
			var sg = Meta.Get(typeof(T), Options).Surrogate;
			if (sg.FuncFrom == null)
				throw Error(
					"Expected type '{0}', but got '{1}'",
					typeof(T), actualType == null ? "number" : TypeSerializer.Serialize(actualType));
			if (actualType != null && !sg.SurrogateType.IsAssignableFrom(actualType))
				throw Error(
					"Expected type '{0}' or '{1}', but got '{2}'",
					typeof(T), sg.SurrogateType.Name, actualType);
			return sg;
		}

		// T is neither a collection nor a bare object.
		private T ReadObject<T>() where T: class {
			KillBuf();
			var ch = SkipSpaces();
			switch (ch) {
				case 'n':
					Require("ull");
					return null;
				case '{':
					var name = GetNextName(first: true);
					if (name != JsonOptions.ClassTag) {
						var meta = Meta.Get(typeof(T), Options);
						return (T)ReadFields(meta.Factory(), name);
					}
					var typeName = RequireUnescapedString();
					var t = FindType(typeName);
					if (Migrations.Storage.typeMigrations.ContainsKey(t)) {
						var meta = Meta.Get(t, Options);
						var o = ReadFields(meta.Factory(), GetNextName(first: false));
						var m = Migrations.Storage.typeMigrations[t];
						return (T)m.Item1.Invoke(null, new[] { o });
					}
					if (typeof(T).IsAssignableFrom(t)) {
						var meta = Meta.Get(t, Options);
						return (T)ReadFields(meta.Factory(), GetNextName(first: false));
					}
					return (T)GetSurrogate<T>(t).FuncFrom(
						ReadFields(Activator.CreateInstance(t), GetNextName(first: false)));
				case '[': {
					var meta = Meta.Get(typeof(T), Options);
					return (T)ReadFieldsCompact(meta.Factory());
				}
				case '"':
					PutBack(ch);
					return (T)GetSurrogate<T>(typeof(string)).FuncFrom(RequireString());
				case 't':
				case 'f':
					PutBack(ch);
					return (T)GetSurrogate<T>(typeof(bool)).FuncFrom(RequireBool());
				default:
					PutBack(ch);
					var sg = GetSurrogate<T>(null);
					return (T)sg.FuncFrom(ReadValueFunc(sg.SurrogateType)()); // TODO: Optimize
			}
		}

		// T is neither a collection nor a bare object.
		private void ReadIntoObject<T>(object obj) where T : class
		{
			KillBuf();
			switch (Require('{', '[')) {
				case '{':
					var name = GetNextName(first: true);
					if (name != JsonOptions.ClassTag) {
						ReadFields(obj, name);
					}
					else {
						CheckExpectedType(RequireUnescapedString(), typeof(T));
						ReadFields(obj, GetNextName(first: false));
					}
					return;
				case '[':
					ReadFieldsCompact(obj);
					return;
				default:
					throw new YuzuAssert();
			}
		}

		private T ReadInterface<T>() where T : class
		{
			KillBuf();
			if (RequireOrNull('{')) return null;
			CheckClassTag(GetNextName(first: true));
			var typeName = RequireUnescapedString();
			var t = FindType(typeName);
			if (!typeof(T).IsAssignableFrom(t))
				throw Error("Expected interface '{0}', but got '{1}'", typeof(T), typeName);
			var meta = Meta.Get(t, Options);
			return (T)ReadFields(meta.Factory(), GetNextName(first: false));
		}

		private object ReadStruct<T>() where T : new()
		{
			var obj = new T();
			switch (Require('{', '[')) {
				case '{':
					var name = GetNextName(first: true);
					if (name != JsonOptions.ClassTag)
						return ReadFields(obj, name);
					CheckExpectedType(RequireUnescapedString(), typeof(T));
					return ReadFields(obj, GetNextName(first: false));
				case '[':
					return ReadFieldsCompact(obj);
				default:
					throw new YuzuAssert();
			}
		}

		public override object FromReaderInt() => ReadAnyObject();

		public override object FromReaderInt(object obj)
		{
			KillBuf();
			var expectedType = obj.GetType();
			if (expectedType == typeof(object))
				throw Error("Unable to read into bare object");
			switch (RequireBracketOrNull()) {
				case 'n':
					return null;
				case '{':
					if (expectedType.IsGenericType && expectedType.GetGenericTypeDefinition() == typeof(Dictionary<,>)) {
						var m = Utils.GetPrivateCovariantGenericAll(GetType(), nameof(ReadIntoDictionaryNG), expectedType);
						MakeDelegateAction(m)(obj);
						return obj;
					}
					var name = GetNextName(first: true);
					if (name != JsonOptions.ClassTag)
						return ReadFields(obj, name);
					CheckExpectedType(RequireUnescapedString(), expectedType);
					return ReadFields(obj, GetNextName(first: false));
				case '[':
					var icoll = Utils.GetICollection(expectedType);
					if (icoll != null) {
						var m = Utils.GetPrivateCovariantGeneric(GetType(), nameof(ReadIntoCollectionNG), icoll);
						MakeDelegateAction(m)(obj);
						return obj;
					}
					return ReadFieldsCompact(obj);
				default:
					throw new YuzuAssert();
			}
		}

		public override T FromReaderInt<T>() => (T)ReadValueFunc(typeof(T))();
	}
}
