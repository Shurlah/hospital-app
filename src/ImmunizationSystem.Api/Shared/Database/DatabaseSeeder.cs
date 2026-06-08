using ImmunizationSystem.Api.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace ImmunizationSystem.Api.Shared.Database;

public static class DatabaseSeeder
{
    public static async Task SeedSuperAdminAsync(IServiceProvider serviceProvider)
    {
        var configuration = serviceProvider.GetRequiredService<IConfiguration>();
        var dbContext = serviceProvider.GetRequiredService<ApplicationDbContext>();
        var passwordHasher = serviceProvider.GetRequiredService<IPasswordHasher>();

        var email = configuration["SeedSuperAdmin:Email"] ?? "admin@example.com";
        var password = configuration["SeedSuperAdmin:Password"] ?? "Password123!";
        var fullName = configuration["SeedSuperAdmin:FullName"] ?? "Super Administrator";

        var role = await dbContext.Roles.SingleAsync(x => x.Name == RoleNames.SystemAdministrator);
        var existing = await dbContext.Users.SingleOrDefaultAsync(x => x.Email == email);

        if (existing is null)
        {
            dbContext.Users.Add(new User
            {
                Id = SeedIds.SuperAdministratorUser,
                FullName = fullName,
                Email = email,
                PhoneNumber = configuration["SeedSuperAdmin:PhoneNumber"],
                PasswordHash = passwordHasher.Hash(password),
                RoleId = role.Id,
                IsActive = true,
                CreatedAt = SeedIds.SeedTime
            });

            await dbContext.SaveChangesAsync();
            return;
        }

        var changed = false;
        if (!existing.IsActive)
        {
            existing.IsActive = true;
            changed = true;
        }

        if (existing.RoleId != role.Id)
        {
            existing.RoleId = role.Id;
            changed = true;
        }

        if (changed)
        {
            await dbContext.SaveChangesAsync();
        }
    }
}
