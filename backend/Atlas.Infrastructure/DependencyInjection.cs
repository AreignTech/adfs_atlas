using Atlas.Application.Interfaces;
using Atlas.Application.Interfaces.Repositories;
using Atlas.Infrastructure.Data;
using Atlas.Infrastructure.Data.Migrations;
using Atlas.Infrastructure.Repositories;
using Atlas.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Atlas.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the SQL Server data layer, repositories and security services.
    /// Shared by the API and the database CLI so both use identical wiring.
    /// </summary>
    public static IServiceCollection AddAtlasInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<IDbConnectionFactory, SqlServerConnectionFactory>();
        services.AddSingleton<IPasswordHasher, BetterAuthPasswordHasher>();
        services.AddSingleton<ISecretProtector, LdapSecretProtector>();
        services.AddScoped<ILdapDirectory, LdapDirectory>();
        services.AddScoped<ILdapUserWriter, LdapUserWriter>();
        services.AddSingleton<DatabaseMigrator>();
        services.AddSingleton<DatabaseSeeder>();
        services.AddSingleton<Atlas.Application.Observability.IMonitoringStore, MonitoringStore>();

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IIdentityWriter, IdentityWriter>();
        services.AddScoped<ISessionRepository, SessionRepository>();
        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<IVerificationRepository, VerificationRepository>();

        services.AddScoped<IProjectRepository, ProjectRepository>();
        services.AddScoped<ISearchRepository, SearchRepository>();
        services.AddScoped<IWorkspaceWriter, WorkspaceWriter>();
        services.AddScoped<IAdminWriter, AdminWriter>();
        services.AddScoped<ISettingsRepository, SettingsRepository>();
        services.AddScoped<ITaskRepository, TaskRepository>();
        services.AddScoped<ITaskCommentRepository, TaskCommentRepository>();
        services.AddScoped<IMemberRepository, MemberRepository>();
        services.AddScoped<IProjectMemberRepository, ProjectMemberRepository>();

        services.AddScoped<ILabRepository, LabRepository>();
        services.AddScoped<ISectionRepository, SectionRepository>();
        services.AddScoped<IOrgAssignmentRepository, OrgAssignmentRepository>();
        services.AddScoped<IOrgAuditRepository, OrgAuditRepository>();

        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IPermissionRepository, PermissionRepository>();
        services.AddScoped<IUserRoleRepository, UserRoleRepository>();
        services.AddScoped<IRolePermissionRepository, RolePermissionRepository>();
        services.AddScoped<IRbacAuditRepository, RbacAuditRepository>();

        services.AddScoped<ILdapConfigRepository, LdapConfigRepository>();
        services.AddScoped<ILdapIdentityRepository, LdapIdentityRepository>();
        services.AddScoped<ILdapGroupMappingRepository, LdapGroupMappingRepository>();
        services.AddScoped<ILdapAuditRepository, LdapAuditRepository>();

        return services;
    }
}
