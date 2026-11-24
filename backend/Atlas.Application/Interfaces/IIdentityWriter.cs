using Atlas.Domain.Entities;
namespace Atlas.Application.Interfaces;

public interface IIdentityWriter
{
    Task<User> AcceptInvitationAsync(string tokenHash, string email, string name, string passwordHash, CancellationToken cancellationToken = default);
    Task CreateAsync(User user, Member member, Account? account, string? roleName = null, CancellationToken cancellationToken = default);
    Task ChangePasswordAsync(string userId, string passwordHash, string? keepTokenHash, CancellationToken cancellationToken = default);
}
