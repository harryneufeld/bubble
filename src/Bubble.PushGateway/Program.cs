var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => "Bubble Push Gateway – only wakes devices, never sees content.");
app.MapGet("/healthz", () => Results.Ok());

app.Run();
