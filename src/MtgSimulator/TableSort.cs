namespace MtgSimulator;

public sealed class TableSort
{
    public string? Column { get; private set; }
    public bool Descending { get; private set; }
    public void Toggle(string column){Descending=Column==column&&!Descending;Column=column;}
    public IEnumerable<T> Apply<T>(IEnumerable<T> rows,Func<T,string,IComparable?> value)
    {
        if(Column is null)return rows;
        var column=Column;var descending=Descending;
        return rows.OrderBy(row=>value(row,column),Comparer<IComparable?>.Create((left,right)=>{
            // Unknown values stay last in both directions; LINQ preserves ties.
            if(left is null)return right is null?0:1;
            if(right is null)return -1;
            var comparison=left is string a&&right is string b?StringComparer.OrdinalIgnoreCase.Compare(a,b):left.CompareTo(right);
            return descending?-Math.Sign(comparison):comparison;
        }));
    }
}
