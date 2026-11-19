# Calificaciones: Ausente y Sin Calificar

- `A` (Ausente) cuenta como cero y se guarda con condicion `Ausente`.
- `S/C` (Sin Calificar, por falta de docente) no promedia y se guarda con condicion `SinCalificar` y valor nulo.
- Un casillero vacio sigue pendiente: valor nulo y condicion `Numerica`. No equivale a S/C.
- Los ceros numericos historicos conservan su condicion `Numerica`; no se convierten en ausencias.
- Las condiciones corresponden a los dos promedios cuatrimestrales y los recuperatorios de diciembre/marzo. Los seis parciales siguen siendo numericos y se preservan al editar desde la app.

## Calculo

Con dos cuatrimestres computables, el anual es el techo del promedio. Para aprobar se requiere segundo cuatrimestre >= 6 y promedio **sin redondear** >= 6. Por ejemplo, 5/7 aprueba con 6; 7/5 recupera el segundo y A/8 recupera el primero con anual 4.

Si un cuatrimestre es S/C, se usa exclusivamente el otro, sin redondear su nota anual. Si es >= 6, aprueba. Si es menor que 6, la app consulta que recuperar, preseleccionando el cuatrimestre desaprobado y permitiendo primero, segundo o ambos. Cancelar no guarda. La API rechaza este caso si falta una eleccion valida, incluso si ya hay un examen aprobado.

La eleccion se guarda en `recuperacionExcepcional` con `RecuperaPrimerCuatrimestre`, `RecuperaSegundoCuatrimestre` o `RecuperaAmbos`. Se conserva tras aprobar un recuperatorio y se limpia al dejar de ser un caso excepcional. La app vuelve a consultar si cambia el cuatrimestre computable.

Dos S/C o un cuatrimestre pendiente no generan promedio ni aprobacion automatica. Para materias que deben recuperar, diciembre >= 4 aprueba; en su defecto marzo >= 4. La nota final del examen se redondea hacia arriba. Se conserva el flujo manual AP/APE cuando ambos cuatrimestres y examenes estan vacios.

La API calcula anual, final y estado al crear/actualizar; no confia en los valores calculados enviados por la app. Las lecturas y los certificados no recalculan registros historicos. Los certificados distinguen `A (Ausente)` y `S/C (Sin Calificar)`.

## Despliegue

La migracion `20261105012034_AddCalificacionCondiciones` agrega cuatro condiciones integer con valor predeterminado 0 (`Numerica`) y la eleccion nullable. No modifica notas ni recalcula datos.

Esta migracion requiere aprobacion antes de aplicarse en una base real. **La API aplica migraciones pendientes al arrancar**: no reiniciar la nueva API hasta autorizar el cambio y disponer de respaldo. Desplegar primero el esquema/API y luego la app. Los checkpoints anteriores pueden resultar incompatibles por la nueva huella de esquema.

## Verificacion Sin Datos Reales

En la carpeta de API:

```powershell
dotnet test Tests/CENS15_V2.Tests.csproj -c Release --filter FullyQualifiedName~CalificacionTests
```

Estas pruebas usan EF InMemory y no conectan a PostgreSQL. Incluyen matriz de calculo, validacion, rechazo sin efectos sobre entidades seguidas, persistencia de condiciones/parciales, proyeccion docente y certificados.

En la carpeta de app:

```powershell
pnpm run test:calificaciones
pnpm exec tsc --noEmit
```

Las pruebas de pantallas simulan la API y verifican confirmacion/cancelacion, relectura, errores de guardado y guardados concurrentes por fila. No reemplazan una prueba visual en dispositivo ni una integracion PostgreSQL descartable.

## Origen: Normal, Equivalencia Y Pase

- `EstadoMateria.AprobadoPorEquivalencia = 5` agrega A/E sin cambiar los valores 0..4. `Estado` sigue persistido como string, no como entero ni enum PostgreSQL.
- `disposicionEquivalencia` es texto libre nullable (`text`), sin formato ni limite artificial. A/E exige contenido no vacio despues de `Trim()` y conserva el texto recortado.
- `desdePase` es bool con default `false`. Pase es metadata: conserva exactamente las reglas numericas, A/S/C, recuperatorios y eleccion excepcional del origen Normal.
- A/E y Desde Pase son alternativas incompatibles. POST/PUT con ambos devuelve 400. No hay roles nuevos: siguen las responsabilidades Crear/Editar y la asignacion docente activa. PUT comprueba la cursada original y la de destino antes de modificar la calificacion.
- A/E tiene prioridad sobre cuatrimestres y examenes: anual y final quedan null, sin inventar aprobacion numerica. Conserva los seis parciales, promedios brutos, examenes, condiciones y una eleccion excepcional valida. Se validan rangos/condiciones sin normalizar ni sobreescribir valores historicos A/S/C mientras sea A/E.
- Al quitar A/E se recalcula desde los valores brutos enviados en el PUT, ignorando anual/final/estado calculados del cliente. Un unico cuatrimestre menor que 6 y el otro S/C exige `recuperacionExcepcional` valida incluso si hay examen aprobado. Un rechazo valida una copia, no deja cambios pendientes para un `SaveChanges` posterior.
- GET por ID, GET por cursada y la proyeccion docente retornan ambos metadatos. En la proyeccion docente `estado` sigue siendo int (5 para A/E). Las lecturas no recalculan historia ni convierten antiguos Aprobado/APE en A/E: ese alias historico no es distinguible en la base.
- Certificados/boletines muestran estado `A/E` y Disposicion escapada como HTML; Pase agrega `Viene de Pase` junto al nombre de materia. Son leyendas de texto sin JavaScript ni iconos necesarios para PDF.

