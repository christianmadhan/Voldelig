using Newtonsoft.Json.Converters;
using Newtonsoft.Json;
using System.Reflection;
using VoldeligClient;
using static VoldeligClient.Voldelig;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Globalization;


public class VoldeligHttpResponseMessage
{
    public  HttpResponseMessage HttpResponseMessage { get; set; }
    public string MaconomyErrorMessage { get; set; }
    public HttpStatusCode MaconomyErrorStatusCode { get; set; }
} 
public static class Helper
{
    public static string MakeEntityIntoPayload<T>(T entity) where T : class
    {
        Dictionary<string, string> propertyDict = new Dictionary<string, string>();
        foreach (var prop in typeof(T).GetProperties())
        {
            // Skip properties with JsonIgnore attribute
            if (prop.GetCustomAttribute<System.Text.Json.Serialization.JsonIgnoreAttribute>() != null)
                continue;

            var value = prop.GetValue(entity);

            // Skip null values
            if (value == null) continue;

            // Skip DateTime with MinValue
            if (prop.PropertyType == typeof(DateTime) && (DateTime)value == DateTime.MinValue)
                continue;

            // Skip enum with first (default) value
            if (prop.PropertyType.IsEnum)
            {
                var firstEnumValue = Enum.GetValues(prop.PropertyType).GetValue(0);
                if (value.Equals(firstEnumValue))
                    continue;
            }

            // Convert first character of property name to lowercase
            string propertyName = char.ToLowerInvariant(prop.Name[0]) + prop.Name.Substring(1);

            // Convert value to string
            string stringValue = ConvertToString(value);

            propertyDict.Add(propertyName, stringValue);
        }
        var payload = new { data = propertyDict };
        return JsonConvert.SerializeObject(payload);
    }

    private static string ConvertToString(object value)
    {
        // Handle different types of values
        switch (value)
        {
            case Enum enumValue:
                // Convert enum to its string representation
                return enumValue.ToString().ToLower();

            case DateTime dateTime:
                // Convert DateTime to yyyy-MM-dd format
                return dateTime.ToString("yyyy-MM-dd");

            case bool boolValue:
                // Convert boolean to lowercase string
                return boolValue.ToString().ToLowerInvariant();

            case decimal decimalValue:
                // Ensure consistent decimal formatting
                return decimalValue.ToString(CultureInfo.InvariantCulture);

            case double doubleValue:
                // Ensure consistent double formatting
                return doubleValue.ToString(CultureInfo.InvariantCulture);

            case float floatValue:
                // Ensure consistent float formatting
                return floatValue.ToString(CultureInfo.InvariantCulture);

            default:
                // For other types, use ToString()
                return value?.ToString() ?? string.Empty;
        }
    }

    public static async Task<Either<List<T>, VoldeligHttpResponseMessage>> EnsureReconnectTokenFilter<T>(HttpResponseMessage response, Voldelig client)
    {
        if (!response.IsSuccessStatusCode)
        {
            VoldeligHttpResponseMessage voldeligResponse = new();
            string content = await response.Content.ReadAsStringAsync();
            JObject json = JObject.Parse(content);
            if (json.TryGetValue("errorMessage", out var error))
            {
                voldeligResponse.MaconomyErrorMessage = error.ToString();
            }
            voldeligResponse.MaconomyErrorStatusCode = response.StatusCode;
            voldeligResponse.HttpResponseMessage = response;
            return voldeligResponse; // Return Right (failure case)
        }

        // Handle "Maconomy-Reconnect" header
        if (response.Headers.TryGetValues("Maconomy-Reconnect", out var token))
        {
            client.reconnectToken = token.First();
            client.httpClient.DefaultRequestHeaders.Remove("Authorization");
            client.httpClient.DefaultRequestHeaders.Add("Authorization", $"X-Reconnect {client.reconnectToken}");
        }

        // Handle "Content-Type" header (only set if empty)
        if (string.IsNullOrEmpty(client.containerContentType) &&
            response.Content.Headers.TryGetValues("Content-Type", out var type))
        {
            client.containerContentType = type.FirstOrDefault();
        }

        // Handle "Maconomy-Concurrency-Control" header
        if (response.Headers.TryGetValues("Maconomy-Concurrency-Control", out var concurrency))
        {
            client.concurrencyControl = concurrency.FirstOrDefault();
        }

        // Return success (Left) with default(T) since we don't modify T
        return default(List<T>);
    }
    public static async Task<Either<T, VoldeligHttpResponseMessage>> EnsureReconnectToken<T>(HttpResponseMessage response, Voldelig client)
    {
        if (!response.IsSuccessStatusCode)
        {
            VoldeligHttpResponseMessage voldeligResponse = new();
            string content = await response.Content.ReadAsStringAsync();
            JObject json = JObject.Parse(content);
            if(json.TryGetValue("errorMessage", out var error))
            {
                voldeligResponse.MaconomyErrorMessage = error.ToString();
            }
            voldeligResponse.MaconomyErrorStatusCode = response.StatusCode;
            voldeligResponse.HttpResponseMessage = response;
            return voldeligResponse; // Return Right (failure case)
        }

        // Handle "Maconomy-Reconnect" header
        if (response.Headers.TryGetValues("Maconomy-Reconnect", out var token))
        {
            client.reconnectToken = token.First();
            client.httpClient.DefaultRequestHeaders.Remove("Authorization");
            client.httpClient.DefaultRequestHeaders.Add("Authorization", $"X-Reconnect {client.reconnectToken}");
        }

        // Handle "Content-Type" header (only set if empty)
        if (string.IsNullOrEmpty(client.containerContentType) &&
            response.Content.Headers.TryGetValues("Content-Type", out var type))
        {
            client.containerContentType = type.FirstOrDefault();
        }

        // Handle "Maconomy-Concurrency-Control" header
        if (response.Headers.TryGetValues("Maconomy-Concurrency-Control", out var concurrency))
        {
            client.concurrencyControl = concurrency.FirstOrDefault();
        }

        // Return success (Left) with default(T) since we don't modify T
        return default(T);
    }


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