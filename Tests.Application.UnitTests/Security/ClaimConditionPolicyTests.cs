using System.Text.Json;
using Core.Application.DTOs;
using Core.Application.Security;
using Xunit;

namespace Tests.Application.UnitTests.Security;

public sealed class ClaimConditionPolicyTests
{
    private static readonly Dictionary<string, string> Schema = new() { ["is_student"] = "Boolean", ["student_id"] = "String" };
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
