using AutoMapper;
using CENS15_V2.Models;
using CENS15_V2.Models.DTOs.AlumnoDocumentosDTOs;
using CENS15_V2.Models.DTOs.AlumnosDTOs;
using CENS15_V2.Models.DTOs.TiposDocumentoAlumnoDTOs;

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
        }
    }
}
