using System.Reflection;

namespace Mapna.LogData;

public static class FieldChangeDetector
{
    public static IReadOnlyList<string> GetChangedField<Tsource,TTarget>(Tsource src,TTarget target,params string[] excludeprop)
    {
        var changed = new List<string>();
        var sourceProps = typeof(Tsource).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        var targetProps = typeof(TTarget).GetProperties(BindingFlags.Public | BindingFlags.Instance).ToDictionary(p=>p.Name,p=>p);


        foreach (var sourceProp in sourceProps)
        {
            if (excludeprop.Contains(sourceProp.Name))
                continue;

            if (!targetProps.TryGetValue(sourceProp.Name, out var targetProp))
                continue;

            var sourceValue = sourceProp.GetValue(src);
            var targetValue = targetProp.GetValue(target);

            if (!AreEqual(sourceValue, targetValue))
                changed.Add(sourceProp.Name);
        }

        return changed;

    }

    private static bool AreEqual(object? a,object? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;

        return a.Equals(b);
    }
}
