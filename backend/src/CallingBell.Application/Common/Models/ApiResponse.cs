namespace CallingBell.Application.Common.Models;

/// <summary>Standard response envelope: { success, message, data, pagination }.</summary>
public sealed class ApiResponse<T>
{
    public bool Success { get; init; }
    public string Message { get; init; } = "Success";
    public T? Data { get; init; }
    public PaginationMeta? Pagination { get; init; }
    public IDictionary<string, string[]>? Errors { get; init; }

    public static ApiResponse<T> Ok(T data, string message = "Success") => new() { Success = true, Message = message, Data = data };
}

public static class ApiResponse
{
    public static ApiResponse<IReadOnlyList<T>> Paged<T>(PagedResult<T> result) => new()
    {
        Success = true,
        Data = result.Items,
        Pagination = result.Meta
    };

    public static ApiResponse<object> Fail(string message, IDictionary<string, string[]>? errors = null) => new()
    {
        Success = false,
        Message = message,
        Errors = errors
    };
}

public sealed record PaginationMeta(int Page, int PageSize, int TotalCount, int TotalPages);

public sealed class PagedResult<T>
{
    public required IReadOnlyList<T> Items { get; init; }
    public required PaginationMeta Meta { get; init; }

    public static PagedResult<T> Create(IReadOnlyList<T> items, int page, int pageSize, int totalCount) => new()
    {
        Items = items,
        Meta = new PaginationMeta(page, pageSize, totalCount, pageSize == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize))
    };
}

/// <summary>Base for list queries. Page size is clamped so clients cannot request unbounded pages.</summary>
public abstract record PagedRequest
{
    private readonly int _page = 1;
    private readonly int _pageSize = 12;

    public int Page { get => _page; init => _page = value < 1 ? 1 : value; }
    public int PageSize { get => _pageSize; init => _pageSize = value is < 1 or > 100 ? 12 : value; }

    public int Skip => (Page - 1) * PageSize;
}
