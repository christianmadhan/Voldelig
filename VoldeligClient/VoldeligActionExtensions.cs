using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace VoldeligClient
{
    public static class VoldeligActionExtensions
    {
        public static async Task<Either<T, VoldeligHttpResponseMessage>> HandleCreateActionV1<T>(Voldelig client, T entity, string createurl) where T : class, new()
        {
            var jsonPayload = Helper.MakeEntityIntoPayload(entity);
            var response = await client.PostV1Async(createurl, jsonPayload);
            var responseCardContentAfterCreate = await response.Content.ReadAsStringAsync();
            var ensureCardAfterCreate = await Helper.EnsureReconnectToken<T>(response, client);
            return await ensureCardAfterCreate.Match(
                async _2 =>
                {
                    JObject CardAfterCreateJson = JObject.Parse(responseCardContentAfterCreate);
                    JToken cardAfterCreateToken = CardAfterCreateJson.SelectToken("panes.card.records[0].data");

                    return cardAfterCreateToken != null
                        ? Either<T, VoldeligHttpResponseMessage>.FromLeft(cardAfterCreateToken.ToObject<T>())
                        : Either<T, VoldeligHttpResponseMessage>.FromRight(new VoldeligHttpResponseMessage { MaconomyErrorMessage = "Could not find token: panes.card.records[0].data", HttpResponseMessage = new HttpResponseMessage(HttpStatusCode.NoContent) });
                },
                responseCardAfterUpdate => Task.FromResult(Either<T, VoldeligHttpResponseMessage>.FromRight(responseCardAfterUpdate))
            );
        }
        public static async Task<Either<T, VoldeligHttpResponseMessage>> HandleCreateActionV2<T>(Voldelig client, T entity, string createurl) where T : class, new()
        {
            var jsonPayload = Helper.MakeEntityIntoPayload(entity);
            var response = await client.PostV2Async(createurl, jsonPayload, withConcurrency: true);
            var responseCardContentAfterCreate = await response.Content.ReadAsStringAsync();
            var ensureCardAfterCreate = await Helper.EnsureReconnectToken<T>(response, client);
            return await ensureCardAfterCreate.Match(
                async _2 =>
                {
                    JObject CardAfterCreateJson = JObject.Parse(responseCardContentAfterCreate);
                    JToken cardAfterCreateToken = CardAfterCreateJson.SelectToken("panes.card.records[0].data");

                    return cardAfterCreateToken != null
                        ? Either<T, VoldeligHttpResponseMessage>.FromLeft(cardAfterCreateToken.ToObject<T>())
                        : Either<T, VoldeligHttpResponseMessage>.FromRight(new VoldeligHttpResponseMessage { MaconomyErrorMessage = "Could not find token: panes.card.records[0].data", HttpResponseMessage = new HttpResponseMessage(HttpStatusCode.NoContent) });
                },
                responseCardAfterUpdate => Task.FromResult(Either<T, VoldeligHttpResponseMessage>.FromRight(responseCardAfterUpdate))
            );
        }

        public static async Task<Either<T, VoldeligHttpResponseMessage>> HandleDeleteActionV2<T>(Voldelig client, T entity, string deleteurl) where T : class, new()
        {
            var response = await client.PostV2Async(deleteurl, "{}", withConcurrency: true);
            var responseCardContentAfterCreate = await response.Content.ReadAsStringAsync();
            var ensureCardAfterCreate = await Helper.EnsureReconnectToken<T>(response, client);
            return await ensureCardAfterCreate.Match(
                async _ =>
                {
                    return Either<T, VoldeligHttpResponseMessage>.FromLeft(new T());
                },
                responseCardAfterUpdate => Task.FromResult(Either<T, VoldeligHttpResponseMessage>.FromRight(responseCardAfterUpdate))
            );
        }

        public static async Task<Either<T, VoldeligHttpResponseMessage>> HandleDeleteActionV1<T>(Voldelig client, T entity, string deleteurl) where T : class, new()
        {
            var response = await client.deleteV1Async(deleteurl);
            var responseCardContentAfterCreate = await response.Content.ReadAsStringAsync();
            var ensureCardAfterCreate = await Helper.EnsureReconnectToken<T>(response, client);
            return await ensureCardAfterCreate.Match(
                async _ =>
                {
                    return Either<T, VoldeligHttpResponseMessage>.FromLeft(new T());
                },
                responseCardAfterUpdate => Task.FromResult(Either<T, VoldeligHttpResponseMessage>.FromRight(responseCardAfterUpdate))
            );
        }

        public static async Task<Either<T, VoldeligHttpResponseMessage>> HandleUpdateActionV2<T>(Voldelig client, JObject CardJson, T entity) where T : class, new()
        {
            JToken updateUrlToken = CardJson.SelectToken("panes.card.links.action:update.href");
            if (updateUrlToken == null)
                return Either<T, VoldeligHttpResponseMessage>.FromRight(new VoldeligHttpResponseMessage { MaconomyErrorMessage = "Could not find token: panes.card.links.action:update.href", HttpResponseMessage = new HttpResponseMessage(HttpStatusCode.NoContent) });

            string updateUrl = updateUrlToken.ToString();
            var jsonPayload = Helper.MakeEntityIntoPayload(entity); 
            var response = await client.PostV2Async(updateUrl, jsonPayload, withConcurrency: true);
            var responseCardContentAfterUpdate = await response.Content.ReadAsStringAsync();
            var ensureCardAfterUpdate = await Helper.EnsureReconnectToken<T>(response, client);

            return await ensureCardAfterUpdate.Match(
                async _2 =>
                {
                    JObject CardAfterUpdateJson = JObject.Parse(responseCardContentAfterUpdate);
                    JToken cardAfterUpdateToken = CardAfterUpdateJson.SelectToken("panes.card.records[0].data");

                    return cardAfterUpdateToken != null
                        ? Either<T, VoldeligHttpResponseMessage>.FromLeft(cardAfterUpdateToken.ToObject<T>())
                        : Either<T, VoldeligHttpResponseMessage>.FromRight(new VoldeligHttpResponseMessage { MaconomyErrorMessage = "Could not find token: panes.card.records[0].data", HttpResponseMessage = new HttpResponseMessage(HttpStatusCode.NoContent) });
                },
                responseCardAfterUpdate => Task.FromResult(Either<T, VoldeligHttpResponseMessage>.FromRight(responseCardAfterUpdate))
            );
        }
        public static async Task<Either<T, VoldeligHttpResponseMessage>> HandleUpdateActionV1<T>(Voldelig client, T entity, string updateurl) where T : class, new()
        {
            
            var jsonPayload = Helper.MakeEntityIntoPayload(entity);
            var response = await client.PostV1Async(updateurl, jsonPayload, withConcurrency: true);
            var responseCardContentAfterUpdate = await response.Content.ReadAsStringAsync();
            var ensureCardAfterUpdate = await Helper.EnsureReconnectToken<T>(response, client);

            return await ensureCardAfterUpdate.Match(
                async _2 =>
                {
                    JObject CardAfterUpdateJson = JObject.Parse(responseCardContentAfterUpdate);
                    JToken cardAfterUpdateToken = CardAfterUpdateJson.SelectToken("panes.card.records[0].data");

                    return cardAfterUpdateToken != null
                        ? Either<T, VoldeligHttpResponseMessage>.FromLeft(cardAfterUpdateToken.ToObject<T>())
                        : Either<T, VoldeligHttpResponseMessage>.FromRight(new VoldeligHttpResponseMessage { MaconomyErrorMessage = "Could not find token: panes.card.records[0].data", HttpResponseMessage = new HttpResponseMessage(HttpStatusCode.NoContent) });
                },
                responseCardAfterUpdate => Task.FromResult(Either<T, VoldeligHttpResponseMessage>.FromRight(responseCardAfterUpdate))
            );
        }
    }
}
