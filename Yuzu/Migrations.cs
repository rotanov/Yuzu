using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Yuzu
{
	namespace Migrations
	{
		// mutable state of migration per Yuzu ReadFrom call
		public class MigrationContext
		{
			// current version of data being deserialized
			public int Version;

			// migration data table for storing input and output for migrations applicable to concrete objects
			public List<(object Owner, List<MigrationSpecification> Migration, Dictionary<string, object> Values)> MigrationTable =
				new List<(object Owner, List<MigrationSpecification>, Dictionary<string, object>)>();

			//public List<(object Owner, Dictionary<Path, object>)> GetTableFor
		}

		// should be immatable i.e. single instance for each one
		public class MigrationSpecification
		{
			public int Version;
			public Type InputType;
			public Type OutputType;
			public List<Path> Inputs;
			public List<Path> Outputs;
			public System.Reflection.MethodInfo MigrateMethodInfo;
		}

		// immutable (currently designed to be) storage for migrations
		public static class Storage
		{
			public static Dictionary<Type, List<MigrationSpecification>> migrations =
				new Dictionary<Type, List<MigrationSpecification>>();

			public static IEnumerable<MigrationSpecification> GetMigrationsForInstance(Object obj, int version)
			{
				var inhChain = new List<Type>();
				var baseType = obj.GetType();
				while (baseType != null && baseType != typeof(object)) {
					inhChain.Add(baseType);
					baseType = baseType.BaseType;
				}
				// TODO: reverse inhChain
				foreach (var t in inhChain) {
					if (
						migrations.TryGetValue(t, out List<MigrationSpecification> m) ||
						t.IsGenericType && migrations.TryGetValue(t.GetGenericTypeDefinition(), out m)
					) {
						foreach (var f in m) {
							if (f.Version >= version) {
								yield return f;
							}
						}
					}
				}
			}

			public static void Deconstruct<T1, T2>(this KeyValuePair<T1, T2> tuple, out T1 key, out T2 value)
			{
				key = tuple.Key;
				value = tuple.Value;
			}

			public static void BuildMigrations()
			{
				Dictionary<string, Path> c = new Dictionary<string, Path>();
				// TODO: group by type inheritance island
				foreach (var (key, value) in migrations) {
					value.Sort((a, b) => a.Version.CompareTo(b.Version));
					foreach (var m in value) {
						foreach (var i in m.Inputs) {
							if (c.TryGetValue(i.ToString(), out var p)) {
								i.AdjacentPath = p;
								p.AdjacentPath = i;
							}
						}
						foreach (var o in m.Outputs) {
							c.Add(o.ToString(), o);
						}
					}
					// all that's left in c and unused are outputs leading directly into dest type instance
					c.Clear();
				}
			}

			public static void RegisterMigration(Type type)
			{
				var aas = type.GetCustomAttributes(false);
				var a = aas.Where(i => i is YuzuMigrationAttribute).Cast<YuzuMigrationAttribute>().First();
				var targetVersion = a.FromVersion;

				var nestedTypes = type.GetNestedTypes();
				// TODO: check for 0 input types and > 1 input types
				var inputType = nestedTypes.Where(i => i.Name == "Input").First();
				Type outputType = null;
				var outputTypes = nestedTypes.Where(i => i.Name == "Output");
				if (outputTypes.Count() > 1) {
					// multiple output types specified for migrations
					throw new InvalidOperationException();
				}
				if (outputTypes.Any()) {
					outputType = outputTypes.First();
				}


				List<Path> inputs = new List<Path>();
				List<Path> outputs = new List<Path>();

				var ms = new MigrationSpecification();

				var inputMembers = inputType.GetMembers().Where(m => m.GetCustomAttributes(false).Where(i => i is YuzuMigrationSourceAttribute).Any());
				foreach (var im in inputMembers) {
					if (im is System.Reflection.PropertyInfo || im is System.Reflection.FieldInfo) {
						System.Reflection.MemberInfo targetInputMemberInfo = im;
						YuzuMigrationSourceAttribute sourceAttribute = im.GetCustomAttributes(false)
							.Where(i => i is YuzuMigrationSourceAttribute)
							.Cast<YuzuMigrationSourceAttribute>().First();
						//targetType
						inputs.Add(new Path(ms, im, sourceAttribute.Path, sourceAttribute.Types));
					}
				}

				if (outputType != null) {
					var outputMembers = outputType.GetMembers().Where(m => m.GetCustomAttributes(false).Where(i => i is YuzuMigrationDestinationAttribute).Any());
					foreach (var om in outputMembers) {
						if (om is System.Reflection.PropertyInfo || om is System.Reflection.FieldInfo) {
							System.Reflection.MemberInfo targetOutputMemberInfo = om;
							YuzuMigrationDestinationAttribute destAttribute = om.GetCustomAttributes(false)
								.Where(i => i is YuzuMigrationDestinationAttribute)
								.Cast<YuzuMigrationDestinationAttribute>().First();
							//targetType
							outputs.Add(new Path(ms, om, destAttribute.Path, destAttribute.Types));
						}
					}
				}

				var migrateMethodInfo = type.GetMembers()
					.Where(m => m is System.Reflection.MethodInfo && m.GetCustomAttributes(false).Any(attribute => attribute is YuzuMigrateMethod))
					.Cast<System.Reflection.MethodInfo>()
					.First();

				// TODO: validate input and output fields types

				{
					ms.InputType = inputType;
					ms.OutputType = outputType;
					ms.Version = targetVersion;
					ms.Inputs = inputs;
					ms.Outputs = outputs;
					ms.MigrateMethodInfo = migrateMethodInfo;
				}

				foreach (var targetType in a.Types) {
					if (!migrations.TryGetValue(targetType, out List<MigrationSpecification> l)) {
						migrations.Add(targetType, l = new List<MigrationSpecification>());
					}
					l.Add(ms);
				}
			}
		}

		public class Path : IEquatable<Path>
		{
			public readonly string[] Parts;
			public readonly Type[] Types;
			public MigrationSpecification Migration;
			public Path AdjacentPath;
			public System.Reflection.MemberInfo MemberInfo;

			// current version of migrated object should be provided
			public bool IsDirectInput(int version) =>
				AdjacentPath == null || AdjacentPath.Migration.Version <= version;

			// latest schema version should be provided
			public bool IsDirectOutput(int version) =>
				AdjacentPath == null || AdjacentPath.Migration.Version >= version;

			public Path(MigrationSpecification migration, System.Reflection.MemberInfo memberInfo, string path, Type[] types)
			{
				Migration = migration;
				MemberInfo = memberInfo;
				Parts = path.Split(new [] { '.' }, StringSplitOptions.RemoveEmptyEntries);
				Types = types;
				if (Types.Count() != Parts.Count()) {
					throw new System.Exception();
				}
			}

			public object GetValueFromIntermediate(object @object)
			{
				var fi = MemberInfo as System.Reflection.FieldInfo;
				var pi = MemberInfo as System.Reflection.PropertyInfo;
				if (fi != null) {
					return fi.GetValue(@object);
				} else if (pi != null) {
					return pi.GetValue(@object);
				} else {
					throw new InvalidOperationException();
				}
			}

			public void SetValueToIntermediate(object @object, object value)
			{
				var fi = MemberInfo as System.Reflection.FieldInfo;
				var pi = MemberInfo as System.Reflection.PropertyInfo;
				if (fi != null) {
					fi.SetValue(@object, value);
				} else if (pi != null) {
					pi.SetValue(@object, value);
				} else {
					throw new InvalidOperationException();
				}
			}

			public object GetValueByPath(object @object, bool skipFirst = true)
			{
				var o = @object;
				bool first = skipFirst;
				foreach (var p in Parts) {
					if (first) {
						first = false;
						continue;
					}
					var m = o.GetType().GetMembers().Where(i => i.Name == p).First();
					if (m is System.Reflection.PropertyInfo pi) {
						o = pi.GetValue(o);
					} else if (m is System.Reflection.FieldInfo fi) {
						o = fi.GetValue(o);
					} else {
						throw new InvalidOperationException();
					}
				}
				return o;
			}

			public void SetValueByPath(object @object, object value)
			{
				var o = @object;
				for (int iPart = 0; iPart < Parts.Length - 1; iPart++) {
					var p = Parts[iPart];
					var m = o.GetType().GetMembers().Where(i => i.Name == p).First();
					var po = o;
					{
						if (m is System.Reflection.PropertyInfo pi) {
							o = pi.GetValue(o);
						} else if (m is System.Reflection.FieldInfo fi) {
							o = fi.GetValue(o);
						} else {
							throw new InvalidOperationException();
						}
					}
					if (o == null) {
						o = Activator.CreateInstance(Types[iPart]);
						if (m is System.Reflection.PropertyInfo pi) {
							pi.SetValue(po, o);
						} else if (m is System.Reflection.FieldInfo fi) {
							fi.SetValue(po, o);
						} else {
							throw new InvalidOperationException();
						}
					}
				}
				{
					var p = Parts.Last();
					var m = o.GetType().GetMembers().Where(i => i.Name == p).First();
					if (m is System.Reflection.PropertyInfo pi) {
						pi.SetValue(o, value);
					} else if (m is System.Reflection.FieldInfo fi) {
						fi.SetValue(o, value);
					} else {
						throw new InvalidOperationException();
					}
				}
			}

			public override string ToString() => string.Join(".", Parts);

			public bool Equals(Path other) => Parts.SequenceEqual(other.Parts) && Types.SequenceEqual(other.Types);
		}

		public class YuzuMigrationSourceAttribute : System.Attribute
		{
			public YuzuMigrationSourceAttribute(string path, Type[] types)
			{
				Path = path;
				Types = types;
			}
			public YuzuMigrationSourceAttribute(string path, Type type)
				: this(path, new Type[] { type })
			{ }
			public readonly string Path;
			public readonly Type[] Types;
		}

		public class YuzuMigrationDestinationAttribute : System.Attribute
		{
			public YuzuMigrationDestinationAttribute(MigrationOutputType migrationOutputType, string path, Type[] types)
			{
				MigrationOutputType = migrationOutputType;
				Path = path;
				Types = types;
			}

			public YuzuMigrationDestinationAttribute(MigrationOutputType migrationOutputType, string path, Type type)
				: this(migrationOutputType, path, new Type[] { type })
			{ }

			public readonly MigrationOutputType MigrationOutputType;
			public readonly string Path;
			public readonly Type[] Types;
		}

		public class YuzuMigrationAttribute : System.Attribute
		{
			public YuzuMigrationAttribute(Type[] types, int fromVersion)
			{
				Types = types;
				FromVersion = fromVersion;
			}

			public YuzuMigrationAttribute(Type type, int fromVersion)
				: this(new Type[] { type }, fromVersion)
			{ }

			public readonly Type[] Types;
			public readonly int FromVersion;
		}

		public class YuzuMigrateMethod : System.Attribute
		{

		}
	}

	public enum MigrationOutputType
	{
		Assign,
		InsertIntoCollection,
		Merge
	}
}
