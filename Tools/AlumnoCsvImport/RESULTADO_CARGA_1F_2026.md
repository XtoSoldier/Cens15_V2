# Carga e Inscripcion de Alumnos 1F/2026

Carga aplicada en PostgreSQL local `localhost:5432`, base `cens15`.
Fuente: `C:\Development\CENS15\alumnos_1F_2026.csv`, conservada sin cambios.

SHA-256 de la fuente:
`bcb2a318bb64a80ce36ddecdad242d62763e7f342c854fb4390bff31408edbda`.

## Destino Confirmado

- Curso ID 6: 1F.
- Anexo ID 1: ESC. 41.
- Orientacion ID 4: Perito Auxiliar en Relaciones Humanas.
- Anio de inscripcion: 2026; estado Activa.
- Materias existentes: 8, IDs 90 a 97.
- El curso_id 28 del CSV es legacy; no se utilizo como ID destino.

## Resultado

| Registros | Nuevos | Total antes | Total despues |
| --- | ---: | ---: | ---: |
| Alumnos | 11 | 533 | 544 |
| Contactos | 11 | 525 | 536 |
| Datos de nacimiento | 11 | 525 | 536 |
| Items de documentacion | 44 | 1199 | 1243 |
| Inscripciones | 11 | 50 | 61 |
| Cursadas de materias | 88 | 284 | 372 |
| Calificaciones | 0 | 7 | 7 |

Las 11 identidades del archivo eran nuevas por DNI, sin duplicados o conflictos.
Se crearon 11 inscripciones en 1F/2026 y sus ocho materias por alumno, sin notas.
Los 44 items de documentacion respetan las banderas presentes en el CSV y los
cuatro tipos existentes del catalogo. No se inventaron imagenes o documentos.

Las fechas de inscripcion registran el momento de esta carga; el anio academico
es 2026. Los IDs, curso_id y timestamps legacy no se reutilizaron como claves o
fechas de matricula nuevas. No se modificaron alumnos ni datos preexistentes,
usuarios, cursos, materias, docentes, calificaciones u otras tablas.

## Advertencias de Calidad

Numeracion fisica de filas, incluyendo el encabezado:

- Fila 5: nombre y telefono de emergencia estaban invertidos. Se intercambiaron
  esos dos valores al mapearlos en memoria.
- Fila 11: el nombre de emergencia contenia un telefono y el telefono estaba
  vacio. Se guardo el numero en telefono y el nombre quedo vacio.
- Filas 2 y 3: correos sospechosos por dominio o marcador de correo ausente.
  Se conservaron literalmente, sin corregir ni adivinar direcciones.

## Identificadores Generados

| Fila CSV | AlumnoId | InscripcionId |
| ---: | ---: | ---: |
| 2 | 540 | 74 |
| 3 | 541 | 75 |
| 4 | 542 | 76 |
| 5 | 543 | 77 |
| 6 | 544 | 78 |
| 7 | 545 | 79 |
| 8 | 546 | 80 |
| 9 | 547 | 81 |
| 10 | 548 | 82 |
| 11 | 549 | 83 |
| 12 | 550 | 84 |

## Verificacion

Altas, matriculas y cursadas se aplicaron en una unica transaccion serializable,
con locks y comprobacion de fingerprints de todas las tablas antes del commit.
Se verifico cada perfil nuevo y cada matricula con sus ocho cursadas. Una nueva
previsualizacion READ ONLY confirmo 11 alumnos e inscripciones ya existentes,
cero altas pendientes y ningun conflicto. Repetir el plan no duplica registros.

Receipt aplicado:
`be79fedea54c7bdbf200049daf3c0a607f69ebb6c82120a22a2bf421705fe022`.

Pruebas: 354 correctas excluyendo las integraciones PostgreSQL opt-in. No se
iniciaron servidores, enviaron correos ni ejecutaron migraciones o seeder.
