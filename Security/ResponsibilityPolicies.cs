using System.Security.Claims;

namespace CENS15_V2.Security
{
    public static class ResponsibilityPolicies
    {
        public const string ClaimType = "responsibility";

        public const string Usuarios = "Usuarios";
        public const string Roles = "Roles";
        public const string Responsabilidades = "Responsabilidades";
        public const string Alumnos = "Alumnos";
        public const string AlumnoDocumentos = "Documentos de Alumnos";
        public const string TiposDocumentoAlumno = "Tipos de Documento";
        public const string Inscripciones = "Inscripciones";
        public const string CursadasMaterias = "Cursadas Materias";
        public const string Calificaciones = "Calificaciones";
        public const string Docentes = "Docentes";
        public const string Materias = "Materias";
        public const string Cursos = "Cursos";
        public const string Anexos = "Anexos";
        public const string Orientaciones = "Orientaciones";
        public const string Certificados = "Certificados";
        public const string ActividadLogin = "Actividad Login";

        public const string UsuariosConsultar = "Usuarios.Consultar";
        public const string UsuariosCrear = "Usuarios.Crear";
        public const string UsuariosEditar = "Usuarios.Editar";
        public const string UsuariosEliminar = "Usuarios.Eliminar";

        public const string RolesConsultar = "Roles.Consultar";
        public const string RolesCrear = "Roles.Crear";
        public const string RolesEditar = "Roles.Editar";
        public const string RolesEliminar = "Roles.Eliminar";

        public const string ResponsabilidadesConsultar = "Responsabilidades.Consultar";
        public const string ResponsabilidadesCrear = "Responsabilidades.Crear";
        public const string ResponsabilidadesEditar = "Responsabilidades.Editar";
        public const string ResponsabilidadesEliminar = "Responsabilidades.Eliminar";

        public const string AlumnosConsultar = "Alumnos.Consultar";
        public const string AlumnosCrear = "Alumnos.Crear";
        public const string AlumnosEditar = "Alumnos.Editar";
        public const string AlumnosEliminar = "Alumnos.Eliminar";

        public const string AlumnoDocumentosConsultar = "Documentos de Alumnos.Consultar";
        public const string AlumnoDocumentosCrear = "Documentos de Alumnos.Crear";
        public const string AlumnoDocumentosEditar = "Documentos de Alumnos.Editar";
        public const string AlumnoDocumentosEliminar = "Documentos de Alumnos.Eliminar";

        public const string TiposDocumentoAlumnoConsultar = "Tipos de Documento.Consultar";
        public const string TiposDocumentoAlumnoCrear = "Tipos de Documento.Crear";
        public const string TiposDocumentoAlumnoEditar = "Tipos de Documento.Editar";
        public const string TiposDocumentoAlumnoEliminar = "Tipos de Documento.Eliminar";

        public const string InscripcionesConsultar = "Inscripciones.Consultar";
        public const string InscripcionesCrear = "Inscripciones.Crear";
        public const string InscripcionesEditar = "Inscripciones.Editar";
        public const string InscripcionesEliminar = "Inscripciones.Eliminar";

        public const string CursadasMateriasConsultar = "Cursadas Materias.Consultar";
        public const string CursadasMateriasCrear = "Cursadas Materias.Crear";
        public const string CursadasMateriasEditar = "Cursadas Materias.Editar";

        public const string CalificacionesConsultar = "Calificaciones.Consultar";
        public const string CalificacionesCrear = "Calificaciones.Crear";
        public const string CalificacionesEditar = "Calificaciones.Editar";

        public const string DocentesConsultar = "Docentes.Consultar";
        public const string DocentesCrear = "Docentes.Crear";
        public const string DocentesEditar = "Docentes.Editar";
        public const string DocentesEliminar = "Docentes.Eliminar";

        public const string MateriasConsultar = "Materias.Consultar";
        public const string MateriasCrear = "Materias.Crear";
        public const string MateriasEditar = "Materias.Editar";
        public const string MateriasEliminar = "Materias.Eliminar";

        public const string CursosConsultar = "Cursos.Consultar";
        public const string CursosCrear = "Cursos.Crear";
        public const string CursosEditar = "Cursos.Editar";
        public const string CursosEliminar = "Cursos.Eliminar";

        public const string AnexosConsultar = "Anexos.Consultar";
        public const string AnexosCrear = "Anexos.Crear";
        public const string AnexosEditar = "Anexos.Editar";
        public const string AnexosEliminar = "Anexos.Eliminar";

