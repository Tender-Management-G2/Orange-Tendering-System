namespace Orange_Tendering_System.Services.Assistant;

public enum ColumnKind { Text, Number, Money, Date, DateTime }

public sealed record AssistantColumn(string Header, ColumnKind Kind = ColumnKind.Text, double Width = 0);

// The only data the chat, the model and the report generators ever receive: rows already filtered by server-side authorization.
public sealed class AssistantDataset
{
    public required AssistantTopic Topic { get; init; }
    public required string Title { get; init; }
    public required bool Arabic { get; init; }
    public string ScopeNote { get; init; } = "";
    public List<AssistantColumn> Columns { get; } = [];
    public List<object?[]> Rows { get; } = [];
    public List<(string Label, string Value)> Summary { get; } = [];
    // Optional single series for a bar chart (e.g. tenders by status); values are real counts from the rows above.
    public List<(string Label, double Value)> Chart { get; } = [];
    public string? ChartTitle { get; set; }
    public List<string> Links { get; } = [];
    public bool Truncated { get; set; }
    public int TotalCount { get; set; }
    public string? Empty { get; set; }

    public string L(string en, string ar) => Arabic ? ar : en;
}
