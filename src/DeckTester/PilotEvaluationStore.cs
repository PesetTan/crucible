using System.Security.Cryptography;

namespace DeckTester;

public sealed record PilotEvaluationCase(string Profile,string Dimension,string Id,string Origin,string Kind,string Expected,string Actual,string Status);
public sealed record PilotDimension(string Key,string Label,string Scope,bool Measured,string Kind,int Passed,int Total,int Errors,double? Percent,List<string> Origins,List<PilotEvaluationCase> Cases);
public sealed record PilotScorecard(string Profile,List<PilotDimension> Dimensions);
public sealed record PilotEvaluationReport(int Version,string Id,DateTimeOffset StartedAt,DateTimeOffset CompletedAt,string EngineHash,string AdapterHash,
    string BundleId,long FixtureSeed,string BaseProfile,Dictionary<string,string> Settings,Dictionary<string,string> SourceHashes,
    string FixtureInventoryHash,string RawEvidenceHash,string LogHash,string Scope,List<PilotScorecard> Profiles);
public sealed record PilotEvaluationSnapshot(PilotEvaluationReport? Report,bool Current,string? Error);
public sealed record PilotSourceDocument(string File,string Text,string Hash);

// Read-only evidence, shared by both hosts. Evaluations never open or change saved runs.
public sealed class PilotEvaluationStore(AppPaths paths)
{
    private readonly object gate=new();
    private string? cachedPointer;
    private PilotEvaluationReport? cachedReport;
    private static string Hash(string path)=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    public PilotEvaluationSnapshot Read()
    {
        lock(gate){
            var folder=Path.Combine(paths.Data,"pilot-evaluations");
            var latest=Path.Combine(folder,"latest.json");
            if(!File.Exists(latest))return new(null,false,null);
            try{
                var pointerText=File.ReadAllText(latest);
                var pointer=Json.Decode<Pointer>(pointerText);
                if(string.IsNullOrEmpty(pointer.Directory)||pointer.Directory.Length>80||pointer.Directory.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='-'))
                    throw new InvalidDataException("Invalid pilot evaluation directory.");
                var directory=Path.Combine(folder,pointer.Directory);
                var reportPath=Path.Combine(directory,"scorecard.json");
                if(pointerText!=cachedPointer){
                    if(Hash(reportPath)!=pointer.Sha256)throw new InvalidDataException("Pilot scorecard checksum failed.");
                    var report=Json.Decode<PilotEvaluationReport>(File.ReadAllText(reportPath));
                    if(report.Version!=1||report.Id!=pointer.Directory||report.BundleId!=report.EngineHash+"-"+report.AdapterHash)
                        throw new InvalidDataException("Incompatible pilot scorecard identity.");
                    if(Hash(Path.Combine(directory,"positions.json"))!=report.RawEvidenceHash
                        ||Hash(Path.Combine(directory,"registry.json"))!=report.FixtureInventoryHash
                        ||Hash(Path.Combine(directory,"execution.log"))!=report.LogHash)
                        throw new InvalidDataException("Pilot evaluation evidence checksum failed.");
                    if(report.Profiles.Select(p=>p.Profile).Distinct().Count()!=report.Profiles.Count)
                        throw new InvalidDataException("Duplicate pilot scorecards.");
                    foreach(var dimension in report.Profiles.SelectMany(p=>p.Dimensions)){
                        var observed=dimension.Key=="aggression";
                        var cases=dimension.Cases.Where(c=>observed?c.Status=="observed":c.Status is "passed" or "failed").ToList();
                        var passed=cases.Count(c=>observed?c.Actual=="attack":c.Status=="passed");
                        if(dimension.Total!=cases.Count||dimension.Passed!=passed||dimension.Measured!=(cases.Count>0)
                            ||dimension.Errors!=dimension.Cases.Count(c=>c.Status=="error")
                            ||(cases.Count==0?dimension.Percent is not null:dimension.Percent is null||Math.Abs(dimension.Percent.Value-100d*passed/cases.Count)>.00001))
                            throw new InvalidDataException("Pilot scorecard denominator or rate does not match its cases.");
                    }
                    cachedReport=report;cachedPointer=pointerText;
                }
                var activePath=Path.Combine(paths.Data,"engine-bundles","active.json");
                var active=File.Exists(activePath)?Json.Decode<ActivePointer>(File.ReadAllText(activePath)).Id:null;
                var current=cachedReport!.BundleId==active;
                foreach(var source in cachedReport.SourceHashes){
                    var sourcePath=Path.GetFullPath(Path.Combine(paths.Root,source.Key));
                    if(!sourcePath.StartsWith(paths.Root+Path.DirectorySeparatorChar,StringComparison.Ordinal))throw new InvalidDataException("Invalid evaluation source path.");
                    if(!File.Exists(sourcePath)||Hash(sourcePath)!=source.Value)current=false;
                }
                return new(cachedReport,current,null);
            }catch(Exception error)when(error is IOException or System.Text.Json.JsonException or InvalidDataException or ArgumentException){
                return new(null,false,"Pilot evaluation unavailable: "+error.Message);
            }
        }
    }
    internal sealed record Pointer(string Directory,string Sha256);
    internal sealed record ActivePointer(string Id);

    public PilotSourceDocument ReadSource(PilotEvaluationReport report,string file)
    {
        if(!report.SourceHashes.TryGetValue(file,out var expected)||Path.IsPathRooted(file)||file.Split('/').Any(p=>p is "." or ".."))
            throw new ArgumentException("This source is not part of the evaluation.");
        if(report.Id.Length>80||report.Id.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='-'))throw new ArgumentException("Invalid report ID.");
        var path=Path.Combine(paths.Data,"pilot-evaluations",report.Id,"sources",file);
        if(!File.Exists(path))throw new InvalidDataException("This older report has no frozen source snapshot.");
        if(Hash(path)!=expected)throw new InvalidDataException("Evaluation source checksum failed.");
        return new(file,File.ReadAllText(path),expected);
    }
}
