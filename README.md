# Cens15_V2
BackEnd en .net 8.0

## Desarrollo local

```powershell
dotnet restore
dotnet run
```

El comando inicia el backend de desarrollo en:

```text
Swagger: https://localhost:7000/swagger/index.html
API web: https://localhost:7000/api
API Expo Go: http://IP_DE_LA_PC:5211/api
```

Para aplicar migraciones manualmente:

```powershell
dotnet ef database update
```

El teléfono y la PC deben estar en la misma red Wi-Fi. El firewall debe permitir
conexiones entrantes al puerto `5211`.

# FTP Conexion
Server: cens15Admin
cens.15.ftp
# Datos de desarrollo

Al iniciar la API se crean únicamente roles, responsabilidades, tipos de documento y el
administrador inicial si no existe. No se generan docentes, materias, cursadas ni alumnos demo.

Con una sesión Admin o SuperAdmin y exclusivamente en `Development`, Swagger expone:

- `POST /api/development/seed/demo-data`: genera los datos de demostración bajo demanda.
- `DELETE /api/development/seed/demo-data`: elimina docentes, materias y sus relaciones académicas de demostración.
