# Carga Local De Alumnos

Archivo: `C:\Development\CENS15\alumnos.csv`.
Destino verificado: PostgreSQL local `localhost:5432`, base `cens15`, configuracion Development.
SHA-256 del archivo original: `ab9c4cadc95817f70db294d26e69b213a2764a7db64a2bec7f240d75af672bda`.

## Resultado Verificado

- Filas del CSV: 500.
- Fichas nuevas cargadas: 498.
- Repeticiones equivalentes omitidas: 2 (filas 235 y 236, repetidas de 211 y 230).
- Alumnos existentes conservados sin actualizaciones: 35.
- Total de alumnos en la base, incluidas posibles bajas anteriores: 533.
- Inscripciones antes/despues: 18 / 18.
- Cursadas antes/despues: 16 / 16.
- Calificaciones antes/despues: 6 / 6.
- Inscripciones creadas para los alumnos importados: 0.
- Verificacion posterior en modo lectura: 498 coincidencias, 0 nuevas fichas propuestas.

## Adaptacion

`nombre`, `apellido` y `dni` se asignaron a nombres, apellidos y numero de documento.
`fecha_de_nacimiento` y `sexo` se asignaron a fecha de nacimiento y genero.
`domicilio`, `loc_nac`, `prov_nac`, `pais_nac`, `tel_alumno` y `email` se asignaron a
los campos equivalentes de ficha, nacimiento y contacto. `nombre_tutor` y `tel_tutor`
se conservaron como contacto de emergencia.

Los indicadores `cert_estudio`, `pase`, `fot_dni` y `cuil` se mapearon a `Presentado`
usando los tipos documentales existentes. No se crearon imagenes ni archivos;
los indicadores vacios o NULL no generaron registros documentales.

Se aplico la migracion aprobada para permitir valores ausentes: quedaron en NULL
6 DNI, 149 fechas de nacimiento y 103 generos de las nuevas fichas. Los demas
campos de texto sin datos quedaron vacios. No se inventaron fechas ni generos.

Se ignoraron IDs antiguos, `curso_id`, `inscripcion`, `created_at` y `updated_at`.
La base genero sus propios IDs. Las correcciones de comillas se hicieron solamente
en memoria, sin cambiar `alumnos.csv`. El proceso fue transaccional y no modifico
la base de produccion.
