using System.Collections.Generic;

using Yuzu;
using Yuzu.Migrations;

namespace YuzuTest.SampleMigrations
{
	public class TestSimpleIntField
	{
		[YuzuMember]
		public int Value;

		[YuzuMember]
		public int ValueSetByMigration;

		[YuzuMigration(typeof(TestSimpleIntField), 0)]
		public class MigrateTestSimpleInt_SimpleInput
		{
			public class Input
			{
				[YuzuMigrationSource("Value", typeof(int))]
				public int V;
			}

			public class Output
			{
				[YuzuMigrationDestination("ValueSetByMigration", typeof(int))]
				public int V;
			}

			[YuzuMigrateMethod]
			public static Output Migrate(Input input)
			{
				return new Output {
					V = input.V * input.V + 333,
				};
			}
		}

		[YuzuMigration(typeof(TestSimpleIntField), 0)]
		public class MigrateTestSimpleInt_ChangeFieldValue
		{
			public class Input
			{
				[YuzuMigrationSource("Value", typeof(int))]
				public int V;
			}

			public class Output
			{
				[YuzuMigrationDestination("Value", typeof(int))]
				public int V;
			}

			[YuzuMigrateMethod]
			public static Output Migrate(Input input)
			{
				return new Output {
					V = input.V * input.V + 333,
				};
			}
		}

		[YuzuMigration(typeof(TestSimpleIntField), 0)]
		public class MigrateTestSimpleInt_RenameField
		{
			public class Input
			{
				[YuzuMigrationSource("PreviousFieldName", typeof(int))]
				public int V;
			}

			public class Output
			{
				[YuzuMigrationDestination("Value", typeof(int))]
				public int V;
			}

			[YuzuMigrateMethod]
			public static Output Migrate(Input input)
			{
				return new Output {
					V = input.V,
				};
			}
		}
	}

	public class TestSimpleClassField
	{
		[YuzuMember]
		public Foo Value;

		public class Foo
		{
			[YuzuMember]
			public int IValue;
			[YuzuMember]
			public string SValue;
		}

		public class Bar
		{
			[YuzuMember]
			public int IValue;
			[YuzuMember]
			public string SValue;
		}

		[YuzuMigration(typeof(TestSimpleClassField), 0)]
		public class MigrateTestSimpleInt_FieldTypeRename
		{
			public class Input
			{
				[YuzuMigrationSource("Value", typeof(Bar))]
				public Bar V;
			}

			public class Output
			{
				[YuzuMigrationDestination("Value", typeof(Foo))]
				public Foo V;
			}

			[YuzuMigrateMethod]
			public static Output Migrate(Input input)
			{
				if (input.V == null) {
					return null;
				}
				return new Output {
					V = new Foo {
						IValue = input.V.IValue,
						SValue = input.V.SValue,
					}
				};
			}
		}
	}

	public class TestIntermediateNullInPropertyPath
	{
		public class Foo
		{
			[YuzuMember]
			public Bar Bar1;

			[YuzuMember]
			public Bar Bar2;
		}

		public class Bar
		{
			[YuzuMember]
			public Moo Moo = Moo.Instance;
		}

		public class Moo
		{
			[YuzuMember]
			public int V;

			public static Moo Instance = new Moo();
		}

		[YuzuMigration(typeof(Foo), 0)]
		public class Migration
		{
			public class Input
			{
				[YuzuMigrationSource("Bar1.Moo.V", new [] { typeof(Bar), typeof(Moo), typeof(int) })]
				public int V1;
			}

			public class Output
			{
				[YuzuMigrationDestination("Bar2.Moo.V", new[] { typeof(Bar), typeof(Moo), typeof(int) })]
				public int V2;
			}

			[YuzuMigrateMethod]
			public static Output Migrate(Input input)
			{
				return new Output {
					V2 = input.V1,
				};
			}
		}
	}

	public class Test
	{
		[YuzuMember]
		public List<Test> Children { get; private set; }

		public Test()
		{
			Children = new List<Test>();
		}

		[YuzuMember]
		public Bar BarValue;

		public class Foo
		{
			[YuzuMember]
			public string FooS;
		}

		public class Bar
		{
			[YuzuMember]
			public string BarS;
		}

		[YuzuMigration(typeof(Test), 0)]
		public class MigrateFooIntoBar
		{
			public class Input
			{
				[YuzuMigrationSource("FooValue.FooS", new[] { typeof(Foo), typeof(string) })]
				public string Text;
			}

			public class Output
			{
				[YuzuMigrationDestination("BarValue.BarS", new[] { typeof(Bar), typeof(string) })]
				public string Text;
			}

			[YuzuMigrateMethod]
			public static Output Migrate(Input input)
			{
				return new Output {
					Text = input.Text + input.Text,
				};
			}
		}
	}

	public struct StructData
	{
		[YuzuMember]
		public int V;
	}

	[YuzuCompact]
	public struct CompactStructData
	{
		[YuzuMember]
		public int A;

		[YuzuMember]
		public int B;

		[YuzuMember]
		public int C;
	}

	public class Node
	{
		[YuzuMember]
		public string Id { get; set; }

		[YuzuMember]
		public Node Leaf1;

		[YuzuMember]
		public Node Leaf2;

		[YuzuMember]
		public List<Node> Nodes1 { get; private set; } = new List<Node>();

		[YuzuMember]
		public List<Node> Nodes2 { get; set; }
	}

	public class DerivedFromNode : Node
	{

	}

	namespace CombinedMigrations
	{
		public class Bar : BarBase
		{
			[YuzuMember]
			public int V;
		}

		public class Bar2 : BarBase
		{
			[YuzuMember]
			public string B;
		}

		public class Foo : FooBase
		{
			[YuzuMember]
			public BarBase Bar2;
		}

		public class Foo2 : FooBase
		{
			[YuzuMember]
			public BarBase Bar;
		}

		public interface FooBase
		{

		}

		public interface BarBase
		{

		}

		public class Tar
		{
			[YuzuMember]
			public FooBase Foo;
		}

		public class Migrations
		{
			[YuzuMigration(typeof(Foo), 0)]
			public class MigrateFooProperty
			{
				public class Input
				{
					[YuzuMigrationSource("Bar", typeof(Bar))]
					public Bar Bar;
				}

				public class Output
				{
					[YuzuMigrationDestination("Bar2", typeof(Bar2))]
					public Bar2 Bar2;
				}

				[YuzuMigrateMethod]
				public static Output Migrate(Input input)
				{
					return new Output {
						Bar2 = new Bar2 {
							B = input.Bar.V.ToString()
						}
					};
				}
			}

			[YuzuTypeMigration(1)]
			public static Foo2 MigrateFoo(Foo foo)
			{
				return new Foo2 {
					Bar = foo.Bar2
				};
			}
		}
	}
}
