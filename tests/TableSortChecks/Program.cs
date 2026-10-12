using DeckTester;
static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
var sort=new TableSort();
var rows=new[]{(Name:"ten",Value:(double?)10),(Name:"two",Value:(double?)2),(Name:"unknown",Value:(double?)null),(Name:"equal",Value:(double?)2)};
sort.Toggle("value");
Check(sort.Apply(rows,(r,_)=>r.Value).Select(r=>r.Name).SequenceEqual(["two","equal","ten","unknown"]),"Sort numbers numerically, retain ties, and keep unknowns last.");
sort.Toggle("value");
Check(sort.Descending&&sort.Apply(rows,(r,_)=>r.Value).Select(r=>r.Name).SequenceEqual(["ten","two","equal","unknown"]),"Reverse numeric order while retaining ties and unknown placement.");
sort.Toggle("name");
Check(!sort.Descending&&sort.Apply(new[]{"beta","Alpha","alpha"},(r,_)=>r).SequenceEqual(["Alpha","alpha","beta"]),"A new column starts ascending with case-insensitive stable text order.");
sort.Toggle("value");sort.Toggle("value");
Check(sort.Apply(Enumerable.Range(1,100),(r,_)=>r).Take(25).First()==100,"Sort the entire collection before selecting a page.");
var dates=new[]{DateTimeOffset.Parse("2026-10-12T08:00:00Z"),DateTimeOffset.Parse("2026-10-11T23:00:00Z")};
Check(sort.Apply(dates,(r,_)=>r).First()==dates[0],"Sort dates by timestamp rather than formatted month/day text.");
var pairs=new[]{(Median:1.0,Max:5),(Median:1.0,Max:2),(Median:2.0,Max:1)};
sort.Toggle("pair");
Check(sort.Apply(pairs,(r,_)=>(r.Median,r.Max)).SequenceEqual([pairs[1],pairs[0],pairs[2]]),"Compound summary values use the primary measurement then the secondary measurement.");
Check(new TableSort().Apply(rows,(r,_)=>r.Value).SequenceEqual(rows),"Tables retain their existing order before a column is selected.");
Console.WriteLine("Table sort checks passed: numeric and text ordering, stable ties, unknowns, timestamps, compound values, and whole-collection pagination.");
