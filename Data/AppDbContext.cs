using System.Collections.Generic;
using CENS15_V2.Models;
using CENS15_V2.Entities;
using Microsoft.EntityFrameworkCore;

namespace CENS15_V2.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<User> Users { get; set; }
        public DbSet<Auth> Auths { get; set; }
        public DbSet<Token> Tokens { get; set; }
        public DbSet<Role> Roles { get; set; }
        public DbSet<Responsibility> Responsibilities { get; set; }


        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            ConfigureUserAuthToken(modelBuilder);
            ConfigureClientProduct(modelBuilder);
        }

        public static void ConfigureUserAuthToken(ModelBuilder modelBuilder)
        {
            //            ✔ Esto crea:
            //Auth.Id → PK + FK → User.Id
            //Token.Id → PK + FK → Auth.Id

            modelBuilder.Entity<User>()
                .HasOne(u => u.Auth)
                .WithOne(a => a.User)
                .HasForeignKey<Auth>(a => a.Id)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<Auth>()
                .HasOne(a => a.Token)
                .WithOne(t => t.Auth)
                .HasForeignKey<Token>(t => t.Id)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<RolePermission>()
                .HasKey(rp => new { rp.RoleId, rp.PermissionId });
            modelBuilder.Entity<RoleResponsibility>()
                .HasKey(rr => new { rr.RoleId, rr.ResponsibilityId });
            modelBuilder.Entity<RoleResponsibility>()
                .HasOne(rr => rr.Role)
                .WithMany(r => r.Responsibilities)
                .HasForeignKey(rr => rr.RoleId);
            modelBuilder.Entity<RoleResponsibility>()
                .HasOne(rr => rr.Responsibility)
                .WithMany(r => r.Roles)
                .HasForeignKey(rr => rr.ResponsibilityId);
        }

        private static void ConfigureClientProduct(ModelBuilder modelBuilder)
        {
            ///Product ↔ Module (1–N)
                        ///UUID automático en PostgreSQL
            modelBuilder.HasPostgresExtension("pgcrypto");
            modelBuilder.Entity<User>()
                        .Property(u => u.Id)
                        .HasDefaultValueSql("gen_random_uuid()");
            modelBuilder.Entity<Auth>()
                        .Property(a => a.Id)
                        .HasDefaultValueSql("gen_random_uuid()");
            modelBuilder.Entity<Token>()
                        .Property(t => t.Id)
                        .HasDefaultValueSql("gen_random_uuid()");
            modelBuilder.Entity<Role>()
                        .Property(r => r.Id)
                        .HasDefaultValueSql("gen_random_uuid()");
            modelBuilder.Entity<Responsibility>()
                        .Property(r => r.Id)
                        .HasDefaultValueSql("gen_random_uuid()");
          




        }


    }
}
