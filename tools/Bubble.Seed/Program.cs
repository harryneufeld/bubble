using Bubble.Core;

// Creates test bubbles with made-up friends and stock photos on a relay (photos: picsum.photos, Unsplash license;
// profile pictures: pravatar.cc). Development only. Usage: dotnet run --project tools/Bubble.Seed -- http://<pc-ip>:5080
var relayUrl = (args.Length > 0 ? args[0] : "http://localhost:5080").TrimEnd('/') + "/";
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("BubbleSeed/1.0");

var relays = new Dictionary<string, RelayClient>();
RelayClient RelayFor(string url) => relays.TryGetValue(url, out var r) ? r : relays[url] = new RelayClient(new HttpClient { BaseAddress = new Uri(url) });

async Task<byte[]?> Download(string url)
{
    try { return await http.GetByteArrayAsync(url); }
    catch (HttpRequestException e) { Console.WriteLine($"  ! {url}: {e.Message}"); return null; }
}

// name, profile picture no., bio
var people = new (string Name, int Face, string Bio)[]
{
    ("Mia Berger", 47, "Coffee first, then talk"),
    ("Jonas Weber", 12, "Works. Mostly."),
    ("Lena Fischer", 32, "Always on the move"),
    ("Tom Schulz", 15, "Pizza expert"),
    ("Sophie Klein", 5, "Cat person"),
}.ToDictionary(p => p.Name);

Console.WriteLine("Loading profile pictures …");
var vaults = new Dictionary<string, Vault>();
foreach (var (name, p) in people)
{
    var v = Vault.Create(name);
    v.Bio = p.Bio;
    v.Avatar = await Download($"https://i.pravatar.cc/512?img={p.Face}");
    vaults[name] = v;
}

// Time offset so posts look spread over the day
BubbleClient As(string person, TimeSpan ago) => new(vaults[person], RelayFor, new Offset(ago));

async Task Post(BubbleMembership bubble, string person, TimeSpan ago, string emoji, string? text, string? photoSeed)
{
    byte[]? image = null;
    if (photoSeed is not null)
    {
        Console.WriteLine($"  Foto {photoSeed} …");
        image = await Download($"https://picsum.photos/seed/{photoSeed}/1080/1350");
        if (image is null && text is null) return;
    }
    await As(person, ago).PostAsync(bubble, emoji, text, image);
    Console.WriteLine($"  {person}: {text ?? "(photo)"}");
}

async Task Join(BubbleMembership bubble, params string[] members)
{
    foreach (var m in members) await As(m, TimeSpan.Zero).PublishProfileAsync(bubble);
}

var h = (double hours) => TimeSpan.FromHours(hours);

Console.WriteLine("\nBubble 1: Flat share (24 hours)");
var wg = BubbleMembership.Create("Flat share", relayUrl, PostLifetime.Story24h);
await Join(wg, "Mia Berger", "Jonas Weber", "Lena Fischer", "Tom Schulz");
await Post(wg, "Mia Berger", h(6.5), "😌", "Sunrise from the balcony, it has not been this quiet in a long time", "sunrise-balcony");
await Post(wg, "Jonas Weber", h(4), "😤", "Train cancelled, stood in the rain for 40 minutes", "rainy-station");
await Post(wg, "Lena Fischer", h(2.5), "🥳", "Passed the exam! Finally.", null);
await Post(wg, "Tom Schulz", h(1), "😋", "Homemade pizza, the dough rested for three days", "pizza-night");

Console.WriteLine("\nBubble 2: Family (permanent)");
var family = BubbleMembership.Create("Family", relayUrl, PostLifetime.Persistent);
await Join(family, "Sophie Klein", "Mia Berger", "Tom Schulz");
await Post(family, "Sophie Klein", h(30), "🥹", "Grandma celebrated her birthday, 90!", "birthday-cake");
await Post(family, "Mia Berger", h(28), "😌", "Walk by the lake", "lake-walk");
await Post(family, "Sophie Klein", h(3), "😍", "The garden is in bloom", "garden-flowers");
await Post(family, "Tom Schulz", h(0.5), "😩", "Car broke down on the way home, roadside assistance is on its way", null);

Console.WriteLine("\nBubble 3: Running group (24 hours, only text and a photo from Lena)");
var run = BubbleMembership.Create("Running group", relayUrl, PostLifetime.Story24h);
await Join(run, "Lena Fischer", "Jonas Weber");
await Post(run, "Lena Fischer", h(8), "💪", "10 km in 52 minutes", "morning-run");

Console.WriteLine("\n=== Invitations (valid for 24 hours) ===");
foreach (var b in new[] { wg, family, run })
{
    var code = await ShortInvite.PublishAsync(Invite.For(b), RelayFor(relayUrl));
    Console.WriteLine($"{b.Name,-16} Code: {code}");
}
Console.WriteLine($"\nRelay: {relayUrl}");

/// <summary>Shifts "now" into the past so posts look like they were created at different times.</summary>
sealed class Offset(TimeSpan ago) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => base.GetUtcNow() - ago;
}
