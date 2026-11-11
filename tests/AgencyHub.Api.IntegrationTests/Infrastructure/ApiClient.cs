using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgencyHub.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AgencyHub.Api.IntegrationTests.Infrastructure;

internal sealed record AgencyUserClient(HttpClient Http, TestUser User) : IDisposable
{
    public void Dispose() => Http.Dispose();
}

/// <summary>JSON helpers matching the API's serializer settings (web defaults + string enums).</summary>
internal static class ApiClient
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Creates a user with the given role in a (new or given) agency and returns a client carrying its access token.</summary>
    public static async Task<AgencyUserClient> CreateAgencyUserClientAsync(
        this WebApplicationFactory<Program> factory,
        AgencyRole role = AgencyRole.Owner,
        Guid? agencyId = null)
    {
        var user = await factory.CreateUserAsync(agencyId, role);
        var http = factory.CreateHttpsClient();
        var login = await http.LoginAsync(user);
        if (login.Body is null)
        {
            throw new InvalidOperationException($"Login failed with {(int)login.Response.StatusCode}.");
        }

        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Body.AccessToken);
        return new AgencyUserClient(http, user);
    }

    public static Task<HttpResponseMessage> PostJsonAsync(this HttpClient client, string path, object body) =>
        client.PostAsJsonAsync(new Uri(path, UriKind.Relative), body, Json, TestContext.Current.CancellationToken);

    public static Task<HttpResponseMessage> PutJsonAsync(this HttpClient client, string path, object body) =>
        client.PutAsJsonAsync(new Uri(path, UriKind.Relative), body, Json, TestContext.Current.CancellationToken);

    public static Task<HttpResponseMessage> GetPathAsync(this HttpClient client, string path) =>
        client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

    public static Task<HttpResponseMessage> DeletePathAsync(this HttpClient client, string path) =>
        client.DeleteAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            throw new InvalidOperationException($"Expected success but got {(int)response.StatusCode}: {body}");
        }

        return (await response.Content.ReadFromJsonAsync<T>(Json, TestContext.Current.CancellationToken))!;
    }

    public static async Task<Guid> CreateAndReadIdAsync(this HttpClient client, string path, object body)
    {
        using var response = await client.PostJsonAsync(path, body);
        using var json = JsonDocument.Parse(await response.EnsureSuccessStatusCode().Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return json.RootElement.GetProperty("id").GetGuid();
    }
}
