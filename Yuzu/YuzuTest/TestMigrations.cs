using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Yuzu;
using Yuzu.Binary;
using Yuzu.Metadata;
using Yuzu.Unsafe;
using YuzuGenBin;
using YuzuTestAssembly;
using YuzuTest.SampleMigrations;
using Yuzu.Migrations;

namespace YuzuTest.Migrations
{
	[TestClass]
	public class TestMigrations
	{
		[TestMethod]
		public void TestMigrateFooIntoBar()
		{
			Yuzu.Migrations.Storage.RegisterMigration(typeof(Test.MigrateFooIntoBar));
			Yuzu.Migrations.Storage.BuildMigrations();
			var s = "{\n\t\"FooValue\":{\n\t\t\"FooS\":\"FooBar\"\n\t}\n}";
			var jd = new Yuzu.Json.JsonDeserializer() {
				JsonOptions = new Yuzu.Json.JsonSerializeOptions {
					Unordered = true
				}
			};
			jd.MigrationContext = new Yuzu.Migrations.MigrationContext {
				Version = 0,
			};
			var o = jd.FromString<Test>(s);
			Storage.ApplyMigrations(jd.MigrationContext, 1);
			Assert.AreEqual(o.BarValue.BarS, "FooBarFooBar");
			Yuzu.Migrations.Storage.Clear();
		}

		[TestMethod]
		public void TestSimpleInput()
		{
			Yuzu.Migrations.Storage.RegisterMigration(typeof(TestSimpleIntField.MigrateTestSimpleInt_SimpleInput));
			Yuzu.Migrations.Storage.BuildMigrations();
			var s1 = "{\n\t\"Value\": 666\n}";
			var s2 = "{}";
			var jd = JD();
			var o = jd.FromString<TestSimpleIntField>(s1);
			Storage.ApplyMigrations(jd.MigrationContext, 1);
			Assert.AreEqual(o.Value, 666);
			Assert.AreEqual(o.ValueSetByMigration, 666 * 666 + 333);
			o = jd.FromString<TestSimpleIntField>(s2);
			Storage.ApplyMigrations(jd.MigrationContext, 1);
			Assert.AreEqual(o.Value, 0);
			Assert.AreEqual(o.ValueSetByMigration, 0 * 0 + 333);
			Yuzu.Migrations.Storage.Clear();
		}


		[TestMethod]
		public void TestChangeFieldValue()
		{
			Yuzu.Migrations.Storage.RegisterMigration(typeof(TestSimpleIntField.MigrateTestSimpleInt_ChangeFieldValue));
			Yuzu.Migrations.Storage.BuildMigrations();
			var s1 = "{\n\t\"Value\": 666\n}";
			var s2 = "{}";
			var jd = JD();
			var o = jd.FromString<TestSimpleIntField>(s1);
			Storage.ApplyMigrations(jd.MigrationContext, 1);
			Assert.AreEqual(o.Value, 666 * 666 + 333);
			Assert.AreEqual(o.ValueSetByMigration, 0);
			o = jd.FromString<TestSimpleIntField>(s2);
			Storage.ApplyMigrations(jd.MigrationContext, 1);
			Assert.AreEqual(o.Value, 0 * 0 + 333);
			Assert.AreEqual(o.ValueSetByMigration, 0);
			Yuzu.Migrations.Storage.Clear();
		}

		[TestMethod]
		public void TestRenameField()
		{
			Yuzu.Migrations.Storage.RegisterMigration(typeof(TestSimpleIntField.MigrateTestSimpleInt_RenameField));
			Yuzu.Migrations.Storage.BuildMigrations();
			var s1 = "{\n\t\"PreviousFieldName\": 666\n}";
			var s2 = "{}";
			var jd = JD();
			var o = jd.FromString<TestSimpleIntField>(s1);
			Storage.ApplyMigrations(jd.MigrationContext, 1);
			Assert.AreEqual(o.Value, 666);
			o = jd.FromString<TestSimpleIntField>(s2);
			Storage.ApplyMigrations(jd.MigrationContext, 1);
			Assert.AreEqual(o.Value, 0);
			Yuzu.Migrations.Storage.Clear();
		}

		[TestMethod]
		public void TestFieldTypeRename()
		{
			Yuzu.Migrations.Storage.RegisterMigration(typeof(TestSimpleClassField.MigrateTestSimpleInt_FieldTypeRename));
			Yuzu.Migrations.Storage.BuildMigrations();
			var s1 = "{\n\t\"Value\":\n\t{\n\t\t\"IValue\": 666,\n\t\t\"SValue\": \"FooBar\"\n\t}\n}";
			var s2 = "{}";
			var s3 = "{\n\t\"Value\":\n\t{\n\t\t\"class\":\"YuzuTest.SampleMigrations.TestSimpleClassField+Bar, YuzuTest\",\n\t\t\"IValue\": 666,\n\t\t\"SValue\": \"FooBar\"\n\t}\n}";
			var jd = JD();
			var o = jd.FromString<TestSimpleClassField>(s1);
			Storage.ApplyMigrations(jd.MigrationContext, 1);
			Assert.AreEqual(o.Value.IValue, 666);
			Assert.AreEqual(o.Value.SValue, "FooBar");
			o = jd.FromString<TestSimpleClassField>(s2);
			Storage.ApplyMigrations(jd.MigrationContext, 1);
			Assert.AreEqual(o.Value, null);
			o = jd.FromString<TestSimpleClassField>(s3);
			Storage.ApplyMigrations(jd.MigrationContext, 1);
			Assert.AreEqual(o.Value.IValue, 666);
			Assert.AreEqual(o.Value.SValue, "FooBar");
			Yuzu.Migrations.Storage.Clear();
		}

		private Yuzu.Json.JsonDeserializer JD() =>
			new Yuzu.Json.JsonDeserializer() {
			JsonOptions = new Yuzu.Json.JsonSerializeOptions {
				Unordered = true
			},
			MigrationContext = new Yuzu.Migrations.MigrationContext {
				Version = 0,
			},
		};
	}
}
