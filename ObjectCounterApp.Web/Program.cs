using ObjectCounterApp.Core;
using ObjectCounterApp.Web.Services;

var builder = WebApplication.CreateBuilder(args);

var personModelPath = Path.Combine(AppContext.BaseDirectory, "human-detector.onnx");
var yunetModelPath = Path.Combine(AppContext.BaseDirectory, "face_detection_yunet_2023mar.onnx");
var sfaceModelPath = Path.Combine(AppContext.BaseDirectory, "face_recognition_sface_2021dec.onnx");
var dataSetPath = Path.Combine(builder.Environment.ContentRootPath, "..", "DataSet");
var attendancePath = Path.Combine(dataSetPath, "Attandance");
var absenceTimeoutMinutes = builder.Configuration.GetValue("Attendance:AbsenceTimeoutMinutes", 1);

// EnrolledPeopleStore is registered under both its concrete type and
// IEnrolledPeopleStore (same instance) because PersonIdentifier's
// constructor still takes the concrete type - everything else in this app
// depends on the interfaces.
var peopleStore = new EnrolledPeopleStore(dataSetPath);

builder.Services.AddSingleton(new PersonDetector(personModelPath));
builder.Services.AddSingleton(new FaceDetector(yunetModelPath));
builder.Services.AddSingleton(new FaceEmbedder(sfaceModelPath));
builder.Services.AddSingleton(peopleStore);
builder.Services.AddSingleton<IEnrolledPeopleStore>(peopleStore);
builder.Services.AddSingleton<IAttendanceStore>(new AttendanceStore(attendancePath, TimeSpan.FromMinutes(absenceTimeoutMinutes)));
builder.Services.AddSingleton<IPersonIdentifier, PersonIdentifier>();

builder.Services.AddSingleton<ITempFileService, TempFileService>();
builder.Services.AddScoped<IAttendanceService, AttendanceService>();
builder.Services.AddScoped<IPeopleService, PeopleService>();
builder.Services.AddScoped<IDetectionService, DetectionService>();

builder.Services.AddControllers();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();

app.Run();
