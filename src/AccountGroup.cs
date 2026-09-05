namespace OtpManager;

/// <summary>A heading in the list. Groups are ordered and can be folded away.</summary>
internal sealed class AccountGroup
{
    public string Name { get; set; } = "";
    public bool Collapsed { get; set; }
}
