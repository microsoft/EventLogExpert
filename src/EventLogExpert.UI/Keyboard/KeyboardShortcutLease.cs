// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Keyboard;

public sealed class KeyboardShortcutLease
{
    private volatile bool _isReleased;

    internal KeyboardShortcutLease(long sequence) => Sequence = sequence;

    internal bool IsReleased => _isReleased;

    internal long Sequence { get; }

    internal void Release() => _isReleased = true;
}
