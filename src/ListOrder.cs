namespace OtpManager;

/// <summary>
/// The order of the list, as one sequence of tokens: <c>g:name</c> for a group heading and
/// <c>a:id</c> for an account.
/// <para>
/// Keeping headings and accounts in the same sequence is what lets ungrouped accounts sit between
/// groups. The one rule is that a group's members follow its heading immediately - everything else
/// is free-form, and <see cref="Repair"/> puts back any arrangement that breaks it.
/// </para>
/// </summary>
internal static class ListOrder
{
    public const string GroupPrefix = "g:";
    public const string AccountPrefix = "a:";

    public static string ForGroup(string name) => GroupPrefix + name;
    public static string ForAccount(Account account) => AccountPrefix + account.Id;

    public static bool IsGroup(string token) => token.StartsWith(GroupPrefix, StringComparison.Ordinal);
    public static bool IsAccount(string token) => token.StartsWith(AccountPrefix, StringComparison.Ordinal);

    public static string GroupName(string token) => token[GroupPrefix.Length..];
    public static string AccountId(string token) => token[AccountPrefix.Length..];

    /// <summary>
    /// Returns a layout that lists every group and account exactly once, keeps the order of the
    /// given one as far as the rules allow, and puts each group's members under its heading.
    /// </summary>
    public static List<string> Repair(List<string> layout, List<Account> accounts, List<AccountGroup> groups)
    {
        var byId = accounts.Where(a => a.Id.Length > 0).ToDictionary(a => a.Id);
        var groupNames = groups.Select(g => g.Name).ToHashSet(StringComparer.Ordinal);

        var placed = new HashSet<string>(StringComparer.Ordinal);
        var emitted = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();

        // Members keep the relative order they already had, so repairing never shuffles a group.
        var order = new Dictionary<string, int>(StringComparer.Ordinal);
        for(var i = 0; i < layout.Count; i++)
            if(IsAccount(layout[i])) order.TryAdd(AccountId(layout[i]), i);

        void EmitGroup(string name)
        {
            if(!emitted.Add(name)) return;
            result.Add(ForGroup(name));
            var members = accounts
                .Where(a => a.Group == name && a.Id.Length > 0)
                .OrderBy(a => order.TryGetValue(a.Id, out var at) ? at : int.MaxValue);
            foreach(var member in members)
            {
                if(!placed.Add(member.Id)) continue;
                result.Add(ForAccount(member));
            }
        }

        foreach(var token in layout)
        {
            if(IsGroup(token))
            {
                var name = GroupName(token);
                if(groupNames.Contains(name)) EmitGroup(name);
                continue;
            }
            if(!IsAccount(token)) continue;

            var id = AccountId(token);
            if(!byId.TryGetValue(id, out var account) || placed.Contains(id)) continue;

            // A grouped account pulls its whole group into place the first time it is reached.
            if(account.Group.Length > 0 && groupNames.Contains(account.Group)) EmitGroup(account.Group);
            else if(placed.Add(id)) result.Add(token);
        }

        foreach(var group in groups) EmitGroup(group.Name);
        foreach(var account in accounts)
        {
            if(account.Id.Length == 0 || placed.Contains(account.Id)) continue;
            if(account.Group.Length > 0 && groupNames.Contains(account.Group)) { EmitGroup(account.Group); continue; }
            placed.Add(account.Id);
            result.Add(ForAccount(account));
        }

        return result;
    }
}
