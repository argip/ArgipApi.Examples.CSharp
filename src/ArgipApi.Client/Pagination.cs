namespace ArgipApi.Client;

/// <summary>Content of the X-Pagination response header of paged lists. Pages are numbered from 0.</summary>
public sealed record Pagination(int TotalCount, int TotalPages, string? PrevPageLink, string? NextPageLink);

/// <summary>One page of a paged list.</summary>
public sealed record Page<T>(IReadOnlyList<T> Items, Pagination Pagination);
