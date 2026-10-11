using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace TalkGPT.Services;

public sealed class HuggingFaceOptions
{
    public const string SectionName = "HuggingFace";

    public string? Token { get; set; }

    public string Model { get; set; } = "Qwen/Qwen2.5-7B-Instruct-1M";
}

public interface IChatService
{
    Task<string> GetReplyAsync(ChatRequest request, CancellationToken cancellationToken);
}

public sealed class HuggingFaceException(string message, int statusCode, Exception? innerException = null)
    : Exception(message, innerException)
{
    public int StatusCode { get; } = statusCode;
}

public sealed class HuggingFaceChatService(
    HttpClient httpClient,
    IOptions<HuggingFaceOptions> options,
    ILogger<HuggingFaceChatService> logger) : IChatService
{
    private const string SystemPrompt =
        "You are TalkGPT, a helpful and thoughtful assistant. Reply in the same language as the user's latest message.";

    public async Task<string> GetReplyAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        var token = options.Value.Token;
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new HuggingFaceException(
                "Hugging Face is not configured. Set the HuggingFace__Token environment variable and restart the app.",
                StatusCodes.Status503ServiceUnavailable);
        }

        var model = options.Value.Model;
        if (string.IsNullOrWhiteSpace(model))
        {
            throw new HuggingFaceException(
                "Hugging Face is not configured. Set HuggingFace:Model to an available Inference Providers model.",
                StatusCodes.Status503ServiceUnavailable);
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(new ChatCompletionRequest(
                model,
                new[] { new ChatCompletionMessage("system", SystemPrompt) }
                    .Concat((request.History ?? [])
                        .TakeLast(20)
                        .Select(turn => new ChatCompletionMessage(turn.Role!, turn.Content!)))
                    .Append(new ChatCompletionMessage("user", request.Message!))
                    .ToArray()))
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());

        using var response = await httpClient.SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Hugging Face returned HTTP {StatusCode} for model {Model}.",
                (int)response.StatusCode,
                model);

            var (statusCode, detail) = response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                    (StatusCodes.Status503ServiceUnavailable,
                        "The Hugging Face token is invalid or does not have Inference Providers permission."),
                HttpStatusCode.TooManyRequests =>
                    (StatusCodes.Status429TooManyRequests,
                        "Hugging Face rate limit reached. Please wait and try again."),
                _ =>
                    (StatusCodes.Status502BadGateway,
                        $"Hugging Face could not serve model '{model}'. Check that the model is available to your account.")
            };

            throw new HuggingFaceException(detail, statusCode);
        }

        ChatCompletionResponse? completion;
        try
        {
            completion = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(
                cancellationToken: cancellationToken);
        }
        catch (JsonException exception)
        {
            logger.LogError(exception, "Hugging Face returned an invalid chat completion response.");
            throw new HuggingFaceException(
                "The AI service returned an unreadable response. Please try again.",
                StatusCodes.Status502BadGateway,
                exception);
        }

        var answer = completion?.Choices?.FirstOrDefault()?.Message?.Content;
        if (string.IsNullOrWhiteSpace(answer))
        {
            logger.LogWarning("Hugging Face returned no assistant message for model {Model}.", model);
            throw new HuggingFaceException(
                "The AI service returned an empty response. Please try again.",
                StatusCodes.Status502BadGateway);
        }

        return answer.Trim();
    }

    private sealed record ChatCompletionRequest(
        string Model,
        IReadOnlyList<ChatCompletionMessage> Messages)
    {
        [JsonPropertyName("max_tokens")]
        public int MaxTokens => 1_024;
    }

    private sealed record ChatCompletionMessage(string Role, string Content);

    private sealed record ChatCompletionResponse(IReadOnlyList<ChatCompletionChoice>? Choices);

    private sealed record ChatCompletionChoice(ChatCompletionMessage? Message);
}
