using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;
using Microsoft.Extensions.AI;

namespace DirectorPrompt.Agents.Tools;

public sealed class AuditTools
{
    private readonly List<Violation> violations = [];

    public IReadOnlyList<Violation> Violations => violations;

    public IList<AIFunction> Create() =>
    [
        AIFunctionFactory.Create
        (
            AddViolation,
            "add_violation",
            "报告审计问题。type: 问题类型 (setting/state/character/time/memory); description: 问题描述; severity: 严重程度 (unacceptable/severe/general); suggestion: 可选, 修改建议"
        )
    ];

    private Task<string> AddViolation(string type, string description, string severity, string? suggestion)
    {
        var parsedSeverity = severity.ToLowerInvariant() switch
        {
            "unacceptable" => AuditSeverity.Unacceptable,
            "severe"       => AuditSeverity.Severe,
            _              => AuditSeverity.General
        };

        violations.Add
        (
            new Violation
            {
                Type        = type,
                Description = description,
                Severity    = parsedSeverity,
                Suggestion  = suggestion
            }
        );

        return Task.FromResult($"{{\"added\": true, \"severity\": \"{severity}\"}}");
    }
}
