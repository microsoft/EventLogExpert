// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Common.AppTitle;

public interface IAppTitleTextComposer
{
    void Compose(AppTitleState state);
}
