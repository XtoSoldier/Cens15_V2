using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CENS15_V2.Data
{
    public class AppDbContextFactory
        : IDesignTimeDbContextFactory<AppDbContext>
    {
        public const string ConnectionString =
            "Host=localhost;Database=cens15;Username=postgres;Password=localpass;";

        public AppDbContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseNpgsql(ConnectionString);

            return new AppDbContext(optionsBuilder.Options);
        }
    }
}
