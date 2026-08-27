using System.Text.Json;
using System.Text.Json.Nodes;

namespace MintPlayer.Assertions.Tests;

public class JsonAssertionsTests
{
    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    #region BeJsonEquivalentTo / NotBeJsonEquivalentTo (JsonElement)

    [Fact]
    public void BeJsonEquivalentTo_IsPropertyOrderInsensitive()
    {
        var element = Parse("""{"a":1,"b":{"c":true,"d":"x"}}""");
        element.Should().BeJsonEquivalentTo("""{"b":{"d":"x","c":true},"a":1}""");
    }

    [Fact]
    public void BeJsonEquivalentTo_ComparesNumbersByValue()
    {
        var element = Parse("""{"a":1.0,"b":2.50,"c":3}""");
        element.Should().BeJsonEquivalentTo("""{"a":1.00,"b":2.5,"c":3.0}""");
    }

    [Fact]
    public void BeJsonEquivalentTo_AcceptsExpectedElement()
    {
        var element = Parse("""[1,"two",null,true]""");
        element.Should().BeJsonEquivalentTo(Parse("""[1,"two",null,true]"""));
    }

    [Fact]
    public void BeJsonEquivalentTo_ReportsAllDifferences()
    {
        var element = Parse("""{"name":"b","items":[1,2,3],"extra":true}""");

        var ex = Record.Exception(() => element.Should().BeJsonEquivalentTo("""{"name":"a","items":[1,5,3],"missing":7}"""));

        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Expected element to be JSON equivalent to", failure.Message);
        Assert.Contains("$.name: expected \"a\", found \"b\"", failure.Message);
        Assert.Contains("$.items[1]: expected 5, found 2", failure.Message);
        Assert.Contains("$.missing: missing property (expected 7)", failure.Message);
        Assert.Contains("$.extra: extra property (found true)", failure.Message);
    }

    [Fact]
    public void BeJsonEquivalentTo_ReportsNestedDifferencesWithFullPath()
    {
        var element = Parse("""{"a":{"items":[{"name":"b"}]}}""");

        var ex = Record.Exception(() => element.Should().BeJsonEquivalentTo("""{"a":{"items":[{"name":"a"}]}}"""));

        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("$.a.items[0].name: expected \"a\", found \"b\"", failure.Message);
    }

    [Fact]
    public void BeJsonEquivalentTo_ReportsArrayLengthAndOrderDifferences()
    {
        var element = Parse("[1,2]");

        var lengthEx = Record.Exception(() => element.Should().BeJsonEquivalentTo("[1,2,3]"));
        var orderEx = Record.Exception(() => element.Should().BeJsonEquivalentTo("[2,1]"));

        var lengthFailure = Assert.IsType<AssertionFailedException>(lengthEx);
        Assert.Contains("$: expected array of length 3, found length 2", lengthFailure.Message);
        var orderFailure = Assert.IsType<AssertionFailedException>(orderEx);
        Assert.Contains("$[0]: expected 2, found 1", orderFailure.Message);
        Assert.Contains("$[1]: expected 1, found 2", orderFailure.Message);
    }

    [Fact]
    public void BeJsonEquivalentTo_PreservesBracesOfComplexValuesInMessage()
    {
        var element = Parse("""{"a":1}""");

        var ex = Record.Exception(() => element.Should().BeJsonEquivalentTo("""{"a":1,"b":{"x":1}}"""));

        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("""$.b: missing property (expected {"x":1})""", failure.Message);
    }

    [Fact]
    public void BeJsonEquivalentTo_ReportsKindMismatch()
    {
        var element = Parse("""{"a":"1"}""");

        var ex = Record.Exception(() => element.Should().BeJsonEquivalentTo("""{"a":1}"""));

        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("$.a: expected 1, found \"1\"", failure.Message);
    }

    [Fact]
    public void BeJsonEquivalentTo_ThrowsArgumentExceptionForInvalidExpectedJson()
    {
        var element = Parse("""{"a":1}""");

        var ex = Record.Exception(() => element.Should().BeJsonEquivalentTo("{not json"));

        Assert.IsType<ArgumentException>(ex);
    }

    [Fact]
    public void NotBeJsonEquivalentTo_PassesForDifferentJson()
    {
        var element = Parse("""{"a":1}""");
        element.Should().NotBeJsonEquivalentTo("""{"a":2}""").And.NotBeJsonEquivalentTo(Parse("[]"));
    }

    [Fact]
    public void NotBeJsonEquivalentTo_FailsForEquivalentJson()
    {
        var element = Parse("""{"a":1,"b":2}""");

        var ex = Record.Exception(() => element.Should().NotBeJsonEquivalentTo("""{"b":2.0,"a":1}"""));

        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Did not expect element to be JSON equivalent to", failure.Message);
    }

