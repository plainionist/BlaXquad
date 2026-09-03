using System.Text.Json;
using GitHub.Copilot;

namespace squad.CopilotSdk;

internal sealed class CopilotSdkRuntimeSession : IAsyncDisposable
{
    private readonly CopilotSession mySession;
    private readonly Lazy<Task<long?>> myContextLimit;

    public CopilotSdkRuntimeSession(CopilotSession session)
    {
        mySession = session;
        myContextLimit = CreateContextLimitCache(GetContextLimitAsync);
    }

    public string SessionId => mySession.SessionId;

    public Task SendAsync(string prompt, CancellationToken cancellationToken = default) =>
        SendSdkAsync(prompt, cancellationToken);

    public Task AbortAsync(CancellationToken cancellationToken = default) =>
        mySession.AbortAsync(cancellationToken);

    internal void StartContextWindowResolution() => _ = myContextLimit.Value;

    public async Task<(long UsedTokens, long LimitTokens)?> GetContextUsageAsync(CancellationToken cancellationToken = default)
    {
        var contextLimit = myContextLimit.Value;
        var attribution = await mySession.Rpc.Metadata.GetContextAttributionAsync(cancellationToken);
        var context = attribution?.ContextAttribution;
        var limit = await contextLimit;
        return limit is > 0 ? (context?.TotalTokens ?? 0, limit.Value) : null;
    }

    public async Task<decimal> GetAicUsageAsync(CancellationToken cancellationToken = default)
    {
        var usage = await mySession.Rpc.Usage.GetMetricsAsync(cancellationToken);
        return Convert.ToDecimal((object?)usage.TotalNanoAiu) / 1_000_000_000m;
    }

    public ValueTask DisposeAsync() => mySession.DisposeAsync();

    internal static long? CalculateContextLimit(
        long? defaultPromptTokens,
        long? maxOutputTokens,
        long? fallbackPromptTokens,
        long? attributionLimit) =>
        defaultPromptTokens is > 0
            ? defaultPromptTokens.Value + (maxOutputTokens ?? 0)
            : fallbackPromptTokens ?? attributionLimit;

    internal static Lazy<Task<T>> CreateContextLimitCache<T>(Func<Task<T>> lookup) =>
        new(lookup, LazyThreadSafetyMode.ExecutionAndPublication);

    private async Task<long?> GetContextLimitAsync()
    {
        var currentModel = await mySession.Rpc.Model.GetCurrentAsync(CancellationToken.None);

        var models = await mySession.Rpc.Model.ListAsync(new(), CancellationToken.None);
        var selectedModelData = models.List.FirstOrDefault(model =>
            model.TryGetProperty("id", out var id) && string.Equals(id.GetString(), currentModel.ModelId, StringComparison.Ordinal));

        return CalculateContextLimit(
            GetDefaultMaxPromptTokens(selectedModelData),
            GetMaxOutputTokens(selectedModelData),
            null,
            null);
    }

    private async Task SendSdkAsync(string prompt, CancellationToken cancellationToken) =>
        _ = await mySession.SendAsync(new MessageOptions
        {
            Prompt = prompt,
            Mode = "enqueue",
        }, cancellationToken);

    private static long GetMaxOutputTokens(JsonElement model)
    {
        var limits = GetProperty(model, "capabilities", "limits");
        return limits is { } value
            ? TryGetInt64(value, "maxOutputTokens") ?? TryGetInt64(value, "max_output_tokens") ?? 0
            : 0;
    }

    private static long? GetDefaultMaxPromptTokens(JsonElement model)
    {
        var defaultPrices = GetProperty(model, "billing", "tokenPrices", "default") ??
                            GetProperty(model, "billing", "token_prices", "default");
        return defaultPrices is { } value
            ? TryGetInt64(value, "maxPromptTokens") ?? TryGetInt64(value, "max_prompt_tokens")
            : null;
    }

    private static JsonElement? GetProperty(JsonElement element, params string[] propertyPath)
    {
        foreach (var propertyName in propertyPath)
        {
            if (element.ValueKind is not JsonValueKind.Object || !element.TryGetProperty(propertyName, out element))
                return null;
        }

        return element;
    }

    private static long? TryGetInt64(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.TryGetInt64(out var value) ? value : null;
}



