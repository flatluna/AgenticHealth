using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using PersonalAgent.Data;

namespace PersonalAgent.AzureFunctions;

public sealed class AuthFunctions
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDbContextFactory<PersonalAgentDbContext> _dbFactory;

    public AuthFunctions(IDbContextFactory<PersonalAgentDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    [Function("AuthMe")]
    public async Task<HttpResponseData> MeAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "auth/me")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var authHeader = request.Headers.TryGetValues("x-msal-user", out var values) ? values.FirstOrDefault() : null;
        if (string.IsNullOrWhiteSpace(authHeader))
        {
            return await FunctionResponseFactory.ErrorResponseAsync(request, "No autenticado", HttpStatusCode.Unauthorized);
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var user = await db.AppUsers
            .Include(u => u.Profile)
            .FirstOrDefaultAsync(u => u.AzureObjectId == authHeader, cancellationToken);

        if (user is null)
        {
            return await FunctionResponseFactory.ErrorResponseAsync(request, "Usuario no encontrado", HttpStatusCode.NotFound);
        }

        return await FunctionResponseFactory.SuccessResponseAsync(request, new
        {
            isAuthenticated = true,
            user = new
            {
                id = user.Id,
                azureObjectId = user.AzureObjectId,
                email = user.Email,
                displayName = user.DisplayName,
                preferredLanguage = user.PreferredLanguage,
                subscriptionStatus = user.SubscriptionStatus,
                profile = user.Profile is null ? null : new
                {
                    id = user.Profile.Id,
                    bio = user.Profile.Bio,
                    goal = user.Profile.Goal,
                    city = user.Profile.City,
                    country = user.Profile.Country,
                    preferredFocus = user.Profile.PreferredFocus,
                    timezone = user.Profile.Timezone,
                    wantsWellnessTips = user.Profile.WantsWellnessTips,
                }
            }
        });
    }

    [Function("AuthSubscribe")]
    public async Task<HttpResponseData> SubscribeAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/subscribe")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        try
        {
            var payload = await System.Text.Json.JsonSerializer.DeserializeAsync<AuthSubscribeRequest>(request.Body, JsonOptions, cancellationToken);
            if (payload is null || string.IsNullOrWhiteSpace(payload.AzureObjectId) || string.IsNullOrWhiteSpace(payload.Email))
            {
                return await FunctionResponseFactory.ErrorResponseAsync(request, "Datos inválidos", HttpStatusCode.BadRequest);
            }

            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            var existing = await db.AppUsers.FirstOrDefaultAsync(u => u.AzureObjectId == payload.AzureObjectId, cancellationToken);
            if (existing is not null)
            {
                existing.Email = payload.Email;
                existing.DisplayName = payload.DisplayName;
                existing.PreferredLanguage = payload.PreferredLanguage ?? existing.PreferredLanguage;
                existing.SubscriptionStatus = "active";
                existing.UpdatedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
                return await FunctionResponseFactory.SuccessResponseAsync(request, new { subscribed = true, userId = existing.Id });
            }

            var user = new AppUser
            {
                AzureObjectId = payload.AzureObjectId,
                Email = payload.Email,
                DisplayName = payload.DisplayName,
                PreferredLanguage = payload.PreferredLanguage ?? "en",
                SubscriptionStatus = "active",
            };

            db.AppUsers.Add(user);
            await db.SaveChangesAsync(cancellationToken);

            db.UserProfiles.Add(new UserProfile
            {
                AppUserId = user.Id,
                Timezone = payload.Timezone ?? "UTC",
                WantsWellnessTips = true,
            });
            await db.SaveChangesAsync(cancellationToken);

            return await FunctionResponseFactory.SuccessResponseAsync(request, new { subscribed = true, userId = user.Id });
        }
        catch (Exception ex)
        {
            var detail = ex.ToString();
            return await FunctionResponseFactory.ErrorResponseAsync(request, detail, HttpStatusCode.InternalServerError);
        }
    }

    [Function("AuthProfile")]
    public async Task<HttpResponseData> ProfileAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "auth/profile")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var authHeader = request.Headers.TryGetValues("x-msal-user", out var values) ? values.FirstOrDefault() : null;
        if (string.IsNullOrWhiteSpace(authHeader))
        {
            return await FunctionResponseFactory.ErrorResponseAsync(request, "No autenticado", HttpStatusCode.Unauthorized);
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var user = await db.AppUsers.Include(u => u.Profile).FirstOrDefaultAsync(u => u.AzureObjectId == authHeader, cancellationToken);
        if (user?.Profile is null)
        {
            return await FunctionResponseFactory.ErrorResponseAsync(request, "Perfil no encontrado", HttpStatusCode.NotFound);
        }

        return await FunctionResponseFactory.SuccessResponseAsync(request, new
        {
            profile = new
            {
                id = user.Profile.Id,
                bio = user.Profile.Bio,
                goal = user.Profile.Goal,
                city = user.Profile.City,
                country = user.Profile.Country,
                preferredFocus = user.Profile.PreferredFocus,
                timezone = user.Profile.Timezone,
                wantsWellnessTips = user.Profile.WantsWellnessTips,
            }
        });
    }

    [Function("AuthProfileSave")]
    public async Task<HttpResponseData> SaveProfileAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/profile")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var authHeader = request.Headers.TryGetValues("x-msal-user", out var values) ? values.FirstOrDefault() : null;
        if (string.IsNullOrWhiteSpace(authHeader))
        {
            return await FunctionResponseFactory.ErrorResponseAsync(request, "No autenticado", HttpStatusCode.Unauthorized);
        }

        var payload = await System.Text.Json.JsonSerializer.DeserializeAsync<ProfileSaveRequest>(request.Body, JsonOptions, cancellationToken);
        if (payload is null)
        {
            return await FunctionResponseFactory.ErrorResponseAsync(request, "Datos inválidos", HttpStatusCode.BadRequest);
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var user = await db.AppUsers.Include(u => u.Profile).FirstOrDefaultAsync(u => u.AzureObjectId == authHeader, cancellationToken);
        if (user is null)
        {
            return await FunctionResponseFactory.ErrorResponseAsync(request, "Usuario no encontrado", HttpStatusCode.NotFound);
        }

        if (user.Profile is null)
        {
            user.Profile = new UserProfile
            {
                AppUserId = user.Id,
                Timezone = payload.Timezone ?? "UTC",
            };
            db.UserProfiles.Add(user.Profile);
        }

        user.Profile.Bio = payload.Bio;
        user.Profile.Goal = payload.Goal;
        user.Profile.City = payload.City;
        user.Profile.Country = payload.Country;
        user.Profile.PreferredFocus = payload.PreferredFocus;
        user.Profile.Timezone = payload.Timezone ?? user.Profile.Timezone;
        user.Profile.WantsWellnessTips = payload.WantsWellnessTips;
        user.Profile.UpdatedAtUtc = DateTime.UtcNow;
        user.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return await FunctionResponseFactory.SuccessResponseAsync(request, new { saved = true });
    }

    public sealed record AuthSubscribeRequest(
        string AzureObjectId,
        string Email,
        string DisplayName,
        string? PreferredLanguage,
        string? Timezone);

    public sealed record ProfileSaveRequest(
        string? Bio,
        string? Goal,
        string? City,
        string? Country,
        string? PreferredFocus,
        string? Timezone,
        bool WantsWellnessTips);
}
