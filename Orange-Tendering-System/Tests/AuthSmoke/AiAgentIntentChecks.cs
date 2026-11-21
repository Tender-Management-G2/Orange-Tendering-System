using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Orange_Tendering_System.Services.Assistant;
using Orange_Tendering_System.Services;

public static class AiAgentIntentChecks
{
    private sealed class EmbeddingHandler : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"embeddings\":[[1,0,0],[0,1,0]]}", Encoding.UTF8, "application/json") };
        }
    }

    public static async Task Run()
    {
        var cases = new (string Text, string[]? Expected)[]
        {
            ("Create a user named Lina Saleh with email lina@example.invalid as Finance in the IT department", ["create_user"]),
            ("Add an employee with the Finance role", ["create_user"]),
            ("Create an IT department, then create a user and assign them to that department", ["create_department", "create_user"]),
            ("بدي اعمل كاتاغوري جديده", ["create_category"]),
            ("أنشئ تصنيف مشتريات باسم أجهزة", ["create_category"]),
            ("Create a new procurement category called Hardware", ["create_category"]),
            ("How do I create a category?", null),
            ("List users", ["list_users"]),
            ("Find user Ahmad", ["find_user"]),
            ("Show Ahmad's profile", ["get_user_details"]),
            ("Change Ahmad's phone number", ["update_user_phone"]),
            ("Move Ahmad from Finance to IT", ["change_user_department"]),
            ("Update Ahmad's name to Ahmed Ali", ["update_user_name"]),
            ("Deactivate user Ahmad", ["deactivate_user"]),
            ("Reactivate user Ahmad", ["reactivate_user"]),
            ("Assign the Requesting Department role to Ahmad", ["prepare_user_role_change"]),
            ("Change Ahmad's role to Finance", ["prepare_user_role_change"]),
            ("غيّر صلاحية محمد", ["prepare_user_role_change"]),
            ("اعرض المستخدمين", ["list_users"]),
            ("انقل أحمد من Finance إلى IT", ["change_user_department"]),
            ("اعمللي يوزر جديد", ["create_user"]),
            ("كيف أغير رقم الموظف؟", null),
            ("Can I deactivate an administrator?", null)
        };

        var failed = 0;
        foreach (var (text, expected) in cases)
        {
            var actual = AgentTools.Detect(text);
            if (expected?.SequenceEqual(actual ?? []) == true || expected == null && actual == null)
                Console.WriteLine($"PASS agent intent: {text}");
            else
            {
                failed++;
                Console.WriteLine($"FAIL agent intent: {text}; expected [{string.Join(",", expected ?? [])}], got [{string.Join(",", actual ?? [])}]");
            }
        }
        Console.WriteLine($"RESULT AI_AGENT_INTENT_PASS={cases.Length - failed} FAIL={failed}");
        if (failed > 0) throw new Exception("AI agent intent checks failed.");
        if (!AgentTools.IsCreationFollowUp("انت بدك تعملها انشاء") ||
            !AgentTools.IsCreationFollowUp("بدي تعملو انشاء") ||
            AgentTools.RecoverCreation(["اعمللي يوزر جديد"])?.Tools.SequenceEqual(["create_user"]) != true ||
            AgentTools.RecoverCategoryRequest(["Redai", "بدي اعمل كاتاغوري جديده"]) != "بدي اعمل كاتاغوري جديده باسم Redai" ||
            AgentTools.RecoverCategoryRequest(["Redai", "شو عندي أقسام؟"]) != null)
            throw new Exception("Category creation follow-up checks failed.");
        Console.WriteLine("PASS category creation follow-up uses the user's earlier category request and name");

        var handler = new EmbeddingHandler();
        using var client = new HttpClient(handler);
        var provider = new OllamaEmbeddingProvider(client, Options.Create(new OllamaSettings { BaseUrl = "http://localhost:11434", EmbeddingModel = "bge-m3", NumGpu = 99 }));
        var vectors = await provider.EmbedAsync(["supplier performance", "أداء المورد"], CancellationToken.None);
        using var request = JsonDocument.Parse(handler.RequestBody ?? "{}");
        var inputs = request.RootElement.GetProperty("input").EnumerateArray().Select(x => x.GetString()).ToArray();
        if (vectors.Count != 2 || vectors.Any(v => v.Length != 3) || request.RootElement.GetProperty("model").GetString() != "bge-m3" || !inputs.SequenceEqual(["supplier performance", "أداء المورد"]))
            throw new Exception("Multilingual embedding provider request/response check failed.");
        Console.WriteLine("PASS knowledge embeddings: bilingual Ollama batch request validated");
    }
}
