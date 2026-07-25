using PPather.Graph;

namespace PPather;

public sealed class SearchParam
{
    public string Continent { get; set; }
    public SearchStrategy SearchType { get; set; }
    public SearchLocation From { get; set; }
    public SearchLocation To { get; set; }

    /// <summary>Random corner displacement in yards; 0 disables jitter.</summary>
    public float Jitter { get; set; }

    /// <summary>Seed for <see cref="Jitter"/>; null draws a fresh seed per search.</summary>
    public int? Seed { get; set; }
}
