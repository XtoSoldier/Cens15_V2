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
        public DbSet<Docente> Docentes { get; set; }
        public DbSet<Inscripcion> Inscripciones { get; set; }
        public DbSet<MateriaDocente> MateriaDocentes { get; set; }
        public DbSet<CursadaMateria> CursadasMaterias { get; set; }
        public DbSet<Calificacion> Calificaciones { get; set; }

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


            modelBuilder.Entity<MateriaDocente>()
                .HasKey(md => new { md.MateriaId, md.DocenteId });

            modelBuilder.Entity<MateriaDocente>()
                .HasOne(md => md.Materia)
                .WithMany(m => m.Docentes)
                .HasForeignKey(md => md.MateriaId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<MateriaDocente>()
                .HasOne(md => md.Docente)
                .WithMany(d => d.Materias)
                .HasForeignKey(md => md.DocenteId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<MateriaDocente>()
                .HasIndex(md => new { md.MateriaId, md.Rol })
                .IsUnique();

            modelBuilder.Entity<Docente>()
                .HasOne(d => d.User)
                .WithOne(u => u.Docente)
                .HasForeignKey<Docente>(d => d.UserId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<Docente>()
                .HasIndex(d => d.Email)
                .IsUnique();

            modelBuilder.Entity<Docente>()
                .HasIndex(d => d.UserId)
                .IsUnique();

            modelBuilder.Entity<Inscripcion>()
                .HasOne(i => i.Alumno)
                .WithMany(a => a.Inscripciones)
                .HasForeignKey(i => i.AlumnoId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Inscripcion>()
                .HasOne(i => i.Curso)
                .WithMany(c => c.Inscripciones)
                .HasForeignKey(i => i.CursoId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Inscripcion>()
                .HasIndex(i => new { i.AlumnoId, i.CursoId, i.Anio })
                .IsUnique();
            modelBuilder.Entity<Inscripcion>()
                .Property(i => i.Estado)
                .HasConversion<string>();

            modelBuilder.Entity<Inscripcion>()
                .Property(i => i.FechaInscripcion)
                .HasDefaultValueSql("NOW()");

            modelBuilder.Entity<CursadaMateria>()
                .HasOne(cm => cm.Inscripcion)
                .WithMany(i => i.CursadasMaterias)
                .HasForeignKey(cm => cm.InscripcionId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<CursadaMateria>()
                .HasOne(cm => cm.Materia)
                .WithMany(m => m.CursadasMaterias)
                .HasForeignKey(cm => cm.MateriaId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<CursadaMateria>()
                .HasIndex(cm => new { cm.InscripcionId, cm.MateriaId })
                .IsUnique();

            modelBuilder.Entity<Calificacion>()
                .HasOne(c => c.CursadaMateria)
                .WithOne(cm => cm.Calificacion)
                .HasForeignKey<Calificacion>(c => c.CursadaMateriaId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Calificacion>()
                .HasIndex(c => c.CursadaMateriaId)
                .IsUnique();

            modelBuilder.Entity<Calificacion>()
                .Property(c => c.Estado)
                .HasConversion<string>();

            modelBuilder.Entity<Inscripcion>()
                .Property(i => i.CursoNombre)
                .HasDefaultValue(string.Empty);

            modelBuilder.Entity<Inscripcion>()
                .Property(i => i.Division)
                .HasDefaultValue(string.Empty);

            modelBuilder.Entity<CursadaMateria>()
                .Property(cm => cm.MateriaNombre)
                .HasDefaultValue(string.Empty);

            modelBuilder.Entity<Calificacion>()
                .Property(c => c.MateriaNombre)
                .HasDefaultValue(string.Empty);

        }
         
    }
}
