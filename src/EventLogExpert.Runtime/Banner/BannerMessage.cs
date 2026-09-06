// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Banner;

public abstract partial record BannerMessage
{
    public virtual bool RequiresAction => false;
}
