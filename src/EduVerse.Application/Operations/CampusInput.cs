namespace EduVerse.Application.Operations;

/// <summary>Normalization shared by regular and idempotent campus commands.</summary>
public static class CampusInput
{
    public static (string Code, string Name) Normalize(string code, string name)
    {
        code = code.Trim().ToUpperInvariant();
        name = name.Trim();
        if (code.Length is < 2 or > 30 || !code.All(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '-'))
            throw new ArgumentException("Campus code must be 2-30 characters (A-Z, 0-9, hyphen).", nameof(code));
        if (name.Length is < 2 or > 160)
            throw new ArgumentException("Campus name must be 2-160 characters.", nameof(name));
        return (code, name);
    }
}
