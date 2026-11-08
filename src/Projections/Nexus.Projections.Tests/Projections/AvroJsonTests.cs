using Avro;
using Avro.Generic;
using Avro.Util;
using Nexus.Projections.Projections;
using Shouldly;

namespace Nexus.Projections.Tests.Projections;

public sealed class AvroJsonTests
{
    private static string Json(string fieldSchemaJson, object? value, string fieldName = "f")
    {
        var schema = (RecordSchema)Schema.Parse(
            $$"""{"type":"record","name":"R","namespace":"t","fields":[{"name":"{{fieldName}}","type":{{fieldSchemaJson}}}]}""");
        var r = new GenericRecord(schema);
        r.Add(fieldName, value);
        return AvroJson.ToJson(r);
    }

    [Fact(DisplayName = "record fields keep schema names and order; compact output")]
    public void Record_object_in_schema_order()
    {
        var schema = (RecordSchema)Schema.Parse(
            """{"type":"record","name":"R","fields":[{"name":"zeta","type":"string"},{"name":"alpha","type":"boolean"},{"name":"n","type":"long"}]}""");
        var r = new GenericRecord(schema);
        r.Add("zeta", "z"); r.Add("alpha", true); r.Add("n", 42L);
        AvroJson.ToJson(r).ShouldBe("""{"zeta":"z","alpha":true,"n":42}""");
    }

    [Fact(DisplayName = "string uuid: raw string and converted Guid give the same string")]
    public void Uuid()
    {
        var g = Guid.Parse("0197a000-0000-7000-8000-000000000001");
        var s = """{"type":"string","logicalType":"uuid"}""";
        Json(s, g.ToString()).ShouldBe("""{"f":"0197a000-0000-7000-8000-000000000001"}""");
        Json(s, g).ShouldBe("""{"f":"0197a000-0000-7000-8000-000000000001"}""");
    }

    [Fact(DisplayName = "union: non-null branch value, null becomes JSON null")]
    public void Union()
    {
        Json("""["null","string"]""", "x").ShouldBe("""{"f":"x"}""");
        Json("""["null","string"]""", null).ShouldBe("""{"f":null}""");
        Json("""["null",{"type":"string","logicalType":"uuid"}]""", Guid.Empty).ShouldBe("""{"f":"00000000-0000-0000-0000-000000000000"}""");
    }

    [Fact(DisplayName = "int, long, float, double are JSON numbers; boolean")]
    public void Numbers()
    {
        Json("\"int\"", 7).ShouldBe("""{"f":7}""");
        Json("\"long\"", 9_000_000_000L).ShouldBe("""{"f":9000000000}""");
        Json("\"double\"", 1.5d).ShouldBe("""{"f":1.5}""");
        Json("\"float\"", 2.5f).ShouldBe("""{"f":2.5}""");
        Json("\"boolean\"", false).ShouldBe("""{"f":false}""");
    }

    private const string Dec = """{"type":"bytes","logicalType":"decimal","precision":18,"scale":4}""";

    [Theory(DisplayName = "bytes decimal: exact string from big-endian two's complement, never a double")]
    [InlineData(new byte[] { 0x00, 0x01, 0xE2, 0x40 }, "12.3456")]      // 123456
    [InlineData(new byte[] { 0x00 }, "0.0000")]
    [InlineData(new byte[] { 0x05 }, "0.0005")]
    [InlineData(new byte[] { 0xFF }, "-0.0001")]                         // -1
    [InlineData(new byte[] { 0xFE, 0x1D, 0xC0 }, "-12.3456")]          // -123456
    [InlineData(new byte[] { 0x0D, 0xE0, 0xB6, 0xB3, 0xA7, 0x64, 0x00, 0x00 }, "100000000000000.0000")] // 10^18
    public void Decimal_bytes(byte[] unscaled, string expected) =>
        Json(Dec, unscaled).ShouldBe($$"""{"f":"{{expected}}"}""");

