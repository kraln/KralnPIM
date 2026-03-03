using PIM.Core.Models;
using PIM.Core.Providers;
using PIM.Server.Models;

namespace PIM.Server.Api;

internal static class SearchEndpoints
{
    internal static void MapSearchEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/search");

        group.MapGet("/", async (
            string q,
            string? scope,
            ISearchService searchService,
            CancellationToken ct) =>
        {
            var searchScope = ParseScope(scope);
            var result = await searchService.LocalSearchAsync(q, searchScope, ct: ct);
            return Results.Ok(result);
        });

        group.MapPost("/deep", async (
            DeepSearchRequest request,
            ISearchService searchService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Query))
                return Results.Json(new ErrorResponse("Query is required."),
                    ServerJsonContext.Default.ErrorResponse, statusCode: 400);

            var searchScope = ParseScope(request.Scope);
            var result = await searchService.DeepSearchAsync(request.Query, searchScope, ct);
            return Results.Ok(result);
        });
    }

    private static SearchScope ParseScope(string? scope) =>
        scope?.ToLowerInvariant() switch
        {
            "mail" => SearchScope.Mail,
            "calendar" => SearchScope.Calendar,
            _ => SearchScope.All
        };
}
