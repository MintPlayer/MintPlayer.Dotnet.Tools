namespace MintPlayer.Assertions.Equivalency;

/// <summary>
/// One structural difference found by the equivalency engine: the path into the object graph
/// where it was found (empty for the root; e.g. <c>"Address.City"</c>, <c>"Items[2].Name"</c>,
/// <c>"[key]"</c>) and a human-readable description of what differs there.
/// </summary>
public sealed record Difference(string Path, string Message);
