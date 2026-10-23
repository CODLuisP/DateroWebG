using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using DateroWeb.Models;

namespace DateroWeb.API
{
    public class ServidorHTTP
    {
        private HttpListener _listener;
        private CancellationTokenSource _cts;
        private readonly int _puerto;

        public event Func<IniciarMonitoreoRequest, Task<bool>> OnIniciarMonitoreo;

        public ServidorHTTP(int puerto = 8080)
        {
            _puerto = puerto;
        }

        public void Iniciar()
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://+:{_puerto}/");  // ✅ Acepta cualquier IP

            _listener.Start();
            _cts = new CancellationTokenSource();

            Console.WriteLine($"✓ Servidor HTTP iniciado en http://+:{_puerto}");
            Console.WriteLine($"✓ Endpoint disponible: POST http://+:{_puerto}/api/iniciar-monitoreo");
            Console.WriteLine();

            Task.Run(() => EscucharPeticiones(_cts.Token));
        }

        private async Task EscucharPeticiones(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    _ = Task.Run(() => ProcesarPeticion(context), ct);
                }
                catch (Exception ex)
                {
                    if (!ct.IsCancellationRequested)
                    {
                        Console.WriteLine($"❌ Error en servidor HTTP: {ex.Message}");
                    }
                }
            }
        }

        private async Task ProcesarPeticion(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            // Configurar CORS
            response.AddHeader("Access-Control-Allow-Origin", "*");
            response.AddHeader("Access-Control-Allow-Methods", "POST, GET, OPTIONS");
            response.AddHeader("Access-Control-Allow-Headers", "Content-Type");

            try
            {
                // Manejar OPTIONS (preflight CORS)
                if (request.HttpMethod == "OPTIONS")
                {
                    response.StatusCode = 200;
                    response.Close();
                    return;
                }

                // Endpoint: POST /api/iniciar-monitoreo
                if (request.HttpMethod == "POST" && request.Url.AbsolutePath == "/api/iniciar-monitoreo")
                {
                    await ProcesarIniciarMonitoreo(request, response);
                    return;
                }

                // Endpoint: GET /health (para verificar que está corriendo)
                if (request.HttpMethod == "GET" && request.Url.AbsolutePath == "/health")
                {
                    await ResponderJSON(response, 200, new { status = "running", timestamp = DateTime.Now });
                    return;
                }

                // Endpoint no encontrado
                await ResponderJSON(response, 404, new { error = "Endpoint no encontrado" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error procesando petición: {ex.Message}");
                await ResponderJSON(response, 500, new { error = ex.Message });
            }
        }

        private async Task ProcesarIniciarMonitoreo(HttpListenerRequest request, HttpListenerResponse response)
        {
            try
            {
                // Leer el body de la petición
                string body;
                using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                {
                    body = await reader.ReadToEndAsync();
                }

                // Deserializar JSON
                var datos = JsonConvert.DeserializeObject<IniciarMonitoreoRequest>(body);

                if (datos == null || string.IsNullOrEmpty(datos.Placa))
                {
                    await ResponderJSON(response, 400, new { error = "Placa es requerida" });
                    return;
                }

                Console.WriteLine($"\n📨 Petición recibida: Iniciar monitoreo de placa {datos.Placa}");

                // Invocar el evento
                if (OnIniciarMonitoreo != null)
                {
                    bool exito = await OnIniciarMonitoreo.Invoke(datos);

                    if (exito)
                    {
                        await ResponderJSON(response, 200, new
                        {
                            success = true,
                            message = $"Monitoreo iniciado para placa {datos.Placa}",
                            placa = datos.Placa
                        });
                    }
                    else
                    {
                        await ResponderJSON(response, 500, new
                        {
                            success = false,
                            message = "Error al iniciar monitoreo"
                        });
                    }
                }
                else
                {
                    await ResponderJSON(response, 500, new { error = "Handler no configurado" });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error en ProcesarIniciarMonitoreo: {ex.Message}");
                await ResponderJSON(response, 500, new { error = ex.Message });
            }
        }

        private async Task ResponderJSON(HttpListenerResponse response, int statusCode, object data)
        {
            response.StatusCode = statusCode;
            response.ContentType = "application/json";

            var json = JsonConvert.SerializeObject(data);
            var buffer = Encoding.UTF8.GetBytes(json);

            response.ContentLength64 = buffer.Length;
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            response.Close();
        }

        public void Detener()
        {
            _cts?.Cancel();
            _listener?.Stop();
            _listener?.Close();
            Console.WriteLine("✓ Servidor HTTP detenido");
        }
    }
}