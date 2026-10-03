using Mapna.Contracts;

namespace Mapna.Sender;

public sealed class SourceSnapshot
{
    public Dictionary<int, PersonnelRecord> ByPerId { get; } = new();
    public Dictionary<int, int> DuplicatePerIds { get; } = new();
    public Dictionary<int, string> Names { get; } = new();

    public int Count => ByPerId.Count + DuplicatePerIds.Count;

    public IEnumerable<int> AllPerIds => ByPerId.Keys.Concat(DuplicatePerIds.Keys);

    public static SourceSnapshot Create(IEnumerable<PersonnelRecord> records)
    {
        var snapshot = new SourceSnapshot();
        foreach (var group in records.GroupBy(r => r.PerId))
        {
            var first = group.First();
            snapshot.Names[group.Key] = DisplayName(first);
            var count = group.Count();
            if (count == 1)
                snapshot.ByPerId[group.Key] = first;
            else
                snapshot.DuplicatePerIds[group.Key] = count;
        }
        return snapshot;
    }

    public static string DisplayName(PersonnelRecord r) => $"{r.PerName} {r.PerSurname}".Trim();
}
