namespace squad.Domain;

/// <summary>A squad member's provider-neutral permission behavior: <see cref="Prompt"/> (the default) presents
/// each permission request for user approval; <see cref="ApproveAll"/> automatically approves requests that do
/// not require managed approval. The stable JSON spellings ("prompt"/"approveAll") are mapped to and from this
/// enum at the configuration boundary.</summary>
public enum PermissionMode
{
    Prompt,
    ApproveAll
}
