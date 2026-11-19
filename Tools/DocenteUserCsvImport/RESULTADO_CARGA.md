# Resultado de Carga de Usuarios Docentes

Carga aplicada en PostgreSQL local: `localhost:5432`, base `cens15`.
Fuente: `C:\Development\CENS15\docentes.csv`, sin modificar.

SHA-256 de la fuente:
`339109cbe649f0f9e7c11fa42b0a5edbc5f2f35f2c1d991b3e275a8d91326a1d`.

## Resultado

| Concepto | Cantidad |
| --- | ---: |
| Filas leidas | 45 |
| Correos unicos | 38 |
| Repeticiones omitidas | 7 |
| Usuarios nuevos con rol Docente | 36 |
| Cuentas Docente existentes conservadas | 2 |
| Usuarios totales antes / despues | 10 / 46 |
| Credenciales Auth antes / despues | 10 / 46 |
| Conflictos o errores | 0 |

Las cuentas ya existentes corresponden a las filas 18 y 25. No se modificaron
sus nombres, apellidos, correos, roles ni contrasenas. Tampoco se modificaron
las demas cuentas preexistentes.

Para los usuarios nuevos se uso el apellido en minusculas salvo su primer
caracter en mayuscula, con cultura es-AR y conservando acentos. Los nombres se
tomaron del segundo campo, quitando espacios exteriores.

Las credenciales nuevas se derivaron de la parte exacta del correo anterior a
`@`, sin incluirlo y respetando mayusculas/minusculas. Solo se guardaron hashes
BCrypt con la configuracion existente de la API. Se verificaron las 36
credenciales antes del commit, junto con rol Docente y estado activo. No se
imprimieron ni se agregaron contrasenas en texto plano a archivos.

No se enviaron correos ni se crearon tokens, fichas Docente, asignaciones,
inscripciones o calificaciones. Los conteos de todas las tablas ajenas a Users
y Auths permanecieron iguales. El rol Docente ya existia; no se creo otro ni
se alteraron sus responsabilidades. No se aplicaron migraciones ni seed.

## Advertencia del Archivo

Las filas 7 y 11 repiten una cuenta. La fila 19 tiene los mismos nombres y
apellidos pero otro correo. Se conservaron ambos correos literales como cuentas
distintas; no se corrigio ni se fusiono ninguna direccion por suposicion.

## Verificacion Posterior

Una nueva previsualizacion READ ONLY encontro los 38 correos unicos con rol
Docente y **0 altas pendientes**, sin conflictos. El hash del CSV siguio igual.
La herramienta es insert-only: repetir la carga con una previsualizacion nueva
no duplica usuarios ni restablece contrasenas.

Pruebas de backend: 282 aprobadas, incluidas 31 del importador; se excluyeron
las integraciones PostgreSQL opt-in. No se utilizaron pruebas para ejecutar
la carga real: se aplico explicitamente con confirmacion de hash, base y receipt.

Las contrasenas derivadas del correo son predecibles. Se recomienda cambiarlas
tras el primer acceso; no se impuso un flujo nuevo de cambio obligatorio.
