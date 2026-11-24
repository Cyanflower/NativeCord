using System.Globalization;
using System.Runtime.CompilerServices;

namespace NativeCord.Core.Common;

public static class StringHandler
{
    public static DefaultInterpolatedStringHandler New(
        Span<char> initialBuffer,
        int literalLength = 48,
        int formattedCount = 4)
    {
        return new DefaultInterpolatedStringHandler(
            literalLength,
            formattedCount,
            CultureInfo.InvariantCulture,
            initialBuffer);
    }
}