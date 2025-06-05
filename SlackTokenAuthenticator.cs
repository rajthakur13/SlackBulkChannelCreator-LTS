using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace SlackTokenFetcher
{
    class Program
    {
        public static string GetRedirectURI()
        {
            return string.Format("http://{0}:{1}/", IPAddress.Loopback, "54321");
        }
        private static string GetRandomNumber()
        {
            var Random = new Random();
            return Random.Next().ToString();
        }

        static async Task Main(string[] args)
        {
            string clientId = "883051993858.1028263366226";
            string clientSecret = "bcf1d3c72871025d1d899925eff26217";
            string redirectUri = $"{GetRedirectURI()}{GetRandomNumber()}/";

            await FetchSlackAccessTokenAndSaveToFileAsync(clientId, clientSecret, redirectUri);
        }
        public static readonly string UserScope = "calls:read,channels:history,channels:read,channels:write,chat:write,dnd:read,emoji:read,files:read,groups:history,groups:read,groups:write,identify,im:history,im:read,links:read,mpim:history,mpim:read,pins:read,reactions:read,reminders:read,remote_files:read,remote_files:share,search:read,stars:read,team:read,usergroups:read,users.profile:read,users:read,users:read.email";
        public static async Task FetchSlackAccessTokenAndSaveToFileAsync(string clientId, string clientSecret, string redirectUri, string[] scopes = null)
        {
            string state = Guid.NewGuid().ToString("N");
            string scope = UserScope;

            string authorizationUrl = $"https://slack.com/oauth/v2/authorize?client_id={clientId}&user_scope={scope}&redirect_uri={redirectUri}&state={state}";

            Console.WriteLine("Opening browser for Slack OAuth...");
            Console.WriteLine("If the browser doesn't open, manually visit the following URL:");
            Console.WriteLine(authorizationUrl);
            Process.Start(new ProcessStartInfo(authorizationUrl) { UseShellExecute = true });

            Console.WriteLine("Waiting for Slack redirect...");

            using (var listener = new HttpListener())
            {
                listener.Prefixes.Add(redirectUri.EndsWith("/") ? redirectUri : redirectUri + "/");
                listener.Start();

                var context = await listener.GetContextAsync();
                var response = context.Response;

                string responseHtml = "<html><body>Slack authorization complete. You may close this window.</body></html>";
                byte[] buffer = Encoding.UTF8.GetBytes(responseHtml);
                response.ContentLength64 = buffer.Length;
                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                response.OutputStream.Close();

                string code = context.Request.QueryString["code"];
                string returnedState = context.Request.QueryString["state"];

                if (state != returnedState)
                {
                    Console.WriteLine("Invalid state. Possible CSRF attack.");
                    return;
                }

                Console.WriteLine("Code received. Exchanging for token...");

                using (var httpClient = new HttpClient())
                {
                    var content = new FormUrlEncodedContent(new[]
                    {
                        new KeyValuePair<string, string>("client_id", clientId),
                        new KeyValuePair<string, string>("client_secret", clientSecret),
                        new KeyValuePair<string, string>("code", code),
                        new KeyValuePair<string, string>("redirect_uri", redirectUri)
                    });

                    var tokenResponse = await httpClient.PostAsync("https://slack.com/api/oauth.v2.access", content);
                    var json = await tokenResponse.Content.ReadAsStringAsync();

                    var jsonObj = JsonSerializer.Deserialize<JsonElement>(json);

                    if (!jsonObj.GetProperty("ok").GetBoolean())
                    {
                        Console.WriteLine("Error fetching token: " + json);
                        return;
                    }

                    string accessToken = jsonObj.GetProperty("authed_user").GetProperty("access_token").GetString();
                    string userId = jsonObj.GetProperty("authed_user").GetProperty("id").GetString();

                    Console.WriteLine($"User ID: {userId}");
                    Console.WriteLine($"Access Token: {accessToken}");
                    string name = jsonObj.TryGetProperty("authed_user", out var authedUser) && authedUser.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : "Unknown";


                    var tokenData = new
                    {
                        id = userId,
                        username = name,
                        token = accessToken,
                        //Scopes = scope.Split(',')
                    };

                    string documentsFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    string appFolder = Path.Combine(documentsFolder, "SlackTokenAuthenticator");

                    if (!Directory.Exists(appFolder))
                    {
                        Directory.CreateDirectory(appFolder);
                    }

                    string filePath = Path.Combine(appFolder, "slack_tokens.json");
                    List<object> existing = new List<object>();

                    if (File.Exists(filePath))
                    {
                        var existingContent = await File.ReadAllTextAsync(filePath);
                        if (!string.IsNullOrWhiteSpace(existingContent))
                        {
                            existing = JsonSerializer.Deserialize<List<object>>(existingContent) ?? new List<object>();
                        }
                    }

                    existing.Add(tokenData);
                    await File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(existing, new JsonSerializerOptions { WriteIndented = true }));

                    Console.WriteLine("Token saved to slack_tokens.json");
                }
            }
        }
    }
}
