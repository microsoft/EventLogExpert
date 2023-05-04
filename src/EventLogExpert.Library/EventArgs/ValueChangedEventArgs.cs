// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Library.EventArgs;

public class ValueChangedEventArgs
{
    public object Value { get; private set; }

    public ValueChangedEventArgs(object value) => Value = value;
}
