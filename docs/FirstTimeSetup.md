# [EventLogExpert](Home.md)

## First time setup

After installing EventLogExpert, there are a few settings you may want to change.

### Set the time zone

From Tools -> Options, the time zone can be changed at any time. Any .evtx files that are opened will be adjusted to show in the selected time zone. This allows the Combined view to line up events from machines in different time zones. It can also be helpful to set this to the time of a particular server, or to the time zone of a customer you are working with, so you can view the event logs as if you were in their time zone.

### Opt in to prerelease builds

As of this writing, the tool is under active development. In Tools -> Options, you can opt in to prereleases to test the latest features.

### Choose the Description pane behavior

By default, the Description pane at the bottom will pop open the first time an event is selected. If the user collapses it, it will then stay closed until it is manually expanded again. Tools -> Options has a setting that makes this pane always pop open when the selection is changed.

### Import provider databases

If you have created event databases, they can be imported in Tools -> Options. See [Provider Databases](ProviderDatabases.md).

[Docs home](Home.md)
