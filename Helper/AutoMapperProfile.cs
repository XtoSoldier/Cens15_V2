using AutoMapper;
using CENS15_V2.Models;
using CENS15_V2.Models.DTOs.AlumnoDocumentosDTOs;
using CENS15_V2.Models.DTOs.AnexosDTOs;
using CENS15_V2.Models.DTOs.CursosDTOs;
using CENS15_V2.Models.DTOs.AlumnosDTOs;
using CENS15_V2.Models.DTOs.TiposDocumentoAlumnoDTOs;
using CENS15_V2.Models.DTOs.OrientacionesDTOs;
using CENS15_V2.Models.DTOs.MateriasDTOs;
using CENS15_V2.Models.DTOs.InscripcionesDTOs;
using CENS15_V2.Models.DTOs.DocentesDTOs;
using CENS15_V2.Models.DTOs.CursadasMateriasDTOs;
using CENS15_V2.Models.DTOs.CalificacionesDTOs;

namespace CENS15_V2.Helper
{
    public class AutoMapperProfile : Profile
    {
        public AutoMapperProfile()
        {
            CreateMap<Alumno, AlumnoDto>();
            CreateMap<AlumnoNacimiento, AlumnoNacimientoDto>();
            CreateMap<AlumnoContacto, AlumnoContactoDto>();
            CreateMap<AlumnoDocumento, AlumnoDocumentoDto>()
                .ForMember(dest => dest.TipoDocumentoAlumnoNombre,
                    opt => opt.MapFrom(src => src.TipoDocumentoAlumno.Nombre));

            CreateMap<AlumnoDocumento, AlumnoDocumentoItemDto>()
                .ForMember(dest => dest.TipoDocumentoAlumnoNombre,
                    opt => opt.MapFrom(src => src.TipoDocumentoAlumno.Nombre));

            CreateMap<CreateAlumnoDocumentoItemRequest, AlumnoDocumento>();
            CreateMap<UpdateAlumnoDocumentoItemRequest, AlumnoDocumento>();

            CreateMap<CreateAlumnoRequest, Alumno>();
            CreateMap<CreateAlumnoNacimientoRequest, AlumnoNacimiento>();
            CreateMap<CreateAlumnoContactoRequest, AlumnoContacto>();
            CreateMap<CreateAlumnoDocumentoRequest, AlumnoDocumento>();

            CreateMap<UpdateAlumnoRequest, Alumno>();
            CreateMap<UpdateAlumnoNacimientoRequest, AlumnoNacimiento>();
            CreateMap<UpdateAlumnoContactoRequest, AlumnoContacto>();
            CreateMap<UpdateAlumnoDocumentoRequest, AlumnoDocumento>();

            CreateMap<TipoDocumentoAlumno, TipoDocumentoAlumnoDto>();
            CreateMap<Orientacion, OrientacionDto>();
            CreateMap<Anexo, AnexoDto>();
            CreateMap<Docente, DocenteDto>();

            CreateMap<MateriaDocente, MateriaDocenteDto>()
                .ForMember(dest => dest.DocenteId, opt => opt.MapFrom(src => src.DocenteId))
                .ForMember(dest => dest.Docente, opt => opt.MapFrom(src => $"{src.Docente.Apellidos}, {src.Docente.Nombres}"));

            CreateMap<Materia, MateriaDto>()
                .ForMember(dest => dest.CursoId, opt => opt.MapFrom(src => src.CursoId))
                .ForMember(dest => dest.Curso, opt => opt.MapFrom(src => src.Curso.CursoNombre))
                .ForMember(dest => dest.Docentes, opt => opt.MapFrom(src => src.Docentes));

            CreateMap<Inscripcion, InscripcionDto>()
                .ForMember(dest => dest.Alumno, opt => opt.MapFrom(src => $"{src.Alumno.Apellidos}, {src.Alumno.Nombres}"))
                .ForMember(dest => dest.Curso, opt => opt.MapFrom(src => $"{src.CursoNombre} {src.Division}".Trim()))
                .ForMember(dest => dest.CursoNombre, opt => opt.MapFrom(src => src.CursoNombre))
                .ForMember(dest => dest.Division, opt => opt.MapFrom(src => src.Division));

            CreateMap<CursadaMateria, CursadaMateriaDto>();
            CreateMap<Calificacion, CalificacionDto>();

            CreateMap<Curso, CursoDto>()
                .ForMember(dest => dest.Curso, opt => opt.MapFrom(src => src.CursoNombre))
                .ForMember(dest => dest.IdOrientacion, opt => opt.MapFrom(src => src.OrientacionId))
                .ForMember(dest => dest.IdAnexo, opt => opt.MapFrom(src => src.AnexoId))
                .ForMember(dest => dest.OrientacionNombreCorto, opt => opt.MapFrom(src => src.Orientacion.NombreCorto))
                .ForMember(dest => dest.AnexoNombre, opt => opt.MapFrom(src => src.Anexo.Nombre));
        }
    }
}
