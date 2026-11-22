using Atlas.Application.Interfaces;
using Atlas.Application.Interfaces.Repositories;
using Atlas.Domain.Entities;
using Atlas.Infrastructure;
using Atlas.Infrastructure.Data;
using Atlas.Infrastructure.Data.Migrations;
using Atlas.Infrastructure.Security;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

var command = (args.FirstOrDefault() ?? "help").ToLowerInvariant();

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var services = new ServiceCollection();
services.AddSingleton<IConfiguration>(configuration);
services.AddAtlasInfrastructure(configuration);
await using var provider = services.BuildServiceProvider();

var migrator = provider.GetRequiredService<DatabaseMigrator>();
var seeder = provider.GetRequiredService<DatabaseSeeder>();

try
{
    switch (command)
    {
        case "import-data":
            await SourceDataImporter.ImportAsync(provider.GetRequiredService<IDbConnectionFactory>());
            break;
        case "verify-data":
            await SourceDataImporter.ImportAsync(provider.GetRequiredService<IDbConnectionFactory>(), true);
            break;
        case "repair-timestamp-precision":
            await SourceDataImporter.ImportAsync(provider.GetRequiredService<IDbConnectionFactory>(), true, true);
            break;
        case "migrate":
        {
            var applied = await migrator.MigrateAsync();
            if (applied.Count == 0)
            {
                Console.WriteLine("No pending migrations.");
            }
            else
            {
                foreach (var script in applied)
                {
                    Console.WriteLine($"Applied {script}");
                }
            }
            break;
        }

        case "status":
        {
            var applied = (await migrator.GetAppliedScriptsAsync()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var script in DatabaseMigrator.Scripts())
            {
                var name = script[(script.LastIndexOf(".Migrations.", StringComparison.OrdinalIgnoreCase) + ".Migrations.".Length)..];
                Console.WriteLine($"{(applied.Contains(name) ? "[applied]" : "[pending]")} {name}");
            }
            break;
        }

        case "seed":
        {
            var force = args.Contains("--force", StringComparer.OrdinalIgnoreCase);
            var password = Environment.GetEnvironmentVariable("ATLAS_DEMO_PASSWORD");
            if (string.IsNullOrWhiteSpace(password) || password.Length < 12)
            {
                throw new InvalidOperationException("Set ATLAS_DEMO_PASSWORD to a development password of at least 12 characters before seeding.");
            }
            var result = await seeder.SeedDemoAsync(force, password);
            if (result.Skipped)
            {
                Console.WriteLine($"Skipped: database already contains {result.Users} user(s). Use 'seed --force' to reseed.");
            }
            else
            {
                Console.WriteLine(
                    $"Seeded users={result.Users}, members={result.Members}, projects={result.Projects}, tasks={result.Tasks}, accounts={result.Accounts}.");
            }
            break;
        }

        case "verify-schema":
        {
            await using var connection = await provider.GetRequiredService<IDbConnectionFactory>().OpenAsync();
            var assembly = typeof(DatabaseMigrator).Assembly;
            var resource = assembly.GetManifestResourceNames().Single(n => n.EndsWith(".Verification.schema.sql", StringComparison.Ordinal));
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            var result = await connection.QuerySingleAsync<string>(new CommandDefinition(await reader.ReadToEndAsync()));
            Console.WriteLine(result);
            break;
        }

        case "verify":
            await RepositoryVerification.RunAsync(provider);
            break;

        case "migrate-data":
            Console.Error.WriteLine("The PostgreSQL data migration utility is not implemented yet. See backend/README.md and progress.md.");
            Environment.ExitCode = 2;
            break;

        default:
            Console.WriteLine("""
                Atlas.DbTool - SQL Server schema and development data tool

                Usage:
                  dotnet run --project Atlas.DbTool -- migrate
                  dotnet run --project Atlas.DbTool -- status
                  dotnet run --project Atlas.DbTool -- verify-schema
                  dotnet run --project Atlas.DbTool -- seed [--force]
                  dotnet run --project Atlas.DbTool -- verify

                Connection string: ConnectionStrings__Atlas environment variable
                (falls back to appsettings.json, Windows integrated auth).
                """);
            break;
    }
}
catch (Exception exception)
{
    if (command is "import-data" or "verify-data" or "repair-timestamp-precision") Console.Error.WriteLine(exception is InvalidOperationException ? exception.Message : "Data transfer/verification failed. Any transaction was rolled back; source values are omitted from diagnostics.");
    else Console.Error.WriteLine(exception);
    Environment.ExitCode = 1;
}