    #endregion

    #region HaveProperty / NotHaveProperty (JsonElement)

    [Fact]
    public void HaveProperty_PassesAndExposesValue()
    {
        var element = Parse("""{"a":{"b":42}}""");

        var value = element.Should().HaveProperty("a").Which;

        Assert.Equal(42, value.GetProperty("b").GetInt32());
        value.Should().HaveProperty("b").Which.Should().BeJsonNumber();
    }

    [Fact]
    public void HaveProperty_FailsForMissingProperty()
    {
        var element = Parse("""{"a":1}""");

        var ex = Record.Exception(() => element.Should().HaveProperty("b"));

        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Expected element to have property \"b\", but it does not.", failure.Message);
    }

    [Fact]
    public void HaveProperty_FailsForNonObject()
    {
        var element = Parse("[1,2]");

        var ex = Record.Exception(() => element.Should().HaveProperty("a"));

        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("it is a JSON array rather than a JSON object", failure.Message);
    }

    [Fact]
    public void NotHaveProperty_PassesForMissingPropertyAndNonObjects()
    {
        var element = Parse("""{"a":1}""");
        element.Should().NotHaveProperty("b");
        Parse("[1]").Should().NotHaveProperty("a");
    }

    [Fact]
    public void NotHaveProperty_FailsForPresentProperty()
    {
        var element = Parse("""{"a":1}""");

        var ex = Record.Exception(() => element.Should().NotHaveProperty("a"));

        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Did not expect element to have property \"a\", but it does.", failure.Message);
    }

    #endregion

    #region Value-kind checks (JsonElement)

    [Fact]
    public void KindChecks_PassForMatchingKinds()
    {
        Parse("{}").Should().BeJsonObject();
        Parse("[]").Should().BeJsonArray();
        Parse("\"a\"").Should().BeJsonString();
        Parse("1.5").Should().BeJsonNumber();
        Parse("true").Should().BeJsonBoolean();
        Parse("false").Should().BeJsonBoolean();
        Parse("null").Should().BeJsonNull();
    }

    [Fact]
    public void BeJsonObject_FailsForOtherKind()
    {
        var element = Parse("[]");
        var ex = Record.Exception(() => element.Should().BeJsonObject());
        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Expected element to be a JSON object, but it is a JSON array.", failure.Message);
    }

    [Fact]
    public void BeJsonArray_FailsForOtherKind()
    {
        var element = Parse("{}");
        var ex = Record.Exception(() => element.Should().BeJsonArray());
        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Expected element to be a JSON array, but it is a JSON object.", failure.Message);
    }

    [Fact]
    public void BeJsonString_FailsForOtherKind()
    {
        var element = Parse("1");
        var ex = Record.Exception(() => element.Should().BeJsonString());
        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Expected element to be a JSON string, but it is a JSON number.", failure.Message);
    }

    [Fact]
    public void BeJsonNumber_FailsForOtherKind()
    {
        var element = Parse("\"1\"");
        var ex = Record.Exception(() => element.Should().BeJsonNumber());
        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Expected element to be a JSON number, but it is a JSON string.", failure.Message);
    }

    [Fact]
    public void BeJsonBoolean_FailsForOtherKind()
    {
        var element = Parse("null");
        var ex = Record.Exception(() => element.Should().BeJsonBoolean());
        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Expected element to be a JSON boolean, but it is JSON null.", failure.Message);
    }

    [Fact]
    public void BeJsonNull_FailsForOtherKind()
    {
        var element = Parse("false");
        var ex = Record.Exception(() => element.Should().BeJsonNull());
        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Expected element to be JSON null, but it is a JSON boolean.", failure.Message);
    }

    #endregion

    #region Scalar value checks (JsonElement)

    [Fact]
    public void HaveStringValue_PassesForEqualString()
    {
        var element = Parse("\"hello\"");
        element.Should().HaveStringValue("hello");
    }

    [Fact]
    public void HaveStringValue_FailsForDifferentString()
    {
        var element = Parse("\"hello\"");
        var ex = Record.Exception(() => element.Should().HaveStringValue("world"));
        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Expected element to have string value \"world\", but found \"hello\".", failure.Message);
    }

    [Fact]
    public void HaveStringValue_FailsForNonString()
    {
        var element = Parse("1");
        var ex = Record.Exception(() => element.Should().HaveStringValue("1"));
        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("it is a JSON number rather than a JSON string", failure.Message);
    }

    [Fact]
    public void HaveNumberValue_PassesIgnoringNumberFormat()
    {
        var element = Parse("1.50");
        element.Should().HaveNumberValue(1.5m);
    }

    [Fact]
    public void HaveNumberValue_FailsForDifferentNumber()
    {
        var element = Parse("2");
        var ex = Record.Exception(() => element.Should().HaveNumberValue(3m));
        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Expected element to have number value 3, but found 2.", failure.Message);
    }

    [Fact]
    public void HaveBooleanValue_PassesForEqualBoolean()
    {
        var element = Parse("true");
        element.Should().HaveBooleanValue(true);
    }

    [Fact]
    public void HaveBooleanValue_FailsForDifferentBoolean()
    {
        var element = Parse("true");
        var ex = Record.Exception(() => element.Should().HaveBooleanValue(false));
        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Expected element to have boolean value false, but found true.", failure.Message);
    }

    [Fact]
    public void HaveArrayLength_PassesForMatchingLength()
    {
        var element = Parse("[1,2,3]");
        element.Should().HaveArrayLength(3);
    }

    [Fact]
    public void HaveArrayLength_FailsForDifferentLength()
    {
        var element = Parse("[1,2,3]");
        var ex = Record.Exception(() => element.Should().HaveArrayLength(2));
        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Expected element to have array length 2, but found 3.", failure.Message);
    }

    [Fact]
    public void HaveArrayLength_FailsForNonArray()
    {
        var element = Parse("{}");
        var ex = Record.Exception(() => element.Should().HaveArrayLength(0));
        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("it is a JSON object rather than a JSON array", failure.Message);
    }

    #endregion

    #region Because clause

    [Fact]
    public void FailureMessage_ContainsBecauseClause()
    {
        var element = Parse("[]");
        var ex = Record.Exception(() => element.Should().BeJsonObject("we expect {0}", "an object"));
        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("because we expect an object", failure.Message);
    }

    #endregion

    #region JsonDocument

    [Fact]
    public void JsonDocument_Should_AssertsOnRootElement()
    {
        using var document = JsonDocument.Parse("""{"a":1}""");
        document.Should().BeJsonObject().And.HaveProperty("a").Which.Should().HaveNumberValue(1m);
    }

    [Fact]
    public void JsonDocument_Null_FailsWithClearMessage()
    {
        JsonDocument? document = null;

        var ex = Record.Exception(() => document.Should().BeJsonObject());

        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Expected document to be a JSON object, but found <null>.", failure.Message);
    }

    [Fact]
    public void JsonDocument_Null_FailsEquivalency()
    {
        JsonDocument? document = null;

        var ex = Record.Exception(() => document.Should().BeJsonEquivalentTo("""{"a":1}"""));

        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("but found <null>", failure.Message);
    }

    #endregion

    #region JsonNode

    [Fact]
    public void JsonNode_BeJsonEquivalentTo_PassesForEquivalentJson()
    {
        var node = JsonNode.Parse("""{"a":1,"b":[true,"x"]}""");
        node.Should().BeJsonEquivalentTo("""{"b":[true,"x"],"a":1.0}""");
    }

    [Fact]
    public void JsonNode_BeJsonEquivalentTo_FailsWithDifferences()
    {
        var node = JsonNode.Parse("""{"a":1}""");

        var ex = Record.Exception(() => node.Should().BeJsonEquivalentTo("""{"a":2}"""));

        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Expected node to be JSON equivalent to", failure.Message);
        Assert.Contains("$.a: expected 2, found 1", failure.Message);
    }

    [Fact]
    public void JsonNode_NotBeJsonEquivalentTo_PassesForDifferentJson()
    {
        var node = JsonNode.Parse("""{"a":1}""");
        node.Should().NotBeJsonEquivalentTo("""{"a":2}""").And.NotBeJsonEquivalentTo(Parse("null"));
    }

    [Fact]
    public void JsonNode_NotBeJsonEquivalentTo_FailsForEquivalentJson()
    {
        var node = JsonNode.Parse("""{"a":1}""");

        var ex = Record.Exception(() => node.Should().NotBeJsonEquivalentTo("""{"a":1.00}"""));

        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Did not expect node to be JSON equivalent to", failure.Message);
    }

    [Fact]
    public void JsonNode_HaveProperty_PassesAndExposesNode()
    {
        var node = JsonNode.Parse("""{"a":{"b":42}}""");

        var value = node.Should().HaveProperty("a").Which;

        Assert.NotNull(value);
        Assert.Equal(42, value["b"]!.GetValue<int>());
        value.Should().HaveProperty("b").Which.Should().HaveNumberValue(42m);
    }

    [Fact]
    public void JsonNode_HaveProperty_FailsForMissingProperty()
    {
        var node = JsonNode.Parse("""{"a":1}""");

        var ex = Record.Exception(() => node.Should().HaveProperty("b"));

        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Expected node to have property \"b\", but it does not.", failure.Message);
    }

    [Fact]
    public void JsonNode_HaveProperty_FailsForNonObject()
    {
        var node = JsonNode.Parse("[1]");

        var ex = Record.Exception(() => node.Should().HaveProperty("a"));

        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("it is a JSON array rather than a JSON object", failure.Message);
    }

    [Fact]
    public void JsonNode_HaveProperty_FailsForNullNode()
    {
        JsonNode? node = null;

        var ex = Record.Exception(() => node.Should().HaveProperty("a"));

        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Expected node to have property \"a\", but found <null>.", failure.Message);
    }

    [Fact]
    public void JsonNode_NotHaveProperty_PassesAndFails()
    {
        var node = JsonNode.Parse("""{"a":1}""");
        node.Should().NotHaveProperty("b");

        var ex = Record.Exception(() => node.Should().NotHaveProperty("a"));

        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Did not expect node to have property \"a\", but it does.", failure.Message);
    }

    [Fact]
    public void JsonNode_KindChecks_PassForMatchingKinds()
    {
        JsonNode.Parse("{}").Should().BeJsonObject();
        JsonNode.Parse("[]").Should().BeJsonArray();
        JsonNode.Parse("\"a\"").Should().BeJsonString();
        JsonNode.Parse("1").Should().BeJsonNumber();
        JsonNode.Parse("true").Should().BeJsonBoolean();
    }

    [Fact]
    public void JsonNode_KindChecks_FailForOtherKinds()
    {
        var node = JsonNode.Parse("[1]");

        var objectEx = Record.Exception(() => node.Should().BeJsonObject());
        var stringEx = Record.Exception(() => node.Should().BeJsonString());
        var numberEx = Record.Exception(() => node.Should().BeJsonNumber());
        var booleanEx = Record.Exception(() => node.Should().BeJsonBoolean());
        var arrayEx = Record.Exception(() => JsonNode.Parse("{}").Should().BeJsonArray());

        Assert.Contains("to be a JSON object, but it is a JSON array.", Assert.IsType<AssertionFailedException>(objectEx).Message);
        Assert.Contains("to be a JSON string, but it is a JSON array.", Assert.IsType<AssertionFailedException>(stringEx).Message);
        Assert.Contains("to be a JSON number, but it is a JSON array.", Assert.IsType<AssertionFailedException>(numberEx).Message);
        Assert.Contains("to be a JSON boolean, but it is a JSON array.", Assert.IsType<AssertionFailedException>(booleanEx).Message);
        Assert.Contains("to be a JSON array, but it is a JSON object.", Assert.IsType<AssertionFailedException>(arrayEx).Message);
    }

    [Fact]
    public void JsonNode_BeJsonNull_PassesForNullNode()
    {
        // In the JsonNode model a JSON null literal is represented by a null node reference.
        JsonNode? node = null;
        node.Should().BeJsonNull();
    }

    [Fact]
    public void JsonNode_BeJsonNull_FailsForNonNullNode()
    {
        var node = JsonNode.Parse("1");

        var ex = Record.Exception(() => node.Should().BeJsonNull());

        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Expected node to be JSON null, but it is a JSON number.", failure.Message);
    }

    [Fact]
    public void JsonNode_ScalarValueChecks_Pass()
    {
        JsonNode.Parse("\"hello\"").Should().HaveStringValue("hello");
        JsonNode.Parse("1.50").Should().HaveNumberValue(1.5m);
        JsonNode.Parse("false").Should().HaveBooleanValue(false);
        JsonNode.Parse("[1,2]").Should().HaveArrayLength(2);
    }

    [Fact]
    public void JsonNode_ScalarValueChecks_Fail()
    {
        var stringEx = Record.Exception(() => JsonNode.Parse("\"hello\"").Should().HaveStringValue("world"));
        var numberEx = Record.Exception(() => JsonNode.Parse("2").Should().HaveNumberValue(3m));
        var booleanEx = Record.Exception(() => JsonNode.Parse("true").Should().HaveBooleanValue(false));
        var lengthEx = Record.Exception(() => JsonNode.Parse("[1,2]").Should().HaveArrayLength(3));

        Assert.Contains("but found \"hello\"", Assert.IsType<AssertionFailedException>(stringEx).Message);
        Assert.Contains("to have number value 3, but found 2.", Assert.IsType<AssertionFailedException>(numberEx).Message);
        Assert.Contains("to have boolean value false, but found true.", Assert.IsType<AssertionFailedException>(booleanEx).Message);
        Assert.Contains("to have array length 3, but found 2.", Assert.IsType<AssertionFailedException>(lengthEx).Message);
    }

    [Fact]
    public void JsonNode_BeJsonEquivalentTo_ThrowsArgumentExceptionForInvalidExpectedJson()
    {
        var node = JsonNode.Parse("1");

        var ex = Record.Exception(() => node.Should().BeJsonEquivalentTo("{oops"));

        Assert.IsType<ArgumentException>(ex);
    }

    #endregion
}
