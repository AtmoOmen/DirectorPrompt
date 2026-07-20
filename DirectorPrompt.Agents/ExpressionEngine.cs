using System.Globalization;
using DirectorPrompt.Domain.Services;
using NCalc;

namespace DirectorPrompt.Agents;

public sealed class ExpressionEngine : IExpressionEngine
{
    public bool Evaluate(string expression, string currentValue)
    {
        var result = EvaluateExpression(expression, currentValue);

        return result is bool value ?
                   value :
                   throw new ArgumentException("表达式结果必须为布尔值", nameof(expression));
    }

    public float EvaluateNumeric(string expression, string currentValue)
    {
        if (!float.TryParse(currentValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var current))
            throw new ArgumentException($"无法将 '{currentValue}' 转换为数值", nameof(currentValue));

        var assignment = FindAssignment(expression);

        if (assignment is null)
            return ToFiniteFloat(EvaluateExpression(expression, current), expression);

        var (index, operation) = assignment.Value;
        var left = expression[..(index - 1)].Trim();

        if (left != "{val}")
            throw new ArgumentException("赋值表达式左侧必须为 {val}", nameof(expression));

        var right = ToFiniteFloat(EvaluateExpression(expression[(index + 1)..], current), expression);

        return operation switch
        {
            '+' => ToFiniteFloat(current + right, expression),
            '-' => ToFiniteFloat(current - right, expression),
            '*' => ToFiniteFloat(current * right, expression),
            '/' => ToFiniteFloat(current / right, expression),
            '%' => ToFiniteFloat(current % right, expression),
            _   => throw new ArgumentOutOfRangeException(nameof(expression))
        };
    }

    private static object? EvaluateExpression(string expression, object currentValue)
    {
        var evaluator = new Expression
        (
            expression.Replace("{val}", "[val]")
                      .Replace(" AND ", " && ")
                      .Replace(" OR ",  " || ")
        );
        evaluator.Parameters["val"] = currentValue;

        return evaluator.Evaluate();
    }

    private static (int Index, char Operation)? FindAssignment(string expression)
    {
        for (var i = 1; i < expression.Length; i++)
            if (expression[i] == '=' && expression[i - 1] is '+' or '-' or '*' or '/' or '%')
                return (i, expression[i - 1]);

        return null;
    }

    private static float ToFiniteFloat(object? value, string expression)
    {
        var result = Convert.ToSingle(value, CultureInfo.InvariantCulture);

        if (!float.IsFinite(result))
            throw new ArgumentException("数值表达式结果必须是有限数", nameof(expression));

        return result;
    }
}
