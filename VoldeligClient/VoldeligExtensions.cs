using System;
using System.Linq.Expressions;
using System.Net;
using System.Net.Http;
using System.Reflection;
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

    public static async Task<Either<List<T>, HttpResponseMessage>> Filter<T>(this Task<Voldelig> authTask, Expression<Func<T, bool>> expr, int limit = 25)
    {
        var client = await authTask;
        string entityName = typeof(T).Name.ToLower();
        Type type = typeof(T);
        MethodInfo method = typeof(T).GetMethod("FilterJObject");
        HttpResponseMessage response;
        string exprAsString = ExpressionHelper.ExpressionToFilterString(expr);
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
            var filterUrl = $"{client.baseUrl}/containers/v1/{client.shortname}/{entityName}/filter?restriction={exprAsString}&fields={fieldNames}&limit={limit}";
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
                        return new HttpResponseMessage(HttpStatusCode.NotFound);

                    var cardUrl = dataToken.ToString().Replace("{0}", keyFieldValue);
                    response = await client.PostV2Async(cardUrl, "{}", withConcurrency: true);
                    var responseCardContent = await response.Content.ReadAsStringAsync();
                    var ensureCard = EnsureReconnectToken<T>(response, client);
                    return await ensureCard.Match(
                        async _ =>
                        {
                            JObject CardJson = JObject.Parse(responseCardContent);
                            JToken cardToken = CardJson.SelectToken("panes.card.records[0].data");
                            if (action == ActionType.Get)
                            {
                                return cardToken != null ? cardToken.ToObject<T>() : new HttpResponseMessage(HttpStatusCode.NoContent);

                            }
                            else
                            {
                                return cardToken != null ? cardToken.ToObject<T>() : new HttpResponseMessage(HttpStatusCode.NoContent);
                            }

                        },
                         responseCard => Task.FromResult<Either<T, HttpResponseMessage>>(responseCard)

                     );
                },
                responseInstances => Task.FromResult<Either<T, HttpResponseMessage>>(responseInstances)
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

                    return cardToken != null ? cardToken.ToObject<T>() : new HttpResponseMessage(HttpStatusCode.NoContent);
                },
                responseCard => Task.FromResult<Either<T, HttpResponseMessage>>(responseCard)
            );
        }
    }
}