using System;
using System.Linq.Expressions;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VoldeligClient;

public static class VoldeligExtention
{

    public static Either<List<T>, HttpResponseMessage> EnsureReconnectTokenFilter<T>(HttpResponseMessage response, Voldelig client)
    {
        if (!response.IsSuccessStatusCode)
        {
            return response; // Return Right (failure case)
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
    public static Either<T, HttpResponseMessage> EnsureReconnectToken<T>(HttpResponseMessage response, Voldelig client)
    {
        if (!response.IsSuccessStatusCode)
        {
            return response; // Return Right (failure case)
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

    public static async Task<Either<List<T>, HttpResponseMessage>> Filter<T>(this Task<Voldelig> authTask, Expression<Func<T, bool>>? expr, int limit = 25)
    {
        var client = await authTask;
        string entityName = typeof(T).Name.ToLower();
        Type type = typeof(T);
        MethodInfo method = typeof(T).GetMethod("FilterJObject");
        HttpResponseMessage response;
        string exprAsString = expr == null ? "" : ExpressionHelper.ExpressionToFilterString(expr);
        if (method != null && client.maconomyversion == 2)
        {
            object instance = Activator.CreateInstance(type);
            var filterUrl = $"{client.baseUrl}/containers/{client.shortname}/{entityName}/filter";
            object jsonBody = method.Invoke(instance, new object[] { exprAsString, limit });
            response = await client.PostV2Async(filterUrl, (string)jsonBody);
            var ensureFilter = EnsureReconnectTokenFilter<List<T>>(response, client);

            return await ensureFilter.Match<Task<Either<List<T>, HttpResponseMessage>>>(
                async _ =>
                {
                    string responseContent = await response.Content.ReadAsStringAsync();
                    JObject filterJson = JObject.Parse(responseContent);

                    // Ensure the token exists and is an array
                    JToken recordsToken = filterJson.SelectToken("panes.filter.records");
                    if (recordsToken == null || recordsToken.Type != JTokenType.Array)
                    {
                        return new List<T>();
                    }

                    JArray records = (JArray)recordsToken;

                    // Deserialize directly into a List<T>
                    List<T> filterList = records
                        .Select(record => record.SelectToken("data").ToObject<T>())
                        .Where(item => item != null) // Filter out null items
                        .ToList();

                    return filterList;
                },
                responseFilterx => Task.FromResult<Either<List<T>, HttpResponseMessage>>(responseFilterx)
            );

        }
        else
        {
            string fieldNames = string.Join(",", typeof(T).GetProperties().Select(p => p.Name.ToLower()));
            var filterUrl = "";
            if(exprAsString.Length > 0)
            {
                filterUrl = $"{client.baseUrl}/containers/v1/{client.shortname}/{entityName}/filter?restriction={exprAsString}&fields={fieldNames}&limit={limit}";
            } else
            {
                filterUrl = $"{client.baseUrl}/containers/v1/{client.shortname}/{entityName}/filter?&fields={fieldNames}&limit={limit}";
            }
            response = await client.GetAsync(filterUrl);
            var ensureFilter = EnsureReconnectTokenFilter<List<T>>(response, client);
            return await ensureFilter.Match<Task<Either<List<T>, HttpResponseMessage>>>(
               async _ =>
               {
                   string responseContent = await response.Content.ReadAsStringAsync();
                   JObject filterJson = JObject.Parse(responseContent);

                   // Ensure the token exists and is an array
                   JToken recordsToken = filterJson.SelectToken("panes.filter.records");
                   if (recordsToken == null || recordsToken.Type != JTokenType.Array)
                   {
                       return new List<T>();
                   }

                   JArray records = (JArray)recordsToken;

                   // Deserialize directly into a List<T>
                   List<T> filterList = records
                       .Select(record => record.SelectToken("data").ToObject<T>())
                       .Where(item => item != null) // Filter out null items
                       .ToList();

                   return filterList;
               },
               responseFilterx => Task.FromResult<Either<List<T>, HttpResponseMessage>>(responseFilterx)
           );
        }
    }

    public static async Task<Either<T, HttpResponseMessage>> Card<T>(this Task<Voldelig> authTask, ActionType action, T entity) where T : class, new()
    {
        var client = await authTask;
        T cardObj = new();
        string entityName = typeof(T).Name.ToLower();
        PropertyInfo keyProperty = Helper.GetKeyProperty<T>();
        string keyFieldName = keyProperty?.Name.ToLower() ?? "id";
        string keyFieldValue = keyProperty?.GetValue(entity)?.ToString();
        string actionName = Helper.GetActionKey(action);
        HttpResponseMessage response;
        string url = "";

        MethodInfo method = typeof(T).GetMethod("InstancesJObject");

        if (method != null && client.maconomyversion == 2)
        {
            var instancesUrl = $"{client.baseUrl}/containers/{client.shortname}/{entityName}/instances";
            string json = (string)method.Invoke(null, null);

            // Get Instances
            response = await client.PostV2Async(instancesUrl, json);
            var ensureInstances = EnsureReconnectToken<T>(response, client);

            return await ensureInstances.Match(
                async _ =>
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    JObject instancesJson = JObject.Parse(responseContent);
                    JToken dataToken = instancesJson.SelectToken("links.data:some-key.template");

                    if (dataToken == null)
                        return Either<T, HttpResponseMessage>.FromRight(new HttpResponseMessage(HttpStatusCode.NotFound));

                    var cardUrl = dataToken.ToString().Replace("{0}", keyFieldValue);
                    response = await client.PostV2Async(cardUrl, "{}", withConcurrency: true);
                    var responseCardContent = await response.Content.ReadAsStringAsync();
                    var ensureCard = EnsureReconnectToken<T>(response, client);

                    return await ensureCard.Match(
                        async _1 =>
                        {
                            JObject CardJson = JObject.Parse(responseCardContent);
                            JToken cardToken = CardJson.SelectToken("panes.card.records[0].data");

                            if (action == ActionType.Get)
                            {
                                return cardToken != null
                                    ? Either<T, HttpResponseMessage>.FromLeft(cardToken.ToObject<T>())
                                    : Either<T, HttpResponseMessage>.FromRight(new HttpResponseMessage(HttpStatusCode.NoContent));
                            }
                            else if (action == ActionType.Update)
                            {
                                JToken updateUrlToken = CardJson.SelectToken("panes.card.links.action:update.href");
                                if (updateUrlToken == null)
                                    return Either<T, HttpResponseMessage>.FromRight(new HttpResponseMessage(HttpStatusCode.NotFound));

                                string updateUrl = updateUrlToken.ToString();
                                Dictionary<string, object> propertyDict = new Dictionary<string, object>();

                                foreach (var prop in typeof(T).GetProperties())
                                {
                                    if (prop.GetCustomAttribute<System.Text.Json.Serialization.JsonIgnoreAttribute>() != null)
                                        continue;

                                    var value = prop.GetValue(entity);
                                    if (value == null) continue;

                                    if (prop.PropertyType == typeof(DateTime) && (DateTime)value == DateTime.MinValue)
                                        continue;

                                    if (prop.PropertyType.IsEnum)
                                    {
                                        var firstEnumValue = Enum.GetValues(prop.PropertyType).GetValue(0);
                                        if (value.Equals(firstEnumValue))
                                            continue;
                                    }

                                    string propertyName = char.ToLowerInvariant(prop.Name[0]) + prop.Name.Substring(1);
                                    propertyDict.Add(propertyName, value);
                                }

                                var requestObject = new { data = propertyDict };
                                string jsonPayload = JsonConvert.SerializeObject(requestObject,
                                    new JsonSerializerSettings
                                    {
                                        NullValueHandling = NullValueHandling.Ignore,
                                        Formatting = Formatting.None
                                    });

                                response = await client.PostV2Async(updateUrl, jsonPayload, withConcurrency: true);
                                var responseCardContentAfterUpdate = await response.Content.ReadAsStringAsync();
                                var ensureCardAfterUpdate = EnsureReconnectToken<T>(response, client);

                                return await ensureCardAfterUpdate.Match(
                                    async _2 =>
                                    {
                                        JObject CardAfterUpdateJson = JObject.Parse(responseCardContentAfterUpdate);
                                        JToken cardAfterUpdateToken = CardAfterUpdateJson.SelectToken("panes.card.records[0].data");

                                        return cardAfterUpdateToken != null
                                            ? Either<T, HttpResponseMessage>.FromLeft(cardAfterUpdateToken.ToObject<T>())
                                            : Either<T, HttpResponseMessage>.FromRight(new HttpResponseMessage(HttpStatusCode.NoContent));
                                    },
                                    responseCardAfterUpdate => Task.FromResult(Either<T, HttpResponseMessage>.FromRight(responseCardAfterUpdate))
                                );
                            }
                            else
                            {
                                return cardToken != null
                                    ? Either<T, HttpResponseMessage>.FromLeft(cardToken.ToObject<T>())
                                    : Either<T, HttpResponseMessage>.FromRight(new HttpResponseMessage(HttpStatusCode.NoContent));
                            }
                        },
                        responseCard => Task.FromResult(Either<T, HttpResponseMessage>.FromRight(responseCard))
                    );
                },
                responseInstances => Task.FromResult(Either<T, HttpResponseMessage>.FromRight(responseInstances))
            );

        }
        else
        {
            url = $"{client.baseUrl}/containers/v1/{client.shortname}/{entityName}/data;{keyFieldName}={keyFieldValue}";
            response = await client.GetAsync(url);
            var ensureCard = EnsureReconnectToken<T>(response, client);
            return await ensureCard.Match(
                async _ =>
                {
                    var responseCardContent = await response.Content.ReadAsStringAsync();
                    JObject CardJson = JObject.Parse(responseCardContent);
                    JToken cardToken = CardJson.SelectToken("panes.card.records[0].data");
                    if(action == ActionType.Get)
                    {
                        return cardToken != null ? cardToken.ToObject<T>() : new HttpResponseMessage(HttpStatusCode.NoContent);
                    } else if(action == ActionType.Update) // NOT IMPLEMENTED YET => TODO!
                    {
                       string concurrencyControl = CardJson.SelectToken("panes.card.records[0].meta.concurrencyControl").ToString();
                        return cardToken != null ? cardToken.ToObject<T>() : new HttpResponseMessage(HttpStatusCode.NoContent);
                    } else
                    {
                        return cardToken != null ? cardToken.ToObject<T>() : new HttpResponseMessage(HttpStatusCode.NoContent);
                    }
                },
                responseCard => Task.FromResult<Either<T, HttpResponseMessage>>(responseCard)
            );
        }
    }
}