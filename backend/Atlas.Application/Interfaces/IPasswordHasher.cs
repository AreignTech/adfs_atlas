namespace Atlas.Application.Interfaces;

/// <summary>Password hashing and verification abstraction.</summary>
public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string? hash, string password);
}
