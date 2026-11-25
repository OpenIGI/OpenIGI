using System.Text.Json;

namespace OpenIGI.Releases.Accounts;

/// <summary>What the master's account, stats and runs routes answer a request they refuse with: the reason it gives.</summary>
internal static class MasterAnswers
{
    /// <summary>Throws an <see cref="AccountRefusedException"/> for a refusal, with the reason the master gave or its status.</summary>
    /// <param name="response">The answer.</param>
    /// <param name="server">What it is the master's, for a refusal that gives no reason: "account", "stats", "runs".</param>
    /// <param name="cancel">Stops reading it.</param>
    /// <returns>A task done once the answer is known not to be a refusal.</returns>
    public static async Task ThrowIfRefusedAsync(HttpResponseMessage response, string server, CancellationToken cancel)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string message = $"The {server} server said {(int)response.StatusCode}.";
        try
        {
            using JsonDocument said = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false));
            if (said.RootElement.TryGetProperty("error", out JsonElement error) && error.GetString() is { Length: > 0 } reason)
            {
                message = reason;
            }
        }
        catch (JsonException)
        {
            // Not the server's JSON: its status says enough.
        }

        throw new AccountRefusedException(message, response.StatusCode);
    }
}
