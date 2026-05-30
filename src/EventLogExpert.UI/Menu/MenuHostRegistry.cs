// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Menu;

internal sealed class MenuHostRegistry : IMenuHostRegistry
{
    private readonly Lock _gate = new();
    private readonly Stack<MenuHost> _stack = new();

    public event Action? ActiveHostChanged;

    public MenuHost? ActiveHost
    {
        get
        {
            using (_gate.EnterScope())
            {
                return _stack.Count > 0 ? _stack.Peek() : null;
            }
        }
    }

    public void Register(MenuHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        using (_gate.EnterScope()) { _stack.Push(host); }

        ActiveHostChanged?.Invoke();
    }

    public void Unregister(MenuHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        bool changed = false;

        using (_gate.EnterScope())
        {
            if (_stack.Count == 0) { return; }

            if (ReferenceEquals(_stack.Peek(), host))
            {
                _stack.Pop();
                changed = true;
            }
            else if (_stack.Any(h => ReferenceEquals(h, host)))
            {
                // Out-of-order dispose: DynamicComponent @key swaps may run a new
                // component's OnInitialized before the old component's Dispose.
                var retained = _stack
                    .Where(h => !ReferenceEquals(h, host))
                    .Reverse()
                    .ToList();
                _stack.Clear();
                foreach (var h in retained) { _stack.Push(h); }
                changed = true;
            }
        }

        if (changed) { ActiveHostChanged?.Invoke(); }
    }
}
