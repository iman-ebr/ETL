using Mapna.Contracts;

namespace Mapna.Sender;

/// <summary>
/// The source rows of one run, indexed by PerId. <c>records.ToDictionary(r => r.PerId)</c> used to throw on the first
/// duplicate PER_ID, so one bad source row stopped the sync for all personnel. Picking one of the duplicates
/// "arbitrarily" would be worse: it could overwrite a real person with the wrong row. Duplicated PerIds are kept
/// aside, reported as ValidationFailed, and never sent.
/// </summary>
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
