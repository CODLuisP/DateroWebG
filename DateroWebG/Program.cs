using DateroWeb.API;
using DateroWeb.Config;
using DateroWeb.Models;
using DateroWeb.Services;
using Microsoft.AspNetCore.SignalR.Client;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DateroWeb
{
    public class Program
    {
        private static HubConnection _despachoConnection;
        private static Dictionary<string, MonitoreoGPSService> _monitoreosActivos = new Dictionary<string, MonitoreoGPSService>();
        private static Dictionary<string, DatosDespacho> _datosDespachos = new Dictionary<string, DatosDespacho>();
        private static AppSettings _settings;
        private static string _username;
        private static ServidorHTTP _servidor;
        private static GeocercaService _geocercaService;
        private static APIEnvioService _apiEnvioService;
        private static DatabaseService _dbService;

        // ── ZONAS DE DESPACHO AUTOMÁTICO ────────────────────────────────────
        // Cualquier unidad que entra y sale de una de estas geocercas circulares
        // queda despachada en el sentido y la ruta fija de esa zona. JOSE_GALVEZ
        // es el terminal de inicio (IDA); TUPAC es el punto de giro de vuelta.
        private record ZonaDespacho(string Nombre, double Lat, double Lon, double RadioM, ModoRuta ModoAlSalir, string CodRuta);

        private const string RUTA_IDA = "67";
        private const string RUTA_VUELTA = "68";
        private const int ID_GEOCERCA_TUPAC = 8;

        private static readonly List<ZonaDespacho> _zonasDespacho = new()
        {
            new ZonaDespacho("JOSE_GALVEZ", -12.229356, -76.909526, 200.0, ModoRuta.Ida,     RUTA_IDA),
            new ZonaDespacho("TUPAC",       -12.194796, -76.983054, 200.0, ModoRuta.Regreso, RUTA_VUELTA),
        };

        private static readonly TimeSpan INTERVALO_RECARGA_ESPERA = TimeSpan.FromMinutes(15);

        private static readonly ConcurrentDictionary<string, MonitoreoGPSService> _monitoreosEspera = new();
        private static readonly ConcurrentDictionary<string, string> _zonaActualPorPlaca = new();   // placa → nombre de zona (si está dentro)
        private static readonly ConcurrentDictionary<string, bool> _placasDespachoVuelta = new();
        private static readonly HashSet<string> _despachoEnCurso = new();
        private static bool _monitoreoEsperaActivo;
        private static DateTime _ultimaCargaEspera = DateTime.MinValue;

        private class UltimaPosicion
        {
            public double Latitud { get; set; }
            public double Longitud { get; set; }
            public DateTime Timestamp { get; set; }
            public double Velocidad { get; set; }
        }

        static async Task Main(string[] args)
        {
            // Cargar configuración desde appsettings.json
            _settings = AppSettings.Cargar();
            _username = _settings.Usuario;

            Console.WriteLine("╔════════════════════════════════════════════════╗");
            Console.WriteLine("║  Sistema de Control de Tiempos de Paraderos   ║");
            Console.WriteLine($"║{$"CLIENTE 2 - {_username}".PadLeft(30).PadRight(48)}║");
            Console.WriteLine("╚════════════════════════════════════════════════╝");
            Console.WriteLine();

            // Inicializar servicios
            _geocercaService = new GeocercaService();
            _apiEnvioService = new APIEnvioService(_settings);
            _dbService = new DatabaseService(_settings);

            // El cambio IDA→REGRESO ya no lo hace GeocercaService.CambiarModo (solo actualizaba
            // rutaact sin crear un despacho nuevo); ahora lo hace DespacharAutomaticoAsync al salir
            // de la zona TUPAC, que sí crea el despacho de vuelta completo.

            Console.WriteLine($"✓ Usuario configurado: {_username}");
            Console.WriteLine($"✓ Sistema de geocercas inicializado (16 paraderos)\n"); // ← CAMBIO: 16

            // Iniciar servidor HTTP
            _servidor = new ServidorHTTP(_settings.PuertoHttp);
            _servidor.OnIniciarMonitoreo += IniciarMonitoreoAsync;
            _servidor.Iniciar();

            // Configurar la conexión al Hub de Despachos (opcional)
            _despachoConnection = new HubConnectionBuilder()
                .WithUrl($"{_settings.ApiBaseUrl}/urbanoHub")
                .WithAutomaticReconnect()
                .Build();

            _despachoConnection.On<RecentUrbano>("ActualizarDatosUrbano", async (datos) =>
            {
                await ManejarActualizacionDespacho(datos);
            });

            try
            {
                await _despachoConnection.StartAsync();
                Console.WriteLine($"[DESPACHO] ✓ Conectado al Hub de Despachos (opcional)\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Hub de Despachos no disponible: {ex.Message}");
                Console.WriteLine("   (No es crítico, se usará solo la API HTTP)\n");
            }

            _ = GestionarMonitoreoEsperaAsync();

            // Menú principal
            await MenuPrincipal();
        }

        private static async Task<bool> IniciarMonitoreoAsync(IniciarMonitoreoRequest datos)
        {
            try
            {
                var placa = datos.Placa.ToUpper();
                var usuario = string.IsNullOrEmpty(datos.Usuario) ? _username : datos.Usuario;
                var codasig = datos.Codasig;
                var fechaIni = datos.FechaIni;

                // Si el aviso trae codruta se confía en él; si no (p.ej. backend sin actualizar),
                // se usa la marca local del auto-despacho de vuelta.
                bool esVuelta = !string.IsNullOrEmpty(datos.Codruta)
                    ? datos.Codruta == RUTA_VUELTA
                    : _placasDespachoVuelta.ContainsKey(placa);

                if (esVuelta)
                    _placasDespachoVuelta[placa] = true;
                else
                    _placasDespachoVuelta.TryRemove(placa, out _);

                await QuitarDeEsperaAsync(placa);

                Console.WriteLine($"\n🚀 Iniciando monitoreo automático...");
                Console.WriteLine($"   Placa:        {placa}");
                Console.WriteLine($"   Usuario:      {usuario}");
                Console.WriteLine($"   Codasig:      {codasig}");
                Console.WriteLine($"   Codruta:      {datos.Codruta ?? "-"} ({(esVuelta ? "VUELTA" : "IDA")})");
                Console.WriteLine($"   Hora Inicio:  {fechaIni}");

                // Parsear la hora de inicio
                DateTime horaDespacho;
                if (!string.IsNullOrEmpty(fechaIni))
                {
                    var partes = fechaIni.Split(':');
                    var hoy = DateTime.Today;
                    horaDespacho = new DateTime(hoy.Year, hoy.Month, hoy.Day,
                        int.Parse(partes[0]), int.Parse(partes[1]), 0);
                }
                else
                {
                    horaDespacho = DateTime.Now;
                }

                // Calcular tiempos esperados
                var minutosEsperados = _geocercaService.ObtenerMinutosDinamicos(horaDespacho);

                // Los minutos están contados desde la salida en ida; en un despacho de vuelta
                // la salida es TUPAC, así que se restan los minutos de TUPAC.
                if (esVuelta)
                {
                    var minutosTupac = minutosEsperados[ID_GEOCERCA_TUPAC - 1];
                    minutosEsperados = minutosEsperados.Select(m => m - minutosTupac).ToArray();
                }

                Console.WriteLine($"\n📊 Tiempos esperados calculados:");
                for (int i = esVuelta ? ID_GEOCERCA_TUPAC : 0; i < minutosEsperados.Length; i++)
                {
                    var horaEsperada = horaDespacho.AddMinutes(minutosEsperados[i]);
                    Console.WriteLine($"   Geocerca {i + 1}: +{minutosEsperados[i]} min → {horaEsperada:HH:mm:ss}");
                }
                Console.WriteLine();

                // Guardar datos del despacho
                _datosDespachos[placa] = new DatosDespacho
                {
                    Placa = placa,
                    Codasig = codasig,
                    HoraDespacho = horaDespacho,
                    MinutosEsperados = minutosEsperados
                };

                // Inicializar monitoreo de geocercas
                if (esVuelta)
                {
                    _geocercaService.InicializarMonitoreoPlaca(placa, ModoRuta.Regreso);
                }
                else
                {
                    _geocercaService.InicializarMonitoreoPlaca(placa);
                }

                // Si ya existe un monitoreo para esta placa, detenerlo primero
                if (_monitoreosActivos.ContainsKey(placa))
                {
                    Console.WriteLine($"⚠️  Ya existe monitoreo para {placa}, reiniciando...");
                    await _monitoreosActivos[placa].Desconectar();
                    _monitoreosActivos.Remove(placa);
                }

                // Crear nuevo servicio de monitoreo GPS
                var monitoreo = new MonitoreoGPSService(usuario, _settings.ApiBaseUrl);
                monitoreo.OnDatosGPSRecibidos += (datosGPS) => ManejarActualizacionGPS(placa, datosGPS);

                // Conectar al GPS
                await monitoreo.ConectarYMonitorear(placa);

                // Guardar en el diccionario
                _monitoreosActivos[placa] = monitoreo;

                Console.WriteLine($"✅ Monitoreo iniciado exitosamente para {placa}");
                Console.WriteLine($"🎯 Esperando detección de geocercas...\n");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error al iniciar monitoreo: {ex.Message}\n");
                return false;
            }
        }

        private static double CalcularDistanciaHaversine(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371000; // Radio de la Tierra en metros

            double dLat = (lat2 - lat1) * Math.PI / 180;
            double dLon = (lon2 - lon1) * Math.PI / 180;

            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                       Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) *
                       Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

            double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

            return R * c;
        }

        private static DateTime AjustarHoraLlegada(
    double latPunto,
    double lonPunto,
    DateTime horaPunto,
    double latGeocerca,
    double lonGeocerca,
    ModoRuta modo)
        {
            // Calcular distancia entre punto actual y geocerca (en metros)
            double distanciaMetros = CalcularDistanciaHaversine(latPunto, lonPunto, latGeocerca, lonGeocerca);

            // VELOCIDAD FIJA: 40 km/h
            const double VELOCIDAD_FIJA_KMH = 40.0;

            // Convertir velocidad a m/s
            double velocidadMS = VELOCIDAD_FIJA_KMH / 3.6;  // 40 ÷ 3.6 = 11.11 m/s

            // Calcular tiempo en segundos
            double tiempoSegundos = distanciaMetros / velocidadMS;

            // ✅ SIEMPRE RESTAR (el punto detectado siempre está después de haber cruzado)
            DateTime horaAjustada = horaPunto.AddSeconds(-tiempoSegundos);

            // Solo mostrar en consola (sin logs de archivo)
            Console.WriteLine($"[AJUSTE] Distancia: {distanciaMetros:F2}m | Ajuste: -{tiempoSegundos:F0}seg");

            return horaAjustada;
        }

        private static void ManejarActualizacionGPS(string placa, DatosVehiculo datos)
        {
            if (datos?.Vehiculo == null)
                return;

            var v = datos.Vehiculo;

            Console.WriteLine($"\n📍 GPS [{placa}] {v.lastValidLatitude:F6}, {v.lastValidLongitude:F6} | {datos.FechaActual:HH:mm:ss}");

            // Zonas de despacho automático (JOSE_GALVEZ / TUPAC): al salir de TUPAC esto cierra
            // el viaje de IDA y abre uno de REGRESO (ruta 68) — ver VerificarZonasAsync.
            _ = VerificarZonasAsync(placa, v.lastValidLatitude, v.lastValidLongitude);

            var modoActual = _geocercaService.ObtenerModoActual(placa);
            var geocercasVisitadas = _geocercaService.ObtenerGeocercasVisitadas(placa);

            var geocercaCruzada = _geocercaService.VerificarCruceGeocerca(
                placa,
                v.lastValidLongitude,
                v.lastValidLatitude
            );

            if (geocercaCruzada != null)
            {
                DateTime horaAjustada = AjustarHoraLlegada(
                    v.lastValidLatitude,
                    v.lastValidLongitude,
                    datos.FechaActual,
                    geocercaCruzada.CenterLat,
                    geocercaCruzada.CenterLon,
                    modoActual
                );

                _ = ProcesarLlegadaGeocerca(
                    placa,
                    geocercaCruzada,
                    v.lastValidLatitude,
                    v.lastValidLongitude,
                    horaAjustada
                );
            }

            // Geocerca circular PARADERO 01 VUELTA (Id=16) — solo en modo Regreso
            if (modoActual == ModoRuta.Regreso && !geocercasVisitadas.Contains(16))
            {
                const double LAT_P01   = -12.210273;
                const double LON_P01   = -76.906205;
                const double RADIO_P01 = 200.0;

                double distP01 = CalcularDistanciaHaversine(
                    v.lastValidLatitude, v.lastValidLongitude, LAT_P01, LON_P01);

                Console.WriteLine($"[P01_VUELTA] Distancia al centro: {distP01:F1} m");

                if (distP01 <= RADIO_P01)
                {
                    Console.WriteLine($"\n[P01_VUELTA] 🏁 ════════════════════════════════════");
                    Console.WriteLine($"[P01_VUELTA]    ¡DENTRO DE GEOCERCA CIRCULAR!");
                    Console.WriteLine($"[P01_VUELTA]    Distancia: {distP01:F1} m / Radio: {RADIO_P01} m");
                    Console.WriteLine($"[P01_VUELTA] ════════════════════════════════════\n");

                    var geocercaP01 = new Geocerca
                    {
                        Id = 16,
                        Name = "PARADERO 01 VUELTA",
                        CenterLat = LAT_P01,
                        CenterLon = LON_P01
                    };

                    _geocercaService.MarcarGeocercaVisitada(placa, 16);

                    DateTime horaAjustada = AjustarHoraLlegada(
                        v.lastValidLatitude, v.lastValidLongitude, datos.FechaActual,
                        LAT_P01, LON_P01, ModoRuta.Regreso);

                    _ = ProcesarLlegadaGeocerca(placa, geocercaP01,
                        v.lastValidLatitude, v.lastValidLongitude, horaAjustada);

                    _ = Task.Run(async () =>
                    {
                        Console.WriteLine($"🏁 RUTA COMPLETADA - {placa}");
                        await _apiEnvioService.EndRuta(placa);

                        await Task.Delay(30000);

                        if (_monitoreosActivos.ContainsKey(placa))
                        {
                            await _monitoreosActivos[placa].Desconectar();
                            _monitoreosActivos.Remove(placa);
                            _datosDespachos.Remove(placa);
                            _placasDespachoVuelta.TryRemove(placa, out _);
                            _zonaActualPorPlaca.TryRemove(placa, out _);
                            _geocercaService.LimpiarMonitoreoPlaca(placa);
                            Console.WriteLine($"✓ Monitoreo de {placa} finalizado\n");
                        }
                    });
                }
            }
        }

        private static async Task MenuPrincipal()
        {
            while (true)
            {
                Console.WriteLine("\n╔════════════════════════════════════════╗");
                Console.WriteLine("║              MENÚ PRINCIPAL            ║");
                Console.WriteLine("╠════════════════════════════════════════╣");
                Console.WriteLine("║ 1. Ver monitoreos activos              ║");
                Console.WriteLine("║ 2. Detener monitoreo de una placa      ║");
                Console.WriteLine("║ 3. Detener todos los monitoreos        ║");
                Console.WriteLine("║ 4. Estado del servidor                 ║");
                Console.WriteLine("║ 5. Salir                               ║");
                Console.WriteLine("╚════════════════════════════════════════╝");
                Console.Write("Seleccione una opción: ");

                var opcion = Console.ReadLine();
                if (opcion == null)
                {
                    Console.WriteLine("\nℹ️ Sin consola interactiva (modo contenedor). Servicio activo en segundo plano...");
                    await Task.Delay( System.Threading.Timeout.Infinite);
                    return;
                }

                switch (opcion)
                {
                    case "1":
                        MostrarMonitoreosActivos();
                        break;
                    case "2":
                        await DetenerMonitoreoEspecifico();
                        break;
                    case "3":
                        await DetenerTodosLosMonitoreos();
                        break;
                    case "4":
                        MostrarEstadoServidor();
                        break;
                    case "5":
                        await Salir();
                        return;
                    default:
                        Console.WriteLine("❌ Opción inválida");
                        break;
                }
            }
        }

        private static void MostrarMonitoreosActivos()
        {
            Console.WriteLine("\n╔════════════════════════════════════════╗");
            Console.WriteLine("║        MONITOREOS ACTIVOS              ║");
            Console.WriteLine("╚════════════════════════════════════════╝");

            if (_monitoreosActivos.Count == 0)
            {
                Console.WriteLine("⚠️  No hay monitoreos activos");
            }
            else
            {
                foreach (var kvp in _monitoreosActivos)
                {
                    var placa = kvp.Key;
                    var monitoreo = kvp.Value;
                    var estado = monitoreo.EstaConectado() ? "✅ Activo" : "❌ Desconectado";

                    Console.WriteLine($"   🚌 {placa}: {estado}");

                    if (_datosDespachos.ContainsKey(placa))
                    {
                        var datos = _datosDespachos[placa];
                        Console.WriteLine($"      Codasig: {datos.Codasig}");
                        Console.WriteLine($"      Despacho: {datos.HoraDespacho:HH:mm:ss}");
                    }
                }
            }
            Console.WriteLine();
        }

        private static async Task DetenerMonitoreoEspecifico()
        {
            Console.Write("\n📋 Ingrese la placa a detener: ");
            var placa = Console.ReadLine()?.Trim().ToUpper();

            if (string.IsNullOrEmpty(placa))
            {
                Console.WriteLine("❌ Placa inválida");
                return;
            }

            if (!_monitoreosActivos.ContainsKey(placa))
            {
                Console.WriteLine($"⚠️  No hay monitoreo activo para {placa}");
                return;
            }

            await _monitoreosActivos[placa].Desconectar();
            _monitoreosActivos.Remove(placa);
            _datosDespachos.Remove(placa);
            _placasDespachoVuelta.TryRemove(placa, out _);
            _zonaActualPorPlaca.TryRemove(placa, out _);
            _geocercaService.LimpiarMonitoreoPlaca(placa);

            Console.WriteLine($"✓ Monitoreo detenido para {placa}");
        }

        private static async Task DetenerTodosLosMonitoreos()
        {
            Console.WriteLine("\n🛑 Deteniendo todos los monitoreos...");

            foreach (var kvp in _monitoreosActivos)
            {
                await kvp.Value.Desconectar();
                _geocercaService.LimpiarMonitoreoPlaca(kvp.Key);
            }

            _monitoreosActivos.Clear();
            _datosDespachos.Clear();
            _placasDespachoVuelta.Clear();
            _zonaActualPorPlaca.Clear();
            Console.WriteLine("✓ Todos los monitoreos detenidos");
        }

        private static void MostrarEstadoServidor()
        {
            Console.WriteLine("\n╔════════════════════════════════════════╗");
            Console.WriteLine("║         ESTADO DEL SERVIDOR            ║");
            Console.WriteLine("╚════════════════════════════════════════╝");
            Console.WriteLine($"Usuario:              {_username}");
            Console.WriteLine($"Puerto HTTP:          {_settings.PuertoHttp}");
            Console.WriteLine($"Monitoreos activos:   {_monitoreosActivos.Count}");
            Console.WriteLine($"Buses en espera:      {_monitoreosEspera.Count} (zonas de despacho: {string.Join(", ", _zonasDespacho.Select(z => z.Nombre))})");
            Console.WriteLine($"Hub Despacho:         {(_despachoConnection?.State == HubConnectionState.Connected ? "Conectado" : "Desconectado")}");
            Console.WriteLine($"Endpoint API:         http://localhost:{_settings.PuertoHttp}/api/iniciar-monitoreo");
            Console.WriteLine($"API Envío:            {_apiEnvioService.ApiUrl}");
            Console.WriteLine("════════════════════════════════════════\n");
        }

        private static async Task Salir()
        {
            Console.WriteLine("\n🛑 Cerrando aplicación...");

            _servidor?.Detener();

            foreach (var monitoreo in _monitoreosActivos.Values)
            {
                await monitoreo.Desconectar();
            }

            await PausarMonitoreoEsperaAsync();

            if (_despachoConnection != null)
            {
                await _despachoConnection.StopAsync();
            }

            Console.WriteLine("✓ Aplicación cerrada correctamente");
            Console.WriteLine("Presione una tecla para salir...");
            Console.ReadKey();
        }

        private static async Task ManejarActualizacionDespacho(RecentUrbano datos)
        {
            if (datos.Isruta == "1" && !string.IsNullOrEmpty(datos.DeviceID))
            {
                await IniciarMonitoreoAsync(new IniciarMonitoreoRequest
                {
                    Placa = datos.DeviceID,
                    Usuario = _username,
                    FechaIni = datos.Fechaini,
                    Codasig = "0",
                    Codruta = datos.Codruta
                });
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // MONITOREO EN ESPERA — buses sin despacho activo, candidatos a
        // despacharse al salir de JOSE_GALVEZ (ver VerificarZonasAsync)
        // ════════════════════════════════════════════════════════════════════

        private static async Task GestionarMonitoreoEsperaAsync()
        {
            while (true)
            {
                try
                {
                    if (!_monitoreoEsperaActivo || DateTime.Now - _ultimaCargaEspera >= INTERVALO_RECARGA_ESPERA)
                        await CargarYArrancarMonitoreoEsperaAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ESPERA] ❌ Error en ciclo de espera: {ex.Message}");
                }

                await Task.Delay(TimeSpan.FromMinutes(1));
            }
        }

        private static async Task CargarYArrancarMonitoreoEsperaAsync()
        {
            _ultimaCargaEspera = DateTime.Now;
            _monitoreoEsperaActivo = true;

            var devices = await _dbService.GetDevicesEsperaAsync();
            int agregados = 0;

            foreach (var device in devices)
            {
                if (string.IsNullOrEmpty(device.Placa) || device.TieneRutaActiva)
                    continue;

                var placa = device.Placa.ToUpper();

                if (_monitoreosActivos.ContainsKey(placa) || _monitoreosEspera.ContainsKey(placa))
                    continue;

                try
                {
                    var monitoreo = new MonitoreoGPSService(_username, _settings.ApiBaseUrl);
                    monitoreo.OnDatosGPSRecibidos += (datos) => _ = ManejarGPSEsperaAsync(placa, datos);
                    await monitoreo.ConectarYMonitorear(placa);
                    _monitoreosEspera[placa] = monitoreo;
                    agregados++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ESPERA]   ❌ {placa} error al conectar: {ex.Message}");
                }
            }

            Console.WriteLine($"[ESPERA] {DateTime.Now:HH:mm} — {agregados} bus(es) nuevos en espera, {_monitoreosEspera.Count} en total");
        }

        private static async Task PausarMonitoreoEsperaAsync()
        {
            foreach (var placa in _monitoreosEspera.Keys.ToList())
                await QuitarDeEsperaAsync(placa);

            _monitoreoEsperaActivo = false;
        }

        private static async Task QuitarDeEsperaAsync(string placa)
        {
            _zonaActualPorPlaca.TryRemove(placa, out _);

            if (_monitoreosEspera.TryRemove(placa, out var monitoreo))
                await monitoreo.Desconectar();
        }

        private static async Task ManejarGPSEsperaAsync(string placa, DatosVehiculo datos)
        {
            try
            {
                if (datos?.Vehiculo == null)
                    return;

                if (_monitoreosActivos.ContainsKey(placa))
                {
                    await QuitarDeEsperaAsync(placa);
                    return;
                }

                var v = datos.Vehiculo;
                await VerificarZonasAsync(placa, v.lastValidLatitude, v.lastValidLongitude);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ESPERA] ❌ Error procesando GPS en espera de {placa}: {ex.Message}");
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // ZONAS DE DESPACHO — JOSE_GALVEZ (IDA/67) y TUPAC (REGRESO/68)
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Cualquier unidad (en espera o ya despachada) que entra y sale de una zona
        /// queda despachada en el sentido y la ruta fija de esa zona. Se llama en cada
        /// ping GPS, tanto para buses en espera (ManejarGPSEsperaAsync) como ya activos
        /// (ManejarActualizacionGPS) — así TUPAC también cierra el viaje de IDA en curso.
        /// </summary>
        private static async Task VerificarZonasAsync(string placa, double lat, double lon)
        {
            var zonaDetectada = _zonasDespacho.FirstOrDefault(z =>
                CalcularDistanciaHaversine(lat, lon, z.Lat, z.Lon) <= z.RadioM);

            _zonaActualPorPlaca.TryGetValue(placa, out var zonaAnteriorNombre);
            bool estabaDentro = zonaAnteriorNombre != null;
            bool ahoraDentro = zonaDetectada != null;

            if (!estabaDentro && ahoraDentro)
            {
                _zonaActualPorPlaca[placa] = zonaDetectada.Nombre;
                Console.WriteLine($"[ZONA] 🏁 {placa} entró a {zonaDetectada.Nombre}");
                return;
            }

            if (estabaDentro && zonaAnteriorNombre != zonaDetectada?.Nombre)
            {
                _zonaActualPorPlaca.TryRemove(placa, out _);
                var zonaSalida = _zonasDespacho.First(z => z.Nombre == zonaAnteriorNombre);

                Console.WriteLine($"\n[ZONA] 🚀 {placa} salió de {zonaSalida.Nombre} → auto-despacho ({zonaSalida.ModoAlSalir}, ruta {zonaSalida.CodRuta})\n");
                await DespacharAutomaticoAsync(placa, zonaSalida);

                if (ahoraDentro)
                    _zonaActualPorPlaca[placa] = zonaDetectada.Nombre;
            }
        }

        private static async Task DespacharAutomaticoAsync(string placa, ZonaDespacho zona)
        {
            lock (_despachoEnCurso)
            {
                if (!_despachoEnCurso.Add(placa)) return;
            }

            try
            {
                // Se consulta device en el momento: la lista de espera puede tener hasta 15 min de antigüedad
                // y un bus activo puede llevar horas con el mismo registro en memoria.
                var device = await _dbService.GetDeviceAsync(placa);
                if (device == null)
                {
                    Console.WriteLine($"[ZONA] ❌ {placa} no se pudo leer de device — no se despacha");
                    return;
                }

                await QuitarDeEsperaAsync(placa);

                // Liberar: cierra cualquier despacho abierto antes de abrir el nuevo.
                // No-op si no había ninguno (p.ej. saliendo de JOSE_GALVEZ sin viaje previo).
                await _apiEnvioService.EndRuta(placa);

                // Se marca antes de llamar: el aviso a /api/iniciar-monitoreo puede llegar antes de la respuesta.
                if (zona.ModoAlSalir == ModoRuta.Regreso)
                    _placasDespachoVuelta[placa] = true;
                else
                    _placasDespachoVuelta.TryRemove(placa, out _);

                bool asignado = await _apiEnvioService.AsignarDespachoAutomatico(placa, zona.CodRuta, device.CodConductorAct);

                if (!asignado)
                {
                    Console.WriteLine($"[ZONA] ❌ {placa} — asignarG no pudo crear el despacho (zona {zona.Nombre}, ruta {zona.CodRuta})");
                    if (zona.ModoAlSalir == ModoRuta.Regreso)
                        _placasDespachoVuelta.TryRemove(placa, out _);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ZONA] ❌ Error en auto-despacho de {placa}: {ex.Message}");
            }
            finally
            {
                lock (_despachoEnCurso) _despachoEnCurso.Remove(placa);
            }
        }

        private static async Task ProcesarLlegadaGeocerca(string placa, Geocerca geocerca, double latitud, double longitud, DateTime horaLlegada)
        {
            try
            {
                if (!_datosDespachos.ContainsKey(placa))
                {
                    Console.WriteLine($"⚠️  No hay datos de despacho para {placa}");
                    return;
                }

                var datosDespacho = _datosDespachos[placa];
                int indice = geocerca.Id - 1;

                if (indice < 0 || indice >= datosDespacho.MinutosEsperados.Length)
                {
                    Console.WriteLine($"⚠️  Índice de geocerca inválido: {indice}");
                    return;
                }

                var minutosEsperados = datosDespacho.MinutosEsperados[indice];
                var horaEsperada = datosDespacho.HoraDespacho.AddMinutes(minutosEsperados);

                var diferenciaTimeSpan = horaLlegada - horaEsperada;

                string volado = FormatearVolado(diferenciaTimeSpan);

                Console.WriteLine($"✅ {geocerca.Name} | {horaLlegada:HH:mm:ss} | {volado}");

                var registro = new ControlGPS
                {
                    Codasig = datosDespacho.Codasig,
                    DeviceID = placa,
                    Nom_control = geocerca.Name,
                    Hora_inicio = datosDespacho.HoraDespacho.ToString("HH:mm:ss"),
                    Hora_estimada = horaEsperada.ToString("HH:mm:ss"),
                    Hora_llegada = horaLlegada.ToString("HH:mm:ss"),
                    Volado = volado,
                    Fecha = horaLlegada.ToString("yyyy-MM-dd")
                };

                bool enviado = await _apiEnvioService.EnviarRegistro(registro);

                if (!enviado)
                {
                    Console.WriteLine($"❌ Error al enviar datos");
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error procesando llegada: {ex.Message}");
            }
        }

        private static string FormatearVolado(TimeSpan diferencia)
        {
            bool esNegativo = diferencia.TotalSeconds < 0;
            var tiempoAbsoluto = diferencia.Duration();

            int horas = (int)tiempoAbsoluto.TotalHours;
            int minutos = tiempoAbsoluto.Minutes;
            int segundos = tiempoAbsoluto.Seconds;

            string resultado = "";
            string signo = esNegativo ? "-" : "";

            if (horas > 0)
            {
                resultado = $"{signo}{horas}h {minutos}min {segundos}seg";
            }
            else if (minutos > 0)
            {
                resultado = $"{signo}{minutos}min {segundos}seg";
            }
            else
            {
                resultado = $"{signo}{segundos}seg";
            }

            return resultado;
        }

        private class DatosDespacho
        {
            public string Placa { get; set; }
            public string Codasig { get; set; }
            public DateTime HoraDespacho { get; set; }
            public int[] MinutosEsperados { get; set; }
        }
    }
}