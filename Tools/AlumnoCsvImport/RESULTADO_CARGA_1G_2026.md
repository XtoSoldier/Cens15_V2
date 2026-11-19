# Carga e Inscripcion 1G/2026

Aplicada en PostgreSQL local `localhost:5432`, base `cens15`, usando
`C:\Development\CENS15\alumnos_1G_2026.csv` sin modificar el archivo.

SHA-256: `0dce41ed302bd4f0d9bbd03555201c1eb0a420a1e88eec8c994f3a921d1cb5fc`.

## Destino

- Curso ID 7: 1G, anexo ESC. 41 (ID 1).
- Orientacion: Perito Auxiliar Aduana (ID 3).
- Inscripcion 2026, estado Activa.
- Siete materias existentes, IDs 98 a 104.
- El ID legacy 25 del CSV solo se valido como origen; no se uso como CursoId destino.

## Resultado

| Concepto | Resultado |
| --- | ---: |
| Filas / identidades unicas | 9 / 9 |
| Alumnos nuevos | 8 |
| Fichas existentes reutilizadas sin cambios | 1 |
| Inscripciones nuevas 1G/2026 | 9 |
| Cursadas nuevas | 63 |
| Contactos y nacimientos nuevos | 8 de cada uno |
| Items de documentacion nuevos | 32 |
| Calificaciones nuevas o modificadas | 0 |

Totales antes/despues: Alumnos 544/552; Inscripciones 61/70;
CursadasMaterias 372/435; Calificaciones 7/7.

Se preservaron todas las filas preexistentes, incluidos los datos de la ficha
reutilizada y cualquier otra inscripcion. No se crearon usuarios, cursos,
materias, tipos de documento ni imagenes. Los items de documentacion nuevos
reflejan exclusivamente las banderas del CSV y el catalogo existente.

## Calidad Del Archivo

Filas fisicas, incluido el encabezado:

- Fila 3: se conservo literalmente el marcador de correo `completar@cens15.com`.
- Filas 4 y 5: el nombre de emergencia contenia un telefono y la columna de
  telefono estaba vacia. Se movio el numero a telefono sin inventar nombre.
- Los demas campos vacios o literales se conservaron segun el mapeo habitual.

## Identificadores

| Fila CSV | AlumnoId | InscripcionId |
| ---: | ---: | ---: |
| 2 | 85 (existente) | 85 |
| 3 | 551 | 86 |
| 4 | 552 | 87 |
| 5 | 553 | 88 |
| 6 | 554 | 89 |
| 7 | 555 | 90 |
| 8 | 556 | 91 |
| 9 | 557 | 92 |
| 10 | 558 | 93 |

Se uso una sola transaccion serializable, con locks y snapshots completos de
tablas para comprobar que solo se agregaron las filas previstas. Las fechas de
inscripcion corresponden a la carga; el anio academico es 2026. No se reutilizaron
claves ni timestamps legacy.

La previsualizacion READ ONLY posterior encontro nueve alumnos con sus nueve
inscripciones activas completas y cero altas pendientes, sin conflictos.
No se aplicaron migraciones ni seed, ni se enviaron correos o modificaron notas.

Receipt aplicado:
`cc28fcc3da9209e50444a6288ce971652b35327bf47334cae0d2d0815666d034`.
