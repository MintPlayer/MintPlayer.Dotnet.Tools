namespace MintPlayer.Pagination.Tests;

internal sealed class PagedPerson
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Age { get; set; }
    public DateTime BirthDate { get; set; }

    /// <summary>A field rather than a property — SortByBase only resolves properties.</summary>
    public string NotAProperty = string.Empty;
}

internal sealed class PagedPersonDto
{
    public int Id { get; set; }
    public string Display { get; set; } = string.Empty;
}
