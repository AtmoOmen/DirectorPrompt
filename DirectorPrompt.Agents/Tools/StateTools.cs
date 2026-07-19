using System.Globalization;
using System.Text.Json;
using DirectorPrompt.Domain;
using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Repositories;
using Microsoft.Extensions.AI;
using Serilog;

namespace DirectorPrompt.Agents.Tools;

public sealed class StateTools
(
    IStateRepository stateRepository
)
{
    public IList<AIFunction> Create(ToolExecutionContext context) =>
    [
        AIFunctionFactory.Create
        (
            (string attribute) => GetStateAsync(context, attribute),
            "get_state",
            """
            查询特定状态属性当前值
            attribute: 属性名
            """
        ),
        AIFunctionFactory.Create
        (
            () => GetAllStateAsync(context),
            "get_all_state",
            "查询所有状态属性的当前值"
        ),
        AIFunctionFactory.Create
        (
            (string attribute, double delta, string reason) =>
                UpdateStateAsync(context, attribute, delta, reason),
            "update_state",
            """
            数值增减状态属性
            attribute: 属性名
            delta: 变化量 (正为增, 负为减)
            reason: 变更原因
            """
        ),
        AIFunctionFactory.Create
        (
            (string attribute, string value, string reason) =>
                SetStateAsync(context, attribute, value, reason),
            "set_state",
            """
            设置状态属性为指定值
            attribute: 属性名
            value: 新值
            reason: 变更原因
            """
        )
    ];

    private async Task<string> GetStateAsync(ToolExecutionContext context, string attribute)
    {
        var attributes = await stateRepository.GetAttributesAsync(context.ProjectID);
        var attr       = attributes.FirstOrDefault(a => a.Name == attribute);

        if (attr is null)
            return ToolResult.Error($"状态属性 {attribute} 不存在");

        var value = await stateRepository.GetStateValueAsync(attr.ID, context.SessionID);

        return ToolResult.Data
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

        return ToolResult.Data(result);
    }

    private async Task<string> UpdateStateAsync
    (
        ToolExecutionContext context,
        string               attribute,
        double               delta,
        string               reason
    )
    {
        Log.Information("工具调用: update_state(attribute={Attribute}, delta={Delta}, reason={Reason})", attribute, delta, reason);
        var attributes = await stateRepository.GetAttributesAsync(context.ProjectID);
        var attr       = attributes.FirstOrDefault(a => a.Name == attribute);

        if (attr is null)
            return ToolResult.Error($"状态属性 {attribute} 不存在");

        if (attr.Driver == Driver.System || attr.ValueType == StateValueType.Enum)
            return ToolResult.Error($"状态属性 {attribute} 为系统驱动或枚举类型, AI 不可直接修改");

        var currentValue = await stateRepository.GetStateValueAsync(attr.ID, context.SessionID);
        var currentNum   = double.Parse(currentValue?.Value ?? "0");
        var newValue     = currentNum + delta;

        await stateRepository.SetStateValueAsync
        (
            attr.ID,
            context.SessionID,
            newValue.ToString(CultureInfo.InvariantCulture),
            StateChangeSource.StateAgent,
            reason,
            context.SceneID ?? 0,
            context.RoundID
        );

        Log.Information
        (
            "工具调用完成: update_state, {OldValue} -> {NewValue}",
            currentValue?.Value,
            newValue.ToString(CultureInfo.InvariantCulture)
        );

        return ToolResult.Data
        (
            new
            {
                oldValue = currentValue?.Value,
                newValue = newValue.ToString(CultureInfo.InvariantCulture)
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
        Log.Information("工具调用: set_state(attribute={Attribute}, value={Value}, reason={Reason})", attribute, value, reason);
        var attributes = await stateRepository.GetAttributesAsync(context.ProjectID);
        var attr       = attributes.FirstOrDefault(a => a.Name == attribute);

        if (attr is null)
            return ToolResult.Error($"状态属性 {attribute} 不存在");

        if (attr.Driver == Driver.System)
            return ToolResult.Error($"状态属性 {attribute} 为系统驱动, AI 不可直接修改");

        var normalizedValue = value.Trim();

        if (attr.ValueType == StateValueType.Enum)
        {
            var config = string.IsNullOrWhiteSpace(attr.Config) ?
                             null :
                             JsonSerializer.Deserialize<StateAttributeConfig>(attr.Config, JsonOptions.Default);

            if (config?.Options is not { Count: > 0 } options || !options.Contains(normalizedValue, StringComparer.Ordinal))
                return ToolResult.Error($"状态属性 {attribute} 不包含枚举值 {normalizedValue}");
        }

        var oldValue = await stateRepository.GetStateValueAsync(attr.ID, context.SessionID);

        await stateRepository.SetStateValueAsync
        (
            attr.ID,
            context.SessionID,
            normalizedValue,
            StateChangeSource.StateAgent,
            reason,
            context.SceneID ?? 0,
            context.RoundID
        );

        return ToolResult.Data
        (
            new
            {
                oldValue = oldValue?.Value,
                newValue = normalizedValue
            }
        );
    }
}
