using System.Text;

namespace MintPlayer.SourceGenerators.Tools;

public class IndentedStringBuilder
{
    private StringBuilder builder = new StringBuilder();

    public int IndentationSize { get; set; } = 4;
    public int IndentationLevel { get; set; }
    public IndentationStyle IndentationStyle { get; set; } = IndentationStyle.Tabs;

    public void AppendLine() => builder.AppendLine();
    public void AppendLine(string value) => builder.AppendLine(value);

    public void Append(bool value) => builder.Append(value);
    public void Append(byte value) => builder.Append(value);
    public void Append(char value) => builder.Append(value);
    public void Append(char[] value) => builder.Append(value);
    public void Append(decimal value) => builder.Append(value);
    public void Append(double value) => builder.Append(value);
    public void Append(float value) => builder.Append(value);
    public void Append(int value) => builder.Append(value);
    public void Append(long value) => builder.Append(value);
    public void Append(object value) => builder.Append(value);
    public void Append(sbyte value) => builder.Append(value);
    public void Append(short value) => builder.Append(value);
    public void Append(string value) => builder.Append(value);
    public void Append(uint value) => builder.Append(value);
    public void Append(ulong value) => builder.Append(value);
    public void Append(ushort value) => builder.Append(value);
    public void Append(char value, int repeatCount) => builder.Append(value, repeatCount);
    public void Append(char[] value, int startIndex, int charCount) => builder.Append(value, startIndex, charCount);
    public void Append(string value, int startIndex, int count) => builder.Append(value, startIndex, count);

    public void AppendFormat(IFormatProvider provider, string format, object arg0) => builder.AppendFormat(provider, format, arg0);
    public void AppendFormat(IFormatProvider provider, string format, object arg0, object arg1) => builder.AppendFormat(provider, format, arg0, arg1);
    public void AppendFormat(IFormatProvider provider, string format, params object[] args) => builder.AppendFormat(provider, format, args);
    public void AppendFormat(string format, object arg0) => builder.AppendFormat(format, arg0);
    public void AppendFormat(string format, object arg0, object arg1) => builder.AppendFormat(format, arg0, arg1);
    public void AppendFormat(string format, object arg0, object arg1, object arg2) => builder.AppendFormat(format, arg0, arg1, arg2);
    public void AppendFormat(string format, params object[] args) => builder.AppendFormat(format, args);
}

public enum IndentationStyle
{
    Spaces,
    Tabs
}