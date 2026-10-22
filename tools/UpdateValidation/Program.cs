using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RetroGamingCollection;

var root=Path.Combine(Path.GetTempPath(),"RetroGaming-UpdateValidation",Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
Environment.SetEnvironmentVariable("RETROGAMING_DATA_DIRECTORY",root);
if (args.Contains("--live"))
{
    var liveService=new UpdateService();
    var liveUpdate=await liveService.CheckAsync(new ReleaseVersion(99));
    if (liveUpdate is null) throw new Exception("No complete published release is available for live verification.");
    var downloaded=await liveService.DownloadAsync(liveUpdate);
    Console.WriteLine($"PASS: Live GitHub v{liveUpdate.Version.Display} update download and checksum verification ({new FileInfo(downloaded).Length:N0} bytes).");
    Console.WriteLine($"Changelog received: {liveUpdate.Notes.Length:N0} characters. Verified installer: {downloaded}");
    var currentUpdate=await liveService.CheckAsync(liveUpdate.Version);
    if (currentUpdate is not null) throw new Exception("Current release incorrectly offered itself as an update.");
    Console.WriteLine("PASS: Current published release reports up to date.");
    return;
}
var passed=0;
void Check(bool condition,string label) { if (!condition) throw new Exception(label); Console.WriteLine("PASS: "+label); passed++; }
Check(ReleaseVersion.TryParse("v1.09",out var nine) && ReleaseVersion.TryParse("1.10",out var ten) && ten.Counter==nine.Counter+1,"Exact hundredth version increment and comparison");
Check(new ReleaseVersion(199).Display=="1.99" && new ReleaseVersion(200).Display=="2.00","Hundredth rollover");
Check(!ReleaseVersion.TryParse("v1.001",out _) && !ReleaseVersion.TryParse("../../x",out _) && !ReleaseVersion.TryParse("999999999999.00",out _),"Reject malformed versions");
GameEntry Game(string title) => new() {Title=title,Platform="SNES",Confidence="fixture"};
Check(LibraryFilters.Matches(Game("Game (USA)"),"Untagged",true) && !LibraryFilters.Matches(Game("Game (USA) (En,Fr)"),"Untagged",true) && !LibraryFilters.Matches(Game("Game (Japan)"),"Untagged",true),"Default USA/untagged filter");
Check(LibraryFilters.Matches(Game("Game (Europe) (En,Fr)"),"Fr",false),"Multilingual filter");

var payload=Encoding.UTF8.GetBytes("Synthetic installer test bytes, never executed.");
var hash=Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
string Installer(int counter) => $"RetroGamingCollection-Setup-{new ReleaseVersion(counter).Display}-win-x64.exe";
string Url(int counter,string file) => $"https://github.com/{AppVersion.Repository}/releases/download/v{new ReleaseVersion(counter).Display}/{file}";
object Release(int counter,bool prerelease=false,bool incomplete=false,string? hostileUrl=null) => new {
    tag_name="v"+new ReleaseVersion(counter).Display,draft=false,prerelease,body=$"Changes for {new ReleaseVersion(counter).Display}\n- New feature",
    assets=incomplete ? Array.Empty<object>() : new object[] {
        new {name=Installer(counter),browser_download_url=hostileUrl??Url(counter,Installer(counter)),size=payload.Length,digest="sha256:"+hash},
        new {name="SHA256SUMS.txt",browser_download_url=Url(counter,"SHA256SUMS.txt"),size=100,digest=(string?)null}
    }
};
var releaseJson=JsonSerializer.Serialize(new[]{Release(102),Release(101),Release(103,prerelease:true),Release(104,incomplete:true)});
bool corrupt=false;
var handler=new FakeHandler(request => {
    var uri=request.RequestUri!;
    if (uri.Host=="api.github.com") return new(HttpStatusCode.OK){Content=new StringContent(releaseJson)};
    if (uri.AbsolutePath.EndsWith("SHA256SUMS.txt")) return new(HttpStatusCode.OK){Content=new StringContent(hash+"  "+Installer(102)+"\n")};
    return new(HttpStatusCode.OK){Content=new ByteArrayContent(corrupt?Encoding.UTF8.GetBytes(new string('x',payload.Length)):payload)};
});
using var client=new HttpClient(handler);
var service=new UpdateService(client);
var update=await service.CheckAsync(new(100));
Check(update?.Version.Counter==102,"Select latest complete stable release, skipping unfinished and prerelease builds");
Check(update!.Notes.Contains("Changes for 1.02") && update.Notes.Contains("Changes for 1.01") && !update.Notes.Contains("1.03"),"Changelog includes skipped stable updates");
Check(await service.CheckAsync(new(104)) is null,"Already current returns no update");
var path=await service.DownloadAsync(update);
Check(File.ReadAllBytes(path).SequenceEqual(payload),"Download size and SHA-256 validation");
Check(!File.Exists(path+".partial"),"No partial installer remains after success");
File.Delete(path);corrupt=true;
try { await service.DownloadAsync(update); throw new Exception("Corrupt update accepted"); }
catch(InvalidDataException) { Check(!File.Exists(path) && !File.Exists(path+".partial"),"Reject corrupt installer and clean partial file"); }
corrupt=false;
using(var cancel=new CancellationTokenSource()) {
    cancel.Cancel();
    try { await service.DownloadAsync(update,cancellationToken:cancel.Token); throw new Exception("Cancelled update accepted"); }
    catch(OperationCanceledException) { Check(!File.Exists(path),"Cancelled download installs nothing"); }
}
releaseJson=JsonSerializer.Serialize(new[]{Release(102,hostileUrl:"https://example.com/unsafe.exe")});
try { await service.CheckAsync(new(100)); throw new Exception("Foreign installer accepted"); }
catch(InvalidDataException) { Check(true,"Reject installer URLs outside the configured repository"); }
var offline=new UpdateService(new HttpClient(new FakeHandler(_ => new(HttpStatusCode.ServiceUnavailable))));
try { await offline.CheckAsync(new(100)); throw new Exception("Server error ignored"); }
catch(HttpRequestException) { Check(true,"Network failure leaves current installation unchanged"); }

var portable=Path.Combine(root,"portable");var target=Path.Combine(root,"installed-data","Configuration");
Directory.CreateDirectory(Path.Combine(portable,"Configuration"));
File.WriteAllText(Path.Combine(portable,"Configuration","settings.json"),JsonSerializer.Serialize(new LauncherSettings {RetroArchPath="../Dependencies/retroarch.exe",SnesCorePath="../Dependencies/snes.dll",LibraryPaths=[@"E:\Games"],SaveDirectory="Saves",LanguageFilter="Fr",UsaOnly=false}));
File.WriteAllText(Path.Combine(portable,"Configuration","library.json"),"[]");
AppPaths.ImportPortableConfiguration(portable,target);
var migrated=JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(Path.Combine(target,"settings.json")))!;
Check(Path.IsPathRooted(migrated.RetroArchPath!) && migrated.SaveDirectory==Path.Combine(portable,"Saves") && migrated.LibraryPaths.SequenceEqual([@"E:\Games"]) && migrated.LanguageFilter=="Fr" && !migrated.UsaOnly,"Portable import preserves libraries, filters and save locations");
File.WriteAllText(Path.Combine(target,"settings.json"),"existing-settings-must-survive");
File.WriteAllText(Path.Combine(target,"library.json"),"existing-library-must-survive");
AppPaths.ImportPortableConfiguration(portable,target);
Check(File.ReadAllText(Path.Combine(target,"settings.json"))=="existing-settings-must-survive" && File.ReadAllText(Path.Combine(target,"library.json"))=="existing-library-must-survive","Migration does not overwrite existing installed user data");
Console.WriteLine($"Completed {passed} checks. Fixtures: {root}");

sealed class FakeHandler(Func<HttpRequestMessage,HttpResponseMessage> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
    { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(respond(request)); }
}
