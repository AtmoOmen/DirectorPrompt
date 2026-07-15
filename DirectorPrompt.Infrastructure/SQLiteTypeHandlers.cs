using System.Data;
using System.Text.Json;
using Dapper;
using DirectorPrompt.Domain;
using DirectorPrompt.Domain.Enums;

namespace DirectorPrompt.Infrastructure;

public static class SQLiteTypeHandlers
{
    public static void Register()
    {
        SqlMapper.AddTypeHandler(DateTimeHandler.Instance);
        SqlMapper.AddTypeHandler(new JsonArrayHandler<string>());
        SqlMapper.AddTypeHandler(new JsonArrayHandler<long>());

        RegisterEnumStringHandler<StateScope>();
        RegisterEnumStringHandler<StateValueType>();
        RegisterEnumStringHandler<Driver>();
        RegisterEnumStringHandler<CharacterStatus>();
        RegisterEnumStringHandler<SceneStatus>();
        RegisterEnumStringHandler<EventType>();
        RegisterEnumStringHandler<DirectiveType>();
        RegisterEnumStringHandler<StateChangeSource>();
        RegisterEnumStringHandler<RelationChangeSource>();

        SqlMapper.TypeMapProvider = type =>
        {
            if (type.GetConstructor(Type.EmptyTypes) is null)
                return new DefaultTypeMap(type);

            return new CustomPropertyTypeMap
            (
                type,
                (t, columnName) =>
                {
                    var normalizedColumn = columnName.Replace("_", "");

                    return t.GetProperties().FirstOrDefault
                    (p => p.Name.Replace("_", "").Equals
                     (
                         normalizedColumn,
                         StringComparison.OrdinalIgnoreCase
                     )
                    );
                }
            );
        };
    }

    private static void RegisterEnumStringHandler<T>() where T : struct, Enum =>
        SqlMapper.AddTypeHandler(new EnumStringHandler<T>());
}

sealed file class DateTimeHandler : SqlMapper.TypeHandler<DateTime>
{
    public static readonly DateTimeHandler Instance = new();

    public override DateTime Parse(object value) =>
        DateTime.Parse((string)value);

    public override void SetValue(IDbDataParameter parameter, DateTime value)
    {
        parameter.DbType = DbType.String;
        parameter.Value  = value.ToString("O");
    }
}

sealed file class JsonArrayHandler<T> : SqlMapper.TypeHandler<T[]>
{
    public override T[] Parse(object value)
    {
        if (value is null or DBNull)
            return [];

        var json = (string)value;

        if (string.IsNullOrWhiteSpace(json))
            return [];

        return JsonSerializer.Deserialize<T[]>(json, JsonOptions.Compact) ?? [];
    }

    public override void SetValue(IDbDataParameter parameter, T[]? value)
    {
        parameter.DbType = DbType.String;
        parameter.Value = value is null or { Length: 0 } ?
                              "[]" :
                              JsonSerializer.Serialize(value, JsonOptions.Compact);
    }
}

sealed file class EnumStringHandler<T> : SqlMapper.TypeHandler<T> where T : struct, Enum
{
    public override T Parse(object value)
    {
        if (value is null or DBNull)
            return default;

        if (value is T enumValue)
            return enumValue;

        var str = value.ToString()!;

        if (Enum.TryParse<T>(str, true, out var result))
            return result;

        var pascal = ToPascal(str);

        if (Enum.TryParse(pascal, true, out result))
            return result;

        return default;
    }

    public override void SetValue(IDbDataParameter parameter, T value)
    {
        parameter.DbType = DbType.String;
        parameter.Value  = value.ToString();
    }

    private static string ToPascal(string s) =>
        string.Concat
        (
            s.Split('_').Select
            (w => string.IsNullOrEmpty(w) ?
                      w :
                      char.ToUpperInvariant(w[0]) + w[1..]
            )
        );
}
