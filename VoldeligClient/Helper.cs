using Newtonsoft.Json.Converters;
using Newtonsoft.Json;
using System.Reflection;
using VoldeligClient;
using static VoldeligClient.Voldelig;

public static class Helper
{

    public static string GetActionKey(ActionType action)
    {
        return action.ToString();
    }
    public class SafeEnumConverter<T> : StringEnumConverter where T : struct
    {
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.String)
            {
                string enumText = reader.Value.ToString();
                if (Enum.TryParse(enumText, true, out T result)) // Case-insensitive parsing
                {
                    return result;
                }
            }
            return default(T); // Default value if invalid (e.g., DENMARK if T is CountryType)
        }
    }


    public static PropertyInfo GetKeyProperty<T>() where T : class
    {
        Type type = typeof(T);

        // First try to find a property with the KeyField attribute
        var keyProperty = type.GetProperties()
            .FirstOrDefault(p => p.GetCustomAttributes(typeof(KeyFieldAttribute), true).Any());

        if (keyProperty != null)
            return keyProperty;

        // Then try common naming conventions for key fields
        var conventionNames = new[]
        {
            type.Name + "Number",
            type.Name + "Id",
            "Id",
            "Number"
        };

        foreach (var name in conventionNames)
        {
            keyProperty = type.GetProperties()
                .FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (keyProperty != null)
                return keyProperty;
        }

        return null;
    }
}