### Contrato PUT Y Clientes Existentes

`desdePase: null` u omitido conserva el valor actual. `disposicionEquivalencia: null` u omitida conserva la disposicion solamente si el registro ya era A/E y sigue A/E; en Normal/Pase se limpia. Para reemplazar una disposicion se envia texto no vacio. Una cadena vacia en A/E es un error, no una orden de borrado.

La UI nueva envia siempre `desdePase` explicito y `disposicionEquivalencia` como string o null. Para quitar A/E envia estado Normal y `desdePase: false` (o true si cambia a Pase). Un cliente antiguo que omite ambos metadatos y envia un estado distinto para una A/E recibe 400 controlado; no se elimina la equivalencia silenciosamente. PUT conserva su semantica existente de reemplazo para notas/condiciones: el cliente debe reenviar sus valores brutos, no omitirlos como si fuera PATCH.

### Migracion De Origen Pendiente De Autorizacion

`20261111201830_AddCalificacionOrigen` agrega **solo** estas columnas en `Calificaciones`:

```sql
ALTER TABLE "Calificaciones" ADD "DesdePase" boolean NOT NULL DEFAULT FALSE;
ALTER TABLE "Calificaciones" ADD "DisposicionEquivalencia" text;
```

El SQL incremental generado esta en `Migrations/20261111201830_AddCalificacionOrigen.sql`, desde `20261105235342_AddPreceptores`. Incluye transaccion y registro en `__EFMigrationsHistory`; no contiene actualizaciones de notas/estados, backfill de equivalencias ni cambios de `AlumnoContactos.Email`. El drift preexistente de ese Email no forma parte de esta tarea: Designer y snapshot mantienen su nullable legacy. No incluir automaticamente ese drift al generar migraciones futuras.

**No se aplico la migracion ni se arranco la API.** Antes de desplegar, autorizar el cambio, respaldar y verificar migraciones previas. La API migra al arrancar, por lo que no se debe reiniciar con este binario sin esa autorizacion. El rollback elimina las dos columnas y perderia sus metadatos; requiere respaldo si ya se cargaron equivalencias/pases.

Comandos de generacion (no aplican SQL):

```powershell
dotnet build CENS15_V2.csproj -c Release --no-restore
dotnet ef migrations script 20261105235342_AddPreceptores 20261111201830_AddCalificacionOrigen --configuration Release --no-build --context AppDbContext --output Migrations/20261111201830_AddCalificacionOrigen.sql
```

### Pruebas Backend De Origen

```powershell
dotnet test Tests/CENS15_V2.Tests.csproj -c Release --filter "FullyQualifiedName~CalificacionTests|FullyQualifiedName~CalificacionOrigenTests"
dotnet test Tests/CENS15_V2.Tests.csproj -c Release --filter FullyQualifiedName~CalificacionOrigenRoundTripsOnlyInDisposablePostgresAndCleansUp --environment CENS15_RUN_SNAPSHOT_POSTGRES_TESTS=true
```

Las pruebas nuevas cubren reglas A/E, persistencia Create/Update y ambos GET, transiciones Normal/A/E/Pase, compatibilidad de PUT, rechazo sin efectos tracked, proyeccion docente, HTML/XSS y 403 con principal docente al intentar redirigir una calificacion de cursada ajena. Se verifica tambien que Up/Down solo agreguen/eliminen las dos columnas y el Designer preserve Email nullable. `CalificacionTests` preexistente permanece sin cambios.

La integracion PostgreSQL reutiliza el fixture privado de `CheckpointPostgresTests`. Crea exclusivamente `cens15_checkpoint_it_<UUID>`, usa `EnsureCreated` alli (no aplica migraciones), verifica persistencia real del estado string y metadatos, rechazos y recalculo; elimina la base al terminar y comprueba su ausencia. No conecta a la base de la aplicacion ni ejecuta API/seeder. La prueba se omite sin el opt-in indicado; no comprueba la ejecucion del script de migracion sobre bases existentes.
