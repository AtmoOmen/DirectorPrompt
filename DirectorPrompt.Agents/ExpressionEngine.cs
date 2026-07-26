using System.Globalization;
using System.Text.RegularExpressions;
using DirectorPrompt.Domain.Services;
using NCalc;

namespace DirectorPrompt.Agents;

public sealed partial class ExpressionEngine : IExpressionEngine
{
    public bool Evaluate(string expression, string currentValue)
    {
        var result = EvaluateExpression(expression, currentValue);

        return result is bool value ?
                   value :
                   throw new ArgumentException("表达式结果必须为布尔值", nameof(expression));
    }

    public bool Evaluate(string expression, IReadOnlyDictionary<string, object?> parameters)
    {
        var result = EvaluateExpression(expression, parameters);

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

    public float EvaluateNumeric(string expression, IReadOnlyDictionary<string, object?> parameters)
    {
        var assignment = FindAssignment(expression);

        if (assignment is null)
            return ToFiniteFloat(EvaluateExpression(expression, parameters), expression);

        var (index, operation) = assignment.Value;
        var left = expression[..(index - 1)].Trim();

        if (left != "{val}")
            throw new ArgumentException("赋值表达式左侧必须为 {val}", nameof(expression));

        if (!parameters.TryGetValue("val", out var currentValue))
            throw new ArgumentException("表达式上下文缺少 val", nameof(parameters));

        var current = ToFiniteFloat(currentValue, expression);
        var right   = ToFiniteFloat(EvaluateExpression(expression[(index + 1)..], parameters), expression);

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
        return EvaluateExpression
        (
            expression,
            new Dictionary<string, object?>
            {
                ["val"] = currentValue
            }
        );
    }

    private static object? EvaluateExpression
    (
        string                              expression,
        IReadOnlyDictionary<string, object?> parameters
    )
    {
        if (string.IsNullOrWhiteSpace(expression))
            throw new ArgumentException("表达式不能为空", nameof(expression));

        var normalizedExpression = ReferencePattern().Replace
        (
            expression,
            match =>
            {
                var name = match.Groups[1].Value;

                if (!parameters.ContainsKey(name))
                    throw new ArgumentException($"未找到状态属性引用: {{{name}}}", nameof(expression));

                return $"[{name}]";
            }
        ).Replace(" AND ", " && ")
         .Replace(" OR ",  " || ");

        var evaluator = new Expression(normalizedExpression);

        foreach (var (name, value) in parameters)
            evaluator.Parameters[name] = value;

        return evaluator.Evaluate();
    }

    [GeneratedRegex(@"\{([^{}]+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex ReferencePattern();

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
