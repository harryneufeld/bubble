using Bubble.Core;

// Legt auf einem Relay Test-Bubbles mit erfundenen Freunden und Stock-Fotos an (Fotos: picsum.photos, Unsplash-Lizenz;
// Profilbilder: pravatar.cc). Nur für Entwicklung. Aufruf: dotnet run --project tools/Bubble.Seed -- http://<pc-ip>:5080
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

// name, Profilbild-Nr., Bio
var people = new (string Name, int Face, string Bio)[]
{
    ("Mia Berger", 47, "Kaffee zuerst, dann reden"),
    ("Jonas Weber", 12, "Läuft. Meistens."),
    ("Lena Fischer", 32, "Immer unterwegs"),
    ("Tom Schulz", 15, "Pizza-Experte"),
    ("Sophie Klein", 5, "Katzenmensch"),
}.ToDictionary(p => p.Name);

Console.WriteLine("Lade Profilbilder …");
var vaults = new Dictionary<string, Vault>();
foreach (var (name, p) in people)
{
    var v = Vault.Create(name);
    v.Bio = p.Bio;
    v.Avatar = await Download($"https://i.pravatar.cc/512?img={p.Face}");
    vaults[name] = v;
}

// Zeitversatz, damit Posts über den Tag verteilt aussehen
BubbleClient As(string person, TimeSpan ago) => new(vaults[person], RelayFor, new Offset(ago));

async Task Post(BubbleMembership bubble, string person, TimeSpan ago, Mood mood, string? text, string? photoSeed)
{
    byte[]? image = null;
    if (photoSeed is not null)
    {
        Console.WriteLine($"  Foto {photoSeed} …");
        image = await Download($"https://picsum.photos/seed/{photoSeed}/1080/1350");
        if (image is null && text is null) return;
    }
    await As(person, ago).PostAsync(bubble, mood, text, image);
    Console.WriteLine($"  {person}: {text ?? "(Foto)"}");
}

async Task Join(BubbleMembership bubble, params string[] members)
{
    foreach (var m in members) await As(m, TimeSpan.Zero).PublishProfileAsync(bubble);
}

var h = (double hours) => TimeSpan.FromHours(hours);

Console.WriteLine("\nBubble 1: WG Sonnenallee (24 Stunden)");
var wg = BubbleMembership.Create("WG Sonnenallee", relayUrl, PostLifetime.Story24h);
await Join(wg, "Mia Berger", "Jonas Weber", "Lena Fischer", "Tom Schulz");
await Post(wg, "Mia Berger", h(6.5), Mood.Top, "Sonnenaufgang vom Balkon, so ruhig war es lange nicht", "sunrise-balcony");
await Post(wg, "Jonas Weber", h(4), Mood.Flop, "Bahn ausgefallen, 40 Minuten im Regen gestanden", "rainy-station");
await Post(wg, "Lena Fischer", h(2.5), Mood.Top, "Prüfung bestanden! Endlich.", null);
await Post(wg, "Tom Schulz", h(1), Mood.Top, "Selbstgemachte Pizza, Teig hat drei Tage gereift", "pizza-night");

Console.WriteLine("\nBubble 2: Familie (dauerhaft)");
var family = BubbleMembership.Create("Familie", relayUrl, PostLifetime.Persistent);
await Join(family, "Sophie Klein", "Mia Berger", "Tom Schulz");
await Post(family, "Sophie Klein", h(30), Mood.Top, "Oma hat Geburtstag gefeiert, 90!", "birthday-cake");
await Post(family, "Mia Berger", h(28), Mood.Top, "Spaziergang am See", "lake-walk");
await Post(family, "Sophie Klein", h(3), Mood.Top, "Der Garten blüht", "garden-flowers");
await Post(family, "Tom Schulz", h(0.5), Mood.Flop, "Autopanne auf dem Heimweg, ADAC ist unterwegs", null);

Console.WriteLine("\nBubble 3: Laufgruppe (24 Stunden, nur Text und Foto von Lena)");
var run = BubbleMembership.Create("Laufgruppe", relayUrl, PostLifetime.Story24h);
await Join(run, "Lena Fischer", "Jonas Weber");
await Post(run, "Lena Fischer", h(8), Mood.Top, "10 km in 52 Minuten", "morning-run");

Console.WriteLine("\n=== Einladungen (24 Stunden gültig) ===");
foreach (var b in new[] { wg, family, run })
{
    var code = await ShortInvite.PublishAsync(Invite.For(b), RelayFor(relayUrl));
    Console.WriteLine($"{b.Name,-16} Code: {code}");
}
Console.WriteLine($"\nRelay: {relayUrl}");

/// <summary>Verschiebt „jetzt“ in die Vergangenheit, damit Posts zu unterschiedlichen Zeiten entstanden wirken.</summary>
sealed class Offset(TimeSpan ago) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => base.GetUtcNow() - ago;
}
