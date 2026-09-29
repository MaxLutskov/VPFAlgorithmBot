using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace VPFAlgorithmBot.Data;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AlgorithmDbContext>
{
    public AlgorithmDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AlgorithmDbContext>()
            .UseSqlServer("Server=localhost;Database=VPFAlgorithmBotDesign;User Id=design;Password=design;Encrypt=True;",
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", AlgorithmDbContext.Schema))
            .Options;
        return new AlgorithmDbContext(options);
    }
}