        public const string OrientacionesConsultar = "Orientaciones.Consultar";
        public const string OrientacionesCrear = "Orientaciones.Crear";
        public const string OrientacionesEditar = "Orientaciones.Editar";
        public const string OrientacionesEliminar = "Orientaciones.Eliminar";

        public const string CertificadosConsultar = "Certificados.Consultar";
        public const string CertificadosCrear = "Certificados.Crear";
        public const string CertificadosEditar = "Certificados.Editar";
        public const string CertificadosEliminar = "Certificados.Eliminar";

        public const string ActividadLoginConsultar = "Actividad Login.Consultar";

        public static readonly string[] Modules =
        {
            Usuarios,
            Roles,
            Responsabilidades,
            Alumnos,
            AlumnoDocumentos,
            TiposDocumentoAlumno,
            Inscripciones,
            CursadasMaterias,
            Calificaciones,
            Docentes,
            Materias,
            Cursos,
            Anexos,
            Orientaciones,
            Certificados,
            ActividadLogin
        };

        public static readonly IReadOnlyDictionary<string, string> GranularToModule = new Dictionary<string, string>
        {
            [UsuariosConsultar] = Usuarios,
            [UsuariosCrear] = Usuarios,
            [UsuariosEditar] = Usuarios,
            [UsuariosEliminar] = Usuarios,
            [RolesConsultar] = Roles,
            [RolesCrear] = Roles,
            [RolesEditar] = Roles,
            [RolesEliminar] = Roles,
            [ResponsabilidadesConsultar] = Responsabilidades,
            [ResponsabilidadesCrear] = Responsabilidades,
            [ResponsabilidadesEditar] = Responsabilidades,
            [ResponsabilidadesEliminar] = Responsabilidades,
            [AlumnosConsultar] = Alumnos,
            [AlumnosCrear] = Alumnos,
            [AlumnosEditar] = Alumnos,
            [AlumnosEliminar] = Alumnos,
            [AlumnoDocumentosConsultar] = AlumnoDocumentos,
            [AlumnoDocumentosCrear] = AlumnoDocumentos,
            [AlumnoDocumentosEditar] = AlumnoDocumentos,
            [AlumnoDocumentosEliminar] = AlumnoDocumentos,
            [TiposDocumentoAlumnoConsultar] = TiposDocumentoAlumno,
            [TiposDocumentoAlumnoCrear] = TiposDocumentoAlumno,
            [TiposDocumentoAlumnoEditar] = TiposDocumentoAlumno,
            [TiposDocumentoAlumnoEliminar] = TiposDocumentoAlumno,
            [InscripcionesConsultar] = Inscripciones,
            [InscripcionesCrear] = Inscripciones,
            [InscripcionesEditar] = Inscripciones,
            [InscripcionesEliminar] = Inscripciones,
            [CursadasMateriasConsultar] = CursadasMaterias,
            [CursadasMateriasCrear] = CursadasMaterias,
            [CursadasMateriasEditar] = CursadasMaterias,
            [CalificacionesConsultar] = Calificaciones,
            [CalificacionesCrear] = Calificaciones,
            [CalificacionesEditar] = Calificaciones,
            [DocentesConsultar] = Docentes,
            [DocentesCrear] = Docentes,
            [DocentesEditar] = Docentes,
            [DocentesEliminar] = Docentes,
            [MateriasConsultar] = Materias,
            [MateriasCrear] = Materias,
            [MateriasEditar] = Materias,
            [MateriasEliminar] = Materias,
            [CursosConsultar] = Cursos,
            [CursosCrear] = Cursos,
            [CursosEditar] = Cursos,
            [CursosEliminar] = Cursos,
            [AnexosConsultar] = Anexos,
            [AnexosCrear] = Anexos,
            [AnexosEditar] = Anexos,
            [AnexosEliminar] = Anexos,
            [OrientacionesConsultar] = Orientaciones,
            [OrientacionesCrear] = Orientaciones,
            [OrientacionesEditar] = Orientaciones,
            [OrientacionesEliminar] = Orientaciones,
            [CertificadosConsultar] = Certificados,
            [CertificadosCrear] = Certificados,
            [CertificadosEditar] = Certificados,
            [CertificadosEliminar] = Certificados,
            [ActividadLoginConsultar] = ActividadLogin
        };

        public static readonly string[] All = Modules.Concat(GranularToModule.Keys).ToArray();

        public static bool HasResponsibility(ClaimsPrincipal user, string responsibility)
        {
            if (user.HasClaim(ClaimType, responsibility))
            {
                return true;
            }

            return GranularToModule.TryGetValue(responsibility, out var module)
                && user.HasClaim(ClaimType, module);
        }
    }
}
