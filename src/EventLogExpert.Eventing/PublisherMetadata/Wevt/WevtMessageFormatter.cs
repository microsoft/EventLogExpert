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
            bool firstNumberedInsertSeen = false;

            for (int index = 0; index < raw.Length; index++)
            {
                char current = raw[index];

                // MAX_WIDTH folds literal newlines to one space; %n-emitted CRLF is not folded again.
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
                        // Preserve %%nnnn for render-time parameter resolution under FORMAT_MESSAGE_IGNORE_INSERTS semantics.
                        buffer[length++] = '%';
                        buffer[length++] = '%';
                        index++;
                        break;
                    case '0':
                        return new string(buffer[..length]);
                    case >= '1' and <= '9':
                        // Native strips !S!/!s! only from the first numbered insert; keep all other specs for parity.
                        buffer[length++] = '%';
                        buffer[length++] = escape;
                        index++;

                        if (index + 1 < raw.Length && raw[index + 1] is >= '0' and <= '9')
                        {
                            buffer[length++] = raw[index + 1];
                            index++;
                        }

                        if (!firstNumberedInsertSeen)
                        {
                            firstNumberedInsertSeen = true;

                            if (index + 1 < raw.Length && raw[index + 1] == '!')
                            {
                                int specClose = raw.IndexOf('!', index + 2);
                                bool singleCharStringSpec = specClose == index + 3 && raw[index + 2] is 'S' or 's';

                                if (singleCharStringSpec) { index = specClose; }
                            }
                        }

                        break;
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
