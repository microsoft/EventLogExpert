using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Shared.Base;
using EventLogExpert.UI;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Shared.Components.Filters;

public partial class FilterValueSelect : SelectComponent<string>
{
    private List<string> _items = new();
    private FilterType _type;

    [Parameter]
    public FilterType Type
    {
        get => _type;
        set
        {
            _type = value;
            ResetItems();
        }
    }

    private List<string> FilteredItems => _items.Where(x => x.ToLower().Contains(Value.ToLower())).ToList();

    private async void OnInputChange(ChangeEventArgs args)
    {
        Value = args.Value?.ToString() ?? string.Empty;
        await ValueChanged.InvokeAsync(Value);
    }

    private void ResetItems()
    {
        switch (Type)
        {
            case FilterType.Id :
                _items = EventLogState.Value.ActiveLogs.Values.SelectMany(log => log.EventIds)
                    .Distinct().OrderBy(id => id).Select(id => id.ToString()).ToList();

                break;
            case FilterType.Level :
                _items = new List<string>();

                foreach (SeverityLevel item in Enum.GetValues(typeof(SeverityLevel)))
                {
                    _items.Add(item.ToString());
                }

                break;
            case FilterType.KeywordsDisplayNames :
                _items = EventLogState.Value.ActiveLogs.Values.SelectMany(log => log.KeywordNames)
                    .Distinct().OrderBy(name => name).Select(name => name.ToString()).ToList();

                break;
            case FilterType.Source :
                _items = EventLogState.Value.ActiveLogs.Values.SelectMany(log => log.EventProviderNames)
                    .Distinct().OrderBy(name => name).Select(name => name.ToString()).ToList();

                break;
            case FilterType.TaskCategory :
                _items = EventLogState.Value.ActiveLogs.Values.SelectMany(log => log.TaskNames)
                    .Distinct().OrderBy(name => name).Select(name => name.ToString()).ToList();

                break;
            case FilterType.Description :
            default :
                break;
        }
    }
}
