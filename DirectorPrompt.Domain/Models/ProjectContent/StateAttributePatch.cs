using DirectorPrompt.Domain.Enums;

namespace DirectorPrompt.Domain.Models;

public sealed class StateAttributePatch
{
    public string? Name { get; set; }

    public string? DisplayName { get; set; }

    public StateScope? Scope { get; set; }

    public long? CategoryID { get; set; }

    public StateValueType? ValueType { get; set; }

    public Driver? Driver { get; set; }

    public NumericStateDefinition? Numeric { get; set; }

    public EnumStateDefinition? Enumeration { get; set; }

    public List<PhaseDefinition>? Phases { get; set; }
}
