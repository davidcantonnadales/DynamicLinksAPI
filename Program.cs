using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

IConfiguration configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json") // Carga el archivo appsettings.json predeterminado
    .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json",
        true)
    .Build();

Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", configuration["FirebaseJson"]);

FirebaseApp.Create(new AppOptions
{
    Credential = GoogleCredential.FromFile(configuration["FirebaseJson"])
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFirebaseHosting",
        builder => builder.WithOrigins("https://enlaces.proximacita.com")
                          .AllowAnyHeader()
                          .AllowAnyMethod());
});


builder.Services.AddOpenApi();

// Configuración de Firestore
builder.Services.AddSingleton(FirestoreDb.Create("proximacita-com"));

var app = builder.Build();

app.UseCors("AllowFirebaseHosting");

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapPost("/api/shortlinks/create", async ([FromBody] CreateLinkRequest request, FirestoreDb firestoreDb) =>
{
    if (string.IsNullOrEmpty(request.DestinationUrl))
        return Results.BadRequest("La URL de destino es requerida.");

    string shortId = Guid.NewGuid().ToString("N").Substring(0, 6);
    var docRef = firestoreDb.Collection("ShortLinks").Document(shortId);

    var data = new Dictionary<string, object>
    {
        { "Destination", request.DestinationUrl },
        { "Clicks", 0 },
        { "CreatedAt", Timestamp.GetCurrentTimestamp() },
        { "Expiration", request.Expiration.HasValue ? Timestamp.FromDateTime(request.Expiration.Value) : null }
    };

    await docRef.SetAsync(data);
    return Results.Ok(new { shortUrl = $"https://enlaces.proximacita.com/{shortId}" });
});

app.MapGet("/api/shortlinks/{shortId}", async (string shortId, FirestoreDb firestoreDb) =>
{
    var docRef = firestoreDb.Collection("ShortLinks").Document(shortId);
    var snapshot = await docRef.GetSnapshotAsync();

    if (!snapshot.Exists)
        return Results.NotFound("URL no encontrada");

    var data = snapshot.ToDictionary();
    if (!Uri.IsWellFormedUriString(data["Destination"].ToString(), UriKind.Absolute))
        return Results.BadRequest("La URL de destino no es válida.");

    await docRef.UpdateAsync("Clicks", (long)data["Clicks"] + 1);
    return Results.Redirect(data["Destination"].ToString());
});

app.Run();

record CreateLinkRequest(string DestinationUrl, DateTime? Expiration);
