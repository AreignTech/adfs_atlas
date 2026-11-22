using Atlas.Domain.Entities;
namespace Atlas.Application.Interfaces;

public interface IIdentityWriter
{
    Task CreateAsync(User user, Member member, Account? account, string? roleName = null, CancellationToken cancellationToken = default);
    Task ChangePasswordAsync(string userId, string passwordHash, string? keepTokenHash, CancellationToken cancellationToken = default);
}
