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


    public static async Task<Either<List<T>, VoldeligHttpResponseMessage>> Filter<T>(this Task<Voldelig> authTask, Expression<Func<T, bool>>? expr, int limit = 25)
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
            var ensureFilter = await Helper.EnsureReconnectTokenFilter<List<T>>(response, client);

            return await ensureFilter.Match<Task<Either<List<T>, VoldeligHttpResponseMessage>>>(
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
                responseFilterx => Task.FromResult<Either<List<T>, VoldeligHttpResponseMessage>>(responseFilterx)
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
            var ensureFilter = await Helper.EnsureReconnectTokenFilter<List<T>>(response, client);
            return await ensureFilter.Match<Task<Either<List<T>, VoldeligHttpResponseMessage>>>(
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
               responseFilterx => Task.FromResult<Either<List<T>, VoldeligHttpResponseMessage>>(responseFilterx)
           );
        }
    }

    public static async Task<Either<T, VoldeligHttpResponseMessage>> Card<T>(this Task<Voldelig> authTask, ActionType action, T entity) where T : class, new()
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
            var ensureInstances = await Helper.EnsureReconnectToken<T>(response, client);
            var responseContent = await response.Content.ReadAsStringAsync();
            JObject instancesJson = JObject.Parse(responseContent);
            JToken dataToken = instancesJson.SelectToken("links.data:some-key.template");
            JToken createToken = instancesJson.SelectToken("links.action:init-create.href");

            if(action == ActionType.Create && createToken != null)
            {
                return await VoldeligActionExtensions.HandleCreateActionV2(client, entity, createToken.ToString());
            } else
            {
                return await ensureInstances.Match(
                    async _ =>
                    {
                        ;
                        if (dataToken == null)
                            return Either<T, VoldeligHttpResponseMessage>.FromRight(new VoldeligHttpResponseMessage { MaconomyErrorMessage = "Could not find token: panes.card.links.action:update.href", HttpResponseMessage = new HttpResponseMessage(HttpStatusCode.NoContent) });

                        var cardUrl = dataToken.ToString().Replace("{0}", keyFieldValue);
                        response = await client.PostV2Async(cardUrl, "{}", withConcurrency: true);
                        var responseCardContent = await response.Content.ReadAsStringAsync();
                        var ensureCard = await Helper.EnsureReconnectToken<T>(response, client);

                        return await ensureCard.Match(
                            async _1 =>
                            {
                                JObject CardJson = JObject.Parse(responseCardContent);
                                JToken cardToken = CardJson.SelectToken("panes.card.records[0].data");

                                if (action == ActionType.Get)
                                {
                                    return cardToken != null
                                        ? Either<T, VoldeligHttpResponseMessage>.FromLeft(cardToken.ToObject<T>())
                                        : Either<T, VoldeligHttpResponseMessage>.FromRight(new VoldeligHttpResponseMessage { MaconomyErrorMessage = "Could not find token: panes.card.records[0].data", HttpResponseMessage = new HttpResponseMessage(HttpStatusCode.NoContent) });
                                }
                                else if (action == ActionType.Update)
                                {
                                    return await VoldeligActionExtensions.HandleUpdateActionV2(client, CardJson, entity);
                                }
                                else if (action == ActionType.Create)
                                {
                                    JToken createUrlToken = CardJson.SelectToken("panes.card.links.action:init-create.href");
                                    return await VoldeligActionExtensions.HandleCreateActionV2(client, entity, createUrlToken.ToString());
                                } else if (action == ActionType.Delete) 
                                {
                                        JToken deleteUrlToken = CardJson.SelectToken("panes.card.links.action:delete.href");
                                        return await VoldeligActionExtensions.HandleDeleteActionV2(client, entity, deleteUrlToken.ToString());
                                }
                                else
                                {
                                    return cardToken != null
                                        ? Either<T, VoldeligHttpResponseMessage>.FromLeft(cardToken.ToObject<T>())
                                        : Either<T, VoldeligHttpResponseMessage>.FromRight(new VoldeligHttpResponseMessage { MaconomyErrorMessage = "Could not find token: panes.card.records[0].data", HttpResponseMessage = new HttpResponseMessage(HttpStatusCode.NoContent) });
                                }
                            },
                            responseCard => Task.FromResult(Either<T, VoldeligHttpResponseMessage>.FromRight(responseCard))
                        );
                    },
                    responseInstances => Task.FromResult(Either<T, VoldeligHttpResponseMessage>.FromRight(responseInstances))
                );

            }

        }
        else
        {
            if (action == ActionType.Create) 
            { 
                url = $"{client.baseUrl}/containers/v1/{client.shortname}/{entityName}/data/card";
                return await VoldeligActionExtensions.HandleCreateActionV1(client, entity, url);
            } 
            else
            {
                url = $"{client.baseUrl}/containers/v1/{client.shortname}/{entityName}/data;{keyFieldName}={keyFieldValue}";
                response = await client.GetAsync(url);
                var ensureCard = await Helper.EnsureReconnectToken<T>(response, client);
                return await ensureCard.Match(
                    async _ =>
                    {
                        var responseCardContent = await response.Content.ReadAsStringAsync();
                        JObject CardJson = JObject.Parse(responseCardContent);
                        string concurrencyControl = CardJson.SelectToken("panes.card.records[0].meta.concurrencyControl").ToString();
                        JToken cardToken = CardJson.SelectToken("panes.card.records[0].data");
                        if (action == ActionType.Get)
                        {
                            return cardToken != null ? cardToken.ToObject<T>() : new VoldeligHttpResponseMessage { MaconomyErrorMessage = "Could not find token: panes.card.records[0].data", HttpResponseMessage = new HttpResponseMessage(HttpStatusCode.NoContent) };
                        }
                        else if (action == ActionType.Update)
                        {
                            string updateurl = CardJson.SelectToken("panes.card.records[0].links.action:update.href").ToString();
                            client.concurrencyControl = concurrencyControl;
                            return await VoldeligActionExtensions.HandleUpdateActionV1(client, entity, updateurl);
                        } else if (action == ActionType.Delete)
                        {
                            string deleteurl = CardJson.SelectToken("panes.card.records[0].links.action:delete.href").ToString();
                            client.concurrencyControl = concurrencyControl;
                            return await VoldeligActionExtensions.HandleDeleteActionV1(client,entity, deleteurl);
                        }
                        else
                        {
                            return cardToken != null ? cardToken.ToObject<T>() : new VoldeligHttpResponseMessage { MaconomyErrorMessage = "Could not find token: panes.card.records[0].data", HttpResponseMessage = new HttpResponseMessage(HttpStatusCode.NoContent) };
                        }
                    },
                    responseCard => Task.FromResult<Either<T, VoldeligHttpResponseMessage>>(responseCard)
                );

            }   
        }
    }
}