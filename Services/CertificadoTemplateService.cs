using AutoMapper;
using CENS15_V2.Data;
using CENS15_V2.Models;
using CENS15_V2.Models.DTOs.CertificadoTemplatesDTOs;
using CENS15_V2.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Net;
using System.Text;

namespace CENS15_V2.Services
{
    public class CertificadoTemplateService : ICertificadoTemplateService
    {
        private readonly AppDbContext _context;
        private readonly IMapper _mapper;

        public CertificadoTemplateService(AppDbContext context, IMapper mapper)
        {
            _context = context;
            _mapper = mapper;
        }

        public async Task<IEnumerable<CertificadoTemplateDto>> GetAllAsync()
        {
            var items = await _context.CertificadoTemplates
                .AsNoTracking()
                .OrderBy(t => t.Nombre)
                .ToListAsync();

            return _mapper.Map<IEnumerable<CertificadoTemplateDto>>(items);
        }

        public async Task<CertificadoTemplateDto?> GetByIdAsync(int id)
        {
            var item = await _context.CertificadoTemplates
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == id);

            return item == null ? null : _mapper.Map<CertificadoTemplateDto>(item);
        }

        public async Task<CertificadoTemplateDto> CreateAsync(CreateCertificadoTemplateRequest request)
        {
            var nombre = request.Nombre.Trim();
            await ValidateNombreDisponible(nombre);

            var entity = new CertificadoTemplate
            {
                Nombre = nombre,
                Descripcion = request.Descripcion?.Trim(),
                ContenidoHtml = request.ContenidoHtml,
                Formato = string.IsNullOrWhiteSpace(request.Formato) ? "A4" : request.Formato,
                MargenSuperior = request.MargenSuperior,
                MargenInferior = request.MargenInferior,
                MargenIzquierdo = request.MargenIzquierdo,
                MargenDerecho = request.MargenDerecho,
                ImagenesJson = request.ImagenesJson,
                CreatedAt = DateTime.UtcNow,
            };

            _context.CertificadoTemplates.Add(entity);
            await _context.SaveChangesAsync();

            return _mapper.Map<CertificadoTemplateDto>(entity);
        }

