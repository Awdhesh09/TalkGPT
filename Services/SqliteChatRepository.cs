using Microsoft.Data.Sqlite;

namespace TalkGPT.Services;

public interface IChatRepository
{
    Task InitializeAsync(CancellationToken cancellationToken);

    Task<bool> CreateUserAsync(
        string email,
        string salt,
        string passwordHash,
        CancellationToken cancellationToken);

    Task<(string Salt, string PasswordHash)?> GetUserCredentialsAsync(
        string email,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ConversationSummary>> GetConversationsAsync(
        string userId,
        CancellationToken cancellationToken);

    Task<StoredConversation?> GetConversationAsync(
        string userId,
        string conversationId,
        CancellationToken cancellationToken);

    Task SaveConversationAsync(
        string userId,
        string conversationId,
        string title,
        IReadOnlyList<ChatTurn> messages,
        CancellationToken cancellationToken);
}

public sealed class SqliteChatRepository(IConfiguration configuration) : IChatRepository
{
    private readonly string _connectionString = CreateConnectionString(configuration);

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Users (
                Id TEXT PRIMARY KEY,
                Email TEXT NOT NULL COLLATE NOCASE UNIQUE,
                PasswordSalt TEXT NOT NULL,
                PasswordHash TEXT NOT NULL,
                CreatedAt TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS Conversations (
                Id TEXT PRIMARY KEY,
                UserId TEXT NOT NULL REFERENCES Users(Id) ON DELETE CASCADE,
                Title TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_Conversations_UserId_UpdatedAt
                ON Conversations(UserId, UpdatedAt DESC);
            CREATE TABLE IF NOT EXISTS Messages (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ConversationId TEXT NOT NULL REFERENCES Conversations(Id) ON DELETE CASCADE,
                Position INTEGER NOT NULL,
                Role TEXT NOT NULL CHECK(Role IN ('user', 'assistant')),
                Content TEXT NOT NULL,
                UNIQUE(ConversationId, Position)
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);

        await using var foreignKeysCommand = connection.CreateCommand();
        foreignKeysCommand.CommandText = "PRAGMA foreign_keys = ON;";
        await foreignKeysCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> CreateUserAsync(
        string email,
        string salt,
        string passwordHash,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Users (Id, Email, PasswordSalt, PasswordHash, CreatedAt)
            VALUES ($id, $email, $salt, $hash, $createdAt)
            ON CONFLICT(Email) DO NOTHING;
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("$email", email);
        command.Parameters.AddWithValue("$salt", salt);
        command.Parameters.AddWithValue("$hash", passwordHash);
        command.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("O"));
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<(string Salt, string PasswordHash)?> GetUserCredentialsAsync(
        string email,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT PasswordSalt, PasswordHash FROM Users WHERE Email = $email;";
        command.Parameters.AddWithValue("$email", email);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return (reader.GetString(0), reader.GetString(1));
    }

    public async Task<IReadOnlyList<ConversationSummary>> GetConversationsAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var conversations = new List<ConversationSummary>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Title, UpdatedAt FROM Conversations
            WHERE UserId = $userId ORDER BY UpdatedAt DESC;
            """;
        command.Parameters.AddWithValue("$userId", userId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            conversations.Add(new ConversationSummary(
                reader.GetString(0),
                reader.GetString(1),
                DateTime.Parse(reader.GetString(2), null, System.Globalization.DateTimeStyles.RoundtripKind)));
        }

        return conversations;
    }

    public async Task<StoredConversation?> GetConversationAsync(
        string userId,
        string conversationId,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        string title;
        DateTime updatedAt;
        await using (var conversationCommand = connection.CreateCommand())
        {
            conversationCommand.CommandText = """
                SELECT Title, UpdatedAt FROM Conversations
                WHERE Id = $id AND UserId = $userId;
                """;
            conversationCommand.Parameters.AddWithValue("$id", conversationId);
            conversationCommand.Parameters.AddWithValue("$userId", userId);

            await using var reader = await conversationCommand.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            title = reader.GetString(0);
            updatedAt = DateTime.Parse(
                reader.GetString(1),
                null,
                System.Globalization.DateTimeStyles.RoundtripKind);
        }

        var messages = new List<ChatTurn>();
        await using (var messagesCommand = connection.CreateCommand())
        {
            messagesCommand.CommandText = """
                SELECT Role, Content FROM Messages
                WHERE ConversationId = $id ORDER BY Position;
                """;
            messagesCommand.Parameters.AddWithValue("$id", conversationId);

            await using var reader = await messagesCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                messages.Add(new ChatTurn(reader.GetString(0), reader.GetString(1)));
            }
        }

        return new StoredConversation(conversationId, title, updatedAt, messages);
    }

    public async Task SaveConversationAsync(
        string userId,
        string conversationId,
        string title,
        IReadOnlyList<ChatTurn> messages,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        await using (var conversationCommand = connection.CreateCommand())
        {
            conversationCommand.Transaction = transaction;
            conversationCommand.CommandText = """
                INSERT INTO Conversations (Id, UserId, Title, UpdatedAt)
                VALUES ($id, $userId, $title, $updatedAt)
                ON CONFLICT(Id) DO UPDATE SET Title = excluded.Title, UpdatedAt = excluded.UpdatedAt
                WHERE Conversations.UserId = excluded.UserId;
                """;
            conversationCommand.Parameters.AddWithValue("$id", conversationId);
            conversationCommand.Parameters.AddWithValue("$userId", userId);
            conversationCommand.Parameters.AddWithValue("$title", title);
            conversationCommand.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
            if (await conversationCommand.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException("Could not save this conversation for the current account.");
            }
        }

        await using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = "DELETE FROM Messages WHERE ConversationId = $id;";
            deleteCommand.Parameters.AddWithValue("$id", conversationId);
            await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        for (var index = 0; index < messages.Count; index++)
        {
            var message = messages[index];
            await using var messageCommand = connection.CreateCommand();
            messageCommand.Transaction = transaction;
            messageCommand.CommandText = """
                INSERT INTO Messages (ConversationId, Position, Role, Content)
                VALUES ($id, $position, $role, $content);
                """;
            messageCommand.Parameters.AddWithValue("$id", conversationId);
            messageCommand.Parameters.AddWithValue("$position", index);
            messageCommand.Parameters.AddWithValue("$role", message.Role!);
            messageCommand.Parameters.AddWithValue("$content", message.Content!);
            await messageCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static string CreateConnectionString(IConfiguration configuration)
    {
        var configuredPath = configuration.GetConnectionString("TalkGPT");
        var databasePath = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(AppContext.BaseDirectory, "app_data", "talkgpt.db")
            : configuredPath;

        if (!string.Equals(databasePath, ":memory:", StringComparison.OrdinalIgnoreCase))
        {
            var fullPath = Path.GetFullPath(databasePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            databasePath = fullPath;
        }

        return new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true
        }.ToString();
    }
}
