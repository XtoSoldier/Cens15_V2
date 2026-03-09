using CENS15_V2.Entities;
using CENS15_V2.Helper;
using CENS15_V2.Models;
using Microsoft.EntityFrameworkCore;

namespace CENS15_V2.Data
{
    public class DbSeeder
    {
        public static async Task Seed(AppDbContext context, PasswordHasher hasher)
        {
            var rolesToCreate = new[]
             {
                "Admin",
                "User"
            };

            foreach (var roleName in rolesToCreate)
            {
                if (!await context.Roles.AnyAsync(r => r.Name == roleName))
                {
                    context.Roles.Add(new Role
                    {
                        Id = Guid.NewGuid(),
                        Name = roleName
                    });
                }
            }
            await context.SaveChangesAsync();

            if (!context.Users.Any())
            {
                var adminRole = await context.Roles.FirstAsync(r => r.Name == "Admin");

                var user = new User
                {
                    Id = Guid.NewGuid(),
                    FirstName = "Super",
                    LastName = "Admin",
                    Image = "default.png",
                    RoleId = adminRole.Id,
                    Status = true
                };

                var auth = new Auth
                {
                    Id = user.Id,
                    Email = "admin@admin.com",
                    PasswordHash = hasher.Hash("Admin123*"),
                    User = user
                };

                context.AddRange(user, auth);
                await context.SaveChangesAsync();
            }
        }
    }
}
