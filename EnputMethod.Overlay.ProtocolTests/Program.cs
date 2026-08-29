using EnputMethod.Overlay;

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
