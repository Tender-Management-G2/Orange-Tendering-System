using System.Net.Http.Json;
using System.Text.Json;
using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace Orange_Tendering_System.Services;

public sealed class ExecutiveAgentOptions
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "qwen3.5:9b";
    public int TimeoutSeconds { get; set; } = 120;
    public int MaxTransientRetries { get; set; } = 2;
    // Enable only for isolated test fixtures. Production diagnostics log metadata/hashes, never prompts or record contents.
    public string? DiagnosticDirectory { get; set; }
}

// Separate client/settings: the existing Gemma help chat and bid analysis are untouched.
public sealed class ExecutiveAgentClient(HttpClient http, IOptions<ExecutiveAgentOptions> options, ILogger<ExecutiveAgentClient> logger)
{
    private readonly HttpClient client = Configure(http);
    private static HttpClient Configure(HttpClient value) { value.Timeout = Timeout.InfiniteTimeSpan; return value; }
    public static readonly string[] Operations = ["request_weights", "approve_weights", "sign_evaluation_report", "approve_manager_report", "top_vote", "forward_report", "forward_decision", "notify_suppliers"];
    public static object[] Tools => [
        Tool("search_records", "Search records the signed-in user may access. Use this to resolve a name to an ID.", new { query = new { type = "string", maxLength = 100 } }, ["query"]),
        Tool("tender_status", "Read a permitted tender's live status using its ID. Never invent IDs.", new { tenderId = new { type = "integer", minimum = 1 } }, ["tenderId"]),
        Tool("open_report", "Get the current authorized report review and PDF links for the user to open. No signature or approval is performed.", new { tenderId = new { type = "integer", minimum = 1 } }, ["tenderId"]),
        Tool("draft_tender", "Open the existing Create Tender wizard (إنشاء مناقصة) for an eligible accepted purchase request. Sourcing Officer only. Use purchaseRequestId 0 when no request was named. The wizard inherits the request title, description and items. This tool only opens the wizard.",
            new { purchaseRequestId = new { type = "integer", minimum = 0 } }, ["purchaseRequestId"]),
        Tool("prepare_action", "Prepare ONE action for human review only; does NOT execute it. top_vote Reject is the existing cancellation decision (company no longer needs tender); Retender is for unacceptable offers. notify_suppliers requires separate passkey confirmation, creates existing delivery records and attempts configured email only after commit. Never confirm or sign for the user.",
            new { tenderId = new { type = "integer", minimum = 1 }, operation = new { type = "string", @enum = Operations }, comment = new { type = "string", maxLength = 2000 }, decision = new { type = "string", @enum = new[] { "Approve", "Reject", "Retender", "" } } }, ["tenderId", "operation", "comment", "decision"]),
        Tool("navigate_page", "Open a site page using its exact pageId from the supplied permitted-page catalog. Read-only navigation only; never approvals or mutations.", new { pageId = new { type = "string", maxLength = 150 } }, ["pageId"]),
        Tool("site_help", "Reread the reviewed website workflow and current user's page/action guide when more procedural context is needed. This is procedural help only, never live record state or execution. topic: the user's site question, up to 200 characters.", new { topic = new { type = "string", maxLength = 200 } }, ["topic"])
    ];
    private static object Tool(string name, string description, object properties, string[] required) => new { type = "function", function = new { name, description, parameters = new { type = "object", properties, required, additionalProperties = false } } };

