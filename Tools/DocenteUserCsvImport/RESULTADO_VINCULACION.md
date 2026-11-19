# Resultado de Vinculacion de Docentes

Operacion aplicada en PostgreSQL local: `localhost:5432`, base `cens15`.

| Concepto | Cantidad |
| --- | ---: |
| Docentes revisados | 45 |
| Usuarios existentes revisados | 46 |
| Vinculos nuevos | 30 |
| Vinculos existentes conservados | 2 |
| Docentes vinculados al finalizar | 32 |
| Pendientes sin coincidencia exacta elegible | 12 |
| Pendientes por ambiguedad | 1 |

Los 30 vinculos nuevos corresponden a coincidencias unicas de nombre y apellido
completos, normalizados por mayusculas, tildes y espacios. Los usuarios son
activos, tienen rol Docente y no estaban vinculados a otro docente. Ninguna de
estas coincidencias fue aproximada ni decidida por orden de recorrido.

Ids de Docentes vinculados en esta operacion:
`29, 33, 34, 35, 36, 38, 39, 41, 43, 44, 45, 46, 47, 48, 49, 52, 53, 57, 58, 59, 60, 61, 62, 63, 65, 70, 71, 72, 73, 74`.

Se conservaron los vinculos preexistentes de Dario Alarcon (51) y Paola Allogia
(66). No se sobrescribio ninguno.

## Pendientes Para Revision

| DocenteId | Nombre y apellido | Motivo |
| --- | --- | --- |
| 30 | Roberto Miguel | Sin coincidencia exacta elegible |
| 31 | Marina Aquino | Sin coincidencia exacta elegible |
| 32 | Florencia Pereyra | Sin coincidencia exacta elegible |
| 37 | Juan Assan | Sin coincidencia exacta elegible |
| 40 | Juana Lujan Mereles | Dos usuarios elegibles con el mismo nombre; no se eligio uno por suposicion |
| 42 | Gabriela Ramos | Sin coincidencia exacta elegible |
| 50 | Pedro Viña | Sin coincidencia exacta elegible |
| 54 | Laura Geuna | Sin coincidencia exacta elegible |
| 55 | Luciana Anahí García Lugones | Sin coincidencia exacta elegible |
| 56 | Ericr Uriel White | Sin coincidencia exacta elegible |
| 64 | Magalí Fernandez | Sin coincidencia exacta elegible |
| 68 | Giannina Ríos | Sin coincidencia exacta elegible |
| 69 | Gabriela Inca | Sin coincidencia exacta elegible |

Que no haya coincidencia exacta no implica que no exista una cuenta para esa
persona: puede haber diferencias de nombres, apellidos o datos incompletos.
Estos casos requieren confirmacion antes de vincular.

## Verificacion

Se actualizo exclusivamente `Docentes.UserId` en una transaccion serializable.
Los nombres, apellidos, emails, roles, estados y contrasenas de docentes y
usuarios permanecieron iguales. No se crearon usuarios ni docentes, ni se
modificaron materias, asignaciones, inscripciones o calificaciones. Todos los
conteos de tablas permanecieron iguales. No se enviaron correos, no se inicio
la API y no se ejecutaron migraciones ni seed.

El snapshot de todas las columnas escalares de Docentes/Users/Auths/Roles fue
comparado antes del commit, permitiendo solo los 30 vinculos aprobados. Una
previsualizacion READ ONLY posterior confirmo 32 vinculos conservados y cero
propuestas adicionales. Los 13 casos pendientes permanecieron sin vincular.

Receipt aplicado:
`92e3b4ada63430d71c6e46c0dd1c3759c8150b4c77d029328598e8e429dca24c`.

Pruebas: 304 aprobadas excluyendo integraciones PostgreSQL opt-in, incluidas
22 pruebas nuevas de matching, ambiguedades, estados y confirmacion de apply.
