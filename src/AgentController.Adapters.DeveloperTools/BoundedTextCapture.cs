using System.Buffers;
using System.Text;

namespace AgentController.Adapters.DeveloperTools;

internal sealed record BoundedTextResult(string Text, bool Truncated);

internal static class BoundedTextCapture
{
    internal static async Task<BoundedTextResult> ReadAsync(
        StreamReader reader,
        int maxCharacters)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var builder = new StringBuilder(
            Math.Min(maxCharacters, 16 * 1024));
        var buffer = ArrayPool<char>.Shared.Rent(4 * 1024);
        var truncated = false;
        try
        {
            while (true)
            {
                var read = await reader.ReadAsync(buffer.AsMemory())
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                var remaining = maxCharacters - builder.Length;
                if (remaining > 0)
                {
                    builder.Append(buffer, 0, Math.Min(read, remaining));
                }

                truncated |= read > remaining;
            }
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }

        return new(builder.ToString(), truncated);
    }
}