    [Fact(DisplayName = "fixed decimal: exact string at the schema scale")]
    public void Decimal_fixed()
    {
        var s = """{"type":"fixed","name":"D","size":3,"logicalType":"decimal","precision":10,"scale":2}""";
        var fixedSchema = (FixedSchema)((LogicalSchema)Schema.Parse(s)).BaseSchema;
        Json(s, new GenericFixed(fixedSchema, [0x00, 0x30, 0x39])).ShouldBe("""{"f":"123.45"}""");
    }

    [Fact(DisplayName = "decimal already converted (AvroDecimal / decimal) renders at the schema scale")]
    public void Decimal_converted()
    {
        Json(Dec, new AvroDecimal(1.5m)).ShouldBe("""{"f":"1.5000"}""");
        Json(Dec, 1.5m).ShouldBe("""{"f":"1.5000"}""");
    }

    [Fact(DisplayName = "timestamp-millis/micros: raw long and DateTime give ISO-8601 UTC with microseconds")]
    public void Timestamps()
    {
        var millis = """{"type":"long","logicalType":"timestamp-millis"}""";
        var micros = """{"type":"long","logicalType":"timestamp-micros"}""";
        var instant = new DateTime(2026, 11, 8, 10, 30, 15, 123, DateTimeKind.Utc);
        var ms = (long)(instant - DateTime.UnixEpoch).TotalMilliseconds;
        Json(millis, ms).ShouldBe("""{"f":"2026-11-08T10:30:15.123000Z"}""");
        Json(millis, instant).ShouldBe("""{"f":"2026-11-08T10:30:15.123000Z"}""");
        Json(micros, ms * 1000 + 456).ShouldBe("""{"f":"2026-11-08T10:30:15.123456Z"}""");
        Json(micros, instant.AddTicks(4560)).ShouldBe("""{"f":"2026-11-08T10:30:15.123456Z"}""");
    }

    [Fact(DisplayName = "date: raw days and DateTime give yyyy-MM-dd")]
    public void Dates()
    {
        var s = """{"type":"int","logicalType":"date"}""";
        var days = (int)(new DateTime(2026, 11, 8) - DateTime.UnixEpoch).TotalDays;
        Json(s, days).ShouldBe("""{"f":"2026-11-08"}""");
        Json(s, new DateTime(2026, 11, 8, 0, 0, 0, DateTimeKind.Utc)).ShouldBe("""{"f":"2026-11-08"}""");
    }

    [Fact(DisplayName = "enum is its symbol")]
    public void Enum()
    {
        var s = """{"type":"enum","name":"E","symbols":["A","B"]}""";
        Json(s, new GenericEnum((EnumSchema)Schema.Parse(s), "B")).ShouldBe("""{"f":"B"}""");
    }

    [Fact(DisplayName = "array keeps order; plain bytes are base64")]
    public void Array_and_bytes()
    {
        Json("""{"type":"array","items":"int"}""", new object[] { 3, 1, 2 }).ShouldBe("""{"f":[3,1,2]}""");
        Json("\"bytes\"", new byte[] { 1, 2, 3 }).ShouldBe("""{"f":"AQID"}""");
    }

    [Fact(DisplayName = "map keys are sorted ordinally, whatever the insertion order (deterministic rebuild)")]
    public void Map_sorted()
    {
        var s = """{"type":"map","values":"string"}""";
        var a = new Dictionary<string, object> { ["b"] = "2", ["B"] = "0", ["a"] = "1" };
        var b = new Dictionary<string, object> { ["a"] = "1", ["B"] = "0", ["b"] = "2" };
        Json(s, a).ShouldBe("""{"f":{"B":"0","a":"1","b":"2"}}""");
        Json(s, b).ShouldBe(Json(s, a));
    }

    [Fact(DisplayName = "nested record in union")]
    public void Nested_record()
    {
        var inner = """{"type":"record","name":"In","fields":[{"name":"x","type":"int"}]}""";
        var innerSchema = (RecordSchema)Schema.Parse(inner);
        var ir = new GenericRecord(innerSchema);
        ir.Add("x", 5);
        Json($"[\"null\",{inner}]", ir).ShouldBe("""{"f":{"x":5}}""");
    }

    [Fact(DisplayName = "JSON escaping of strings")]
    public void Escaping() => Json("\"string\"", "a\"b\\c").ShouldBe("""{"f":"a\"b\\c"}""");
}
