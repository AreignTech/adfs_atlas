using System.Text;
using Atlas.Application.Interfaces;
using Dapper;

namespace Atlas.Infrastructure.Data;

public sealed record SeedResult(bool Skipped, int Users, int Members, int Projects, int Tasks, int Accounts);

/// <summary>
/// Reproduces the PostgreSQL demo workspace (scripts/seed.ts + seed-auth.ts)
/// on SQL Server. Reference roles/permissions come from migration 002.
///
/// Safe to re-run: existing data is preserved unless <c>force</c> is set.
/// </summary>
public sealed class DatabaseSeeder
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IPasswordHasher _passwordHasher;

    public DatabaseSeeder(IDbConnectionFactory connectionFactory, IPasswordHasher passwordHasher)
    {
        _connectionFactory = connectionFactory;
        _passwordHasher = passwordHasher;
    }

    private sealed record Person(
        string Name,
        string Email,
        string Role,
        string Status,
        int JoinedDaysAgo,
        double LastActiveHoursAgo);

    private sealed record ProjectSeed(
        string Key,
        string Name,
        string Description,
        string Status,
        string Color,
        string OwnerEmail,
        string[] MemberEmails,
        int StartOffsetDays,
        int DueOffsetDays);

    private sealed record TaskSeed(
        string Title,
        string ProjectKey,
        string Status,
        string Priority,
        string AssigneeEmail,
        int DueOffsetDays);

    private static readonly Person[] People =
    [
        new("Nathan", "nathan@aol.com", "Administrator", "active", -244, 0.4),
        new("Daniel Okafor", "daniel.okafor@flowboard.app", "Engineering Lead", "active", -224, 1.2),
        new("Priya Raman", "priya.raman@flowboard.app", "Product Designer", "active", -177, 18),
        new("Tom Becker", "tom.becker@flowboard.app", "Frontend Engineer", "active", -134, 1.8),
        new("Sofia Alvarez", "sofia.alvarez@flowboard.app", "QA Engineer", "away", -110, 41),
        new("Kenji Watanabe", "kenji.watanabe@flowboard.app", "Backend Engineer", "active", -59, 2.3),
        new("Nora Lindqvist", "nora.lindqvist@flowboard.app", "UX Researcher", "invited", -8, 72),
        new("Liam O'Connell", "liam.oconnell@flowboard.app", "Data Analyst", "active", -13, 0.8),
    ];

    private static readonly ProjectSeed[] Projects =
    [
        new("MOB", "Mobile App Redesign",
            "Rebuild the iOS and Android clients on the new design system and ship offline support.",
            "active", "var(--chart-1)", "nathan@aol.com",
            ["nathan@aol.com", "priya.raman@flowboard.app", "tom.becker@flowboard.app", "nora.lindqvist@flowboard.app"], -38, 21),
        new("MKT", "Q3 Marketing Campaign",
            "Multi-channel launch campaign across paid social, lifecycle email and the partner blog.",
            "active", "var(--chart-2)", "priya.raman@flowboard.app",
            ["priya.raman@flowboard.app", "sofia.alvarez@flowboard.app", "liam.oconnell@flowboard.app"], -24, 12),
        new("BIL", "Billing & Invoicing",
            "Usage-based billing, proration on plan changes and a self-serve invoice archive.",
            "active", "var(--chart-3)", "daniel.okafor@flowboard.app",
            ["daniel.okafor@flowboard.app", "kenji.watanabe@flowboard.app"], -52, 34),
        new("POR", "Customer Portal v2",
            "Self-service portal for invoices, seats, saved views and audit log exports.",
            "planning", "var(--chart-4)", "nathan@aol.com",
            ["nathan@aol.com", "tom.becker@flowboard.app", "nora.lindqvist@flowboard.app", "liam.oconnell@flowboard.app"], -5, 6),
        new("DAT", "Data Warehouse Migration",
            "Move reporting workloads off the primary cluster onto the new warehouse schema.",
            "on-hold", "var(--chart-5)", "daniel.okafor@flowboard.app",
            ["daniel.okafor@flowboard.app", "kenji.watanabe@flowboard.app", "liam.oconnell@flowboard.app"], -70, 47),
        new("BRD", "Brand Refresh",
            "New identity, marketing site refresh and updated product iconography.",
            "completed", "var(--chart-2)", "priya.raman@flowboard.app",
            ["priya.raman@flowboard.app", "nora.lindqvist@flowboard.app"], -96, -9),
    ];

    private static readonly TaskSeed[] Tasks =
    [
        new("Finalise onboarding flow copy", "MOB", "completed", "medium", "nathan@aol.com", -12),
        new("Implement offline task queue", "MOB", "in-progress", "urgent", "tom.becker@flowboard.app", 2),
        new("Audit colour contrast in dark mode", "MOB", "todo", "medium", "priya.raman@flowboard.app", 5),
        new("Ship push notification settings", "MOB", "in-review", "high", "tom.becker@flowboard.app", 3),
        new("Prototype gesture navigation", "MOB", "in-progress", "high", "nora.lindqvist@flowboard.app", 9),
        new("Migrate build pipeline to EAS", "MOB", "todo", "low", "daniel.okafor@flowboard.app", 17),
        new("Regression pass on Android 15", "MOB", "todo", "medium", "sofia.alvarez@flowboard.app", 12),
        new("Book the paid social budget", "MKT", "completed", "high", "liam.oconnell@flowboard.app", -8),
        new("Write lifecycle email sequence", "MKT", "in-progress", "medium", "priya.raman@flowboard.app", 4),
        new("Build campaign attribution report", "MKT", "todo", "high", "liam.oconnell@flowboard.app", 7),
        new("Produce launch video cutdowns", "MKT", "in-review", "medium", "sofia.alvarez@flowboard.app", 1),
        new("Review partner blog placements", "MKT", "todo", "low", "priya.raman@flowboard.app", 10),
        new("Set up UTM naming convention", "MKT", "completed", "medium", "liam.oconnell@flowboard.app", -3),
        new("Model proration on plan change", "BIL", "in-progress", "urgent", "kenji.watanabe@flowboard.app", 6),
        new("Invoice PDF redesign", "BIL", "in-review", "medium", "daniel.okafor@flowboard.app", 3),
        new("Add usage metering to API gateway", "BIL", "todo", "high", "kenji.watanabe@flowboard.app", 14),
        new("Backfill historical invoices", "BIL", "todo", "medium", "kenji.watanabe@flowboard.app", 19),
        new("Dunning emails for failed payments", "BIL", "in-progress", "high", "nathan@aol.com", 8),
        new("Localise billing strings (DE, FR)", "BIL", "todo", "low", "priya.raman@flowboard.app", 24),
        new("Tax rate lookup integration", "BIL", "todo", "medium", "daniel.okafor@flowboard.app", 27),
        new("Interview five portal power users", "POR", "in-progress", "high", "nora.lindqvist@flowboard.app", 2),
        new("Draft saved-views information architecture", "POR", "in-review", "medium", "nathan@aol.com", -1),
        new("Prototype audit log export", "POR", "todo", "medium", "tom.becker@flowboard.app", 4),
        new("Seat management API contract", "POR", "todo", "high", "kenji.watanabe@flowboard.app", 5),
        new("Accessibility audit of portal forms", "POR", "todo", "low", "sofia.alvarez@flowboard.app", 6),
        new("Inventory reporting queries", "DAT", "completed", "medium", "kenji.watanabe@flowboard.app", -10),
        new("Size the new warehouse cluster", "DAT", "todo", "urgent", "daniel.okafor@flowboard.app", 22),
        new("Map legacy report dependencies", "DAT", "todo", "high", "liam.oconnell@flowboard.app", 29),
        new("Write backfill runbook", "DAT", "todo", "medium", "kenji.watanabe@flowboard.app", 36),
        new("Approve new wordmark", "BRD", "completed", "high", "nathan@aol.com", -14),
        new("Update marketing site imagery", "BRD", "completed", "medium", "priya.raman@flowboard.app", -11),
        new("Publish brand guidelines", "BRD", "completed", "low", "nora.lindqvist@flowboard.app", -9),
        new("Resolve flaky checkout test", "BIL", "in-progress", "urgent", "sofia.alvarez@flowboard.app", -4),
        new("Fix calendar timezone drift", "POR", "todo", "urgent", "tom.becker@flowboard.app", -2),
        new("Correct overdue badge contrast", "MOB", "in-progress", "high", "priya.raman@flowboard.app", -1),
        new("Verify CSV export encoding", "DAT", "in-review", "medium", "liam.oconnell@flowboard.app", 3),
    ];

    public async Task<SeedResult> SeedDemoAsync(
        bool force,
        string demoPassword,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);

        var existingUsers = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition("SELECT COUNT(*) FROM dbo.[user]", cancellationToken: cancellationToken));
        if (existingUsers > 0 && !force)
        {
            return new SeedResult(true, existingUsers, 0, 0, 0, 0);
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            if (force)
            {
                const string clear = """
                    DELETE FROM dbo.task;
                    DELETE FROM dbo.project_member;
                    DELETE FROM dbo.project;
                    DELETE FROM dbo.member;
                    DELETE FROM dbo.session;
                    DELETE FROM dbo.account;
                    DELETE FROM dbo.user_org_assignment;
                    DELETE FROM dbo.app_user_role;
                    DELETE FROM dbo.[user];
                    """;
                await connection.ExecuteAsync(new CommandDefinition(clear, transaction: transaction, cancellationToken: cancellationToken));
            }

            var today = DateTime.UtcNow.Date;
            var now = DateTimeOffset.UtcNow;

            var userIds = People.ToDictionary(p => p.Email, p => UserIdFor(p.Email));
            var memberIds = People.ToDictionary(p => p.Email, _ => Guid.NewGuid());

            foreach (var person in People)
            {
                await connection.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO dbo.[user] (id, name, email, email_verified)
                    VALUES (@Id, @Name, @Email, 1);
                    """,
                    new { Id = userIds[person.Email], Name = person.Name, Email = person.Email.ToLowerInvariant() },
                    transaction, cancellationToken: cancellationToken));

                await connection.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO dbo.member (id, user_id, role, status, joined_at, last_active_at)
                    VALUES (@Id, @UserId, @Role, @Status, @JoinedAt, @LastActiveAt);
                    """,
                    new
                    {
                        Id = memberIds[person.Email],
                        UserId = userIds[person.Email],
                        Role = RoleFor(person.Role),
                        Status = person.Status,
                        JoinedAt = new DateTimeOffset(today.AddDays(person.JoinedDaysAgo), TimeSpan.Zero),
                        LastActiveAt = now.AddHours(-person.LastActiveHoursAgo)
                    },
                    transaction, cancellationToken: cancellationToken));
            }

            var projectIds = Projects.ToDictionary(p => p.Key, _ => Guid.NewGuid());
            foreach (var project in Projects)
            {
                await connection.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO dbo.project
                        (id, name, [key], description, status, color, owner_id, start_date, due_date)
                    VALUES
                        (@Id, @Name, @Key, @Description, @Status, @Color, @OwnerId, @StartDate, @DueDate);
                    """,
                    new
                    {
                        Id = projectIds[project.Key],
                        Name = project.Name,
                        Key = project.Key,
                        Description = project.Description,
                        Status = project.Status,
                        Color = project.Color,
                        OwnerId = memberIds[project.OwnerEmail],
                        StartDate = today.AddDays(project.StartOffsetDays),
                        DueDate = today.AddDays(project.DueOffsetDays)
                    },
                    transaction, cancellationToken: cancellationToken));

                foreach (var memberEmail in project.MemberEmails)
                {
                    await connection.ExecuteAsync(new CommandDefinition("""
                        INSERT INTO dbo.project_member (project_id, member_id)
                        VALUES (@ProjectId, @MemberId);
                        """,
                        new { ProjectId = projectIds[project.Key], MemberId = memberIds[memberEmail] },
                        transaction, cancellationToken: cancellationToken));
                }
            }

            await connection.ExecuteAsync(new CommandDefinition(
                "ALTER SEQUENCE dbo.task_reference_seq RESTART WITH 1",
                transaction: transaction, cancellationToken: cancellationToken));

            for (var index = 0; index < Tasks.Length; index++)
            {
                var task = Tasks[index];
                await connection.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO dbo.task
                        (project_id, title, status, priority, assignee_id, due_date, position, completed_at)
                    VALUES
                        (@ProjectId, @Title, @Status, @Priority, @AssigneeId, @DueDate, @Position, @CompletedAt);
                    """,
                    new
                    {
                        ProjectId = projectIds[task.ProjectKey],
                        Title = task.Title,
                        Status = task.Status,
                        Priority = task.Priority,
                        AssigneeId = memberIds[task.AssigneeEmail],
                        DueDate = today.AddDays(task.DueOffsetDays),
                        Position = index,
                        CompletedAt = task.Status == "completed" ? (DateTimeOffset?)now : null
                    },
                    transaction, cancellationToken: cancellationToken));
            }

            var passwordHash = _passwordHasher.Hash(demoPassword);
            foreach (var person in People)
            {
                await connection.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO dbo.account (id, account_id, provider_id, user_id, password)
                    VALUES (@Id, @AccountId, N'credential', @UserId, @Password);
                    """,
                    new
                    {
                        Id = Guid.NewGuid(),
                        AccountId = userIds[person.Email],
                        UserId = userIds[person.Email],
                        Password = passwordHash
                    },
                    transaction, cancellationToken: cancellationToken));
            }

            const string assignRoles = """
                INSERT INTO dbo.app_user_role (user_id, role_id, assigned_by)
                SELECT m.user_id, r.id, N'seed'
                FROM dbo.member m
                JOIN dbo.app_role r ON r.name = m.role
                WHERE NOT EXISTS (
                    SELECT 1 FROM dbo.app_user_role ur
                    WHERE ur.user_id = m.user_id AND ur.role_id = r.id
                );
                """;
            await connection.ExecuteAsync(new CommandDefinition(assignRoles, transaction: transaction, cancellationToken: cancellationToken));

            await transaction.CommitAsync(cancellationToken);

            return new SeedResult(false, People.Length, People.Length, Projects.Length, Tasks.Length, People.Length);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>Node-compatible deterministic user id: usr_ + base64url(email)[0..22].</summary>
    public static string UserIdFor(string email)
    {
        var bytes = Encoding.UTF8.GetBytes(email.Trim().ToLowerInvariant());
        var base64 = Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return $"usr_{base64[..Math.Min(22, base64.Length)]}";
    }

    private static string RoleFor(string memberRole)
    {
        var value = memberRole.ToLowerInvariant();
        if (value.Contains("admin"))
        {
            return "Administrators";
        }

        if (value.Contains("supervisor") || value is "engineering lead" or "product lead")
        {
            return "Supervisors";
        }

        return "Users";
    }
}
