using System.Configuration;
using System.Net;
using System.Reflection;
using System.Text;
using Newtonsoft.Json.Linq;
using Microsoft.Extensions.Configuration;


namespace VoldeligClient
{
    public enum ActionType
    {
        Create,
        Update,
        Delete,
        Get
    }



    public class Voldelig
    {
        public HttpClient httpClient;
        public string baseUrl = "";
        private string _username;
        private string _password;
        public string shortname;
        public int maconomyversion;
        public string reconnectToken = "";
        public string containerContentType = "application/vnd.deltek.maconomy.containers+json";
        public string concurrencyControl = "";

        // Attribute to mark the property as the key field
        [AttributeUsage(AttributeTargets.Property)]
        public class KeyFieldAttribute : Attribute { }

        /// <summary>
        ///  This client is initialized from appsettings.json
        ///  Its expect there to be these values located inside the appsettings
        ///  baseurl: VoldeligClient:connection:baseurl
        ///  username: VoldeligClient:connection:username
        ///  password: VoldeligClient:connection:password
        ///  shortname: VoldeligClient:connection:shortname
        /// </summary>
        /// <param name="configuration"></param>
        /// <exception cref="Exception">Connection Details couldnt be fetched from appsettings.</exception>
        public Voldelig(IConfiguration configuration)
        {
            try
            {
                // Access nested configuration values correctly
                baseUrl = configuration["VoldeligClient:connection:baseurl"];
                _username = configuration["VoldeligClient:connection:username"];
                _password = configuration["VoldeligClient:connection:password"];
                shortname = configuration["VoldeligClient:connection:shortname"];

                if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(_username) || string.IsNullOrEmpty(_password) || string.IsNullOrEmpty(shortname))
                {
                    throw new Exception("One or more configuration values are missing.");
                }

                httpClient = new HttpClient
                {
                    BaseAddress = new Uri(baseUrl)
                };

                var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_username}:{_password}"));
                httpClient.DefaultRequestHeaders.Add("Maconomy-Authentication", "X-Basic,X-Force-Credentials,X-Reconnect");
                httpClient.DefaultRequestHeaders.Add("Accept", "*/*");
                httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
            }
            catch (Exception e)
            {
                throw new Exception($"Error initializing Voldelig: {e.Message}");
            }
        }

        public Voldelig(string baseurl, string username, string password, string shortName)
        {
            baseUrl = baseurl;
            _username = username;
            _password = password;
            shortname = shortName;

            httpClient = new HttpClient
            {
                BaseAddress = new Uri(baseUrl)
            };

            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_username}:{_password}"));
            httpClient.DefaultRequestHeaders.Add("Maconomy-Authentication", "X-Basic,X-Force-Credentials,X-Reconnect");
            httpClient.DefaultRequestHeaders.Add("Accept", "*/*");
            httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
        }

        /// <summary>
        ///  Authenticate towards the Maconomy system.
        ///  This method is a part of method chaining and is supposed to be called first.
        ///  Authenticate will also evaluate which API version to use.
        /// </summary>
        /// <returns>An Instance of Voldelig</returns>
        public async Task<Voldelig> Authenticate()
        {
            if (reconnectToken == "")
            {
                var authUrl = baseUrl + "/auth/" + shortname;
                var response = await httpClient.GetAsync(authUrl);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    // This is probably because the auth endpoint is not available in the system
                    // Hence this system is using v1
                    var containerAuth = baseUrl + "/containers/v1/" + shortname;
                    response = await httpClient.GetAsync(containerAuth);
                    response.EnsureSuccessStatusCode();
                    string jsonContent = await response.Content.ReadAsStringAsync();
                    JObject jsonBody = JObject.Parse(jsonContent);
                    bool authenticated = jsonBody["authenticated"]?.Value<bool?>() ?? false;

                    if (authenticated)
                    {
                        maconomyversion = 1;
                        if (response.Headers.TryGetValues("Maconomy-Reconnect", out var token))
                        {
                            reconnectToken = token.First();
                        }

                        if (response.Content.Headers.TryGetValues("Content-Type", out var type))
                        {
                            containerContentType = type.First();
                        }

                    }
                    else
                    {
                        throw new UnauthorizedAccessException("Unauthorized, please check username or password.");
                    }
                }
                else
                {
                    EnsureReconnectToken(response);
                    var containerUrl = baseUrl + "/containers/" + shortname;
                    var containerResponse = await httpClient.GetAsync(containerUrl);
                    EnsureReconnectToken(response);
                    maconomyversion = 2;
                }
                if (reconnectToken != "")
                {
                    httpClient.DefaultRequestHeaders.Remove("Authorization");
                    httpClient.DefaultRequestHeaders.Add("Authorization", $"X-Reconnect {reconnectToken}");
                }
            }
            return this;
        }

        public void EnsureReconnectToken(HttpResponseMessage response)
        {
            response.EnsureSuccessStatusCode();
            if (response.Headers.TryGetValues("Maconomy-Reconnect", out var token))
            {
                reconnectToken = token.First();
                if (reconnectToken != "")
                {
                    httpClient.DefaultRequestHeaders.Remove("Authorization");
                    httpClient.DefaultRequestHeaders.Add("Authorization", $"X-Reconnect {reconnectToken}");
                }
            }
            if (containerContentType.Length == 0 && response.Content.Headers.TryGetValues("Content-Type", out var type))
            {
                containerContentType = type.First();
            }
        }

        public StringContent GetStringContent(string jsonAsString)
        {
            var content = new StringContent(jsonAsString, Encoding.UTF8, "application/json");
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue(containerContentType);
            content.Headers.ContentType.CharSet = "utf-8";
            return content;
        }

        public async Task<HttpResponseMessage> GetAsync(string endpoint)
        {
            var response = await httpClient.GetAsync(endpoint);
            return response;
        }

        public async Task<HttpResponseMessage> PostV1Async(string endpoint, string body, bool withConcurrency = false)
        {
            var content = new StringContent(body, Encoding.UTF8, "application/json");
            if (withConcurrency)
            {
                httpClient.DefaultRequestHeaders.Remove("Maconomy-Concurrency-Control");
                httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Maconomy-Concurrency-Control", concurrencyControl);
            }
            else
            {
                httpClient.DefaultRequestHeaders.Remove("Maconomy-Concurrency-Control");
            }
            var response = await httpClient.PostAsync(endpoint, content);
            return response;
        }

        public async Task<HttpResponseMessage> deleteV1Async(string endpoint)
        {
            httpClient.DefaultRequestHeaders.Remove("Maconomy-Concurrency-Control");
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Maconomy-Concurrency-Control", concurrencyControl);
            var response = await httpClient.DeleteAsync(endpoint);
            return response;
        }

        public async Task<HttpResponseMessage> PostV2Async(string endpoint, string body, bool withConcurrency = false)
        {
            var content = GetStringContent(body);
            if (withConcurrency)
            {
                httpClient.DefaultRequestHeaders.Remove("Maconomy-Concurrency-Control");
                httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Maconomy-Concurrency-Control", concurrencyControl);
            } else
            {
                httpClient.DefaultRequestHeaders.Remove("Maconomy-Concurrency-Control");
            }
            var response = await httpClient.PostAsync(endpoint, content);
            return response;
        }

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
    }
}
