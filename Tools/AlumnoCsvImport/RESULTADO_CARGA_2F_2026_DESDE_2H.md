# Carga 2F/2026 Desde Archivo 2H

Aplicada en PostgreSQL local `localhost:5432`, base `cens15`.
Fuente: `C:\Development\CENS15\alumnos_2H_2026.csv`, sin modificar el archivo.

SHA-256: `0cd85bcbca214a63c09b0df286b68015cf9a293fe1176fb9841c6b557cd012b5`.

## Destino Explicito

El usuario solicito inscribir en **2F**, no en 2H. Se uso el curso existente
ID 9: 2F, anexo ESC. 41 (ID 1), orientacion Perito Auxiliar Aduana (ID 3).
El anio academico es 2026 y las inscripciones se crearon Activas.

El legacy curso_id 32 solo se valido como origen. Ni ese numero ni el nombre
2H del archivo se usaron para resolver el curso de destino.

Ocho materias existentes: IDs 112, 113, 114, 116, 117, 118, 119 y 120.

## Resultado

| Concepto | Cantidad |
| --- | ---: |
| Filas / identidades unicas | 10 / 10 |
| Alumnos nuevos | 9 |
| Fichas existentes reutilizadas sin cambios | 1 |
| Inscripciones nuevas en 2F/2026 | 10 |
| Cursadas nuevas | 80 |
| Contactos y nacimientos nuevos | 9 de cada uno |
| Items de documentacion nuevos | 36 |
| Calificaciones creadas o modificadas | 0 |
| Conflictos o advertencias del plan | 0 |

Totales antes/despues: Alumnos 552/561; Inscripciones 70/80;
CursadasMaterias 435/515; Calificaciones 7/7.

Se conservaron todas las filas preexistentes, incluida la ficha reutilizada y
cualquier otra inscripcion. Los campos y fechas de nacimiento validos se tomaron
literalmente del CSV, con la normalizacion habitual de nombres; no se hicieron
correcciones inferidas. Las banderas documentales se mapearon al catalogo existente,
sin inventar tipos ni imagenes. No se crearon usuarios, cursos o materias.

## Identificadores

| Fila CSV | AlumnoId | InscripcionId |
| ---: | ---: | ---: |
| 2 | 516 (existente) | 94 |
| 3 | 559 | 95 |
| 4 | 560 | 96 |
| 5 | 561 | 97 |
| 6 | 562 | 98 |
| 7 | 563 | 99 |
| 8 | 564 | 100 |
| 9 | 565 | 101 |
| 10 | 566 | 102 |
| 11 | 567 | 103 |

## Verificacion

La carga se ejecuto en una sola transaccion serializable con locks, verificacion
de conteos y snapshots de todas las tablas. Antes del commit se confirmaron las
diez matriculas con sus ocho cursadas por alumno y la preservacion de datos previos.
Una previsualizacion READ ONLY posterior encontro diez inscripciones completas,
cero altas pendientes y ningun conflicto. Repetir el plan no duplica registros.

Las fechas de inscripcion son las de esta carga; el anio academico es 2026. No se
reutilizaron IDs o timestamps legacy, ni se ejecutaron migraciones, seed o correos.

Receipt aplicado:
`68dde5aaa8fdcca308743a335c2a1985520c2eab208d05f52cddf33bbb2c7961`.

Pruebas: 52 casos focalizados de importacion correctos y 398 regresiones de API
correctas, excluyendo las integraciones PostgreSQL opt-in.
