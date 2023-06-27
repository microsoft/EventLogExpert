// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Models;

public class DisplayConverter<T, U>
{
    public Func<U?, T?>? GetFunc { get; set; }

    public Func<T?, U?>? SetFunc { get; set; }

    public T? Get(U? value)
    {
        if (GetFunc is null) { return default; }

        try
        {
            return GetFunc(value);
        }
        catch
        { // TODO: Log Error
        }

        return default;
    }

    public U? Set(T? value)
    {
        if (SetFunc is null) { return default; }

        try
        {
            return SetFunc(value);
        }
        catch
        { // TODO: Log Error
        }

        return default;
    }
}
