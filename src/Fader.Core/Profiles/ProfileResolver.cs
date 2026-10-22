using Fader.Core.Apps;

namespace Fader.Core.Profiles;

/// <summary>An application as seen by the resolver: its group key, identity and effective category.</summary>
public readonly record struct ResolverInput(string Key, AppIdentity Identity, AppCategory Category);

public sealed record ResolvedTarget(string GroupKey, ProfileEntry Entry)
{
    public int Volume => Entry.Volume;
    public bool Mute => Entry.Mute;
}

/// <summary>Decides which profile entry, if any, applies to each running application.</summary>
public static class ProfileResolver
{
    public static ResolvedTarget? ResolveOne(Profile profile, ResolverInput input)
    {
        ProfileEntry? categoryHit = null;
        foreach (var entry in profile.Entries)
        {
            if (entry.Kind == ProfileTargetKind.App)
            {
                if (entry.App is not null && entry.App.Matches(input.Identity))
                    return new ResolvedTarget(input.Key, entry);
            }
            else if (categoryHit is null && entry.Category == input.Category)
            {
                categoryHit = entry;
            }
        }
        return categoryHit is null ? null : new ResolvedTarget(input.Key, categoryHit);
    }

    public static IReadOnlyList<ResolvedTarget> Resolve(Profile profile, IEnumerable<ResolverInput> inputs)
    {
        var result = new List<ResolvedTarget>();
        foreach (var input in inputs)
        {
            var target = ResolveOne(profile, input);
            if (target is not null) result.Add(target);
        }
        return result;
    }
}
