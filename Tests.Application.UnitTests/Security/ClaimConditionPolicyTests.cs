using System.Text.Json;
using Core.Application.DTOs;
using Core.Application.Security;
using Xunit;

namespace Tests.Application.UnitTests.Security;

public sealed class ClaimConditionPolicyTests
{
    private static readonly Dictionary<string, string> Schema = new() { ["is_student"] = "Boolean", ["student_id"] = "String" };

    [Theory]
    [InlineData("String", "\"graduate student\"", "student", true)]
    [InlineData("String", "\"Student\"", "student", false)]
    [InlineData("StringArray", "[\"student\",\"staff\"]", "student", true)]
    [InlineData("StringArray", "[\"student\"]", "stud", false)]
    [InlineData("StringArray", "[]", "student", false)]
    [InlineData("StringArray", "[\"\",\" Student \" ]", "", true)]
    [InlineData("StringArray", "[\" Student \" ]", "Student", false)]
    public void Contains_ShouldUseOrdinalSubstringOrExactArrayMember(string type, string json, string literal, bool expected)
    {
        var schema = new Dictionary<string, string> { ["property"] = type };
        var rule = new ClaimCondition { Operator = "Contains", Property = "property", Value = JsonSerializer.SerializeToElement(literal) };
        Assert.Equal(expected, ClaimConditionPolicy.Evaluate(rule, schema,
            new Dictionary<string, JsonElement> { ["property"] = JsonSerializer.Deserialize<JsonElement>(json) }));
        Assert.Null(ClaimConditionPolicy.Evaluate(rule, schema, new Dictionary<string, JsonElement>()));
    }

    [Theory]
    [InlineData("Boolean", "true", "false", true)]
    [InlineData("Boolean", "false", "false", false)]
    [InlineData("String", "\"student\"", "\"Student\"", true)]
    [InlineData("String", "\"student\"", "\"student\"", false)]
    public void NotEquals_ShouldCompareKnownTypedScalarsAndOmitUnknown(string type, string actual, string literal, bool expected)
    {
        var rule = new ClaimCondition { Operator = "NotEquals", Property = "property", Value = JsonSerializer.Deserialize<JsonElement>(literal) };
        var schema = new Dictionary<string, string> { ["property"] = type };
        Assert.Equal(expected, ClaimConditionPolicy.Evaluate(rule, schema,
            new Dictionary<string, JsonElement> { ["property"] = JsonSerializer.Deserialize<JsonElement>(actual) }));
        Assert.Null(ClaimConditionPolicy.Evaluate(rule, schema, new Dictionary<string, JsonElement>()));
    }

    [Fact]
    public void NewOperators_ShouldRejectUnsupportedTypesAndPreserveUnknownInNestedGroups()
    {
        var schema = new Dictionary<string, string> { ["flag"] = "Boolean", ["categories"] = "StringArray", ["code"] = "String" };
        var contains = new ClaimCondition { Operator = "Contains", Property = "categories", Value = JsonSerializer.SerializeToElement("student") };
        Assert.False(ClaimConditionPolicy.IsValid(contains with { Operator = "Equals" }, schema));
        Assert.False(ClaimConditionPolicy.IsValid(contains with { Operator = "NotEquals" }, schema));
        Assert.False(ClaimConditionPolicy.IsValid(contains with { Property = "flag" }, schema));
        Assert.False(ClaimConditionPolicy.IsValid(contains with { Property = "code", Value = JsonSerializer.SerializeToElement("") }, schema));
        var rule = new ClaimCondition { Operator = "Any", Children = [
            new() { Operator = "Equals", Property = "flag", Value = JsonSerializer.SerializeToElement(true) },
            new() { Operator = "All", Children = [contains] }] };
        Assert.Null(ClaimConditionPolicy.Evaluate(rule, schema, new Dictionary<string, JsonElement> { ["flag"] = JsonSerializer.SerializeToElement(true) }));
        Assert.Null(ClaimConditionPolicy.Evaluate(contains, schema, new Dictionary<string, JsonElement> { ["categories"] = JsonSerializer.SerializeToElement(new object[] { "student", 1 }) }));
    }
    private static ClaimCondition Rule(string operation = "All") => new()
    {
        Operator = operation, Children = [
            new() { Operator = "Equals", Property = "is_student", Value = JsonSerializer.SerializeToElement(true) },
            new() { Operator = "StartsWith", Property = "student_id", Value = JsonSerializer.SerializeToElement("4") }]
    };

    [Theory]
    [InlineData(true, "4123", true)]
    [InlineData(false, "4123", false)]
    [InlineData(true, "3123", false)]
    public void Evaluate_ShouldUseTypedInputsAndOrdinalPrefix(bool student, string id, bool expected)
    {
        var properties = new Dictionary<string, JsonElement>
        {
            ["is_student"] = JsonSerializer.SerializeToElement(student), ["student_id"] = JsonSerializer.SerializeToElement(id)
        };
        Assert.Equal(expected, ClaimConditionPolicy.Evaluate(Rule(), Schema, properties));
    }

    [Theory]
    [InlineData("All")]
    [InlineData("Any")]
    public void Evaluate_ShouldBeUnknownWhenAnyInputIsMissingDespiteShortCircuit(string operation) =>
        Assert.Null(ClaimConditionPolicy.Evaluate(Rule(operation), Schema,
            new Dictionary<string, JsonElement> { ["is_student"] = JsonSerializer.SerializeToElement(true) }));

    [Fact]
    public void Validate_ShouldRejectWrongTypesUnapprovedFieldsAndExecutableOperators()
    {
        var rule = Rule().Children![0];
        Assert.False(ClaimConditionPolicy.IsValid(rule with { Property = "role" }, Schema));
        Assert.False(ClaimConditionPolicy.IsValid(rule with { Value = JsonSerializer.SerializeToElement("true") }, Schema));
        Assert.False(ClaimConditionPolicy.IsValid(rule with { Operator = "Regex" }, Schema));
        var deep = rule;
        for (var i = 0; i < 4; i++) deep = new() { Operator = "All", Children = [deep] };
        Assert.False(ClaimConditionPolicy.IsValid(deep, Schema));
    }
}
