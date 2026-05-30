// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Menu;

public interface IMenuHostRegistry
{
    event Action? ActiveHostChanged;

    MenuHost? ActiveHost { get; }

    void Register(MenuHost host);

    void Unregister(MenuHost host);
}