    public async Task<JsonElement> Next(IReadOnlyList<object> messages, CancellationToken ct, Func<ExecutiveModelRetry, Task>? onRetry = null)
    {
        var config = options.Value;
        if (!Uri.TryCreate(config.BaseUrl, UriKind.Absolute, out var endpoint) || !endpoint.IsLoopback || endpoint.Scheme != "http" || config.Model != "qwen3.5:9b")
            throw new ExecutiveModelException("Configuration", "The Executive Agent requires local Ollama and the installed qwen3.5:9b model.");
        var maxAttempts=1+Math.Clamp(config.MaxTransientRetries,0,2);
        var request=new { model = config.Model, stream = false, think = false, messages, tools = Tools, options = new { temperature = 0, num_ctx = 8192, num_predict = 1024 } };
        var requestId=Guid.NewGuid().ToString("N");
        for(var attempt=1;attempt<=maxAttempts;attempt++)
        {
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(config.TimeoutSeconds,5,300)));
            var watch=Stopwatch.StartNew();string? raw=null;int? status=null;
            try
            {
                using var response=await client.PostAsJsonAsync(new Uri(endpoint,"/api/chat"),request,timeout.Token);
                status=(int)response.StatusCode;raw=await response.Content.ReadAsStringAsync(timeout.Token);
                await Capture(config.DiagnosticDirectory,requestId,attempt,request,raw,status,watch.Elapsed.TotalSeconds);
                if(!response.IsSuccessStatusCode)
                {
                    // This known Qwen parser failure is deterministic, not an availability error.
                    var parsing=raw.Contains("expected element type",StringComparison.OrdinalIgnoreCase)||raw.Contains("tool call parsing",StringComparison.OrdinalIgnoreCase);
                    var transient=!parsing&&(status is 408 or 429 or 502 or 503 or 504);
                    throw new ExecutiveModelException(parsing?"UpstreamToolParsing":"Http",parsing
                        ? $"Ollama could not parse Qwen's structured tool call (HTTP {status}). No tool from that response was invoked."
                        : $"Ollama returned HTTP {status}. No tool from that response was invoked.",transient,status);
                }
                JsonDocument body;
                try{body=JsonDocument.Parse(raw);}catch(JsonException e){throw new ExecutiveModelException("MalformedJson","Ollama returned malformed JSON; no tool was invoked.",false,status,e);}
                using(body)
                {
                    var root=body.RootElement;
                    if(root.ValueKind!=JsonValueKind.Object||!root.TryGetProperty("done",out var done)||done.ValueKind!=JsonValueKind.True||!root.TryGetProperty("message",out var message)||message.ValueKind!=JsonValueKind.Object)
                        throw new ExecutiveModelException("InvalidEnvelope","Ollama returned an incomplete response envelope; no tool was invoked.",false,status);
                    if(root.TryGetProperty("done_reason",out var reason)&&(reason.ValueKind!=JsonValueKind.String||reason.GetString()=="length"))throw new ExecutiveModelException("TruncatedResponse","Qwen returned an invalid finish reason or reached its output limit. No partial tool call was invoked.",false,status);
                    Validate(message,status);return message.Clone();
                }
            }
            catch(Exception e) when(e is ExecutiveModelException or HttpRequestException or OperationCanceledException)
            {
                var failure=e as ExecutiveModelException ?? (e is OperationCanceledException
                    ? new ExecutiveModelException(ct.IsCancellationRequested?"Cancelled":"Timeout",ct.IsCancellationRequested?"The model request was cancelled.":"The local model request timed out before dispatch.",!ct.IsCancellationRequested,status,e)
                    : new ExecutiveModelException("Connection","Could not connect to local Ollama. Check that the service is running.",true,status,e));
                failure.Attempt=attempt;failure.ResponseHash=raw==null?null:Reports.ReportCryptography.Hash(raw);
                logger.LogWarning("Executive model failure {Kind}; HTTP {HttpStatus}; attempt {Attempt}/{MaxAttempts}; elapsed {Elapsed:F2}s; response SHA256 {ResponseHash}",failure.Kind,status,attempt,maxAttempts,watch.Elapsed.TotalSeconds,failure.ResponseHash);
                if(!failure.Transient||attempt==maxAttempts||ct.IsCancellationRequested)throw failure;
                if(onRetry!=null)await onRetry(new(failure.Kind,attempt,maxAttempts,status));
                await Task.Delay(TimeSpan.FromMilliseconds(350*attempt),ct);
            }
        }
        throw new InvalidOperationException("Unreachable model retry state.");
    }

    private static void Validate(JsonElement message,int? status)
    {
        if(!message.TryGetProperty("role",out var role)||role.ValueKind!=JsonValueKind.String||role.GetString()!="assistant")throw new ExecutiveModelException("InvalidEnvelope","Ollama returned an invalid assistant message.",false,status);
        if(message.TryGetProperty("content",out var content)&&content.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))throw new ExecutiveModelException("InvalidEnvelope","Ollama returned invalid assistant content.",false,status);
        if(!message.TryGetProperty("tool_calls",out var calls))return; // Tool-like content remains ordinary text, never execution.
        if(calls.ValueKind!=JsonValueKind.Array||calls.GetArrayLength()>12)throw new ExecutiveModelException("InvalidEnvelope","Ollama returned an invalid tool-call array.",false,status);
        var schemas=JsonSerializer.SerializeToElement(Tools).EnumerateArray().Select(t=>t.GetProperty("function")).ToDictionary(t=>t.GetProperty("name").GetString()!,t=>t.Clone());
        foreach(var call in calls.EnumerateArray())
        {
            if(call.ValueKind!=JsonValueKind.Object||!call.TryGetProperty("function",out var f)||f.ValueKind!=JsonValueKind.Object||!f.TryGetProperty("name",out var name)||name.ValueKind!=JsonValueKind.String)
                throw new ExecutiveModelException("InvalidToolArguments","Qwen returned an invalid structured function call; no tool from the response was invoked.",false,status);
            if(!schemas.TryGetValue(name.GetString()!,out var schema))throw new ExecutiveModelException("UnsupportedTool","Qwen selected an unlisted tool; it was not invoked.",false,status);
            if(!f.TryGetProperty("arguments",out var args)||args.ValueKind!=JsonValueKind.Object)throw new ExecutiveModelException("InvalidToolArguments","Qwen tool arguments must be a structured object, not generated text.",false,status);
            var parameters=schema.GetProperty("parameters");var required=parameters.GetProperty("required").EnumerateArray().Select(x=>x.GetString()!).Order().ToArray();
            if(!args.EnumerateObject().Select(x=>x.Name).Order().SequenceEqual(required))throw new ExecutiveModelException("InvalidToolArguments","Qwen tool arguments do not match the permitted schema.",false,status);
            foreach(var arg in args.EnumerateObject())
            {
                var rule=parameters.GetProperty("properties").GetProperty(arg.Name);var type=rule.GetProperty("type").GetString();var valid=type switch{
                    "string"=>arg.Value.ValueKind==JsonValueKind.String&&(!rule.TryGetProperty("maxLength",out var length)||arg.Value.GetString()!.Length<=length.GetInt32()),
                    "integer"=>arg.Value.ValueKind==JsonValueKind.Number&&arg.Value.TryGetInt32(out var number)&&(!rule.TryGetProperty("minimum",out var min)||number>=min.GetInt32()),_=>false};
                if(rule.TryGetProperty("enum",out var choices))valid=valid&&choices.EnumerateArray().Any(c=>c.ValueKind==JsonValueKind.String&&arg.Value.ValueKind==JsonValueKind.String&&c.GetString()==arg.Value.GetString());
                if(!valid)throw new ExecutiveModelException("InvalidToolArguments","Qwen returned an invalid tool argument type or value; no tool from the response was invoked.",false,status);
            }
        }
    }
    private async Task Capture(string? directory,string id,int attempt,object request,string response,int? status,double seconds)
    {
        if(string.IsNullOrWhiteSpace(directory))return;
        try{Directory.CreateDirectory(directory);await File.WriteAllTextAsync(Path.Combine(directory,$"{id}-{attempt}-request.json"),JsonSerializer.Serialize(request));await File.WriteAllTextAsync(Path.Combine(directory,$"{id}-{attempt}-response.json"),JsonSerializer.Serialize(new{httpStatus=status,seconds,raw=response}));}
        catch(Exception e) when(e is IOException or UnauthorizedAccessException){logger.LogWarning("Could not save isolated model diagnostics; model dispatch policy was unchanged.");}
    }
}
