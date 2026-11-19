# Eliminacion definitiva de alumnos

## Panel Admin

En Alumnos, los perfiles Admin/SuperAdmin disponen de **Eliminar definitivamente**
tanto para alumnos activos como para alumnos con baja logica. Los demas perfiles
no ven la accion y los endpoints tambien comprueban el rol en el servidor.

Al abrir el dialogo se consulta la verificacion del alumno, se muestran nombre,
apellido y DNI si existe, y se advierte que la operacion es irreversible. Si hay
dependencias, se muestran cantidades y orden manual; confirmar queda deshabilitado.
Tambien bloquean los registros de documentos, contacto y nacimiento, aunque sus
campos esten vacios. No se ofrecen botones de limpieza automatica ni borrado en
cascada. La restauracion existente se conserva como una accion independiente.

Sin dependencias, el administrador debe escribir exactamente **ELIMINAR** y
confirmar. Cancelar o Escape no eliminan. Durante el DELETE se bloquean cierre y
envios duplicados. Un 409 vuelve a verificar las dependencias y borra la
confirmacion escrita; otros errores bloquean hasta usar **Volver a verificar**.
Una respuesta de verificacion antigua no puede habilitar el borrado de otro alumno.

## Contrato

Ambas rutas requieren autenticacion y comprobacion en servidor del rol Admin o
SuperAdmin, con la normalizacion existente del controlador (sin espacios y sin
distinguir mayusculas). Un rol no autorizado recibe 403; sin autenticacion, 401.

`GET /api/Alumnos/{id:int}/permanent-deletion-info`

- 404 si no existe, incluso buscando entre alumnos con baja logica.
- 200 con el siguiente contrato JSON (ejemplo sin dependencias):

```json
{
  "alumnoId": 123,
  "puedeEliminar": true,
  "dependencias": [
    { "entidad": "Calificaciones", "cantidad": 0 },
    { "entidad": "CursadasMaterias", "cantidad": 0 },
    { "entidad": "Inscripciones", "cantidad": 0 },
    { "entidad": "AlumnoDocumentos", "cantidad": 0 },
    { "entidad": "AlumnoContactos", "cantidad": 0 },
    { "entidad": "AlumnoNacimientos", "cantidad": 0 }
  ],
  "ordenEliminacion": [
    "Calificaciones", "CursadasMaterias", "Inscripciones",
    "AlumnoDocumentos", "AlumnoContactos", "AlumnoNacimientos", "Alumnos"
  ],
  "mensaje": "La eliminacion es definitiva e irreversible. No se podra restaurar al alumno."
}
```

Siempre incluye las seis cantidades. Los nombres de entidad son identificadores
para la UI, que puede traducirlos a etiquetas. El GET es informativo, no una
reserva: el DELETE vuelve a comprobar las dependencias.

`DELETE /api/Alumnos/{id:int}/permanent` (sin body)

- 204 al eliminar exclusivamente la fila del alumno.
- 404 si no existe.
- 409 con `{ "message": "..." }` ante dependencias o conflicto concurrente/FK.
  Volver a consultar el GET antes de reintentar. No se exponen SQL ni detalles del
  proveedor en la respuesta.

La baja logica `DELETE /api/Alumnos/{id}` y el GET `deletion-info` preexistentes
mantienen su comportamiento y campos, al igual que la restauracion.

## Seguridad Y Orden Manual

Interpretacion conservadora: documentos, contacto y nacimiento TAMBIEN bloquean,
igual que inscripciones, cursadas y calificaciones, aunque el alumno tenga baja
logica. Todas las consultas nuevas ignoran filtros globales, incluidos los joins
de descendientes. No se borran hijos ni archivos automaticamente.

El administrador debe retirar primero calificaciones, luego cursadas, luego
inscripciones, y tambien los datos de perfil antes del alumno, con una herramienta
autorizada. Actualmente no hay endpoints DELETE de calificaciones/cursadas para
esta operacion. Este cambio no agrega rutas SQL ni CRUD masivo ni debilita permisos.
El DELETE de inscripciones ahora devuelve 409 si tiene cursadas o calificaciones;
permite limpiar una inscripcion sin hijos de un alumno con baja logica.

La UI muestra advertencia irreversible y el orden manual, incluidos los datos
de perfil. Vaciar campos no equivale a eliminar las filas hijas.

## Concurrencia Y Esquema

En PostgreSQL, ambos borrados fisicos usan transaccion Serializable, bloquean la
fila padre con FOR UPDATE antes de validar y ejecutan DELETE condicionado por
NOT EXISTS de los hijos directos. Los inserts FK concurrentes necesitan KEY SHARE
y esperan al bloqueo; no pueden introducir hijos entre validacion y borrado.
ExecuteDelete afecta solo al padre, sin adjuntar ni eliminar un grafo EF. Los
rechazos no modifican entidades tracked ni ejecutan SaveChanges.

No se modifican esquema ni relaciones Cascade existentes: este flujo impide que
se alcancen con hijos. Una referencia EF nueva fuera de las relaciones conocidas
bloquea conservadoramente el borrado hasta revisar el codigo. Esto no detecta
relaciones o triggers externos no representados en EF; el esquema desplegado debe
corresponder al modelo. Otros proveedores relacionales fallan de forma cerrada.
InMemory omite transacciones/SQL y solo sirve para pruebas funcionales, no para
garantias de concurrencia.

Se requiere actualizar/reiniciar la API y desplegar/recargar el panel para usar
estos endpoints. Este cambio no requiere una nueva migracion. No se ejecutaron
borrados en la base de la aplicacion para verificarlo.

## Pruebas

```powershell
dotnet test Tests/CENS15_V2.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~AlumnoPermanentDeletionTests|FullyQualifiedName~AlumnoNullableTests"
dotnet test Tests/CENS15_V2.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~PermanentDeletionUsesDisposablePostgresAndNeverCascadesConcurrentChildren" --environment CENS15_RUN_SNAPSHOT_POSTGRES_TESTS=true
```

La prueba PostgreSQL reutiliza DisposableDatabase de CheckpointPostgresTests:
solo loopback, base nueva cens15_checkpoint_it_UUID, EnsureCreated sin migraciones
ni seeder de aplicacion, y DROP solo de esa base validada. La prueba comprueba que
el cleanup haya eliminado la base y que los archivos del alumno bloqueado sigan
existiendo. No arranca la API ni envia correos.
