namespace ArgoBooks.Core.Models.Dashboard;

public class DashboardWidgetEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public WidgetType WidgetType { get; set; }
    public WidgetSize Size { get; set; }

    public Dictionary<string, string> Config { get; set; } = new();

    public DashboardWidgetEntry() { }

    public DashboardWidgetEntry(WidgetType type, WidgetSize size)
    {
        WidgetType = type;
        Size = size;
    }

    public DashboardWidgetEntry Clone() => new()
    {
        Id = Id,
        WidgetType = WidgetType,
        Size = Size,
        Config = new Dictionary<string, string>(Config)
    };
}
