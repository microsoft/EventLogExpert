// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Buffers;

namespace EventLogExpert.Eventing.PublisherMetadata.Wevt;

internal static class WevtMessageFormatter
{
    private const int MaxStackAllocChars = 4096;

    internal static string Format(string raw)
    {
        if (raw.AsSpan().IndexOfAny('%', '\r', '\n') < 0)
        {
            return raw;
        }

        char[]? rented = null;

        Span<char> buffer = raw.Length <= MaxStackAllocChars
            ? stackalloc char[raw.Length]
            : (rented = ArrayPool<char>.Shared.Rent(raw.Length));

        try
        {
            int length = 0;

            for (int index = 0; index < raw.Length; index++)
            {
                char current = raw[index];

                // MAX_WIDTH fold: a literal CRLF or lone CR/LF collapses to one space (a %n-emitted CRLF is not re-folded).
                if (current == '\r')
                {
                    if (index + 1 < raw.Length && raw[index + 1] == '\n')
                    {
                        index++;
                    }

                    buffer[length++] = ' ';

                    continue;
                }

                if (current == '\n')
                {
                    buffer[length++] = ' ';

                    continue;
                }

                if (current != '%')
                {
                    buffer[length++] = current;

                    continue;
                }

                if (index + 1 >= raw.Length)
                {
                    buffer[length++] = '%';

                    continue;
                }

                char escape = raw[index + 1];

                switch (escape)
                {
                    case 'n':
                        buffer[length++] = '\r';
                        buffer[length++] = '\n';
                        index++;
                        break;
                    case 't':
                        buffer[length++] = '\t';
                        index++;
                        break;
                    case 'b':
                        buffer[length++] = ' ';
                        index++;
                        break;
                    case 'r':
                        buffer[length++] = '\r';
                        index++;
                        break;
                    case '%':
                        // %% stays doubled (IGNORE_INSERTS) so DescriptionFormatter can later resolve %%nnnn parameter inserts.
                        buffer[length++] = '%';
                        buffer[length++] = '%';
                        index++;
                        break;
                    case '0':
                        return new string(buffer[..length]);
                    default:
                        buffer[length++] = '%';
                        break;
                }
            }

            return new string(buffer[..length]);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<char>.Shared.Return(rented);
            }
        }
    }
}
