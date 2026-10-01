using Microsoft.EntityFrameworkCore;

namespace VPFAlgorithmBot.Data;

public sealed class AlgorithmDbContext(DbContextOptions<AlgorithmDbContext> options) : DbContext(options)
{
    public const string Schema = "vpfalgo";
    public int? AuditActorUserId { get; set; }
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<UserScope> UserScopes => Set<UserScope>();
    public DbSet<PendingCustomAnswer> PendingCustomAnswers => Set<PendingCustomAnswer>();
    public DbSet<SourceChat> Chats => Set<SourceChat>();
    public DbSet<MonitoredObject> Objects => Set<MonitoredObject>();
    public DbSet<ProblemCategory> Categories => Set<ProblemCategory>();
    public DbSet<AlgorithmRule> AlgorithmRules => Set<AlgorithmRule>();
    public DbSet<RouteRule> RouteRules => Set<RouteRule>();
    public DbSet<ResponseTemplate> ResponseTemplates => Set<ResponseTemplate>();
    public DbSet<TemplateVersion> TemplateVersions => Set<TemplateVersion>();
    public DbSet<IncomingMessage> IncomingMessages => Set<IncomingMessage>();
    public DbSet<Incident> Incidents => Set<Incident>();
    public DbSet<IncidentResponse> Responses => Set<IncidentResponse>();
    public DbSet<NotificationOutbox> NotificationOutbox => Set<NotificationOutbox>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<Setting> Settings => Set<Setting>();

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (AuditActorUserId is not null)
        {
            ChangeTracker.DetectChanges();
            var changes = ChangeTracker.Entries()
                .Where(entry => entry.Entity is not AuditEvent &&
                    entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                .Select(entry => new AuditEvent
                {
                    ActorUserId = AuditActorUserId,
                    Action = entry.State.ToString(),
                    Entity = entry.Metadata.ClrType.Name,
                    Detail = string.Join(", ", entry.Properties
                        .Where(property => entry.State != EntityState.Modified || property.IsModified)
                        .Select(property => $"{property.Metadata.Name}={property.CurrentValue}")),
                    CreatedAtUtc = DateTimeOffset.UtcNow
                }).ToList();
            AuditEvents.AddRange(changes);
        }
        return base.SaveChangesAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema(Schema);
        model.Entity<AppUser>().HasIndex(x => x.TelegramId).IsUnique();
        model.Entity<UserScope>().HasKey(x => new { x.UserId, x.ObjectId });
        model.Entity<PendingCustomAnswer>().HasKey(x => x.UserId);
        model.Entity<PendingCustomAnswer>().Property(x => x.UserId).ValueGeneratedNever();
        model.Entity<SourceChat>().HasIndex(x => x.TelegramChatId).IsUnique();
        model.Entity<MonitoredObject>().HasIndex(x => x.Code).IsUnique();
        model.Entity<ProblemCategory>().HasIndex(x => x.Name).IsUnique();
        model.Entity<IncomingMessage>().HasIndex(x => new { x.ChatId, x.TelegramMessageId }).IsUnique();
        model.Entity<Incident>().HasIndex(x => x.StartMessageId).IsUnique();
        model.Entity<Incident>().HasIndex(x => new { x.ChatId, x.AlgorithmRuleId, x.EndedAtUtc });
        model.Entity<IncidentResponse>().HasIndex(x => x.ActionKey).IsUnique().HasFilter("[ActionKey] IS NOT NULL");
        model.Entity<NotificationOutbox>().HasIndex(x => new { x.IncidentId, x.UserId, x.Kind }).IsUnique();
        model.Entity<Setting>().HasKey(x => x.Key);
        model.Entity<Incident>().Ignore(x => x.ProblemState).Ignore(x => x.AnswerState);
        model.Entity<Incident>().HasMany(x => x.Responses).WithOne().HasForeignKey(x => x.IncidentId);
        model.Entity<AppUser>().HasMany(x => x.Scopes).WithOne().HasForeignKey(x => x.UserId);
        model.Entity<AppUser>().Property(x => x.DisplayName).HasMaxLength(200);
        model.Entity<SourceChat>().Property(x => x.Name).HasMaxLength(200);
        model.Entity<MonitoredObject>().Property(x => x.Code).HasMaxLength(50);
        model.Entity<MonitoredObject>().Property(x => x.Name).HasMaxLength(200);
        model.Entity<ProblemCategory>().Property(x => x.Name).HasMaxLength(200);
        model.Entity<AlgorithmRule>().Property(x => x.Name).HasMaxLength(300);
        model.Entity<AlgorithmRule>().Property(x => x.MatchPattern).HasMaxLength(500);
        model.Entity<IncidentResponse>().Property(x => x.Text).HasMaxLength(2000);
        model.Entity<IncomingMessage>().Property(x => x.Text).HasMaxLength(4000);
    }
}
