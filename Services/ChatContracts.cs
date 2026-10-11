namespace TalkGPT.Services;

public sealed record ChatRequest(
    string? Message,
    IReadOnlyList<ChatTurn>? History,
    string? ConversationId,
    string? Title);

public sealed record ChatTurn(string? Role, string? Content);

public sealed record ChatResponse(string Answer);

public static class ChatRequestValidator
{
    private const int MaxMessageLength = 8_000;
    private const int MaxHistoryMessages = 200;

    public static Dictionary<string, string[]> Validate(ChatRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.Message))
        {
            errors["message"] = ["Enter a message before sending."];
        }
        else if (request.Message.Length > MaxMessageLength)
        {
            errors["message"] = [$"Messages must be {MaxMessageLength} characters or fewer."];
        }

        if (request.History is null)
        {
            errors["history"] = ["Conversation history is required."];
        }
        else if (request.History.Count > MaxHistoryMessages)
        {
            errors["history"] = [$"Conversation history cannot exceed {MaxHistoryMessages} messages."];
        }
        else
        {
            for (var index = 0; index < request.History.Count; index++)
            {
                var turn = request.History[index];
                if (turn is null ||
                    (turn.Role != "user" && turn.Role != "assistant") ||
                    string.IsNullOrWhiteSpace(turn.Content) ||
                    turn.Content.Length > MaxMessageLength)
                {
                    errors[$"history[{index}]"] =
                    [
                        "Each history item must have a user or assistant role and contain a message of at most 8,000 characters."
                    ];
                }
            }
        }

        if (!Guid.TryParseExact(request.ConversationId, "N", out _))
        {
            errors["conversationId"] = ["A valid conversation ID is required."];
        }

        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 100)
        {
            errors["title"] = ["Conversation title must be between 1 and 100 characters."];
        }

        return errors;
    }
}