        public async Task<bool> UpdateAsync(int id, UpdateCertificadoTemplateRequest request)
        {
            var entity = await _context.CertificadoTemplates.FirstOrDefaultAsync(t => t.Id == id);
            if (entity == null)
            {
                return false;
            }

            var nombre = request.Nombre.Trim();
            await ValidateNombreDisponible(nombre, id);

            entity.Nombre = nombre;
            entity.Descripcion = request.Descripcion?.Trim();
            entity.ContenidoHtml = request.ContenidoHtml;
            entity.Formato = string.IsNullOrWhiteSpace(request.Formato) ? "A4" : request.Formato;
            entity.MargenSuperior = request.MargenSuperior;
            entity.MargenInferior = request.MargenInferior;
            entity.MargenIzquierdo = request.MargenIzquierdo;
            entity.MargenDerecho = request.MargenDerecho;
            entity.ImagenesJson = request.ImagenesJson;
            entity.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> DeleteAsync(int id)
        {
            var entity = await _context.CertificadoTemplates.FirstOrDefaultAsync(t => t.Id == id);
            if (entity == null)
            {
                return false;
            }

            _context.CertificadoTemplates.Remove(entity);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<RenderedCertificadoTemplateDto?> RenderForAlumnoAsync(int templateId, int alumnoId, IReadOnlyCollection<int>? inscripcionIds = null)
        {
            var template = await _context.CertificadoTemplates
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == templateId);

            if (template == null)
            {
                return null;
            }

            var alumno = await _context.Alumnos
                .AsNoTracking()
                .Include(a => a.Inscripciones)
                    .ThenInclude(i => i.Curso)
                        .ThenInclude(c => c.Orientacion)
                .Include(a => a.Inscripciones)
                    .ThenInclude(i => i.Curso)
                        .ThenInclude(c => c.Anexo)
                .Include(a => a.Inscripciones)
                    .ThenInclude(i => i.CursadasMaterias)
                        .ThenInclude(cm => cm.Materia)
                .Include(a => a.Inscripciones)
                    .ThenInclude(i => i.CursadasMaterias)
                        .ThenInclude(cm => cm.Calificacion)
                .FirstOrDefaultAsync(a => a.Id == alumnoId);

            if (alumno == null)
            {
                return null;
            }

            var inscripcionActual = alumno.Inscripciones
                .OrderByDescending(i => i.Anio)
                .ThenByDescending(i => i.Id)
                .FirstOrDefault();

            var replacements = new Dictionary<string, string>
            {
                ["fecha_actual"] = DateTime.Today.ToString("d 'de' MMMM 'de' yyyy", new CultureInfo("es-AR")),
                ["FECHA_ACTUAL"] = DateTime.Today.ToString("d 'de' MMMM 'de' yyyy", new CultureInfo("es-AR")),
                ["FECHA_EMISION"] = DateTime.Today.ToString("d 'de' MMMM 'de' yyyy", new CultureInfo("es-AR")),
                ["nombre_alumno"] = FormatAlumnoNombre(alumno),
                ["ALUMNO_NOMBRE"] = FormatAlumnoNombre(alumno),
                ["numero_documento"] = alumno.NumeroDocumento,
                ["NUMERO_DOCUMENTO"] = alumno.NumeroDocumento,
                ["curso"] = inscripcionActual?.Curso.CursoNombre ?? string.Empty,
                ["CURSO"] = inscripcionActual?.Curso.CursoNombre ?? string.Empty,
                ["division"] = inscripcionActual?.Curso.Division ?? string.Empty,
                ["DIVISION"] = inscripcionActual?.Curso.Division ?? string.Empty,
                ["anio_lectivo"] = inscripcionActual?.Anio.ToString() ?? DateTime.Today.Year.ToString(),
                ["ANIO_LECTIVO"] = inscripcionActual?.Anio.ToString() ?? DateTime.Today.Year.ToString(),
                ["orientacion"] = inscripcionActual?.Curso.Orientacion.Nombre ?? string.Empty,
                ["ORIENTACION"] = inscripcionActual?.Curso.Orientacion.Nombre ?? string.Empty,
                ["nombre_institucion"] = "CENS N° 15",
                ["NOMBRE_INSTITUCION"] = "CENS N° 15",
                ["BLOQUE_CALIFICACIONES"] = BuildCalificacionesHtml(alumno, inscripcionIds),
                ["bloque_calificaciones"] = BuildCalificacionesHtml(alumno, inscripcionIds),
            };

            var html = NormalizeDegreeSymbols(ReplacePlaceholders(template.ContenidoHtml, replacements));

            return new RenderedCertificadoTemplateDto
            {
                TemplateId = template.Id,
                AlumnoId = alumno.Id,
                Nombre = template.Nombre,
                Html = html,
                Formato = template.Formato,
                MargenSuperior = template.MargenSuperior,
                MargenInferior = template.MargenInferior,
                MargenIzquierdo = template.MargenIzquierdo,
                MargenDerecho = template.MargenDerecho,
                ImagenesJson = template.ImagenesJson,
            };
        }

        private async Task ValidateNombreDisponible(string nombre, int? id = null)
        {
            if (string.IsNullOrWhiteSpace(nombre))
            {
                throw new InvalidOperationException("El nombre de la plantilla es obligatorio.");
            }

            var exists = await _context.CertificadoTemplates.AnyAsync(t =>
                (!id.HasValue || t.Id != id.Value) &&
                t.Nombre.ToLower() == nombre.ToLower());

            if (exists)
            {
                throw new InvalidOperationException("Ya existe una plantilla con ese nombre.");
            }
        }

        private static string ReplacePlaceholders(string html, Dictionary<string, string> replacements)
        {
            var result = html;
            foreach (var replacement in replacements)
            {
                result = result.Replace("{{" + replacement.Key + "}}", replacement.Value);
            }

            return result;
        }

        private static string NormalizeDegreeSymbols(string value)
        {
            var normalized = (value ?? string.Empty).Replace('º', '°');
            while (normalized.Contains("°°"))
            {
                normalized = normalized.Replace("°°", "°");
            }

            normalized = normalized.Replace("° °", "° ");
            return normalized;
        }

        private static string BuildCalificacionesHtml(Alumno alumno, IReadOnlyCollection<int>? inscripcionIds)
        {
            var sb = new StringBuilder();
            var inscripciones = alumno.Inscripciones
                .Where(i => inscripcionIds == null || inscripcionIds.Contains(i.Id))
                .OrderBy(i => i.Anio)
                .ThenBy(i => i.Curso.CursoNombre)
                .ThenBy(i => i.Curso.Division)
                .ToList();

            if (inscripciones.Count == 0)
            {
                return "<p>No registra inscripciones.</p>";
            }

            foreach (var inscripcion in inscripciones)
            {
                var curso = inscripcion.Curso;
                sb.Append("<section class=\"calificaciones-curso\" style=\"margin:0 0 33px;page-break-inside:avoid;\">");
                sb.Append("<p style=\"margin:0 0 7px;font-weight:700;\"><strong>Curso:</strong> ")
                    .Append(Encode(FormatCursoNombre(curso)))
                    .Append("</p>");
                sb.Append("<table class=\"calificaciones-tabla\" style=\"width:100%;border-collapse:collapse;font-size:10px;\">");
                sb.Append("<thead><tr>");
                sb.Append(BuildTh("Asignatura"));
                sb.Append(BuildTh("1er Cuat."));
                sb.Append(BuildTh("2do Cuat."));
                sb.Append(BuildTh("Pro. Anual"));
                sb.Append(BuildTh("Ex.Dic"));
                sb.Append(BuildTh("Ex.Marzo"));
                sb.Append(BuildTh("Nota Final"));
                sb.Append(BuildTh("Situación"));
                sb.Append("</tr></thead><tbody>");

                var cursadas = inscripcion.CursadasMaterias
                    .OrderBy(cm => GetMateriaNombre(cm))
                    .ToList();

                if (cursadas.Count == 0)
                {
                    sb.Append("<tr><td colspan=\"8\" style=\"padding:3px 4px;text-align:center;\">Sin materias cargadas</td></tr>");
                }
                else
                {
                    foreach (var cursada in cursadas)
                    {
                        var calificacion = cursada.Calificacion;
                        sb.Append("<tr>");
                        sb.Append(BuildTd(Encode(GetMateriaNombre(cursada)), "left", "asignatura"));
                        sb.Append(BuildTd(FormatNota(calificacion?.C1Promedio)));
                        sb.Append(BuildTd(FormatNota(calificacion?.C2Promedio)));
                        sb.Append(BuildTd(FormatNota(calificacion?.PromedioAnual)));
                        sb.Append(BuildTd(FormatNota(calificacion?.RecuperacionDiciembre)));
                        sb.Append(BuildTd(FormatNota(calificacion?.RecuperacionMarzo)));
                        sb.Append(BuildTd(FormatNota(calificacion?.CalificacionFinal)));
                        sb.Append(BuildTd(calificacion == null ? "-" : Encode(FormatEstado(calificacion.Estado))));
                        sb.Append("</tr>");
                    }
                }

                sb.Append("</tbody></table>");
                sb.Append("</section>");
            }

            return sb.ToString();
        }

        private static string FormatAlumnoNombre(Alumno alumno)
        {
            return string.Join(" ", new[] { alumno.Apellidos, alumno.Nombres }.Where(v => !string.IsNullOrWhiteSpace(v)));
        }

        private static string FormatCursoNombre(Curso curso)
        {
            var cursoNombre = NormalizeCursoPart(curso.CursoNombre);
            var division = NormalizeCursoPart(curso.Division).TrimStart('°').Trim();
            var numeroDivision = string.IsNullOrWhiteSpace(cursoNombre)
                ? division
                : cursoNombre.Contains('°')
                    ? $"{cursoNombre} {division}".Trim()
                    : $"{cursoNombre}° {division}".Trim();

            while (numeroDivision.Contains("°°"))
            {
                numeroDivision = numeroDivision.Replace("°°", "°");
            }

            numeroDivision = numeroDivision.Replace("° °", "° ");

            return string.Join(" ", new[] { numeroDivision, curso.Orientacion.Nombre }.Where(v => !string.IsNullOrWhiteSpace(v)));
        }

        private static string NormalizeCursoPart(string value)
        {
            return NormalizeDegreeSymbols(value).Trim();
        }

        private static string BuildTh(string text)
        {
            return $"<th style=\"border-bottom:1px solid #111;font-weight:700;padding:3px 4px;text-align:center;\">{Encode(text)}</th>";
        }

        private static string BuildTd(string html, string align = "center", string? className = null)
        {
            var classAttr = string.IsNullOrWhiteSpace(className) ? string.Empty : $" class=\"{className}\"";
            return $"<td{classAttr} style=\"padding:3px 4px;text-align:{align};\">{html}</td>";
        }

        private static string GetMateriaNombre(CursadaMateria cursada)
        {
            if (!string.IsNullOrWhiteSpace(cursada.MateriaNombre))
            {
                return cursada.MateriaNombre;
            }

            return cursada.Materia?.Nombre ?? string.Empty;
        }

        private static string FormatNota(decimal? nota)
        {
            return nota.HasValue ? nota.Value.ToString("0.##", CultureInfo.InvariantCulture) : "-";
        }

        private static string FormatEstado(EstadoMateria estado)
        {
            return estado switch
            {
                EstadoMateria.EnCurso => "En curso",
                EstadoMateria.Aprobado => "Aprobado",
                EstadoMateria.RecuperaPrimerCuatrimestre => "Recupera 1er cuatrimestre",
                EstadoMateria.RecuperaSegundoCuatrimestre => "Recupera 2do cuatrimestre",
                EstadoMateria.RecuperaAmbos => "Recupera ambos",
                _ => estado.ToString(),
            };
        }

        private static string Encode(string value)
        {
            return WebUtility.HtmlEncode(value);
        }
    }
}
