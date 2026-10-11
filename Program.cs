using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using TalkGPT.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration["ConnectionStrings:TalkGPT"] ??=
    Path.Combine(builder.Environment.ContentRootPath, "app_data", "talkgpt.db");

builder.Services.Configure<HuggingFaceOptions>(
    builder.Configuration.GetSection(HuggingFaceOptions.SectionName));
builder.Services.AddHttpClient<IChatService, HuggingFaceChatService>(client =>
{
    client.BaseAddress = new Uri("https://router.huggingface.co/v1/");
    client.Timeout = TimeSpan.FromMinutes(2);
});
builder.Services.AddSingleton<IChatRepository, SqliteChatRepository>();
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "TalkGPT.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromDays(14);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

await app.Services.GetRequiredService<IChatRepository>()
    .InitializeAsync(CancellationToken.None);

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/account", (ClaimsPrincipal user) =>
{
    var email = user.FindFirstValue(ClaimTypes.Email);
    return string.IsNullOrEmpty(email)
        ? Results.Unauthorized()
        : Results.Ok(new AccountResponse(email));
}).RequireAuthorization();

app.MapPost("/api/register", async (
    AccountRequest request,
    IChatRepository repository,
    HttpContext context,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    var errors = AccountService.Validate(request);
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    var email = request.Email!.Trim().ToLowerInvariant();
    var (salt, hash) = AccountService.HashPassword(request.Password!);
    bool created;
    try
    {
        created = await repository.CreateUserAsync(email, salt, hash, cancellationToken);
    }
    catch (Exception exception) when (exception is Microsoft.Data.Sqlite.SqliteException)
    {
        logger.LogError(exception, "Could not create a TalkGPT account.");
        return Results.Problem(
            title: "Could not create your account.",
            detail: "The account could not be saved. Please try again.",
            statusCode: StatusCodes.Status500InternalServerError);
    }

    if (!created)
    {
        return Results.Conflict(new { message = "An account with this email already exists." });
    }

    await SignInAsync(context, email);
    return Results.Ok(new AccountResponse(email));
});

app.MapPost("/api/login", async (
    AccountRequest request,
    IChatRepository repository,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Email) ||
        request.Email.Length > 254 ||
        string.IsNullOrEmpty(request.Password) ||
        request.Password.Length > 128)
    {
        return Results.BadRequest(new { message = "Enter a valid email address and password." });
    }

    var email = request.Email.Trim().ToLowerInvariant();
    var credentials = await repository.GetUserCredentialsAsync(email, cancellationToken);
    if (credentials is null ||
        !AccountService.VerifyPassword(request.Password, credentials.Value.Salt, credentials.Value.PasswordHash))
    {
        return Results.Json(
            new { message = "Email or password is incorrect." },
            statusCode: StatusCodes.Status401Unauthorized);
    }

    await SignInAsync(context, email);
    return Results.Ok(new AccountResponse(email));
});

app.MapPost("/api/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.NoContent();
}).RequireAuthorization();

app.MapGet("/api/conversations", async (
    ClaimsPrincipal user,
    IChatRepository repository,
    CancellationToken cancellationToken) =>
{
    var conversations = await repository.GetConversationsAsync(
        user.FindFirstValue(ClaimTypes.NameIdentifier)!,
        cancellationToken);
    return Results.Ok(conversations);
}).RequireAuthorization();

app.MapGet("/api/conversations/{id}", async (
    string id,
    ClaimsPrincipal user,
    IChatRepository repository,
    CancellationToken cancellationToken) =>
{
    if (!Guid.TryParseExact(id, "N", out _))
    {
        return Results.NotFound();
    }

    var conversation = await repository.GetConversationAsync(
        user.FindFirstValue(ClaimTypes.NameIdentifier)!,
        id,
        cancellationToken);
    return conversation is null ? Results.NotFound() : Results.Ok(conversation);
}).RequireAuthorization();

app.MapPost("/api/chat", async (
    ChatRequest request,
    IChatService chatService,
    IChatRepository repository,
    ClaimsPrincipal user,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    var validationErrors = ChatRequestValidator.Validate(request);
    if (validationErrors.Count > 0)
    {
        return Results.ValidationProblem(validationErrors);
    }

    try
    {
        var answer = await chatService.GetReplyAsync(request, cancellationToken);
        if (request.History is not null)
        {
            var allMessages = request.History
                .Append(new ChatTurn("user", request.Message))
                .Append(new ChatTurn("assistant", answer))
                .ToArray();
            var title = request.History.Count == 0
                ? request.Message!.Trim()[..Math.Min(request.Message.Trim().Length, 100)]
                : request.Title!.Trim();
            await repository.SaveConversationAsync(
                user.FindFirstValue(ClaimTypes.NameIdentifier)!,
                request.ConversationId!,
                title,
                allMessages,
                cancellationToken);
        }

        return Results.Ok(new ChatResponse(answer));
    }
    catch (HuggingFaceException exception)
    {
        logger.LogWarning(exception, "Hugging Face chat request failed.");
        return Results.Problem(
            title: "The AI service could not complete your request.",
            detail: exception.Message,
            statusCode: exception.StatusCode);
    }
    catch (HttpRequestException exception)
    {
        logger.LogError(exception, "Could not connect to Hugging Face.");
        return Results.Problem(
            title: "Could not connect to the AI service.",
            detail: "Check your internet connection and try again.",
            statusCode: StatusCodes.Status502BadGateway);
    }
    catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
    {
        logger.LogWarning(exception, "Hugging Face chat request timed out.");
        return Results.Problem(
            title: "The AI service took too long to respond.",
            detail: "Please try again.",
            statusCode: StatusCodes.Status504GatewayTimeout);
    }
    catch (Microsoft.Data.Sqlite.SqliteException exception)
    {
        logger.LogError(exception, "Could not save conversation for the current user.");
        return Results.Problem(
            title: "Could not save your conversation.",
            detail: "Your AI response was generated, but could not be saved. Please try again.",
            statusCode: StatusCodes.Status500InternalServerError);
    }
    catch (InvalidOperationException exception)
    {
        logger.LogError(exception, "Could not save conversation for the current user.");
        return Results.Problem(
            title: "Could not save your conversation.",
            detail: "Your AI response was generated, but could not be saved. Please try again.",
            statusCode: StatusCodes.Status500InternalServerError);
    }
}).RequireAuthorization();

app.Run();

static Task SignInAsync(HttpContext context, string email)
{
    var claims = new[]
    {
        new Claim(ClaimTypes.NameIdentifier, email),
        new Claim(ClaimTypes.Email, email)
    };
    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    return context.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        new ClaimsPrincipal(identity));
}
