using RetroGamingCollection;
using System.IO.Compression;
using System.Text.Json;

var checks = new List<string>();
void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks.Add("PASS: " + label); Console.WriteLine(checks[^1]); }
if (args.Contains("--filters-only"))
{
    GameEntry Entry(string title) => new() { Title=title, Platform="SNES", Confidence="Filter fixture" };
    Check(LibraryFilters.Matches(Entry("Game (USA)"),"Untagged",true),"Default includes untagged USA release");
    Check(!LibraryFilters.Matches(Entry("Game (Japan)"),"Untagged",true),"Default excludes untagged non-USA release");
    Check(!LibraryFilters.Matches(Entry("Game (USA) (En,Fr,De)"),"Untagged",true),"Default excludes explicit multilingual release");
    Check(LibraryFilters.Matches(Entry("Game (USA, Europe) (Rev 1)"),"Untagged",true),"Shared USA region is included and revision is not a language");
    Check(LibraryFilters.Matches(Entry("Game (Europe) (En,Fr,De)"),"Fr",false),"Language selection matches multilingual tags independently of region");
    Check(!LibraryFilters.Matches(Entry("Game (USA)"),"En",false),"Untagged does not imply English");
    Check(LibraryFilters.Matches(Entry("Game (Japan) (Ja)"),"All",false),"All languages and regions can reveal excluded games");
    Check(LibraryFilters.Languages("En Garde (USA) (Unl) (NP)").Count==0,"Title words and non-language tags are ignored");
    Check(LibraryFilters.Languages("Game (USA) [English]").SequenceEqual(["En"]),"Named language in brackets is recognized");
    var saved = JsonSerializer.Deserialize<List<GameEntry>>(File.ReadAllText(Path.Combine(Environment.CurrentDirectory,"Playable/Configuration/library.json")))!;
    var visible = saved.Count(g => LibraryFilters.Matches(g,"Untagged",true));
    Check(visible>0 && visible<saved.Count,"Default narrows the real registered library");
    var filterReport = Path.Combine(Environment.CurrentDirectory,"Reports/language-filter-validation.txt");
    File.WriteAllLines(filterReport,checks.Append($"Default view: {visible:N0} of {saved.Count:N0} files"));
    Console.WriteLine($"Default view: {visible:N0} of {saved.Count:N0} files");
    return;
}
var snesRoot = @"E:\Retrobat\roms\snes";
var genesisRoot = @"E:\Retrobat\roms\genesis";
GameEntry Require(string root, string title, GamePlatform platform)
{
    var game = LibraryScanner.Inspect(Path.Combine(root,title+".zip"));
    Check(game?.DetectedPlatform == platform && game.IsInstalled && game.ArchiveMember is not null,title+" ZIP/header/platform");
    return game!;
}
Require(snesRoot,"Looney Tunes B-Ball (USA)",GamePlatform.Snes);
Require(snesRoot,"Mortal Kombat 3 (USA)",GamePlatform.Snes);
Require(genesisRoot,"Mortal Kombat 3 (USA)",GamePlatform.Genesis);
Require(snesRoot,"Contra III - The Alien Wars (USA)",GamePlatform.Snes);
Require(genesisRoot,"Contra - Hard Corps (USA, Korea)",GamePlatform.Genesis);
var scratch=Path.Combine(AppContext.BaseDirectory,"Fixtures");Directory.CreateDirectory(scratch);
File.WriteAllBytes(Path.Combine(scratch,"corrupt.zip"),[1,2,3]);
File.WriteAllBytes(Path.Combine(scratch,"not-a-genesis.bin"),new byte[0x10200]);
Check(LibraryScanner.Inspect(Path.Combine(scratch,"not-a-genesis.bin")) is null,"Arbitrary BIN is not classified as Genesis");
var safe=await LibraryScanner.ScanAsync(scratch);
Check(safe.Count==0,"Scan safely skips corrupt ZIP and unrecognized BIN");
var offline=new GameEntry {Title="Offline",Platform="SNES",DetectedPlatform=GamePlatform.Snes,FilePath=@"Z:\nonexistent-library\game.sfc",Confidence="Test"};
Check(!offline.IsInstalled && offline.Status.Contains("disconnected"),"Missing drive/file status");
try { new RetroArchService().Launch(offline,new()); throw new Exception("Missing content was launched"); }
catch(FileNotFoundException) { Check(true,"Launch refuses disconnected/missing content"); }
var settings=new LauncherSettings { ExternalRoot=@"Q:\",ExternalVolumeId=ExternalDrive.Identity(@"E:\"), LibraryPaths=[@"Q:\Retrobat\roms\snes"] };
var remapped=new GameEntry {Title="Mapping",Platform="SNES",FilePath=@"Q:\Retrobat\roms\snes\Looney Tunes B-Ball (USA).zip",Confidence="Test"};
ExternalDrive.Reconnect(settings,[remapped]);
Check(settings.ExternalRoot==@"E:\" && remapped.IsInstalled && settings.LibraryPaths[0].StartsWith(@"E:\"),"Volume GUID remaps stale drive letter without modifying mount points");
var all=new List<GameEntry>();
all.AddRange(await LibraryScanner.ScanAsync(snesRoot));all.AddRange(await LibraryScanner.ScanAsync(genesisRoot));
Check(all.Count>=4900,"Full production scanner imports actual external libraries");
Check(all.Select(g=>g.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).Count()==all.Count,"No duplicate path registrations");
var store=new SettingsStore();store.SaveLibrary(all);Check(store.LoadLibrary().Count==all.Count,"Library registrations survive restart");
var report=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../Reports/validation.txt"));
File.WriteAllLines(report,checks.Append($"Scanner count: {all.Count}"));
Console.WriteLine($"Completed {checks.Count} checks. Scanner count: {all.Count}");
