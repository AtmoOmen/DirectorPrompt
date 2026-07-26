using System.Globalization;
using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Services;

namespace DirectorPrompt.Agents;

public sealed class StateRuleEvaluator
(
    IExpressionEngine expressionEngine
)
{
    public bool Matches
    (
        IReadOnlyList<StateRuleConditionConfig> conditions,
        StateRuleConditionMatch match,
        string currentValue,
        IReadOnlyDictionary<string, string> globalValues,
        IReadOnlyDictionary<string, string> characterValues,
        StateRuleEvent stateEvent
    )
    {
        if (conditions.Count == 0)
            return true;

        var results = conditions.Select
        (
            condition => EvaluateCondition
            (
                condition,
                currentValue,
                globalValues,
                characterValues,
                stateEvent
            )
        );

        return match == StateRuleConditionMatch.Any ?
                   results.Any(value => value) :
                   results.All(value => value);
    }

    public float EvaluateNumericValue
    (
        string                          expression,
        float                           currentValue,
        IReadOnlyDictionary<string, string> globalValues,
        IReadOnlyDictionary<string, string> characterValues
    )
    {
        if (float.TryParse(expression, NumberStyles.Float, CultureInfo.InvariantCulture, out var literal))
            return literal;

        return expressionEngine.EvaluateNumeric
        (
            expression,
            CreateParameters(currentValue, globalValues, characterValues)
        );
    }

    private bool EvaluateCondition
    (
        StateRuleConditionConfig       condition,
        string                         currentValue,
        IReadOnlyDictionary<string, string> globalValues,
        IReadOnlyDictionary<string, string> characterValues,
        StateRuleEvent                 stateEvent
    )
    {
        var actual = condition.Source switch
        {
            StateRuleConditionSource.CurrentValue => currentValue,
            StateRuleConditionSource.GlobalState => globalValues.GetValueOrDefault(condition.StateName ?? string.Empty),
            StateRuleConditionSource.CharacterState => characterValues.GetValueOrDefault(condition.StateName ?? string.Empty),
            StateRuleConditionSource.InputContent => stateEvent.InputContent ?? string.Empty,
            StateRuleConditionSource.InputType => stateEvent.InputType?.ToString() ?? string.Empty,
            _ => string.Empty
        };

        var expected = condition.ExpectedValue.Trim();

        if (condition.Comparison == StateRuleComparison.Expression)
        {
            try
            {
                return expressionEngine.Evaluate
                (
                    expected,
                    CreateParameters(ParseValue(actual), globalValues, characterValues)
                );
            }
            catch (Exception)
            {
                return false;
            }
        }

        return Compare(actual.Trim(), condition.Comparison, expected);
    }

    private static bool Compare(string actual, StateRuleComparison comparison, string expected)
    {
        if (comparison == StateRuleComparison.Contains)
            return actual.Contains(expected, StringComparison.OrdinalIgnoreCase);

        if (comparison == StateRuleComparison.Equals)
            return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

        if (comparison == StateRuleComparison.NotEquals)
            return !string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

        if (!double.TryParse(actual, NumberStyles.Float, CultureInfo.InvariantCulture, out var actualNumber) ||
            !double.TryParse(expected, NumberStyles.Float, CultureInfo.InvariantCulture, out var expectedNumber))
            return false;

        return comparison switch
        {
            StateRuleComparison.GreaterThan => actualNumber > expectedNumber,
            StateRuleComparison.GreaterThanOrEqual => actualNumber >= expectedNumber,
            StateRuleComparison.LessThan => actualNumber < expectedNumber,
            StateRuleComparison.LessThanOrEqual => actualNumber <= expectedNumber,
            _ => false
        };
    }

    private static Dictionary<string, object?> CreateParameters
    (
        object                              currentValue,
        IReadOnlyDictionary<string, string> globalValues,
        IReadOnlyDictionary<string, string> characterValues
    )
    {
        var parameters = new Dictionary<string, object?>
        {
            ["val"] = currentValue
        };

        foreach (var (name, value) in globalValues)
            parameters[$"global.{name}"] = ParseValue(value);

        foreach (var (name, value) in characterValues)
            parameters[name] = ParseValue(value);

        return parameters;
    }

    private static object ParseValue(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ?
            number :
            value;
}
