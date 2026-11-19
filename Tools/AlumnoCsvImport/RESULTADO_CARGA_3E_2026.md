# Carga 3E/2026

Aplicada en PostgreSQL local `localhost:5432`, base `cens15`, a partir de
`C:\Development\CENS15\alumnos_3E_2026.csv`, conservado sin cambios.

SHA-256: `5ff2b0cb8ffce7f7040a4825c596ca535c90882900584184275df8ad585b9eb3`.

## Destino Y Resultado

Curso ID 11: 3E, ESC. 41 (anexo 1), Perito Auxiliar Aduana (orientacion 3).
Anio 2026, estado Activa. Diez materias existentes, IDs 129 a 138.

| Concepto | Cantidad |
| --- | ---: |
| Filas / identidades unicas | 14 / 14 |
| Alumnos nuevos | 13 |
| Fichas existentes conservadas | 1 |
| Inscripciones nuevas | 14 |
| Cursadas nuevas | 140 |
| Contactos y nacimientos nuevos | 13 de cada uno |
| Items de documentacion nuevos | 52 |
| Calificaciones creadas o modificadas | 0 |

Totales antes/despues: Alumnos 571/584; Inscripciones 92/106;
CursadasMaterias 623/763; Calificaciones 7/7.

## Calidad Del Archivo

En la fila fisica 12 el correo tiene dominio `gmailcom`, sin punto. Se conservo
literalmente y se informo como sospechoso; no se adivino ni corrigio la direccion.
El genero NULL de la fila 15 permanecio nulo. Se reutilizo la ficha 106 sin cambiar
sus datos u otras inscripciones. No hubo bloqueos ni conflictos de identidades.

## Identificadores

| Fila CSV | AlumnoId | InscripcionId |
| ---: | ---: | ---: |
| 2 | 106 (existente) | 116 |
| 3 | 578 | 117 |
| 4 | 579 | 118 |
| 5 | 580 | 119 |
| 6 | 581 | 120 |
| 7 | 582 | 121 |
| 8 | 583 | 122 |
| 9 | 584 | 123 |
| 10 | 585 | 124 |
| 11 | 586 | 125 |
| 12 | 587 | 126 |
| 13 | 588 | 127 |
| 14 | 589 | 128 |
| 15 | 590 | 129 |

La transaccion serializable verifico los incrementos previstos, todas las filas
previas y cada perfil/matricula con sus diez cursadas antes del commit.
La previsualizacion posterior confirmo catorce inscripciones completas, cero
altas pendientes y ningun conflicto. No se crearon usuarios, cursos, materias,
tipos documentales o imagenes, ni se ejecutaron migraciones, seed o correos.
Las fechas de inscripcion corresponden a la carga; el anio academico es 2026.

Receipt aplicado:
`758bdf7e037851e1f469d41f1ab015fa20858b8ed8e150ed427f0a98cd803858`.

Verificacion conjunta de 3D y 3E: 55 pruebas focalizadas de importacion y 401
regresiones de API correctas, excluyendo integraciones PostgreSQL opt-in.
