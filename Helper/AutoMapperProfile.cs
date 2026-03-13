using AutoMapper;
using CENS15_V2.Models;
using CENS15_V2.Models.DTOs.AlumnoDocumentosDTOs;
using CENS15_V2.Models.DTOs.AnexosDTOs;
using CENS15_V2.Models.DTOs.CursosDTOs;
using CENS15_V2.Models.DTOs.AlumnosDTOs;
using CENS15_V2.Models.DTOs.TiposDocumentoAlumnoDTOs;
using CENS15_V2.Models.DTOs.OrientacionesDTOs;
using CENS15_V2.Models.DTOs.MateriasDTOs;

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


            CreateMap<Materia, MateriaDto>()
                .ForMember(dest => dest.CursoId, opt => opt.MapFrom(src => src.CursoId))
                .ForMember(dest => dest.Curso, opt => opt.MapFrom(src => src.Curso.CursoNombre));

            CreateMap<Curso, CursoDto>()
                .ForMember(dest => dest.Curso, opt => opt.MapFrom(src => src.CursoNombre))
                .ForMember(dest => dest.IdOrientacion, opt => opt.MapFrom(src => src.OrientacionId))
                .ForMember(dest => dest.IdAnexo, opt => opt.MapFrom(src => src.AnexoId))
                .ForMember(dest => dest.OrientacionNombreCorto, opt => opt.MapFrom(src => src.Orientacion.NombreCorto))
                .ForMember(dest => dest.AnexoNombre, opt => opt.MapFrom(src => src.Anexo.Nombre));
        }
    }
}
