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
}
