using CENS15_V2.Models;

namespace CENS15_V2.Services
{
    public static class CalificacionRules
    {
        public static void Apply(Calificacion calificacion)
        {
            if (!Enum.IsDefined(calificacion.Estado) ||
                (calificacion.RecuperacionExcepcional.HasValue &&
                 !Enum.IsDefined(calificacion.RecuperacionExcepcional.Value)))
                throw new InvalidOperationException("El estado de la calificacion no es valido.");

            ValidateNota(calificacion.C1Nota1);
            ValidateNota(calificacion.C1Nota2);
            ValidateNota(calificacion.C1Nota3);
            ValidateNota(calificacion.C2Nota1);
            ValidateNota(calificacion.C2Nota2);
            ValidateNota(calificacion.C2Nota3);
            calificacion.C1Promedio = Normalize(calificacion.C1Promedio, calificacion.C1Condicion);
            calificacion.C2Promedio = Normalize(calificacion.C2Promedio, calificacion.C2Condicion);
            calificacion.RecuperacionDiciembre = Normalize(calificacion.RecuperacionDiciembre, calificacion.DiciembreCondicion);
            calificacion.RecuperacionMarzo = Normalize(calificacion.RecuperacionMarzo, calificacion.MarzoCondicion);

            // Preserve the shipped manual AP/APE workflow when no quarter or exam was entered.
            if (calificacion.C1Condicion == CondicionNota.Numerica &&
                calificacion.C2Condicion == CondicionNota.Numerica &&
                calificacion.DiciembreCondicion == CondicionNota.Numerica &&
                calificacion.MarzoCondicion == CondicionNota.Numerica &&
                !calificacion.C1Promedio.HasValue && !calificacion.C2Promedio.HasValue &&
                !calificacion.RecuperacionDiciembre.HasValue && !calificacion.RecuperacionMarzo.HasValue)
            {
                ValidateNota(calificacion.PromedioAnual);
                ValidateNota(calificacion.CalificacionFinal);
                calificacion.RecuperacionExcepcional = null;
                return;
            }

            var c1 = calificacion.C1Promedio;
            var c2 = calificacion.C2Promedio;
            var excepcional = (c1.HasValue && calificacion.C2Condicion == CondicionNota.SinCalificar && c1 < 6) ||
                              (c2.HasValue && calificacion.C1Condicion == CondicionNota.SinCalificar && c2 < 6);
            if (excepcional)
            {
                if (calificacion.RecuperacionExcepcional is not (EstadoMateria.RecuperaPrimerCuatrimestre or
                    EstadoMateria.RecuperaSegundoCuatrimestre or EstadoMateria.RecuperaAmbos))
                    throw new InvalidOperationException("Debe seleccionar que cuatrimestre recuperar: Primero, Segundo o Ambos, cuando el unico cuatrimestre calificado es menor a 6 y el otro es S/C (Sin Calificar).");
            }
            else
            {
                calificacion.RecuperacionExcepcional = null;
            }

            calificacion.PromedioAnual = null;
            calificacion.CalificacionFinal = null;
            calificacion.Estado = EstadoMateria.EnCurso;

            if (c1.HasValue && c2.HasValue)
            {
                var promedio = (c1.Value + c2.Value) / 2;
                calificacion.PromedioAnual = decimal.Ceiling(promedio);
                calificacion.Estado = c2 >= 6 && promedio >= 6 ? EstadoMateria.Aprobado
                    : c1 < 6 && c2 < 6 ? EstadoMateria.RecuperaAmbos
                    : c1 < 6 ? EstadoMateria.RecuperaPrimerCuatrimestre
                    : EstadoMateria.RecuperaSegundoCuatrimestre;
            }
            else if ((c1.HasValue && calificacion.C2Condicion == CondicionNota.SinCalificar) ||
                     (c2.HasValue && calificacion.C1Condicion == CondicionNota.SinCalificar))
            {
                calificacion.PromedioAnual = c1 ?? c2;
                calificacion.Estado = calificacion.PromedioAnual >= 6
                    ? EstadoMateria.Aprobado : calificacion.RecuperacionExcepcional!.Value;
            }
            else
            {
                // A blank quarter is pending, unlike an explicit S/C. Neither permits automatic recovery.
                return;
            }

            if (calificacion.Estado == EstadoMateria.Aprobado)
                calificacion.CalificacionFinal = calificacion.PromedioAnual;
            else if (calificacion.RecuperacionDiciembre >= 4)
            {
                calificacion.Estado = EstadoMateria.Aprobado;
                calificacion.CalificacionFinal = decimal.Ceiling(calificacion.RecuperacionDiciembre.Value);
            }
            else if (calificacion.RecuperacionMarzo >= 4)
            {
                calificacion.Estado = EstadoMateria.Aprobado;
                calificacion.CalificacionFinal = decimal.Ceiling(calificacion.RecuperacionMarzo.Value);
            }
        }

        private static decimal? Normalize(decimal? nota, CondicionNota condicion)
        {
            if (!Enum.IsDefined(condicion))
                throw new InvalidOperationException("La condicion de la nota no es valida.");
            ValidateNota(nota);
            return condicion switch
            {
                CondicionNota.Ausente => 0m,
                CondicionNota.SinCalificar => null,
                _ => nota
            };
        }

        private static void ValidateNota(decimal? nota)
        {
            if (nota is < 0 or > 10)
                throw new InvalidOperationException("Las notas deben estar entre 0 y 10.");
        }
    }
}
