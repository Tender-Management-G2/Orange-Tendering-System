using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orange_Tendering_System.Services;

// Negative transport/protocol fault injection only. No successful model reply is
// fabricated, no dispatcher exists here, and no SQL/workflow operation is called.
public static class ExecutiveModelFailureChecks
{
    sealed class FaultHandler(int status,string body,string? failure=null) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Calls++;
            if(failure=="connection")throw new HttpRequestException("Injected connection failure");
            if(failure=="timeout")throw new TaskCanceledException("Injected model timeout");
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status){Content=new StringContent(body)});
        }
    }
    public static async Task Run()
    {
        var cases=new[]{
            (503,"{\"error\":\"busy\"}","Http",3,(string?)null),
            (429,"{\"error\":\"busy\"}","Http",3,(string?)null),
            (500,"{\"error\":\"expected element type <function> but have <parameter>\"}","UpstreamToolParsing",1,(string?)null),
            (500,"{\"error\":\"internal error\"}","Http",1,(string?)null),
            (200,"not-json","MalformedJson",1,(string?)null),
            (200,"{\"done\":false,\"message\":{\"role\":\"assistant\"}}","InvalidEnvelope",1,(string?)null),
            (200,"{\"done\":true,\"message\":{\"role\":\"assistant\",\"tool_calls\":[{\"function\":{\"name\":\"tender_status\",\"arguments\":\"{tenderId:1}\"}}]}}","InvalidToolArguments",1,(string?)null),
            (200,"{\"done\":true,\"message\":{\"role\":\"assistant\",\"tool_calls\":[{\"function\":{\"name\":\"execute_sql\",\"arguments\":{}}}]}}","UnsupportedTool",1,(string?)null),
            (200,"{\"done\":true,\"message\":{\"role\":\"assistant\",\"tool_calls\":[{\"function\":{\"name\":\"tender_status\",\"arguments\":{\"tenderId\":\"1\"}}}]}}","InvalidToolArguments",1,(string?)null),
            (0,"","Connection",3,"connection"),(0,"","Timeout",3,"timeout")
        };
        var count=0;
        foreach(var test in cases)
        {
            using var handler=new FaultHandler(test.Item1,test.Item2,test.Item5);using var http=new HttpClient(handler);
            var client=new ExecutiveAgentClient(http,Options.Create(new ExecutiveAgentOptions{MaxTransientRetries=2}),NullLogger<ExecutiveAgentClient>.Instance);
            var retries=0;
            try{await client.Next([new{role="user",content="Negative fault-injection test; never execute"}],CancellationToken.None,_=>{retries++;return Task.CompletedTask;});throw new Exception("FAIL expected protocol rejection");}
            catch(ExecutiveModelException e)
            {
                if(e.Kind!=test.Item3||handler.Calls!=test.Item4||retries!=test.Item4-1)throw new Exception($"FAIL kind={e.Kind} attempts={handler.Calls} expected={test.Item3}/{test.Item4}");
                Console.WriteLine($"PASS {++count} {e.Kind}: attempts={handler.Calls}; retries={retries}; zero dispatchers/workflow calls");
            }
        }
        Console.WriteLine($"RESULT MODEL_FAILURE_POLICY_PASS={count} FAIL=0. Negative fault injection only; real model integration is tested separately.");
    }
}
