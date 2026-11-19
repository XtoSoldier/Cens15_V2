# Carga 3D/2026

Aplicada en PostgreSQL local `localhost:5432`, base `cens15`, a partir de
`C:\Development\CENS15\alumnos_3D_2026.csv`, conservado sin cambios.

SHA-256: `87175c355f60c46824aa9e0de5d46e9c0edd09acbd1c04944f44034b2b6ede2e`.

## Destino Y Resultado

Curso ID 10: 3D, ESC. 41 (anexo 1), Perito Auxiliar en Relaciones Humanas
(orientacion 4). Anio 2026, estado Activa. Nueve materias existentes:
IDs 121 a 128 y 139.

| Concepto | Cantidad |
| --- | ---: |
| Filas / identidades unicas | 12 / 12 |
| Alumnos nuevos | 10 |
| Fichas existentes conservadas | 2 |
| Inscripciones nuevas | 12 |
| Cursadas nuevas | 108 |
| Contactos y nacimientos nuevos | 10 de cada uno |
| Items de documentacion nuevos | 40 |
| Calificaciones creadas o modificadas | 0 |

Totales antes/despues: Alumnos 561/571; Inscripciones 80/92;
CursadasMaterias 515/623; Calificaciones 7/7.

## Correccion Autorizada

El preview inicial bloqueo toda la carga por fecha `9999-09-09` en fila fisica 13,
Caren Agustina Pallero. El usuario eligio expresamente **Dejar fecha vacia**.
Se repitio el preview y apply con `--unknown-birth-date 9999-09-09`; solo esa fecha
se convirtio a NULL en memoria, sin inventar otra y sin modificar el CSV.

Los valores sexo=NULL de filas 11 y 12 se conservaron como genero nulo.
Se reutilizaron los alumnos 430 y 454 sin cambiar sus datos ni otras inscripciones.

## Identificadores

| Fila CSV | AlumnoId | InscripcionId |
| ---: | ---: | ---: |
| 2 | 430 (existente) | 104 |
| 3 | 454 (existente) | 105 |
| 4 | 568 | 106 |
| 5 | 569 | 107 |
| 6 | 570 | 108 |
| 7 | 571 | 109 |
| 8 | 572 | 110 |
| 9 | 573 | 111 |
| 10 | 574 | 112 |
| 11 | 575 | 113 |
| 12 | 576 | 114 |
| 13 | 577 | 115 |

La transaccion serializable verifico los incrementos previstos, todas las filas
previas y cada perfil/matricula con sus nueve cursadas antes del commit.
La previsualizacion posterior confirmo las doce inscripciones completas,
cero altas pendientes y ningun conflicto, incluida la fecha nula autorizada.
No se crearon usuarios, cursos, materias, tipos documentales o imagenes, ni se
ejecutaron migraciones, seed o correos. Los IDs legacy no se reutilizaron.

Receipt aplicado:
`5eb9f0c5c4f7cf5d84a38645c9771a65d35c2c2760eb43df03a89955c13ce0b5`.
