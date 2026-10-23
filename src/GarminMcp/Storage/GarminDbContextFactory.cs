using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GarminMcp.Storage;

/// <summary>Używane wyłącznie przez narzędzia dotnet-ef (migracje); połączenie jest atrapą.</summary>
internal sealed class GarminDbContextFactory : IDesignTimeDbContextFactory<GarminDbContext>
{
    public GarminDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<GarminDbContext>()
            .UseNpgsql("Host=localhost;Database=design_time")
            .UseSnakeCaseNamingConvention()
            .Options);
}
