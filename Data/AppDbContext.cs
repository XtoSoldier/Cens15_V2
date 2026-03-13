using CENS15_V2.Entities;
using CENS15_V2.Models;
using Microsoft.EntityFrameworkCore;

namespace CENS15_V2.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<User> Users { get; set; }
        public DbSet<Auth> Auths { get; set; }
        public DbSet<Token> Tokens { get; set; }
        public DbSet<Role> Roles { get; set; }
        public DbSet<Responsibility> Responsibilities { get; set; }

        public DbSet<Alumno> Alumnos { get; set; }
        public DbSet<AlumnoNacimiento> AlumnoNacimientos { get; set; }
        public DbSet<AlumnoContacto> AlumnoContactos { get; set; }
        public DbSet<AlumnoDocumento> AlumnoDocumentos { get; set; }
        public DbSet<TipoDocumentoAlumno> TiposDocumentoAlumno { get; set; }
        public DbSet<Orientacion> Orientaciones { get; set; }
        public DbSet<Curso> Cursos { get; set; }
        public DbSet<Anexo> Anexos { get; set; }
        public DbSet<Materia> Materias { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            ConfigureUserAuthToken(modelBuilder);
            ConfigureRoleResponsibility(modelBuilder);
            ConfigureAlumno(modelBuilder);
        }

        private static void ConfigureUserAuthToken(ModelBuilder modelBuilder)
        {
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
        }

        private static void ConfigureRoleResponsibility(ModelBuilder modelBuilder)
        {
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

        private static void ConfigureAlumno(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Alumno>()
                .HasOne(a => a.DatosNacimiento)
                .WithOne(n => n.Alumno)
                .HasForeignKey<AlumnoNacimiento>(n => n.AlumnoId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Alumno>()
                .HasOne(a => a.Contacto)
                .WithOne(c => c.Alumno)
                .HasForeignKey<AlumnoContacto>(c => c.AlumnoId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Alumno>()
                .HasMany(a => a.Documentos)
                .WithOne(d => d.Alumno)
                .HasForeignKey(d => d.AlumnoId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<AlumnoDocumento>()
                .HasOne(d => d.TipoDocumentoAlumno)
                .WithMany(t => t.Documentos)
                .HasForeignKey(d => d.TipoDocumentoAlumnoId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Alumno>()
                .HasIndex(a => a.NumeroDocumento)
                .IsUnique();

            modelBuilder.Entity<TipoDocumentoAlumno>()
                .HasIndex(t => t.Nombre)
                .IsUnique();

            modelBuilder.Entity<Orientacion>()
                .HasIndex(o => o.Nombre)
                .IsUnique();

            modelBuilder.Entity<Orientacion>()
                .HasIndex(o => o.NombreCorto)
                .IsUnique();

            modelBuilder.Entity<Materia>()
                .HasIndex(m => new { m.CursoId, m.Nombre })
                .IsUnique();
        }
         
    }
}
