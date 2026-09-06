using EnputMethod.Overlay;
using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;

try
{
    await RunTestsAsync(args);
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    Environment.ExitCode = 1;
}

static async Task RunTestsAsync(string[] args)
{
var tests = new (string Name, string Json, bool Expected)[]
{
    ("candidate view", """{"type":"showCandidates","clientId":"host-1","stateId":4,"candidates":{"x":120,"y":80,"ownerWindow":42,"items":[{"text":"hello"},{"text":"Polish","canonicalCaseRequired":true}],"page":0,"pageCount":1,"selectedIndex":0,"capsLock":true,"layout":"vertical"}}""", true),
    ("candidate theme", """{"type":"showCandidates","clientId":"host-1","stateId":5,"candidates":{"x":120,"y":80,"items":[{"text":"hello"}],"page":0,"pageCount":1,"selectedIndex":0,"layout":"horizontal","theme":{"fontSize":16,"opacity":255,"borderWidth":1,"cornerRadius":8,"padding":10,"rowHeight":28,"translationWidth":380,"translationMaxHeight":240,"translationWindowWidth":460,"translationWindowHeight":340}}}""", true),
    ("translation view", """{"type":"showTranslation","clientId":"host-1","stateId":6,"translation":{"title":"hug","content":"en: n. an embrace\nzh-CN: 拥抱","candidateRight":20,"candidateTop":30,"theme":{"fontSize":18,"translationWindowWidth":460,"translationWindowHeight":340}}}""", true),
    ("translation hide", """{"type":"hide","clientId":"host-1","stateId":6,"surface":"translation"}""", true),
    ("internal presented event", """{"type":"presented","clientId":"host-1","stateId":7}""", false),
    ("missing candidates", """{"type":"showCandidates","clientId":"host-1","stateId":8}""", false),
    ("empty candidates", """{"type":"showCandidates","clientId":"host-1","stateId":8,"candidates":{"x":0,"y":0,"items":[],"page":0,"pageCount":1,"selectedIndex":0,"layout":"vertical"}}""", false),
    ("invalid page", """{"type":"showCandidates","clientId":"host-1","stateId":9,"candidates":{"x":0,"y":0,"items":[{"text":"hello"}],"page":1,"pageCount":1,"selectedIndex":0,"layout":"vertical"}}""", false),
    ("invalid surface", """{"type":"hide","clientId":"host-1","stateId":10,"surface":"unknown"}""", false),
    ("invalid action", """{"type":"selectCandidate","clientId":"host-1","stateId":11,"candidateIndex":-1}""", false),
};

foreach ((string name, string json, bool expected) in tests)
{
    bool actual = OverlayProtocol.TryParse(json, out _);
    if (actual != expected) throw new InvalidOperationException($"{name}: expected {expected}, got {actual}");
}

if (!OverlayProtocol.TryParse(tests[0].Json, out OverlayMessage? styledMessage) || styledMessage?.Candidates?.Items[1].CanonicalCaseRequired != true)
{
    throw new InvalidOperationException("Canonical-case candidate style was not preserved by the protocol.");
}

Console.WriteLine($"{tests.Length} protocol cases passed.");

if (args.Contains("--protocol-only", StringComparer.Ordinal)) return;

var received = new ConcurrentBag<OverlayMessage>();
string pipeName = $"EnputMethod.Overlay.ProtocolTests.{Guid.NewGuid():N}";
using (var server = new OverlayPipeServer((message, sendAction) =>
{
    received.Add(message);
    sendAction(new OverlayMessage
    {
        Type = "selectCandidate",
        ClientId = message.ClientId,
        StateId = message.StateId,
        CandidateIndex = message.ClientId == "host-one" ? 0 : 1,
    }).GetAwaiter().GetResult();
}, pipeName: pipeName))
{
    server.Start();
    await AssertRoutedActionAsync(pipeName, "host-one", 11);
    await AssertRoutedActionAsync(pipeName, "host-two", 12);
}

if (received.Count != 2 || !received.Any(message => message.ClientId == "host-one" && message.StateId == 11) || !received.Any(message => message.ClientId == "host-two" && message.StateId == 12))
{
    throw new InvalidOperationException("Pipe server did not preserve independent host messages.");
}

Console.WriteLine("Multi-host pipe test passed.");

var listenerFailure = new UnauthorizedAccessException("Injected pipe-listener failure.");
using (var failedServer = new OverlayPipeServer((_, _) => { }, pipeName: $"EnputMethod.Overlay.ProtocolTests.{Guid.NewGuid():N}", pipeFactory: _ => throw listenerFailure))
{
    failedServer.Start();
    await AssertFailsWithAsync<UnauthorizedAccessException>(failedServer.Ready, "Pipe readiness did not expose a listener startup failure.");
    await AssertFailsWithAsync<UnauthorizedAccessException>(failedServer.Completion, "Pipe completion did not expose a listener startup failure.");
}

Console.WriteLine("Pipe listener failure propagation test passed.");
}

static async Task AssertFailsWithAsync<TException>(Task task, string failure) where TException : Exception
{
    try
    {
        await task;
    }
    catch (TException)
    {
        return;
    }
    throw new InvalidOperationException(failure);
}

static async Task AssertRoutedActionAsync(string pipeName, string clientId, long stateId)
{
    using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
    await pipe.ConnectAsync(3000);
    using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, true);
    using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
    string? ready = await reader.ReadLineAsync();
    if (ready is null || !ready.Contains("\"ready\"", StringComparison.Ordinal)) throw new InvalidOperationException("Missing pipe ready message.");
    await writer.WriteLineAsync($"{{\"type\":\"showCandidates\",\"clientId\":\"{clientId}\",\"stateId\":{stateId},\"candidates\":{{\"x\":1,\"y\":1,\"items\":[{{\"text\":\"hello\"}}],\"page\":0,\"pageCount\":1,\"selectedIndex\":0,\"layout\":\"vertical\"}}}}");
    string? response = await reader.ReadLineAsync();
    if (response is null) throw new InvalidOperationException($"Host {clientId} did not receive a routed action.");
    int candidateIndex = clientId == "host-one" ? 0 : 1;
    if (!response.Contains("\"selectCandidate\"", StringComparison.Ordinal) || !response.Contains($"\"clientId\":\"{clientId}\"", StringComparison.Ordinal) || !response.Contains($"\"candidateIndex\":{candidateIndex}", StringComparison.Ordinal)) throw new InvalidOperationException($"Host {clientId} received an incorrect routed action.");
}
