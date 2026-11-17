namespace NativeCord;

public readonly record struct ImageHash(string Value) : ISpanFormattable
{
    public bool IsAnimated => Value.AsSpan().StartsWith(MagicString.AnimatedPrefix);
    
    public override string ToString() => Value;

    string IFormattable.ToString(string? format, IFormatProvider? formatProvider)
        => Value ?? string.Empty;

    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        ReadOnlySpan<char> span = Value.AsSpan();
        if (span.TryCopyTo(destination))
        {
            charsWritten = span.Length;
            return true;
        }

        charsWritten = 0;
        return false;
    }
    
    public static implicit operator string(ImageHash hash) => hash.Value;
}