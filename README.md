# DateroWebG (`datero-web-g`)

Agente de monitoreo GPS y control de tiempos de paraderos/geocercas para unidades de transporte urbano (**Ruta G: 67 Ida / 68 Vuelta**, cuenta `etudvrg`), desarrollado en **.NET 10**.

Se conecta a:
- **MySQL 8.0** (`mysql-gts`, base `urbano`, tabla `device`) por la red Docker `app-net`.
- **API / Hubs SignalR** (`https://villa.velsat.pe:8443`) para recibir eventos de despacho (`/urbanoHub`), posiciones GPS por unidad (`/dataHubVehicle/{usuario}/{placa}`) y reportar controles de geocercas y despachos automáticos (`/api/Datero/...`, `/api/Caja/asignarG`).

| Parámetro | Valor |
|---|---|
| Servicio / Contenedor | `datero-web-g` |
| Imagen | `datero-web-g:latest` |
| Puerto interno | `0.0.0.0:8080` |
| Puerto en el host | `127.0.0.1:3012` (solo loopback; Nginx hace de proxy) |
| Red Docker | `app-net` (externa, compartida con `mysql-gts`) |
| Zona horaria | `America/Lima` |

---

## Requisitos en el servidor (Ubuntu - Droplet 28)

- Docker y `docker-compose` 1.29.2 (comando con guion).
- Red externa `app-net` creada y con el contenedor `mysql-gts` conectado:
  ```bash
  docker network inspect app-net
  ```

---

## Instalación

```bash
git clone https://github.com/CODLuisP/DateroWebG.git datero-web-g
cd datero-web-g

# 1. Crear el archivo de configuración local a partir de la plantilla (NO se sube a git)
cp DateroWebG/appsettings.json.example DateroWebG/appsettings.json
nano DateroWebG/appsettings.json   # colocar credenciales reales de MySQL (urbano)

# Asegurar permisos de lectura para el usuario del contenedor (uid/gid 10001)
sudo chown 10001:10001 DateroWebG/appsettings.json
chmod 400 DateroWebG/appsettings.json

# 2. Construir y levantar el contenedor
docker-compose build
docker-compose up -d
```

> **Nota:** `DateroWebG/appsettings.json` debe existir como archivo antes de ejecutar `docker-compose up -d`. Si no existe, Docker creará un directorio vacío en su lugar y el contenedor fallará al iniciar.

---

## Actualización

```bash
git pull && docker-compose build && docker-compose down && docker-compose up -d
```

---

## Comprobar que arrancó

```bash
# Estado del contenedor y healthcheck (debe mostrar "Up ... (healthy)")
docker-compose ps

# Ver logs en tiempo real
docker-compose logs -f --tail=100 datero-web-g

# Probar el endpoint de salud (no toca la base de datos)
curl -i http://127.0.0.1:3012/health

# Probar inicio de monitoreo de una unidad (POST)
curl -i -X POST http://127.0.0.1:3012/api/iniciar-monitoreo \
  -H "Content-Type: application/json" \
  -d '{"Placa":"ABC-123","Usuario":"etudvrg","FechaIni":"08:00","Codasig":"0","Codruta":"67"}'
```

---

## Configuración (`DateroWebG/appsettings.json`)

El archivo `DateroWebG/appsettings.json` está fuera de git y de la imagen Docker, y se monta en modo solo lectura en `/app/appsettings.json`. La plantilla de referencia es [`DateroWebG/appsettings.json.example`](DateroWebG/appsettings.json.example).

| Clave | Descripción | Ejemplo |
|---|---|---|
| `ConnectionStrings:MySql` | Cadena de conexión a MySQL (`mysql-gts`, base `urbano`) | `Server=mysql-gts;Port=3306;Database=urbano;...` |
| `App:Usuario` | Cuenta/empresa monitoreada en `device` y SignalR | `etudvrg` |
| `App:PuertoHttp` | Puerto HTTP interno del contenedor | `8080` |
| `App:ApiBaseUrl` | URL base del backend / SignalR Hub | `https://villa.velsat.pe:8443` |
| `App:HttpTimeoutSegundos` | Timeout HTTP en segundos para llamadas salientes | `10` |

También pueden sobrescribirse mediante variables de entorno con el prefijo `DATERO_` (por ejemplo, `DATERO_App__PuertoHttp=8080`).

---

## Ejemplo de configuración en Nginx

Para exponer el servicio detrás de Nginx en el host (por ejemplo, bajo un bloque `server` existente o subruta):

```nginx
location /datero-g/ {
    proxy_pass http://127.0.0.1:3012/;
    proxy_http_version 1.1;
    proxy_set_header Host $host;
    proxy_set_header X-Real-IP $remote_addr;
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto $scheme;
    proxy_read_timeout 60s;
}
```

O en la raíz de un virtual host / puerto dedicado:

```nginx
location / {
    proxy_pass http://127.0.0.1:3012;
    proxy_http_version 1.1;
    proxy_set_header Host $host;
    proxy_set_header X-Real-IP $remote_addr;
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto $scheme;
    proxy_read_timeout 60s;
}
```
