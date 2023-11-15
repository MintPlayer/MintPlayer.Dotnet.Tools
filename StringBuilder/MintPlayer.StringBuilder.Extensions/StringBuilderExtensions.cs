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

        var index = -1;
        var hasNext = false;
        do
        {
            index = valueSpan.IndexOf(nl);
            hasNext = (index >= 0);

            var line = hasNext ? valueSpan.Slice(0, index) : valueSpan.Slice(0);
            builder.AppendIndentation();
            builder.Append(line);
            builder.AppendLine();

            valueSpan = valueSpan.Slice(index + nl.Length);
        }
        while (hasNext);

        return builder;
    }
}