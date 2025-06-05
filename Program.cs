using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace SlackChannelCreator
{
    class Program
    {
        private static Dictionary<string, string> userTokens = new Dictionary<string, string>();

        static async Task Main(string[] args)
        {
            Console.WriteLine("Enter your primary workspace user token (xoxp-...):");
            string primaryToken = Console.ReadLine()?.Trim();
            if (string.IsNullOrWhiteSpace(primaryToken))
            {
                Console.WriteLine("Error:  primary workspace user token cannot be empty.");
                return;
            }

            Console.WriteLine("Enter your Slack User ID of Primary Workspace Owner:");
            string primaryUserId = Console.ReadLine()?.Trim();
            if (string.IsNullOrWhiteSpace(primaryUserId))
            {
                Console.WriteLine("Error: Slack User ID cannot be empty.");
                return;
            }

            List<SlackMember> members;
            try
            {
                members = await GetWorkspaceMembersAsync(primaryToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching workspace members: {ex.Message}");
                return;
            }

            // Exclude primary user from members list
            members = members.Where(m => m.Id != primaryUserId).ToList();

            // Choose token loading mode
            bool tokensLoaded = false;
            while (!tokensLoaded)
            {
                Console.WriteLine("Choose token entry option:");
                Console.WriteLine("1. Load tokens from JSON file");
                Console.WriteLine("2. Enter tokens manually");
                Console.Write("Enter 1 or 2: ");
                string option = Console.ReadLine()?.Trim();

                if (option == "1")
                {
                    tokensLoaded = LoadUserTokensFromJson(members);
                }
                else if (option == "2")
                {
                    await LoadUserTokensManuallyAsync(members);
                    tokensLoaded = true;
                    //SaveUserTokensToJson(members); // Save after manual entry
                }
                else
                {
                    Console.WriteLine("Invalid option. Please enter 1 or 2.");
                }
            }

            if (userTokens.Count == 0)
            {
                Console.WriteLine("No user tokens available. Exiting.");
                return;
            }

            // Main channel creation loop
            while (true)
            {
                bool isPrivateChannel = PromptChannelType();
                int channelCount = PromptChannelCount();

                await CreateChannelsAsync(primaryToken, members, isPrivateChannel, channelCount);

                Console.WriteLine("\nDo you want to create more channels? (yes/no):");
                string choice = Console.ReadLine()?.Trim().ToLower();
                if (choice == "no" || choice == "n")
                {
                    Console.WriteLine("Exiting the application. Goodbye!");
                    break;
                }
                else if (choice != "yes" && choice != "y")
                {
                    Console.WriteLine("Invalid input. Exiting the application. Goodbye!");
                    break;
                }
            }
        }

        private static async Task<List<SlackMember>> GetWorkspaceMembersAsync(string token)
        {
            string url = "https://slack.com/api/users.list";
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

            var response = await client.GetAsync(url);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new Exception($"Slack API error: {response.StatusCode} - {content}");

            var slackResponse = JsonConvert.DeserializeObject<SlackUsersResponse>(content);
            if (!slackResponse.Ok)
                throw new Exception($"Failed to fetch workspace members: {slackResponse.Error}");

            return slackResponse.Members
                .Where(m => !m.IsBot && m.Id != "USLACKBOT")
                .ToList();
        }

        private static bool LoadUserTokensFromJson(List<SlackMember> members)
        {
            Console.WriteLine("Enter full path to JSON file containing user tokens:");
            string path = Console.ReadLine()?.Trim();

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                Console.WriteLine("Invalid file path or file does not exist.");
                return false;
            }

            try
            {
                string json = File.ReadAllText(path);
                var userTokenList = JsonConvert.DeserializeObject<List<UserTokenInfo>>(json);

                // Match tokens by user id, add only if member exists
                userTokens.Clear();
                foreach (var tokenInfo in userTokenList)
                {
                    if (members.Any(m => m.Id == tokenInfo.Id))
                    {
                        userTokens[tokenInfo.Id] = tokenInfo.Token;
                        Console.WriteLine($"Loaded token for user ID: {tokenInfo.Id}, username: {tokenInfo.Username}");
                    }
                }

                if (userTokens.Count == 0)
                {
                    Console.WriteLine("No matching users found in JSON file.");
                    return false;
                }

                Console.WriteLine($"Successfully loaded tokens for {userTokens.Count} users.");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading JSON file: {ex.Message}");
                return false;
            }
        }

        private static async Task LoadUserTokensManuallyAsync(List<SlackMember> members)
        {
            userTokens.Clear();
            foreach (var member in members)
            {
                Console.WriteLine($"Enter access token for user {member.RealName} (ID: {member.Id}) or press ENTER to skip:");
                string token = Console.ReadLine()?.Trim();

                if (!string.IsNullOrWhiteSpace(token))
                {
                    userTokens[member.Id] = token;
                    Console.WriteLine($"Saved token for {member.RealName}");
                }
                else
                {
                    Console.WriteLine($"Skipped user {member.RealName}");
                }
            }
            await Task.CompletedTask;
        }

        private static void SaveUserTokensToJson(List<SlackMember> members)
        {
            try
            {
                var tokenListToSave = userTokens.Select(kvp =>
                {
                    var member = members.FirstOrDefault(m => m.Id == kvp.Key);
                    return new UserTokenInfo
                    {
                        Id = kvp.Key,
                        Username = member?.RealName ?? "",
                        Token = kvp.Value
                    };
                }).ToList();

                string json = JsonConvert.SerializeObject(tokenListToSave, Formatting.Indented);

                // Define the folder path inside the Documents directory
                string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                string appFolder = Path.Combine(documentsPath, "SavedSlackTokens");

                // Ensure the directory exists
                if (!Directory.Exists(appFolder))
                {
                    Directory.CreateDirectory(appFolder);
                }

                string savePath = Path.Combine(appFolder, "UserTokensSaved.json");
                File.WriteAllText(savePath, json);
                Console.WriteLine($"User tokens saved to {savePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving tokens to JSON: {ex.Message}");
            }
        }

        private static bool PromptChannelType()
        {
            while (true)
            {
                Console.WriteLine("What type of channel do you want to create? Enter 'private' or 'public':");
                string input = Console.ReadLine()?.Trim().ToLower();

                if (input == "private") return true;
                if (input == "public") return false;

                Console.WriteLine("Invalid input. Please enter either 'private' or 'public'.");
            }
        }

        private static int PromptChannelCount()
        {
            while (true)
            {
                Console.WriteLine("Enter the number of channels to create:");
                if (int.TryParse(Console.ReadLine()?.Trim(), out int count) && count > 0)
                    return count;

                Console.WriteLine("Invalid number. Please enter a positive integer.");
            }
        }

        private static async Task CreateChannelsAsync(string primaryToken, List<SlackMember> members, bool isPrivateChannel, int channelCount)
        {
            var random = new Random();

            // Filter members who have tokens
            var eligibleMembers = members.Where(m => userTokens.ContainsKey(m.Id)).ToList();
            if (eligibleMembers.Count == 0)
            {
                Console.WriteLine("No eligible members with tokens to add to channels.");
                return;
            }

            for (int i = 1; i <= channelCount; i++)
            {
                string channelName = GenerateRandomChannelName(isPrivateChannel);
                Console.WriteLine($"Creating {(isPrivateChannel ? "private" : "public")} channel {i}/{channelCount}: {channelName}");

                try
                {
                    var channelResponse = await CreateChannelAsync(primaryToken, channelName, isPrivateChannel);
                    Console.WriteLine($"Channel created successfully: {channelResponse.Channel.Name}");

                    // Add a random subset of members to the channel
                    int membersToAdd = random.Next(1, eligibleMembers.Count + 1);
                    var selectedMembers = eligibleMembers.OrderBy(_ => random.Next()).Take(membersToAdd).ToList();

                    foreach (var member in selectedMembers)
                    {
                        try
                        {
                            // Use primary token to invite users (must have invite permission)
                            await AddUserToChannelAsync(primaryToken, channelResponse.Channel.Id, member.Id);
                            Console.WriteLine($"User {member.RealName} added to channel {channelResponse.Channel.Name}");

                            // Use user-specific token to post message as that user
                            if (userTokens.TryGetValue(member.Id, out string userToken))
                            {
                                await PostMessageToChannelAsync(userToken, channelResponse.Channel.Id, member.Id);
                                Console.WriteLine($"Message sent from user {member.RealName} to channel {channelResponse.Channel.Name}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error adding or messaging user {member.RealName}: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error creating channel '{channelName}': {ex.Message}");
                }
            }
            Console.WriteLine("Channel creation process completed.");
        }

        private static string GenerateRandomChannelName(bool isPrivateChannel)
        {
            var random = new Random();
            const string chars = "abcdefghijklmnopqrstuvwxyz0123456789";
            return (isPrivateChannel ? "private-channel-" : "public-channel-") +
                new string(Enumerable.Repeat(chars, 8).Select(s => s[random.Next(s.Length)]).ToArray());
        }

        public static async Task<SlackChannelResponse> CreateChannelAsync(string token, string channelName, bool isPrivate)
        {
            string url = "https://slack.com/api/conversations.create";
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

            var payload = new { name = channelName, is_private = isPrivate };
            var jsonPayload = JsonConvert.SerializeObject(payload);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            var response = await client.PostAsync(url, content);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new Exception($"Slack API error: {response.StatusCode} - {responseContent}");

            var slackResponse = JsonConvert.DeserializeObject<SlackChannelResponse>(responseContent);
            if (!slackResponse.Ok)
                throw new Exception($"Failed to create channel: {slackResponse.Error}");

            return slackResponse;
        }

        public static async Task AddUserToChannelAsync(string token, string channelId, string userId)
        {
            string url = "https://slack.com/api/conversations.invite";
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

            var payload = new { channel = channelId, users = userId };
            var jsonPayload = JsonConvert.SerializeObject(payload);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            var response = await client.PostAsync(url, content);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new Exception($"Slack API error: {response.StatusCode} - {responseContent}");

            var slackResponse = JsonConvert.DeserializeObject<SlackResponse>(responseContent);
            if (!slackResponse.Ok)
                throw new Exception($"Failed to add user {userId} to channel: {slackResponse.Error}");
        }

        public static async Task PostMessageToChannelAsync(string token, string channelId, string userId)
        {
            string url = "https://slack.com/api/chat.postMessage";
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

            var messageText = new StringBuilder();
            for (int i = 1; i <= 10; i++)
            {
                string message = GenerateMessageWithWords(20);
                messageText.AppendLine($"{i}. {message}");
            }

            var payload = new
            {
                channel = channelId,
                text = $"Hello, this is a numbered list of messages from user {userId}:\n{messageText}"
            };

            var jsonPayload = JsonConvert.SerializeObject(payload);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            var response = await client.PostAsync(url, content);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new Exception($"Slack API error: {response.StatusCode} - {responseContent}");

            var slackResponse = JsonConvert.DeserializeObject<SlackResponse>(responseContent);
            if (!slackResponse.Ok)
                throw new Exception($"Failed to send message: {slackResponse.Error}");

            Console.WriteLine("Numbered list message sent successfully.");
        }

        private static string GenerateMessageWithWords(int wordCount)
        {
            const string chars = "abcdefghijklmnopqrstuvwxyz";
            var random = new Random();
            var words = new List<string>();

            for (int i = 0; i < wordCount; i++)
            {
                int length = random.Next(3, 9);
                var word = new string(Enumerable.Range(0, length).Select(_ => chars[random.Next(chars.Length)]).ToArray());
                words.Add(word);
            }

            return string.Join(" ", words);
        }
    }

    public class SlackUsersResponse
    {
        [JsonProperty("ok")]
        public bool Ok { get; set; }

        [JsonProperty("members")]
        public List<SlackMember> Members { get; set; }

        [JsonProperty("error")]
        public string Error { get; set; }
    }

    public class SlackMember
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("real_name")]
        public string RealName { get; set; }

        [JsonProperty("is_bot")]
        public bool IsBot { get; set; }
    }

    public class SlackChannelResponse
    {
        [JsonProperty("ok")]
        public bool Ok { get; set; }

        [JsonProperty("channel")]
        public SlackChannel Channel { get; set; }

        [JsonProperty("error")]
        public string Error { get; set; }
    }

    public class SlackChannel
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("is_private")]
        public bool IsPrivate { get; set; }
    }

    public class SlackResponse
    {
        [JsonProperty("ok")]
        public bool Ok { get; set; }

        [JsonProperty("error")]
        public string Error { get; set; }
    }

    public class UserTokenInfo
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("username")]
        public string Username { get; set; }

        [JsonProperty("token")]
        public string Token { get; set; }
    }
}
