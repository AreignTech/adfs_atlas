using System.Text;
using Atlas.Application.Interfaces;
using Dapper;

namespace Atlas.Infrastructure.Data;

public sealed record SeedResult(bool Skipped, int Users, int Members, int Projects, int Tasks, int Accounts);

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
        new("Daniel Jones", "daniel.jones@adfs.alabama.gov", "Forensic Scientist", "active", -224, 1.2),
        new("Sean Eiland", "sean.eiland@adfs.alabama.gov", "Network Engineer", "active", -177, 18),
        new("Bob Buel", "bob.buel@adfs.alabama.gov", "IT Director", "active", -134, 1.8),
        new("Paul Clanton", "paul.clanton@adfs.alabama.gov", "IT Supervisor", "away", -110, 41),
        new("Nathan Nguyen", "nathan.nguyen@adfs.alabama.gov", "Application Developer", "active", -59, 2.3),
        new("Mary Burns", "mary.burns@adfs.alabama.gov", "DC Section Chief", "invited", -8, 72),
        new("Tim McSpadden", "tim.mcspadden@adfs.alabama.gov", "Mobile Lab Director", "active", -13, 0.8),
    ];

    private static readonly ProjectSeed[] Projects =
    [
        new("MOB", "Mobile Lab PC installation",
            "Installing PC hardware and software for the mobile lab.",
            "active", "var(--chart-1)", "nathan.nguyen@adfs.alabama.gov",
            ["nathan.nguyen@adfs.alabama.gov", "tim.mcspadden@adfs.alabama.gov", "paul.clanton@adfs.alabama.gov", "sean.eiland@adfs.alabama.gov"], -38, 21),
    ];

    private static readonly TaskSeed[] Tasks =
    [
        new("Finalize pc install", "MOB", "completed", "medium", "nathan@aol.com", -12),
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
