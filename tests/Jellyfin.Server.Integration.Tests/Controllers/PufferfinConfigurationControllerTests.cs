using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Api.Models.UserDtos;
using Jellyfin.Extensions.Json;
using Xunit;

namespace Jellyfin.Server.Integration.Tests.Controllers
{
    /// <summary>
    /// PUFFERFIN: end to end check that the configuration endpoints are enforced by the server.
    /// </summary>
    public sealed class PufferfinConfigurationControllerTests : IClassFixture<JellyfinApplicationFactory>
    {
        private static readonly (HttpMethod Method, string Path)[] _configurationEndpoints =
        [
            (HttpMethod.Get, "Pufferfin/Configuration"),
            (HttpMethod.Post, "Pufferfin/Configuration"),
            (HttpMethod.Post, "Pufferfin/Configuration/Services/Radarr/Test"),
            (HttpMethod.Get, "Pufferfin/Configuration/AutoConfig"),
            (HttpMethod.Post, "Pufferfin/Configuration/AutoConfig"),
        ];

        private readonly JellyfinApplicationFactory _factory;
        private readonly JsonSerializerOptions _jsonOptions = JsonDefaults.Options;

        public PufferfinConfigurationControllerTests(JellyfinApplicationFactory factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task ConfigurationEndpoints_AreRefused_ToAnonymousAndNonAdministrators()
        {
            var adminClient = _factory.CreateClient();
            adminClient.DefaultRequestHeaders.AddAuthHeader(await AuthHelper.CompleteStartupAsync(adminClient));

            using var adminResponse = await adminClient.GetAsync("Pufferfin/Configuration", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, adminResponse.StatusCode);

            var userClient = _factory.CreateClient();
            userClient.DefaultRequestHeaders.AddAuthHeader(await CreateUserAndLogInAsync(adminClient, userClient));

            var anonymousClient = _factory.CreateClient();

            foreach (var (method, path) in _configurationEndpoints)
            {
                using var userResponse = await userClient.SendAsync(CreateRequest(method, path), TestContext.Current.CancellationToken);
                Assert.True(userResponse.StatusCode == HttpStatusCode.Forbidden, $"{method} {path} returned {userResponse.StatusCode} to a non administrator.");

                using var anonymousResponse = await anonymousClient.SendAsync(CreateRequest(method, path), TestContext.Current.CancellationToken);
                Assert.True(anonymousResponse.StatusCode == HttpStatusCode.Unauthorized, $"{method} {path} returned {anonymousResponse.StatusCode} to an anonymous client.");
            }

            // Capabilities are for every signed in user, and only for them.
            using var capabilities = await userClient.GetAsync("Pufferfin/Capabilities", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, capabilities.StatusCode);
            using var anonymousCapabilities = await anonymousClient.GetAsync("Pufferfin/Capabilities", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousCapabilities.StatusCode);
        }

        private static HttpRequestMessage CreateRequest(HttpMethod method, string path)
            => new(method, path)
            {
                Content = method == HttpMethod.Post ? JsonContent.Create(new { Settings = new { SEED_MIN_DAYS = 1 } }) : null
            };

        private async Task<string> CreateUserAndLogInAsync(HttpClient adminClient, HttpClient userClient)
        {
            const string Password = "pufferfin-user-password";
            var name = "pufferfin-user-" + Guid.NewGuid().ToString("N")[..8];
            using var created = await adminClient.PostAsJsonAsync("Users/New", new CreateUserByName { Name = name, Password = Password }, _jsonOptions, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, created.StatusCode);

            using var request = new HttpRequestMessage(HttpMethod.Post, "Users/AuthenticateByName");
            request.Headers.TryAddWithoutValidation(AuthHelper.AuthHeaderName, AuthHelper.DummyAuthHeader.Replace("69420", "pufferfin-user", StringComparison.Ordinal));
            request.Content = JsonContent.Create(new AuthenticateUserByName { Username = name, Pw = Password }, options: _jsonOptions);
            using var response = await userClient.SendAsync(request, TestContext.Current.CancellationToken);
            response.EnsureSuccessStatusCode();

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            return document.RootElement.GetProperty("AccessToken").GetString()!;
        }
    }
}
