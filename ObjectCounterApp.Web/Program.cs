using ObjectCounterApp.Core;

var builder = WebApplication.CreateBuilder(args);

var personModelPath = Path.Combine(AppContext.BaseDirectory, "human-detector.onnx");
var yunetModelPath = Path.Combine(AppContext.BaseDirectory, "face_detection_yunet_2023mar.onnx");
var sfaceModelPath = Path.Combine(AppContext.BaseDirectory, "face_recognition_sface_2021dec.onnx");
var dataSetPath = Path.Combine(builder.Environment.ContentRootPath, "..", "DataSet");

builder.Services.AddSingleton(new PersonDetector(personModelPath));
builder.Services.AddSingleton(new FaceDetector(yunetModelPath));
builder.Services.AddSingleton(new FaceEmbedder(sfaceModelPath));
builder.Services.AddSingleton(new EnrolledPeopleStore(dataSetPath));
builder.Services.AddSingleton<PersonIdentifier>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

var tempDir = Path.Combine(Path.GetTempPath(), "objectcounterapp");
Directory.CreateDirectory(tempDir);

app.MapPost("/api/detect", async (HttpRequest request, PersonDetector detector, PersonIdentifier identifier) =>
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest("Expected multipart/form-data with a file field.");
    }

    var form = await request.ReadFormAsync();
    var file = form.Files.GetFile("file");
    if (file is null || file.Length == 0)
    {
        return Results.BadRequest("No file uploaded.");
    }

    bool identify = request.Query["identify"] == "true";

    var tempPath = Path.Combine(tempDir, $"{Guid.NewGuid()}.jpg");
    try
    {
        await using (var stream = File.Create(tempPath))
        {
            await file.CopyToAsync(stream);
        }

        var detections = identify ? identifier.DetectAndIdentify(tempPath) : detector.DetectPersons(tempPath);
        return Results.Ok(new
        {
            count = detections.Count,
            detections = detections.Select(d => new
            {
                label = d.Label,
                score = d.Score,
                x1 = d.X1,
                y1 = d.Y1,
                x2 = d.X2,
                y2 = d.Y2,
                isLikelyReal = d.IsLikelyReal,
                identityName = d.IdentityName
            })
        });
    }
    finally
    {
        File.Delete(tempPath);
    }
});

app.MapGet("/api/people", (EnrolledPeopleStore peopleStore) =>
{
    return Results.Ok(new { names = peopleStore.ListNames() });
});

app.MapGet("/api/people/details", (EnrolledPeopleStore peopleStore) =>
{
    var summaries = peopleStore.GetSummaries().Select(s => new
    {
        name = s.Name,
        photoCount = s.PhotoCount,
        thumbnail = s.ThumbnailBase64 is not null
            ? $"data:{s.ThumbnailContentType};base64,{s.ThumbnailBase64}"
            : null
    });
    return Results.Ok(summaries);
});

app.MapPost("/api/people", async (HttpRequest request, PersonIdentifier identifier, EnrolledPeopleStore peopleStore) =>
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest("Expected multipart/form-data with a 'name' field and one or more 'photos' files.");
    }

    var form = await request.ReadFormAsync();
    var name = form["name"].ToString().Trim();
    if (string.IsNullOrEmpty(name))
    {
        return Results.BadRequest("A name is required.");
    }

    var photos = form.Files.GetFiles("photos");
    if (photos.Count == 0)
    {
        return Results.BadRequest("At least one reference photo is required.");
    }

    int enrolled = 0;
    int failed = 0;
    foreach (var photo in photos)
    {
        var tempPath = Path.Combine(tempDir, $"{Guid.NewGuid()}.jpg");
        try
        {
            await using (var stream = File.Create(tempPath))
            {
                await photo.CopyToAsync(stream);
            }

            var embedding = identifier.ComputeEmbeddingForEnrollment(tempPath);
            if (embedding is null)
            {
                failed++;
                continue;
            }

            var photoBytes = await File.ReadAllBytesAsync(tempPath);
            var contentType = string.IsNullOrEmpty(photo.ContentType) ? "image/jpeg" : photo.ContentType;
            peopleStore.AddEmbedding(name, embedding, photoBytes, contentType);
            enrolled++;
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    return Results.Ok(new { name, enrolledPhotos = enrolled, failedPhotos = failed });
});

app.MapDelete("/api/people/{name}", (string name, EnrolledPeopleStore peopleStore) =>
{
    bool removed = peopleStore.Remove(name);
    return removed ? Results.Ok() : Results.NotFound();
});

app.MapPut("/api/people/{name}", async (string name, HttpRequest request, EnrolledPeopleStore peopleStore) =>
{
    var body = await request.ReadFromJsonAsync<RenameRequest>();
    var newName = body?.NewName?.Trim();
    if (string.IsNullOrEmpty(newName))
    {
        return Results.BadRequest("A newName is required.");
    }

    var result = peopleStore.Rename(name, newName);
    return result switch
    {
        RenameResult.Success => Results.Ok(),
        RenameResult.NameTaken => Results.Conflict("That name is already in use."),
        _ => Results.NotFound()
    };
});

app.Run();

record RenameRequest(string? NewName);
