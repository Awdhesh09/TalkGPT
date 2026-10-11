namespace TalkGPT.Services;

public sealed record AccountRequest(string? Email, string? Password);

public sealed record AccountResponse(string Email);

public sealed record ConversationSummary(string Id, string Title, DateTime UpdatedAt);

public sealed record StoredConversation(
    string Id,
    string Title,
    DateTime UpdatedAt,
    IReadOnlyList<ChatTurn> Messages);

public sealed record SaveConversationRequest(string? Title, IReadOnlyList<ChatTurn>? Messages);
