# [EventLogExpert](Home.md)

## Filtering

There are three ways to filter. All filters apply to all logs open in that window.

### Add filter

This button provides drop-down menus for easy filtering.

### Date filter

Any events falling outside of the starting and ending time specified here are hidden.

### Advanced filter

This button displays a textbox allowing a LINQ expression for filtering. Filterable properties are:

Property Name|Type
-|-
ComputerName|string
Description|string
Id|int
KeywordDisplayNames|IEnumerable<string>
Keywords|long?
LogName|string
OwningLog|string
Qualifiers|int?
RecordId|long?
SeverityLevel|Enum? (Error == 2, Warning == 3, Information == 4)
Source|string
TaskCategory|string
Template|string?
TimeCreated|DateTime
Xml|string

Note that Xml is generated when requested, so filters against Xml may be slower than filters against other properties.

[Docs home](Home.md)
