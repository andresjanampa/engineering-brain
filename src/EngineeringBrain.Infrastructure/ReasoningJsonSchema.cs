using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using EngineeringBrain.Core;

namespace EngineeringBrain.Infrastructure;

public static class ReasoningJsonSchema
{
    private static readonly NullabilityInfoContext Nullability = new();

    public static BinaryData For<T>()
    {
        if (typeof(T) != typeof(InitiativeUnderstanding) && typeof(T) != typeof(InitiativeAnalysis))
        {
            throw new NotSupportedException($"No reasoning schema is registered for {typeof(T).Name}.");
        }

        var schema = Build(typeof(T), nullable: false);
        return BinaryData.FromString(schema.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
    }

    private static JsonNode Build(Type type, bool nullable)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null)
        {
            return AnyOf(Build(underlying, nullable: false), new JsonObject { ["type"] = "null" });
        }

        JsonNode schema;
        if (type == typeof(string))
        {
            schema = new JsonObject { ["type"] = "string" };
        }
        else if (type == typeof(int) || type == typeof(long))
        {
            schema = new JsonObject { ["type"] = "integer" };
        }
        else if (type == typeof(bool))
        {
            schema = new JsonObject { ["type"] = "boolean" };
        }
        else if (type.IsEnum)
        {
            schema = new JsonObject
            {
                ["type"] = "string",
                ["enum"] = new JsonArray(Enum.GetNames(type)
                    .Select(name => (JsonNode?)JsonValue.Create(JsonNamingPolicy.CamelCase.ConvertName(name)))
                    .ToArray())
            };
        }
        else if (TryGetElementType(type, out var elementType))
        {
            schema = new JsonObject
            {
                ["type"] = "array",
                ["items"] = Build(elementType, nullable: false)
            };
        }
        else
        {
            var properties = new JsonObject();
            var required = new JsonArray();
            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                         .OrderBy(property => property.MetadataToken))
            {
                var name = JsonNamingPolicy.CamelCase.ConvertName(property.Name);
                var isNullable = Nullability.Create(property).ReadState == NullabilityState.Nullable;
                properties[name] = Build(property.PropertyType, isNullable);
                required.Add(name);
            }

            schema = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = required,
                ["additionalProperties"] = false
            };
        }

        return nullable ? AnyOf(schema, new JsonObject { ["type"] = "null" }) : schema;
    }

    private static JsonObject AnyOf(params JsonNode[] nodes) => new()
    {
        ["anyOf"] = new JsonArray(nodes)
    };

    private static bool TryGetElementType(Type type, out Type elementType)
    {
        if (type.IsArray)
        {
            elementType = type.GetElementType()!;
            return true;
        }

        var enumerable = type.GetInterfaces()
            .Concat([type])
            .FirstOrDefault(candidate => candidate.IsGenericType
                && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        if (enumerable is not null)
        {
            elementType = enumerable.GetGenericArguments()[0];
            return true;
        }

        elementType = null!;
        return false;
    }
}
