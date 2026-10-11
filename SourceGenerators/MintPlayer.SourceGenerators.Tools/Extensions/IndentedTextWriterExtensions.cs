using System.CodeDom.Compiler;

namespace MintPlayer.SourceGenerators.Tools;

public static class IndentedTextWriterExtensions
{
    /// <summary>
    /// Opens a code block with braces and increases the indentation. The returned IDisposable will close the block and decrease the indentation when disposed.
    /// </summary>
    /// <param name="writer">The <see cref="IndentedTextWriter"/> to write to.</param>
    /// <param name="line">The line that starts the block (e.g. a class, method, namespace declaration).</param>
    /// <returns>An <see cref="IDisposable"/> that will write the closing brace and decrease indentation when disposed.</returns>
    /// <example>
    /// <code>
    /// using (writer.OpenBlock("public class ExampleClass"))
    /// {
    ///     ...
    /// }
    /// </code>
    /// </example>
    public static IDisposableWriterIndent OpenBlock(this IndentedTextWriter writer, string line, bool writeBraces = true)
    {
        if (!string.IsNullOrEmpty(line)) writer.WriteLine(line);
        return new DisposableWriterIndent(writer, writeBraces);
    }

    /// <summary>
    /// Opens a <c>namespace {ns}</c> block, or does nothing when <paramref name="ns"/> is the global namespace
    /// (null, empty, whitespace, or Roslyn's <c>&lt;global namespace&gt;</c> display string).
    /// </summary>
    /// <param name="writer">The <see cref="IndentedTextWriter"/> to write to.</param>
    /// <param name="ns">The namespace, written as is, e.g. <c>PathSpec.ContainingNamespace</c>.</param>
    /// <returns>Closes the block when disposed; a no-op for the global namespace.</returns>
    /// <remarks>
    /// Always a block-scoped namespace, never a file-scoped one, so blocks for several namespaces and code in the
    /// global namespace can share one file, and the output compiles at every language version.
    /// </remarks>
    /// <example>
    /// <code>
    /// using (writer.OpenNamespace(spec.ContainingNamespace))
    /// using (writer.OpenPathSpec(spec, includeTarget: true))
    /// {
    ///     ...
    /// }
    /// </code>
    /// </example>
    public static IDisposableWriterIndent OpenNamespace(this IndentedTextWriter writer, string? ns)
    {
        if (string.IsNullOrWhiteSpace(ns) || ns == "<global namespace>")
            return NoOpWriterIndent.Instance;
        return writer.OpenBlock($"namespace {ns}");
    }

    /// <summary>
    /// Writes a single line to the specified <see cref="IndentedTextWriter"/>,
    /// temporarily increasing the indentation for that line.
    /// </summary>
    /// <remarks>This method increases the indentation level by one for the duration of writing the specified
    /// line, then restores the original indentation. Use this to output a line that should appear visually nested
    /// within the current context without affecting subsequent lines.</remarks>
    /// <param name="writer">The <see cref="IndentedTextWriter"/> to which the indented line will be written. Cannot be null.</param>
    /// <param name="line">The text of the line to write. If null, an empty line is written.</param>
    public static void IndentSingleLine(this IndentedTextWriter writer, string line)
    {
        writer.Indent++;
        writer.WriteLine(line);
        writer.Indent--;
    }
}

public interface IDisposableWriterIndent : IDisposable { }

internal sealed class NoOpWriterIndent : IDisposableWriterIndent
{
    public static readonly NoOpWriterIndent Instance = new();
    private NoOpWriterIndent() { }
    public void Dispose() { }
}

internal sealed class DisposableWriterIndent : IDisposableWriterIndent
{
    private readonly IndentedTextWriter writer;
    private readonly bool writeBraces;
    public DisposableWriterIndent(IndentedTextWriter writer, bool writeBraces)
    {
        this.writer = writer;
        this.writeBraces = writeBraces;
        if (writeBraces) writer.WriteLine("{");
        writer.Indent++;
    }

    public void Dispose()
    {
        writer.Indent--;
        if (writeBraces) writer.WriteLine("}");
    }
}