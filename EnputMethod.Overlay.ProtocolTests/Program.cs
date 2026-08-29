using EnputMethod.Overlay;
using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;

var tests = new (string Name, string Json, bool Expected)[]
{
    ("candidate view", """{"type":"showCandidates","clientId":"host-1","stateId":4,"candidates":{"x":120,"y":80,"items":["hello","help"],"page":0,"pageCount":1,"selectedIndex":0,"capsLock":true,"layout":"vertical"}}""", true),
    ("candidate theme", """{"type":"showCandidates","clientId":"host-1","stateId":5,"candidates":{"x":120,"y":80,"items":["hello"],"page":0,"pageCount":1,"selectedIndex":0,"layout":"horizontal","theme":{"fontSize":16,"opacity":255,"borderWidth":1,"cornerRadius":8,"padding":10,"rowHeight":28,"translationWidth":380,"translationMaxHeight":240}}}""", true),
    ("translation hide", """{"type":"hide","clientId":"host-1","stateId":6,"surface":"translation"}""", true),
    ("missing candidates", """{"type":"showCandidates","clientId":"host-1","stateId":7}""", false),
    ("invalid page", """{"type":"showCandidates","clientId":"host-1","stateId":8,"candidates":{"x":0,"y":0,"items":["hello"],"page":1,"pageCount":1,"selectedIndex":0,"layout":"vertical"}}""", false),
    ("invalid surface", """{"type":"hide","clientId":"host-1","stateId":9,"surface":"unknown"}""", false),
    ("invalid action", """{"type":"selectCandidate","clientId":"host-1","stateId":10,"candidateIndex":-1}""", false),
};

foreach ((string name, string json, bool expected) in tests)
{
    bool actual = OverlayProtocol.TryParse(json, out _);
    if (actual != expected) throw new InvalidOperationException($"{name}: expected {expected}, got {actual}");
}

Console.WriteLine($"{tests.Length} protocol cases passed.");

var received = new ConcurrentBag<OverlayMessage>();
using (var server = new OverlayPipeServer((message, _) => received.Add(message)))
{
    server.Start();
    await AssertAcceptedAsync("host-one", 11);
    await AssertAcceptedAsync("host-two", 12);
}

if (received.Count != 2 || !received.Any(message => message.ClientId == "host-one" && message.StateId == 11) || !received.Any(message => message.ClientId == "host-two" && message.StateId == 12))
{
    throw new InvalidOperationException("Pipe server did not preserve independent host messages.");
}

Console.WriteLine("Multi-host pipe test passed.");

static async Task AssertAcceptedAsync(string clientId, long stateId)
{
    using var pipe = new NamedPipeClientStream(".", "EnputMethod.Overlay.v1", PipeDirection.InOut, PipeOptions.Asynchronous);
    await pipe.ConnectAsync(3000);
    using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, true);
    using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
    string? ready = await reader.ReadLineAsync();
    if (ready is null || !ready.Contains("\"ready\"", StringComparison.Ordinal)) throw new InvalidOperationException("Missing pipe ready message.");
    await writer.WriteLineAsync($"{{\"type\":\"showCandidates\",\"clientId\":\"{clientId}\",\"stateId\":{stateId},\"candidates\":{{\"x\":1,\"y\":1,\"items\":[\"hello\"],\"page\":0,\"pageCount\":1,\"selectedIndex\":0,\"layout\":\"vertical\"}}}}");
    string? accepted = await reader.ReadLineAsync();
    if (accepted is null || !accepted.Contains($"\"stateId\":{stateId}", StringComparison.Ordinal)) throw new InvalidOperationException($"Host {clientId} did not receive its acceptance.");
}
