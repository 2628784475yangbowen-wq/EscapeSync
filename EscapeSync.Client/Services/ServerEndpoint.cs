namespace EscapeSync.Client.Services;

/// <summary>
/// Immutable wrapper around the server's base URL so it can be injected.
/// </summary>
public record ServerEndpoint(string BaseUrl);
