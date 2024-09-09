namespace System.Collections.Generic;

public static class OneOfExtension
{
    /// <summary>
    /// Simplifies include checks.
    /// The following snippet throws "There is no target type for the collection expression"
    /// <code>
    /// [ELineDiffStatus.Removed, ELineDiffStatus.Unchanged].Contains(line.Status)
    /// </code>
    /// Using OneOf on the other hand works fine
    /// <code>
    /// line.Status.OneOf([ELineDiffStatus.Removed, ELineDiffStatus.Unchanged])
    /// </code>
    /// </summary>
    /// <typeparam name="T">Type of enumerable</typeparam>
    /// <param name="value">Item to find</param>
    /// <param name="items">Enumerable</param>
    /// <returns></returns>
    public static bool OneOf<T>(this T value, IEnumerable<T> items) => items.Contains(value);
}