/// <summary>
/// Exercises every repository against the live SQL Server database, then removes
/// everything it created. Run after `migrate` and `seed`.
/// </summary>
internal static class RepositoryVerification
{
    public static async Task RunAsync(IServiceProvider provider)
    {
        await using var verificationConnection = await provider.GetRequiredService<IDbConnectionFactory>().OpenAsync();
        if (!verificationConnection.Database.Contains("Verification", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Repository checks require a dedicated database whose name contains Verification.");
        var users = provider.GetRequiredService<IUserRepository>();
        var sessions = provider.GetRequiredService<ISessionRepository>();
        var accounts = provider.GetRequiredService<IAccountRepository>();
        var projects = provider.GetRequiredService<IProjectRepository>();
        var tasks = provider.GetRequiredService<ITaskRepository>();
        var members = provider.GetRequiredService<IMemberRepository>();
        var projectMembers = provider.GetRequiredService<IProjectMemberRepository>();
        var labs = provider.GetRequiredService<ILabRepository>();
        var sections = provider.GetRequiredService<ISectionRepository>();
        var assignments = provider.GetRequiredService<IOrgAssignmentRepository>();
        var orgAudits = provider.GetRequiredService<IOrgAuditRepository>();
        var roles = provider.GetRequiredService<IRoleRepository>();
        var permissions = provider.GetRequiredService<IPermissionRepository>();
        var userRoles = provider.GetRequiredService<IUserRoleRepository>();
        var rolePermissions = provider.GetRequiredService<IRolePermissionRepository>();
        var rbacAudits = provider.GetRequiredService<IRbacAuditRepository>();
        var ldapConfig = provider.GetRequiredService<ILdapConfigRepository>();
        var ldapAudits = provider.GetRequiredService<ILdapAuditRepository>();
        var verifications = provider.GetRequiredService<IVerificationRepository>();
        var ldapIdentities = provider.GetRequiredService<ILdapIdentityRepository>();
        var ldapMappings = provider.GetRequiredService<ILdapGroupMappingRepository>();
        var previousMappings = await ldapMappings.ListAsync();
        var previousConfig = await ldapConfig.GetAsync();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var createdUser = false;
        var createdLab = false;
        var createdSection = false;
        var createdProject = false;
        var createdRole = false;
        Guid? verificationProjectId = null;

        try
        {
            // Read models that must already exist after seeding.
            var existingUsers = await users.ListAsync();
            var existingProjects = await projects.ListAsync(null);
            var existingTasks = await tasks.ListAsync(null);
            var existingMembers = await members.ListWithUserAsync();
            var existingRoles = await roles.ListAsync();
            var existingPermissions = await permissions.ListAsync();
            Check(existingUsers.Count > 0, "user repository returns seeded users");
            Check(existingProjects.Count > 0, "project repository returns seeded projects");
            Check(existingTasks.Count > 0, "task repository returns seeded tasks");
            Check(existingMembers.Count > 0, "member repository returns joined members");
            Check(existingRoles.Count >= 3, "role repository returns the three system roles");
            Check(existingPermissions.Count == 21, "permission repository returns all 21 permissions");
            Check(await ldapConfig.GetAsync() is null or { Id: 1 }, "LDAP config repository is readable");

            // Organization codes.
            var lab = new LabCode { Code = $"VLAB{suffix}", Name = "Verification Lab", IsActive = true };
            await labs.CreateAsync(lab);
            createdLab = true;
            Check(await labs.ExistsCodeAsync(lab.Code, null), "lab code exists after create");
            Check((await labs.GetByIdAsync(lab.Id))?.Code == lab.Code, "lab code round-trips");

            var section = new SectionCode { Code = $"VSEC{suffix}", Name = "Verification Section", IsActive = true };
            await sections.CreateAsync(section);
            createdSection = true;
            Check((await sections.GetByIdAsync(section.Id))?.Name == section.Name, "section code round-trips");

            // Identity, membership and org assignment.
            var userId = DatabaseSeeder.UserIdFor($"verify-{suffix}@atlas.local");
            await users.CreateAsync(new User
            {
                Id = userId,
                Name = "Verification User",
                Email = $"verify-{suffix}@atlas.local"
            });
            createdUser = true;
            Check((await users.GetByEmailAsync($"verify-{suffix}@atlas.local"))?.Id == userId, "user round-trips by email");

            var member = new Member { UserId = userId, Role = "Users", Status = "invited" };
            await members.CreateAsync(member);
            Check((await members.GetByUserIdAsync(userId))?.Id == member.Id, "member round-trips by user id");

            await assignments.UpsertAsync(new UserOrgAssignment
            {
                UserId = userId,
                LabId = lab.Id,
                SectionId = section.Id,
                AssignedBy = "verify"
            });
            var assignment = await assignments.GetForUserAsync(userId);
            Check(assignment?.LabId == lab.Id && assignment.SectionId == section.Id, "org assignment upserts");
            await assignments.UpsertAsync(new UserOrgAssignment
            {
                UserId = userId,
                LabId = lab.Id,
                SectionId = section.Id,
                AssignedBy = "verify-2"
            });
            Check((await assignments.GetForUserAsync(userId))?.AssignedBy == "verify-2", "org assignment updates");

            // Password hashing compatibility.
            var hasher = provider.GetRequiredService<IPasswordHasher>();
            var hash = hasher.Hash("P@ssword1");
            Check(hasher.Verify(hash, "P@ssword1") && !hasher.Verify(hash, "wrong"), "scrypt password hashing verifies");

            var account = new Account
            {
                Id = Guid.NewGuid().ToString("N"),
                AccountId = userId,
                ProviderId = "credential",
                UserId = userId,
                Password = hash
            };
            await accounts.CreateAsync(account);
            Check((await accounts.GetCredentialAsync(userId))?.Password == hash, "credential account round-trips");
            Check(await accounts.UpdatePasswordAsync(userId, hasher.Hash("N3wPassword!")), "credential password updates");

            // Sessions.
            var session = new Session
            {
                Id = Guid.NewGuid().ToString("N"),
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
                Token = Convert.ToHexStringLower(Guid.NewGuid().ToByteArray()),
                UserId = userId,
                IpAddress = "127.0.0.1",
                UserAgent = "Atlas.DbTool/verify"
            };
            await sessions.CreateAsync(session);
            Check((await sessions.GetByTokenAsync(session.Token))?.UserId == userId, "session round-trips by token");
            await verifications.CreateAsync(new Verification { Id = suffix, Identifier = suffix, Value = "verification", ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1) });
            Check((await verifications.GetAsync(suffix))?.Value == "verification", "verification record round-trips");
            await verifications.DeleteExpiredAsync(DateTimeOffset.UtcNow);
            Check(await verifications.GetAsync(suffix) is null, "expired verification records are removed");
            await ldapIdentities.CreateAsync(new LdapIdentity { UserId = userId, DirectoryIdentifier = suffix, DirectoryDn = "cn=test" });
            Check((await ldapIdentities.GetByDirectoryIdentifierAsync(suffix))?.UserId == userId, "LDAP identity round-trips by directory ID");
            Check((await ldapIdentities.GetByUserIdAsync(userId))?.DirectoryIdentifier == suffix, "LDAP identity round-trips by user ID");
            await ldapMappings.ReplaceAllAsync([new LdapGroupMapping { LdapGroup = suffix, AtlasRole = "Users" }]);
            Check((await ldapMappings.ListAsync()).Single().LdapGroup == suffix, "LDAP group mapping replacement round-trips");
            await ldapConfig.UpsertAsync(new LdapConfig { Host = "localhost", Protocol = "ldaps", Port = 636 });
            await ldapConfig.UpdateTestStatusAsync(true, DateTimeOffset.UtcNow);
            Check((await ldapConfig.GetAsync()) is { Host: "localhost", Status: "successful" }, "LDAP config upsert and test status round-trip");
            await sessions.TouchAsync(session.Id, DateTimeOffset.UtcNow.AddDays(2), DateTimeOffset.UtcNow);
            Check((await sessions.GetByTokenAsync(session.Token))?.ExpiresAt > DateTimeOffset.UtcNow.AddDays(1), "session touch extends expiry");

            // Projects, memberships, tasks.
            var project = new Project
            {
                Name = $"Verification Project {suffix}",
                Key = $"V{suffix[..3]}".ToUpperInvariant(),
                Description = "Created by Atlas.DbTool verify",
                Status = "planning",
                Color = "var(--chart-1)",
                OwnerId = member.Id,
                StartDate = DateTime.UtcNow.Date,
                DueDate = DateTime.UtcNow.Date.AddDays(7),
                LabId = lab.Id,
                SectionId = section.Id
            };
            await projects.CreateAsync(project);
            verificationProjectId = project.Id;
            createdProject = true;
            Check(project.Id != Guid.Empty && project.CreatedAt != default, "project create populates identity and timestamps");
            await projectMembers.ReplaceForProjectAsync(project.Id, [member.Id]);
            Check((await projectMembers.ListForProjectAsync(project.Id)).Count == 1, "project membership replaces");
            Check(await projects.CountOwnedAsync(member.Id) == 1, "project ownership count");

            var scoped = await projects.ListAsync(new OrgScopeFilter(false, lab.Id, section.Id));
            Check(scoped.Count == 1 && scoped[0].Id == project.Id, "project list honours org scope");
            var otherScope = await projects.ListAsync(new OrgScopeFilter(false, Guid.NewGuid(), Guid.NewGuid()));
            Check(otherScope.Count == 0, "project list excludes other org scopes");

            var position = await tasks.NextPositionAsync(project.Id, "todo");
            var task = new TaskItem
            {
                ProjectId = project.Id,
                Title = "Verification task",
                Description = "Round-trip",
                Status = "todo",
                Priority = "high",
                AssigneeId = member.Id,
                DueDate = DateTime.UtcNow.Date.AddDays(3),
                LabId = lab.Id,
                SectionId = section.Id,
                Position = position
            };
            await tasks.CreateAsync(task);
            Check(task.Reference.StartsWith("TSK-"), "task reference is generated by the SQL sequence");
            var nextSequence = await verificationConnection.ExecuteScalarAsync<long>("SELECT COALESCE(MAX(TRY_CONVERT(bigint,SUBSTRING(reference,5,20))),0)+1 FROM dbo.task");
            try
            {
                await verificationConnection.ExecuteAsync("ALTER SEQUENCE dbo.task_reference_seq RESTART WITH 10000");
                var boundaryTask = new TaskItem { ProjectId = project.Id, Title = "Reference boundary", DueDate = DateTime.Today, LabId = lab.Id, SectionId = section.Id };
                await tasks.CreateAsync(boundaryTask);
                Check(boundaryTask.Reference == "TSK-10000", "task references do not truncate after 9999");
                await tasks.DeleteAsync(boundaryTask.Id);
            }
            finally { await verificationConnection.ExecuteAsync($"ALTER SEQUENCE dbo.task_reference_seq RESTART WITH {nextSequence}"); }
            var concurrentTasks = Enumerable.Range(0, 2).Select(i => new TaskItem { ProjectId = project.Id, Title = "Concurrent " + i, DueDate = DateTime.Today, LabId = lab.Id, SectionId = section.Id }).ToArray();
            await Task.WhenAll(concurrentTasks.Select(t => tasks.CreateAsync(t)));
            Check(concurrentTasks.Select(t => t.Position).Distinct().Count() == 2, "concurrent task creation assigns distinct positions transactionally");
            foreach (var concurrentTask in concurrentTasks) await tasks.DeleteAsync(concurrentTask.Id);
            Check((await tasks.GetByIdAsync(task.Id))?.Title == task.Title, "task round-trips");

            task.Status = "completed";
            task.CompletedAt = DateTimeOffset.UtcNow;
            Check(await tasks.UpdateAsync(task), "task updates");
            await tasks.ReorderAsync(project.Id, [new Atlas.Application.DTOs.TaskReorderItem
            {
                TaskId = task.Id.ToString(),
                Status = "in-review",
                Position = 5
            }]);
            var reordered = await tasks.GetByIdAsync(task.Id);
            Check(reordered is not null && reordered.Status == "in-review" && reordered.Position == 5, "task reorder updates status and position");
            Check(reordered is not null && reordered.CompletedAt is null, "leaving completed clears completed_at");
            try
            {
                await projectMembers.ReplaceForProjectAsync(project.Id, [Guid.NewGuid()]);
                throw new InvalidOperationException("Expected foreign key violation.");
            }
            catch (Microsoft.Data.SqlClient.SqlException)
            {
                Check((await projectMembers.ListForProjectAsync(project.Id)).SequenceEqual([member.Id]), "failed membership replacement rolls back prior deletion");
            }
            try
            {
                await tasks.ReorderAsync(project.Id, [new() { TaskId = task.Id.ToString(), Status = "todo", Position = 1 }, new() { TaskId = task.Id.ToString(), Status = "invalid", Position = 2 }]);
                throw new InvalidOperationException("Expected status constraint violation.");
            }
            catch (Microsoft.Data.SqlClient.SqlException)
            {
                Check((await tasks.GetByIdAsync(task.Id))?.Status == "in-review", "failed reorder rolls back every preceding update");
            }
            var search = provider.GetRequiredService<ISearchRepository>();
            var page = await search.ProjectsAsync(new(false, lab.Id, section.Id), new(1, 1, suffix, "planning"));
            Check(page.Total == 1 && page.Items.Single().Id == project.Id, "pagination, literal search and status filtering");
            Check((await search.ProjectsAsync(new(false, Guid.NewGuid(), Guid.NewGuid()), new())).Total == 0, "paged query respects organizational scope");
            Check((await search.ProjectsAsync(new(true, null, null), new(Search: "' OR 1=1 --"))).Total == 0, "search parameters cannot inject SQL");
            Check(await tasks.CountByProjectAsync(project.Id) == 1, "task count by project");

            // RBAC.
            var role = new AppRole
            {
                Name = $"Verification Role {suffix}",
                Description = "Created by verify",
                IsSystemRole = false,
                IsActive = true
            };
            await roles.CreateAsync(role);
            createdRole = true;
            var selected = await permissions.GetByKeysAsync(["tasks.view", "tasks.create", "reports.view"]);
            Check(selected.Count == 3, "permission lookup by keys");
            await rolePermissions.ReplaceForRoleAsync(role.Id, selected.Select(p => p.Id));
            var keys = await rolePermissions.ListKeysForRolesAsync([role.Id]);
            Check(keys.Count == 3 && keys.Contains("tasks.view"), "role permission replace and read");

            await userRoles.ReplaceForUserAsync(userId, role.Id, "verify");
            var assignmentRow = await userRoles.GetForUserAsync(userId);
            Check(assignmentRow?.RoleName == role.Name, "user role replace and read");
            Check(await roles.CountAssignedUsersAsync(role.Id) == 1, "role assignment count");
            Check((await roles.ListAssignedUserNamesAsync(role.Id)).Contains("Verification User"), "role assigned user names");

            // Audits.
            await orgAudits.InsertAsync(new OrgAudit
            {
                ActorEmail = "verify@atlas.local",
                Action = "verify.org",
                AffectedLabId = lab.Id,
                Success = true,
                Diagnostic = "verify"
            });
            Check((await orgAudits.ListRecentAsync(10)).Any(a => a.Action == "verify.org"), "org audit insert and read");
            await rbacAudits.InsertAsync(new RbacAudit
            {
                ActorEmail = "verify@atlas.local",
                Action = "verify.rbac",
                Success = true
            });
            Check((await rbacAudits.ListRecentAsync(10)).Any(a => a.Action == "verify.rbac"), "RBAC audit insert and read");
            await ldapAudits.InsertAsync(new LdapAudit
            {
                EventType = "verify.ldap",
                Administrator = "verify@atlas.local",
                Success = true,
                Diagnostic = "verify"
            });
            Check((await ldapAudits.ListRecentAsync(10)).Any(a => a.EventType == "verify.ldap"), "LDAP audit insert and read");

            Console.WriteLine("PASS: all repository checks succeeded.");
        }
        finally
        {
            await ldapMappings.ReplaceAllAsync(previousMappings);
            if (previousConfig is not null) await ldapConfig.UpsertAsync(previousConfig);
            else await verificationConnection.ExecuteAsync("DELETE FROM dbo.ldap_config WHERE id=1");
            // Remove verification data in dependency order. Cleanup failures are
            // reported but never hide the original verification result.
            if (createdProject && verificationProjectId is Guid cleanupProjectId)
            {
                await projects.DeleteAsync(cleanupProjectId);
            }

            if (createdUser)
            {
                await users.DeleteAsync(DatabaseSeeder.UserIdFor($"verify-{suffix}@atlas.local"));
            }

            if (createdRole)
            {
                var role = await roles.GetByNameAsync($"Verification Role {suffix}");
                if (role is not null)
                {
                    await roles.DeleteAsync(role.Id);
                }
            }

            if (createdLab)
            {
                var lab = (await labs.ListAsync(true)).FirstOrDefault(l => l.Code == $"VLAB{suffix}");
                if (lab is not null)
                {
                    await labs.DeleteAsync(lab.Id);
                }
            }

            if (createdSection)
            {
                var section = (await sections.ListAsync(true)).FirstOrDefault(s => s.Code == $"VSEC{suffix}");
                if (section is not null)
                {
                    await sections.DeleteAsync(section.Id);
                }
            }
        }
    }

    private static void Check(bool condition, string description)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"FAILED: {description}");
        }

        Console.WriteLine($"  ok  {description}");
    }
}
