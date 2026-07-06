using System.Text.Json;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Repositories;
using Microsoft.Extensions.AI;

namespace DirectorPrompt.Agents.Tools;

public sealed class StateTools
{
    private readonly IStateRepository stateRepository;

    public StateTools(IStateRepository stateRepository) =>
        this.stateRepository = stateRepository;

    public IList<AIFunction> Create(ToolExecutionContext context) =>
    [
        AIFunctionFactory.Create
        (
            (string attribute) => GetStateAsync(context, attribute),
            "get_state",
            "查询单个状态属性的当前值。attribute: 属性名"
        ),
        AIFunctionFactory.Create
        (
            () => GetAllStateAsync(context),
            "get_all_state",
            "查询所有全局状态属性的当前值"
        ),
        AIFunctionFactory.Create
        (
            (string attribute) => GetCompositeItemsAsync(context, attribute),
            "get_composite_items",
            "查询复合类型状态属性的所有条目。attribute: 属性名"
        ),
        AIFunctionFactory.Create
        (
            (string attribute, double delta, string reason) =>
                UpdateStateAsync(context, attribute, delta, reason),
            "update_state",
            "数值增减状态属性。attribute: 属性名; delta: 变化量 (正为增, 负为减); reason: 变更原因"
        ),
        AIFunctionFactory.Create
        (
            (string attribute, string value, string reason) =>
                SetStateAsync(context, attribute, value, reason),
            "set_state",
            "设置状态属性为指定值。attribute: 属性名; value: 新值; reason: 变更原因"
        ),
    ];

    private async Task<string> GetStateAsync(ToolExecutionContext context, string attribute)
    {
        var attributes = await stateRepository.GetAttributesAsync(context.ProjectID);
        var attr       = attributes.FirstOrDefault(a => a.Name == attribute);

        if (attr is null)
            return JsonSerializer.Serialize(new { error = $"状态属性 {attribute} 不存在" });

        var value = await stateRepository.GetStateValueAsync(attr.ID, context.SessionID);

        return JsonSerializer.Serialize
        (
            new
            {
                attribute = attr.Name,
                value     = value?.Value,
                updatedAt = value?.UpdatedAt
            }
        );
    }

    private async Task<string> GetAllStateAsync(ToolExecutionContext context)
    {
        var attributes = await stateRepository.GetAttributesAsync(context.ProjectID, StateScope.Global);
        var result     = new List<object>();

        foreach (var attr in attributes)
        {
            var value = await stateRepository.GetStateValueAsync(attr.ID, context.SessionID);
            result.Add
            (
                new
                {
                    attribute = attr.Name,
                    value     = value?.Value ?? string.Empty
                }
            );
        }

        return JsonSerializer.Serialize(result);
    }

    private async Task<string> GetCompositeItemsAsync(ToolExecutionContext context, string attribute)
    {
        var attributes = await stateRepository.GetAttributesAsync(context.ProjectID);
        var attr       = attributes.FirstOrDefault(a => a.Name == attribute);

        if (attr is null)
            return JsonSerializer.Serialize(new { error = $"状态属性 {attribute} 不存在" });

        var items = await stateRepository.GetCompositeItemsAsync(attr.ID, context.SessionID);
        var result = items.Select
        (i => new
            {
                id          = i.ID,
                description = i.Description,
                current     = i.Current,
                target      = i.Target,
                status      = i.Status.ToString()
            }
        );

        return JsonSerializer.Serialize(result);
    }

    private async Task<string> UpdateStateAsync
    (
        ToolExecutionContext context,
        string               attribute,
        double               delta,
        string               reason
    )
    {
        var attributes = await stateRepository.GetAttributesAsync(context.ProjectID);
        var attr       = attributes.FirstOrDefault(a => a.Name == attribute);

        if (attr is null)
            return JsonSerializer.Serialize(new { error = $"状态属性 {attribute} 不存在" });

        var currentValue = await stateRepository.GetStateValueAsync(attr.ID, context.SessionID);
        var currentNum   = double.Parse(currentValue?.Value ?? "0");
        var newValue     = currentNum + delta;

        await stateRepository.SetStateValueAsync
        (
            attr.ID,
            context.SessionID,
            newValue.ToString(),
            StateChangeSource.StateAgent,
            reason,
            context.SceneID ?? 0,
            context.RoundID
        );

        return JsonSerializer.Serialize
        (
            new
            {
                oldValue = currentValue?.Value,
                newValue = newValue.ToString()
            }
        );
    }

    private async Task<string> SetStateAsync
    (
        ToolExecutionContext context,
        string               attribute,
        string               value,
        string               reason
    )
    {
        var attributes = await stateRepository.GetAttributesAsync(context.ProjectID);
        var attr       = attributes.FirstOrDefault(a => a.Name == attribute);

        if (attr is null)
            return JsonSerializer.Serialize(new { error = $"状态属性 {attribute} 不存在" });

        var oldValue = await stateRepository.GetStateValueAsync(attr.ID, context.SessionID);

        await stateRepository.SetStateValueAsync
        (
            attr.ID,
            context.SessionID,
            value,
            StateChangeSource.StateAgent,
            reason,
            context.SceneID ?? 0,
            context.RoundID
        );

        return JsonSerializer.Serialize
        (
            new
            {
                oldValue = oldValue?.Value,
                newValue = value
            }
        );
    }

}
