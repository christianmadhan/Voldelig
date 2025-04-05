using System;
using System.Collections;
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


    public static async Task<Either<T, VoldeligHttpResponseMessage>> Card<T>(
        this Task<Voldelig> authTask,
        ActionType action,
        T entity) // Input entity, potentially used for Update/Create/Delete data
        where T : class, IInstances, new()
    {
        var client = await authTask;
        string entityName = typeof(T).Name.ToLower();
        PropertyInfo keyProperty = Helper.GetKeyProperty<T>();
        string keyFieldName = keyProperty?.Name.ToLower() ?? "id";
        // Use the key value from the *input* entity for identifying the record
        string keyFieldValue = keyProperty?.GetValue(entity)?.ToString();

        // --- Common Variables ---
        HttpResponseMessage response;
        string url;

        // =============================
        // === Maconomy Version 2 Logic ===
        // =============================
        if (client.maconomyversion == 2)
        {
            var instancesUrl = $"{client.baseUrl}/containers/{client.shortname}/{entityName}/instances";
            IInstances instanceForPayload = new T(); // Or use input 'entity' if appropriate for payload
            string instancesPayloadJson = instanceForPayload.InstancesJObject(); // Get payload

            // --- Get Instances Response ---
            response = await client.PostV2Async(instancesUrl, instancesPayloadJson);
            var ensureInstances = await Helper.EnsureReconnectToken<VoldeligHttpResponseMessage>(response, client); // Ensure helper returns compatible type

            // Handle failure to get instances response
            if (ensureInstances.IsRight)
            {
                return Either<T, VoldeligHttpResponseMessage>.FromRight(ensureInstances.Right);
            }

            string instancesResponseContent = await response.Content.ReadAsStringAsync();
            JObject instancesJson = JObject.Parse(instancesResponseContent);
            JToken dataTokenTemplate = instancesJson.SelectToken("links.data:some-key.template"); // Link template to get specific card
            JToken createToken = instancesJson.SelectToken("links.action:init-create.href"); // Link to initiate creation

            // --- Handle Create Action (V2 - Initial Step) ---
            if (action == ActionType.Create && createToken != null)
            {
                // Pass the input 'entity' which should contain the data for creation
                return await VoldeligActionExtensions.HandleCreateActionV2(client, entity, createToken.ToString());
            }
            // If not creating or create token missing, proceed to get the specific card data
            else if (action != ActionType.Create && dataTokenTemplate == null) // Need data link for Get/Update/Delete
            {
                return new VoldeligHttpResponseMessage { MaconomyErrorMessage = "Could not find instance data link template (links.data:some-key.template)", HttpResponseMessage = new HttpResponseMessage(HttpStatusCode.NotFound) };
            }
            else if (action == ActionType.Create) // Create token was null
            {
                return new VoldeligHttpResponseMessage { MaconomyErrorMessage = "Could not find create action link (links.action:init-create.href)", HttpResponseMessage = new HttpResponseMessage(HttpStatusCode.NotFound) };
            }


            // --- Get Card Data ---
            var cardUrl = dataTokenTemplate.ToString().Replace("{0}", keyFieldValue);
            // Note: Usually GET for card data, but API uses POST? Using "{}" payload as per original code.
            response = await client.PostV2Async(cardUrl, "{}", withConcurrency: true);
            var ensureCard = await Helper.EnsureReconnectToken<VoldeligHttpResponseMessage>(response, client); // Ensure helper returns compatible type

            // Handle failure to get card response
            if (ensureCard.IsRight)
            {
                return Either<T, VoldeligHttpResponseMessage>.FromRight(ensureCard.Right);
            }

            string responseCardContent = await response.Content.ReadAsStringAsync();
            JObject cardJson = JObject.Parse(responseCardContent);
            JToken cardDataToken = cardJson.SelectToken("panes.card.records[0].data");

            if (cardDataToken == null)
            {
                return new VoldeligHttpResponseMessage { MaconomyErrorMessage = "Could not find card data token (panes.card.records[0].data)", HttpResponseMessage = new HttpResponseMessage(HttpStatusCode.NoContent) };
            }

            // --- Deserialize Main Entity Data ---
            T entityResult;
            try
            {
                // Create the result entity from the response data
                entityResult = cardDataToken.ToObject<T>();
            }
            catch (Exception ex)
            {
                return new VoldeligHttpResponseMessage { MaconomyErrorMessage = $"Failed to deserialize card data: {ex.Message}", HttpResponseMessage = new HttpResponseMessage(HttpStatusCode.InternalServerError) };
            }


            // --- *** Populate Table Data IF Entity Supports It *** ---
            if (entityResult is ICanPopulateTable tableEntity)
            {
                JToken tableToken = cardJson.SelectToken("panes.table.records");
                // No need to check tableProperty here, the interface handles it
                tableEntity.PopulateTableFromJson(tableToken); // Delegate population
            }
            // --- *** End Table Population *** ---


            // --- Handle Specific Actions based on Card Data (V2) ---
            switch (action)
            {
                case ActionType.Get:
                    return Either<T, VoldeligHttpResponseMessage>.FromLeft(entityResult); // Return the deserialized entity

                case ActionType.Update:
                    // Pass the *input* entity containing the desired updates
                    return await VoldeligActionExtensions.HandleUpdateActionV2(client, cardJson, entity);

                case ActionType.Create: // Should have been handled earlier, but maybe fallback?
                    JToken createUrlToken = cardJson.SelectToken("panes.card.links.action:init-create.href");
                    if (createUrlToken == null) return new VoldeligHttpResponseMessage { MaconomyErrorMessage = "Could not find create link in card data", HttpResponseMessage = new HttpResponseMessage(HttpStatusCode.NotFound) };
                    // Pass the *input* entity containing data for creation
                    return await VoldeligActionExtensions.HandleCreateActionV2(client, entity, createUrlToken.ToString());

                case ActionType.Delete:
                    JToken deleteUrlToken = cardJson.SelectToken("panes.card.links.action:delete.href");
                    if (deleteUrlToken == null) return new VoldeligHttpResponseMessage { MaconomyErrorMessage = "Could not find delete link in card data", HttpResponseMessage = new HttpResponseMessage(HttpStatusCode.NotFound) };
                    // Pass the *input* entity for context if needed by handler, or just the URL
                    return await VoldeligActionExtensions.HandleDeleteActionV2(client, entity, deleteUrlToken.ToString());

                default: // Should not happen
                    return new VoldeligHttpResponseMessage { MaconomyErrorMessage = $"Unsupported action type: {action}", HttpResponseMessage = new HttpResponseMessage(HttpStatusCode.BadRequest) };
            }
        }
        // =============================
        // === Maconomy Version 1 Logic ===
        // =============================
        else // maconomyversion != 2
        {
            // --- Handle Create Action (V1) ---
            if (action == ActionType.Create)
            {
                url = $"{client.baseUrl}/containers/v1/{client.shortname}/{entityName}/data/card";
                // Pass the input 'entity' which should contain the data for creation
                return await VoldeligActionExtensions.HandleCreateActionV1(client, entity, url);
            }

            // --- Get Card Data (V1 - for Get/Update/Delete) ---
            url = $"{client.baseUrl}/containers/v1/{client.shortname}/{entityName}/data;{keyFieldName}={keyFieldValue}";
            response = await client.GetAsync(url); // Typically GET in V1
            var ensureCard = await Helper.EnsureReconnectToken<VoldeligHttpResponseMessage>(response, client); // Ensure helper returns compatible type

            // Handle failure to get card response
            if (ensureCard.IsRight)
            {
                return Either<T, VoldeligHttpResponseMessage>.FromRight(ensureCard.Right);
            }

            string responseCardContent = await response.Content.ReadAsStringAsync();
            JObject cardJson = JObject.Parse(responseCardContent);
            JToken cardDataToken = cardJson.SelectToken("panes.card.records[0].data");
            // Concurrency info might be needed for Update/Delete in V1
            string concurrencyControl = cardJson.SelectToken("panes.card.records[0].meta.concurrencyControl")?.ToString();

            if (cardDataToken == null)
            {
                return new VoldeligHttpResponseMessage { MaconomyErrorMessage = "Could not find card data token (panes.card.records[0].data)", HttpResponseMessage = new HttpResponseMessage(HttpStatusCode.NoContent) };
            }

            // --- Handle Specific Actions based on Card Data (V1) ---
            switch (action)
            {
                case ActionType.Get:
                    try
                    {
                        // Deserialize and return the entity
                        T entityResult = cardDataToken.ToObject<T>();
                        // V1 doesn't seem to have table data in this structure based on original code
                        return Either<T, VoldeligHttpResponseMessage>.FromLeft(entityResult);
                    }
                    catch (Exception ex)
                    {
                        return new VoldeligHttpResponseMessage { MaconomyErrorMessage = $"Failed to deserialize card data: {ex.Message}", HttpResponseMessage = new HttpResponseMessage(HttpStatusCode.InternalServerError) };
                    }

                case ActionType.Update:
                    string updateUrl = cardJson.SelectToken("panes.card.records[0].links.action:update.href")?.ToString();
                    if (updateUrl == null) return new VoldeligHttpResponseMessage { MaconomyErrorMessage = "Could not find update link in card data", HttpResponseMessage = new HttpResponseMessage(HttpStatusCode.NotFound) };
                    client.concurrencyControl = concurrencyControl; // Set concurrency for V1 update
                    // Pass the *input* entity containing the desired updates
                    return await VoldeligActionExtensions.HandleUpdateActionV1(client, entity, updateUrl);

                case ActionType.Delete:
                    string deleteUrl = cardJson.SelectToken("panes.card.records[0].links.action:delete.href")?.ToString();
                    if (deleteUrl == null) return new VoldeligHttpResponseMessage { MaconomyErrorMessage = "Could not find delete link in card data", HttpResponseMessage = new HttpResponseMessage(HttpStatusCode.NotFound) };
                    client.concurrencyControl = concurrencyControl; // Set concurrency for V1 delete
                    // Pass the *input* entity for context if needed, or just the URL
                    return await VoldeligActionExtensions.HandleDeleteActionV1(client, entity, deleteUrl);

                default: // Should not happen
                    return new VoldeligHttpResponseMessage { MaconomyErrorMessage = $"Unsupported action type: {action}", HttpResponseMessage = new HttpResponseMessage(HttpStatusCode.BadRequest) };
            }
        }
    }
}