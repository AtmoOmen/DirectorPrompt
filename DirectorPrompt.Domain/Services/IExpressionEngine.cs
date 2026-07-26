namespace DirectorPrompt.Domain.Services;

public interface IExpressionEngine
{
    bool Evaluate(string expression, string currentValue);

    bool Evaluate(string expression, IReadOnlyDictionary<string, object?> parameters);

    float EvaluateNumeric(string expression, string currentValue);

    float EvaluateNumeric(string expression, IReadOnlyDictionary<string, object?> parameters);
}
