using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using DateroWeb.Config;
using DateroWeb.Models;

namespace DateroWeb.Services
{
    public class APIEnvioService
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly string _apiUrl;

        public string ApiUrl => _apiUrl;

        public APIEnvioService(AppSettings settings)
        {
            // Ignorar errores de certificado SSL (solo para pruebas si es necesario)
            ServicePointManager.ServerCertificateValidationCallback =
                (sender, certificate, chain, sslPolicyErrors) => true;

            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(settings.HttpTimeoutSegundos);
            _baseUrl = settings.ApiBaseUrl;
            _apiUrl = $"{_baseUrl}/api/Datero/EnvioControlB/{settings.Usuario}";
        }

        public async Task<bool> EnviarRegistros(ControlGPS[] registros)
        {
            try
            {
                var json = JsonConvert.SerializeObject(registros);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                Console.WriteLine($"\n[API] Enviando {registros.Length} registro(s) a la API...");

                var response = await _httpClient.PostAsync(_apiUrl, content);

                if (response.IsSuccessStatusCode)
                {
                    var resultado = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"[API] ✓ {resultado}");
                    return true;
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"[API] ❌ Error: {response.StatusCode} - {errorContent}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[API] ❌ Excepción: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> EnviarRegistro(ControlGPS registro)
        {
            return await EnviarRegistros(new[] { registro });
        }

        public async Task<bool> ActualizarRutaact(string placa)
        {
            try
            {
                var url = $"{_baseUrl}/api/Datero/ActualizarRutaactG/{placa}";
                Console.WriteLine($"\n[API] Llamando a ActualizarRutaact para {placa}...");
                Console.WriteLine($"[API] URL: {url}");

                var response = await _httpClient.PutAsync(url, null);

                if (response.IsSuccessStatusCode)
                {
                    var resultado = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"[API] ✓ ActualizarRutaact exitoso: {resultado}");
                    return true;
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"[API] ❌ Error ActualizarRutaact: {response.StatusCode} - {errorContent}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[API] ❌ Excepción en ActualizarRutaact: {ex.Message}");
                return false;
            }
        }

        // Mismo endpoint que el despacho manual de etudvrg: crea urbano_asigna / recor_control
        // y actualiza device, luego el backend notifica a /api/iniciar-monitoreo con el codruta.
        public async Task<bool> AsignarDespachoAutomatico(string placa, string codRuta, string codConductor)
        {
            try
            {
                var url = $"{_baseUrl}/api/Caja/asignarG";

                var payload = new
                {
                    carro = new { codunidad = placa },
                    ruta = new { codigo = codRuta },
                    conductor = new { codigo = string.IsNullOrEmpty(codConductor) ? "0" : codConductor },
                    fecprog = DateTime.Now.ToString("dd/MM/yyyy HH:mm")
                };

                var json = JsonConvert.SerializeObject(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                Console.WriteLine($"\n[API] Llamando a asignarG (auto-despacho ruta {codRuta}) para {placa}...");

                var response = await _httpClient.PostAsync(url, content);
                var resultado = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[API] ✓ asignarG exitoso: {resultado}");
                    return true;
                }

                Console.WriteLine($"[API] ❌ Error asignarG: {response.StatusCode} - {resultado}");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[API] ❌ Excepción en AsignarDespachoAutomatico: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> EndRuta(string deviceID)
        {
            try
            {
                var url = $"{_baseUrl}/api/Datero/endruta/{deviceID}";
                Console.WriteLine($"\n[API] Llamando a endruta para {deviceID}...");
                Console.WriteLine($"[API] URL: {url}");

                var response = await _httpClient.PostAsync(url, null);

                if (response.IsSuccessStatusCode)
                {
                    var resultado = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"[API] ✓ EndRuta exitoso: {resultado}");
                    return true;
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"[API] ❌ Error EndRuta: {response.StatusCode} - {errorContent}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[API] ❌ Excepción en EndRuta: {ex.Message}");
                return false;
            }
        }
    }
}