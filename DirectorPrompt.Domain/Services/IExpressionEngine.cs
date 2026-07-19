namespace DirectorPrompt.Domain.Services;

public interface IExpressionEngine
{
    bool Evaluate(string expression, string currentValue);

    float EvaluateNumeric(string expression, string currentValue);
}
