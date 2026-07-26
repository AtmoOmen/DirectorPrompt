using DirectorPrompt.Agents;
using DirectorPrompt.Localization;

namespace DirectorPrompt.ViewModels;

internal static class StateRuleExpressionValidator
{
    private static readonly ExpressionEngine expressionEngine = new();

    public static (bool IsValid, string Message) ValidateNumeric
    (
        string                   expression,
        IReadOnlyList<string> references
    ) =>
        Validate
        (
            expression,
            references,
            parameters => expressionEngine.EvaluateNumeric(expression, parameters)
        );

    public static (bool IsValid, string Message) ValidateBoolean
    (
        string                   expression,
        IReadOnlyList<string> references
    ) =>
        Validate
        (
            expression,
            references,
            parameters => expressionEngine.Evaluate(expression, parameters)
        );

    private static (bool IsValid, string Message) Validate
    (
        string                                        expression,
        IReadOnlyList<string>                         references,
        Func<IReadOnlyDictionary<string, object?>, object> evaluate
    )
    {
        if (string.IsNullOrWhiteSpace(expression))
            return (false, Loc.Get("State.Rule.ExpressionEmpty"));

        var parameters = references.Distinct().ToDictionary(reference => reference, _ => (object?)1d);

        try
        {
            evaluate(parameters);
            return (true, Loc.Get("State.Rule.ExpressionValid"));
        }
        catch (ArgumentException exception)
        {
            var message = exception.Message.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)[0];
            return (false, Loc.Get("State.Rule.ExpressionInvalid", message));
        }
        catch (Exception)
        {
            return (false, Loc.Get("State.Rule.ExpressionSyntaxInvalid"));
        }
    }
}
