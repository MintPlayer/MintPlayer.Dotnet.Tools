using MintPlayer.StringBuilder.Extensions.Exceptions;

namespace MintPlayer.StringBuilder.Extensions;

public static class StringBuilderExtensions
{
    private static Dictionary<System.Text.StringBuilder, StringBuilderState> states = new Dictionary<System.Text.StringBuilder, StringBuilderState>();
    private static System.Text.StringBuilder AppendIndentation(this System.Text.StringBuilder builder)
    {
        return builder.AppendJoin(null, states[builder].Indentations.Select(s => string.Concat(Enumerable.Repeat(s.IndentationType switch { EIndentationType.Space => ' ', _ => '\t' }, s.Size))));
    }

    private static StringBuilderState EnsurePresent(this System.Text.StringBuilder builder)
    {
        if (states.ContainsKey(builder))
        {
            return states[builder];
        }
        else
        {
            var state = new StringBuilderState();
            states.Add(builder, state);
            return state;
        }
    }

    public static System.Text.StringBuilder Indent(this System.Text.StringBuilder builder, EIndentationType type, int size)
    {
        var state = builder.EnsurePresent();
        state.Indentations.Push(new Indentation { IndentationType = type, Size = size });
        return builder;
    }

    public static System.Text.StringBuilder Unindent(this System.Text.StringBuilder builder)
    {
        if (!states.ContainsKey(builder))
            throw new StringBuilderNotFoundException();

        states[builder].Indentations.Pop();
        return builder;
    }

    public static System.Text.StringBuilder AppendIndented(this System.Text.StringBuilder builder, string? value)
    {
        if (value == null)
            return builder;

        var state = builder.EnsurePresent();
        var valueSpan = value.AsSpan();
        var nl = Environment.NewLine;

        // The slice past the last line used to happen unconditionally, with index == -1:
        // Slice(-1 + nl.Length). On Windows NewLine is two characters, so that was Slice(1)
        // on an already-consumed span and threw ArgumentOutOfRangeException — for an empty
        // string, or for any input ending in a newline. On Linux NewLine is one character,
        // so it was Slice(0) and the bug never showed. Advancing only when there IS a next
        // line removes the platform dependency.
        while (true)
        {
            var index = valueSpan.IndexOf(nl);
            var hasNext = index >= 0;

            var line = hasNext ? valueSpan.Slice(0, index) : valueSpan;
            builder.AppendIndentation();
            builder.Append(line);
            builder.AppendLine();

            if (!hasNext) break;

            valueSpan = valueSpan.Slice(index + nl.Length);
        }

        return builder;
    }
}