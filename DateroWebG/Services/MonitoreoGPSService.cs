using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using DateroWeb.Models;

namespace DateroWeb.Services
{
    public class MonitoreoGPSService
    {
        private HubConnection _gpsConnection;
        private readonly string _username;
        private readonly string _baseUrl;
        private string _placaActual;

        public event Action<DatosVehiculo> OnDatosGPSRecibidos;

        public MonitoreoGPSService(string username, string baseUrl)
        {
            _username = username;
            _baseUrl = baseUrl;
        }

        public async Task ConectarYMonitorear(string placa)
        {
            if (_gpsConnection != null && _gpsConnection.State == HubConnectionState.Connected)
            {
                await Desconectar();
            }

            _placaActual = placa;

            // Construir la URL con username y placa
            var url = $"{_baseUrl}/dataHubVehicle/{_username}/{placa}";

            _gpsConnection = new HubConnectionBuilder()
                .WithUrl(url)
                .WithAutomaticReconnect()
                .Build();

            // Suscribirse al evento de actualización GPS
            _gpsConnection.On<DatosVehiculo>("ActualizarDatosVehiculo", (datos) =>
            {
                if (datos?.Vehiculo != null)
                {

                    Console.WriteLine($"[HUB] Recibido: {datos.FechaActual:HH:mm:ss} | Lon: {datos.Vehiculo.lastValidLongitude:F6}");
                }

                OnDatosGPSRecibidos?.Invoke(datos);
            });

            // Eventos de conexión
            _gpsConnection.Closed += async (error) =>
            {
                if (_gpsConnection != null)
                {
                    Console.WriteLine($"[GPS] Conexión cerrada. Reconectando...");
                    await Task.Delay(5000);
                    try
                    {
                        if (_gpsConnection != null && _gpsConnection.State == HubConnectionState.Disconnected)
                        {
                            await _gpsConnection.StartAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[GPS] Error al reconectar: {ex.Message}");
                    }
                }
            };

            _gpsConnection.Reconnecting += error =>
            {
                Console.WriteLine($"[GPS] Reconectando al monitoreo GPS...");
                return Task.CompletedTask;
            };

            _gpsConnection.Reconnected += connectionId =>
            {
                Console.WriteLine($"[GPS] Reconectado exitosamente al monitoreo GPS");
                return Task.CompletedTask;
            };

            try
            {
                await _gpsConnection.StartAsync();
                Console.WriteLine($"[GPS] ✓ Conectado al monitoreo GPS de {placa}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GPS] Error al conectar: {ex.Message}");
                throw;
            }
        }

        public async Task Desconectar()
        {
            if (_gpsConnection != null)
            {
                try
                {
                    // Detener la conexión si está activa
                    if (_gpsConnection.State == HubConnectionState.Connected ||
                        _gpsConnection.State == HubConnectionState.Connecting ||
                        _gpsConnection.State == HubConnectionState.Reconnecting)
                    {
                        await _gpsConnection.StopAsync();
                    }

                    // Liberar recursos
                    await _gpsConnection.DisposeAsync();
                    _gpsConnection = null;

                    Console.WriteLine($"[GPS] Desconectado del monitoreo GPS");
                }
                catch (ObjectDisposedException)
                {
                    // Ya fue liberado, no hacer nada
                    _gpsConnection = null;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GPS] Error al desconectar (no crítico): {ex.Message}");
                    _gpsConnection = null;
                }
            }
        }

        public bool EstaConectado()
        {
            return _gpsConnection != null && _gpsConnection.State == HubConnectionState.Connected;
        }

        public string GetPlacaActual()
        {
            return _placaActual;
        }
    }
}